#!/usr/bin/env bash
# Image retention under the reconcile lock (#2016, ADR 0149).
set -euo pipefail
readonly LOCK=/run/jobbliggaren-reconcile.lock
readonly PYTHON=/usr/bin/python3
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec "$PYTHON" -I - "$script_dir" "$LOCK" "$@" <<'PY'
import argparse
import fcntl
import json
import os
import re
import signal
import stat
import subprocess
import sys
import time

DOCKER = "/usr/bin/docker"
ID = re.compile(r"sha256:[0-9a-f]{64}\Z")
CID = re.compile(r"[0-9a-f]{64}\Z")
REPO = re.compile(r"(?:(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*(?::[0-9]+)?/)?[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*\Z")
TAG = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\Z")


class Incomplete(Exception):
    pass


def log(message):
    print("retention: " + message, flush=True)


def reference_kind(ref):
    if not isinstance(ref, str) or not ref:
        return "unknown"
    parts = ref.split("@")
    if len(parts) > 2 or (len(parts) == 2 and not ID.fullmatch(parts[1])):
        return "unknown"
    name = parts[0]
    leaf = name.rsplit("/", 1)[-1]
    tagged = ":" in leaf
    if tagged:
        name, tag = name.rsplit(":", 1)
        if not TAG.fullmatch(tag):
            return "unknown"
    if len(name) > 255 - len("docker.io/library/") or not REPO.fullmatch(name):
        return "unknown"
    return "tag" if tagged or len(parts) == 1 else "digest"


def positive(value):
    if not re.fullmatch(r"[1-9][0-9]{0,5}", value):
        raise argparse.ArgumentTypeError("budget must be a positive integer (seconds)")
    return int(value)


def main():
    script_dir, lock_path = sys.argv[1:3]
    parser = argparse.ArgumentParser(description="Protect referenced images; default is dry-run.")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--dry-run", action="store_true")
    modes.add_argument("--apply", action="store_true")
    parser.add_argument("--compose-file", default=os.path.join(script_dir, "../docker-compose.yml"))
    parser.add_argument("--receipt", default="/var/lib/jobbliggaren/applied-release.env")
    parser.add_argument("--budget-seconds", type=positive, default=60)
    parser.add_argument("--lock-fd", type=int, choices=[9])
    args = parser.parse_args(sys.argv[3:])
    deadline = time.monotonic() + args.budget_seconds
    removed = skipped = 0
    lock_fd = None

    def remaining():
        seconds = deadline - time.monotonic()
        if seconds <= 0.5:
            raise Incomplete("budget exhausted; inventory again on the next pass")
        return seconds

    def run(command, operation):
        timeout = remaining() - 0.5
        try:
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                       start_new_session=True, close_fds=True)
        except OSError:
            raise Incomplete(operation + " could not start") from None
        try:
            output, _ = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            # Children never inherit the lock. A daemon mutation may outlive its client.
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            try:
                process.communicate(timeout=max(0.01, min(0.4, deadline - time.monotonic())))
            except subprocess.TimeoutExpired:
                raise Incomplete(operation + " timed out; client reaping incomplete; daemon outcome unknown") from None
            raise Incomplete(operation + " timed out; daemon outcome unknown; inventory again") from None
        if process.returncode:
            raise Incomplete(operation + " failed (exit " + str(process.returncode) + ")")
        try:
            return output.decode("utf-8")
        except UnicodeError:
            raise Incomplete(operation + " returned invalid encoding") from None

    def docker(arguments, operation):
        return run([DOCKER] + arguments, operation)

    def json_result(arguments, operation):
        try:
            return json.loads(docker(arguments, operation))
        except (ValueError, TypeError):
            raise Incomplete(operation + " returned invalid JSON") from None

    def image_rows(refs):
        result = {}
        for offset in range(0, len(refs), 100):
            batch = refs[offset:offset + 100]
            rows = json_result(["image", "inspect"] + batch, "image inspect")
            if not isinstance(rows, list) or len(rows) != len(batch):
                raise Incomplete("image inspect returned incomplete metadata")
            for requested, row in zip(batch, rows):
                if not isinstance(row, dict) or not ID.fullmatch(str(row.get("Id", ""))):
                    raise Incomplete("image inspect returned invalid identity")
                image_id = row["Id"]
                if ID.fullmatch(requested) and requested != image_id:
                    raise Incomplete("image inspect identity mismatch")
                for key in ("RepoTags", "RepoDigests"):
                    if key not in row or (row[key] is not None and
                                          (not isinstance(row[key], list) or
                                           any(not isinstance(ref, str) for ref in row[key]))):
                        raise Incomplete("image inspect returned incomplete reference metadata")
                if not isinstance(row.get("Size"), int) or isinstance(row["Size"], bool) or row["Size"] < 0:
                    raise Incomplete("image inspect returned invalid size")
                result[requested] = row
        return result

    def containers():
        listing = docker(["ps", "-a", "-q", "--no-trunc"], "container inventory").splitlines()
        if any(not CID.fullmatch(cid) for cid in listing) or len(set(listing)) != len(listing):
            raise Incomplete("container inventory returned invalid identities")
        protected = set()
        for offset in range(0, len(listing), 100):
            batch = listing[offset:offset + 100]
            rows = json_result(["container", "inspect"] + batch, "container inspect")
            if not isinstance(rows, list) or len(rows) != len(batch):
                raise Incomplete("container inspect returned incomplete metadata")
            for requested, row in zip(batch, rows):
                if (not isinstance(row, dict) or row.get("Id") != requested or
                        not ID.fullmatch(str(row.get("Image", "")))):
                    raise Incomplete("container inspect returned invalid image identity")
                protected.add(row["Image"])
        return protected

    def reasons(row):
        refs = (row["RepoTags"] or []) + (row["RepoDigests"] or [])
        kinds = {reference_kind(ref) for ref in refs}
        return kinds & {"tag", "unknown"}

    try:
        remaining()
        if args.lock_fd is None:
            lock_fd = os.open(lock_path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        else:
            lock_fd = args.lock_fd
            descriptor = os.fstat(lock_fd)
            path = os.stat(lock_path, follow_symlinks=False)
            if not stat.S_ISREG(descriptor.st_mode) or (descriptor.st_dev, descriptor.st_ino) != (path.st_dev, path.st_ino):
                raise Incomplete("inherited lock descriptor does not name the reconcile lock")
            probe = os.open(lock_path, os.O_RDWR | os.O_NOFOLLOW)
            try:
                try:
                    fcntl.flock(probe, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    pass
                else:
                    raise Incomplete("inherited descriptor was not already locked")
            finally:
                os.close(probe)
        try:
            fcntl.flock(lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            if args.lock_fd is not None:
                raise Incomplete("inherited lock is not held by this caller")
            log("lock overlap; skipped")
            return 0
        os.set_inheritable(lock_fd, False)
        try:
            os.lstat(args.receipt)
        except FileNotFoundError:
            log("no receipt; skipped")
            return 0
        refs = run([os.path.join(script_dir, "jobbliggaren-release-record.sh"),
                    "protected-refs", args.receipt], "receipt validation").splitlines()
        if len(refs) != 6 or any(reference_kind(ref) != "digest" for ref in refs):
            raise Incomplete("record tool returned incomplete protection")
        model = json_result(["compose", "-f", args.compose_file, "--profile", "*",
                             "config", "--format", "json"], "compose model")
        if not isinstance(model, dict) or not isinstance(model.get("services"), dict) or not model["services"]:
            raise Incomplete("compose model has no services")
        for service in model["services"].values():
            if not isinstance(service, dict) or not isinstance(service.get("image"), str) or not service["image"]:
                raise Incomplete("compose service has no image")
            if reference_kind(service["image"]) == "unknown" and not ID.fullmatch(service["image"]):
                raise Incomplete("compose service has an unknown image reference")
            refs.append(service["image"])
        refs = sorted(set(refs))
        resolved = image_rows(refs)
        protected = {row["Id"] for row in resolved.values()} | containers()
        listing = docker(["image", "ls", "-a", "-q", "--no-trunc"], "image inventory").splitlines()
        if any(not ID.fullmatch(image_id) for image_id in listing):
            raise Incomplete("image inventory returned invalid identities")
        ids = sorted(set(listing))
        rows = image_rows(ids)
        if not protected <= set(ids):
            raise Incomplete("protected image missing from inventory")
        candidates = []
        for image_id in ids:
            row = rows[image_id]
            keep = reasons(row)
            if image_id in protected:
                keep.add("protected")
            if keep:
                log("keep " + image_id + " reason=" + ",".join(sorted(keep)))
            else:
                candidates.append(image_id)
        candidates.sort(key=lambda image_id: (not bool(rows[image_id]["RepoDigests"] or rows[image_id]["RepoTags"]), image_id))
        logical_bytes = sum(rows[image_id]["Size"] for image_id in candidates)
        log("inventory complete images=" + str(len(ids)) + " candidates=" + str(len(candidates)) +
            " logical_bytes=" + str(logical_bytes) + " (shared layers; not reclaimable bytes)")
        for image_id in candidates:
            log("candidate " + image_id + " kind=" +
                ("digest-only" if rows[image_id]["RepoDigests"] or rows[image_id]["RepoTags"] else "reference-less"))
        if not args.apply:
            remaining()
            log("complete mode=dry-run removed=0")
            return 0
        for image_id in candidates:
            remaining()
            fresh = image_rows([image_id])[image_id]
            if reasons(fresh) or image_id in containers():
                skipped += 1
                log("keep " + image_id + " reason=fresh-reference")
                continue
            docker(["image", "rm", "--no-prune", image_id], "remove " + image_id)
            removed += 1
            log("removed " + image_id)
        remaining()
        log("complete mode=apply removed=" + str(removed) + " skipped=" + str(skipped))
        return 0
    except (Incomplete, OSError) as error:
        message = str(error) if isinstance(error, Incomplete) else "lock or local input could not be read"
        log("incomplete removed=" + str(removed) + " skipped=" + str(skipped) + " reason=" + message)
        return 2
    finally:
        if lock_fd is not None and args.lock_fd is None:
            os.close(lock_fd)


sys.exit(main())
PY