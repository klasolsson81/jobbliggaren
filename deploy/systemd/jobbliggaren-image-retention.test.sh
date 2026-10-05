#!/usr/bin/env bash
# The fixtures model digest pulls, tag moves and container creation. Malformed
# daemon metadata is an invariant-break case: only safe refusal is asserted.
set -euo pipefail
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
python3 -I - "$script_dir" <<'PY'
import fcntl
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

SOURCE = Path(sys.argv[1])
STUB = r'''#!/usr/bin/python3
import json, os, sys, time
from pathlib import Path
root=Path(__file__).parent
path=root/"state.json"
s=json.loads(path.read_text())
a=sys.argv[1:]
with (root/"calls").open("a") as f: f.write(json.dumps(a)+"\n")
def save(): path.write_text(json.dumps(s))
if s.get("sleep") == a[:2]:
    if s.get("daemon_remove"):
        s["images"].pop(s["daemon_remove"],None); save()
    (root/"pid").write_text(str(os.getpid()))
    time.sleep(20)
if s.get("error") == a[:2]:
    print("SECRET-IN-STDERR",file=sys.stderr); sys.exit(1)
if a[:2]==["compose","-f"]:
    assert a[3:]==["--profile","*","config","--format","json"], a
    print(json.dumps({"services":s["compose"],"secret":"SECRET-IN-MODEL"}))
elif a[:2]==["image","ls"]:
    assert a==["image","ls","-a","-q","--no-trunc"]
    print("\n".join(s["images"]))
elif a[:2]==["container","inspect"]:
    print(json.dumps([s["containers"][i] for i in a[2:]]))
elif a[:1]==["ps"]:
    assert a==["ps","-a","-q","--no-trunc"]
    print("\n".join(s["containers"]), end="\n" if s["containers"] else "")
elif a[:2]==["image","inspect"]:
    rows=[]
    s["inspects"]=s.get("inspects",0)+1
    if s.get("fresh") and s["inspects"]==3:
        i=s["fresh"]["id"]
        if s["fresh"]["kind"]=="tag": s["images"][i]["RepoTags"]=["other/repo:rescued"]
        else: s["containers"]["c"*64]={"Id":"c"*64,"Image":i}
        save()
    save()
    for ref in a[2:]:
        i=ref if ref in s["images"] else s["refs"].get(ref)
        if i not in s["images"]: sys.exit(1)
        rows.append(s["images"][i])
    print(json.dumps(rows))
elif a[:2]==["image","rm"]:
    assert a[2]=="--no-prune" and len(a)==4 and len(a[3])==71
    i=a[3]
    if s.get("conflict")==i:
        print("SECRET-CONFLICT",file=sys.stderr); sys.exit(1)
    assert not s["images"][i]["RepoTags"] or all("@" in t for t in s["images"][i]["RepoTags"])
    assert not any(c["Image"]==i for c in s["containers"].values())
    del s["images"][i]; save()
else:
    print("unsupported",a,file=sys.stderr); sys.exit(4)
'''


def image_id(n):
    return "sha256:" + format(n, "064x")


class Retention(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.lock = self.root / "lock"
        self.helper = self.root / "jobbliggaren-image-retention.sh"
        source = (SOURCE / self.helper.name).read_text()
        self.assertIn('DOCKER = "/usr/bin/docker"', source)
        self.assertIn("readonly LOCK=/run/jobbliggaren-reconcile.lock", source)
        source = source.replace('DOCKER = "/usr/bin/docker"', 'DOCKER = ' + repr(str(self.root / "docker")))
        source = source.replace("readonly LOCK=/run/jobbliggaren-reconcile.lock", "readonly LOCK=" + str(self.lock))
        self.helper.write_text(source)
        tool = self.root / "jobbliggaren-release-record.sh"
        tool.write_bytes((SOURCE / tool.name).read_bytes())
        tool.chmod(0o700)
        docker = self.root / "docker"
        docker.write_text(STUB)
        docker.chmod(0o700)
        self.receipt = self.root / "receipt"
        # The wire shape emitted by cmd_emit and persisted by reconcile.
        lines = [
            "JBL_RECEIPT_RECORD_DIGEST=" + image_id(100),
            "JBL_RELEASE_FORMAT=1",
            "JBL_RELEASE_REPOSITORY=klasolsson81/jobbliggaren",
            "JBL_RELEASE_SOURCE_REF=refs/heads/main",
            "JBL_RELEASE_SOURCE_SHA=" + "a" * 40,
            "JBL_RELEASE_SEQUENCE=10",
        ]
        names = ["API", "WORKER", "MIGRATE", "WEB", "CADDY"]
        lines += ["JBL_RELEASE_IMAGE_" + name + "=" + image_id(i + 101) for i, name in enumerate(names)]
        lines += ["JBL_RELEASE_DEPLOY_SHA256=" + "b" * 64,
                  "JBL_RELEASE_MIGRATIONS_APP=20260419145850_InitialCreate",
                  "JBL_RELEASE_MIGRATIONS_IDENTITY=20260506091354_InitialIdentity"]
        self.receipt.write_text("\n".join(lines) + "\n")
        self.s = {"images": {}, "refs": {}, "containers": {}, "compose": {}}
        prefix = "ghcr.io/klasolsson81/jobbliggaren-"
        for i, name in enumerate(["release"] + [n.lower() for n in names]):
            ref = prefix + name + "@" + image_id(i + 100)
            self.add(i + 1, [ref], [ref])
            self.s["refs"][ref] = image_id(i + 1)
        self.add(20, ["upstream:1"])
        self.s["refs"]["upstream:1"] = image_id(20)
        self.s["compose"] = {"upstream": {"image": "upstream:1", "environment": {"SECRET": "secret"}}}
        self.add(30)
        self.add(31, ["other/repo@" + image_id(300)], ["other/repo@" + image_id(300)])

    def add(self, n, tags=None, digests=None):
        self.s["images"][image_id(n)] = {"Id": image_id(n), "RepoTags": tags or [],
                                         "RepoDigests": digests or [], "Size": 100}

    def invoke(self, *args, fd=None):
        (self.root / "state.json").write_text(json.dumps(self.s))
        command = ["bash", str(self.helper), "--receipt", str(self.receipt), "--compose-file", str(self.root / "compose")] + list(args)
        if fd is None:
            result = subprocess.run(command, capture_output=True, text=True, timeout=10)
        else:
            # Reconcile owns descriptor 9 and passes it through exec.
            command = ["bash", "-c", 'exec 9<>"$1"; flock -n 9; shift; exec "$@" --lock-fd 9',
                       "fixture", str(self.lock)] + command
            result = subprocess.run(command, capture_output=True, text=True, timeout=10)
        self.s = json.loads((self.root / "state.json").read_text())
        self.assertNotIn("SECRET", result.stdout + result.stderr)
        return result

    def removed(self):
        calls = (self.root / "calls").read_text().splitlines() if (self.root / "calls").exists() else []
        return [json.loads(line)[3] for line in calls if json.loads(line)[:2] == ["image", "rm"]]

    def test_default_preview_and_digest_first(self):
        r = self.invoke()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(self.removed(), [])
        candidates = [line for line in r.stdout.splitlines() if "candidate sha256:" in line]
        self.assertEqual(len(candidates), 2)
        self.assertIn(image_id(31), candidates[0])

    def test_apply_explicit_ids_and_no_prune(self):
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertEqual(self.removed(), [image_id(31), image_id(30)])
        self.assertEqual(len(self.s["images"]), 7)
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertIn("candidates=0", r.stdout)

    def test_actual_tags_digest_aliases_and_unknowns(self):
        for i, ref in enumerate(["repo:latest", "host:5000/repo:old", "repo:tag@" + image_id(80),
                                 "UNKNOWN", "repo@sha512:x", "", "malformed ref"]):
            self.add(40 + i, [ref] if ref else [""])
        self.add(50, ["host:5000/repo@" + image_id(90)])
        self.add(51, [], ["repo:old"])
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertEqual(set(self.removed()), {image_id(30), image_id(31), image_id(50)})
        self.assertTrue(all(image_id(i) in self.s["images"] for i in range(40, 47)))
        self.assertIn(image_id(51), self.s["images"])

    def test_all_container_states_and_projects(self):
        for n, status in enumerate(["running", "exited", "created", "paused", "dead"], 40):
            self.add(n)
            cid = format(n, "064x")
            self.s["containers"][cid] = {"Id": cid, "Image": image_id(n),
                                         "State": {"Status": status}, "Config": {"Labels": {"project": "other"}}}
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertEqual(len(self.removed()), 2)
        self.assertTrue(all(image_id(n) in self.s["images"] for n in range(40, 45)))

    def test_compose_all_profiles_multiple_services_and_aliases(self):
        self.add(40, [], ["repo@" + image_id(400)])
        self.s["refs"]["repo@" + image_id(400)] = image_id(40)
        self.s["compose"].update({"ops": {"image": "repo@" + image_id(400), "profiles": ["ops"]},
                                   "backup": {"image": "repo@" + image_id(400), "profiles": ["backup"]}})
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertIn(image_id(40), self.s["images"])

    def test_missing_protected_ref_aborts_before_removal(self):
        self.s["refs"].pop(next(iter(self.s["refs"])))
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_invalid_receipt_aborts(self):
        self.receipt.write_text(self.receipt.read_text().replace("SEQUENCE=10", "SEQUENCE=zero"))
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_missing_receipt_skips(self):
        self.receipt.unlink()
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0)
        self.assertEqual(self.removed(), [])

    def test_metadata_missing_field_aborts_whole_inventory(self):
        del self.s["images"][image_id(30)]["RepoTags"]
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_metadata_wrong_type_aborts(self):
        self.s["images"][image_id(30)]["RepoDigests"] = "repo@digest"
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_missing_container_identity_aborts(self):
        self.s["containers"]["c" * 64] = {"Id": "c" * 64}
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_new_tag_preserved(self):
        self.s["fresh"] = {"id": image_id(31), "kind": "tag"}
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertEqual(self.removed(), [image_id(30)])

    def test_new_container_preserved(self):
        self.s["fresh"] = {"id": image_id(31), "kind": "container"}
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertEqual(self.removed(), [image_id(30)])

    def test_conflict_stops_and_retry_inventories_again(self):
        self.s["conflict"] = image_id(30)
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2, r.stdout)
        self.assertEqual(self.removed(), [image_id(31), image_id(30)])
        self.assertIn("removed=1", r.stdout)
        self.s.pop("conflict")
        self.add(32)
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertNotIn(image_id(32), self.s["images"])

    def test_docker_error_does_not_log_stderr(self):
        self.s["error"] = ["compose", "-f"]
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_timeout_before_deletion_and_lock_released(self):
        self.s["sleep"] = ["image", "ls"]
        start = time.monotonic()
        r = self.invoke("--apply", "--budget-seconds", "1")
        self.assertEqual(r.returncode, 2, r.stdout)
        self.assertLess(time.monotonic() - start, 1.8)
        self.assertEqual(self.removed(), [])
        with self.lock.open("r+") as f:
            fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        pid = int((self.root / "pid").read_text())
        with self.assertRaises(ProcessLookupError):
            os.kill(pid, 0)

    def test_timeout_mutation_outcome_requires_new_inventory(self):
        self.s["sleep"] = ["image", "rm"]
        self.s["daemon_remove"] = image_id(31)
        r = self.invoke("--apply", "--budget-seconds", "2")
        self.assertEqual(r.returncode, 2, r.stdout)
        self.assertIn("daemon outcome unknown", r.stdout)
        self.assertNotIn(image_id(31), self.s["images"])
        self.s.pop("sleep")
        self.s.pop("daemon_remove")
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)

    def test_overlap_no_inventory(self):
        with self.lock.open("w+") as f:
            fcntl.flock(f, fcntl.LOCK_EX)
            r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0)
        self.assertIn("lock overlap", r.stdout)
        self.assertEqual(self.removed(), [])

    def test_inherited_lock(self):
        r = self.invoke("--apply", fd=9)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(len(self.removed()), 2)

    def test_bad_inherited_descriptor(self):
        r = self.invoke("--apply", "--lock-fd", "9")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_empty_model_or_service_without_image_aborts(self):
        self.s["compose"] = {"build": {"build": "."}}
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])

    def test_null_reference_lists_are_docker_empty_shape(self):
        self.s["images"][image_id(30)]["RepoTags"] = None
        self.s["images"][image_id(30)]["RepoDigests"] = None
        r = self.invoke("--apply")
        self.assertEqual(r.returncode, 0, r.stdout)

    def test_unknown_argument_rejected(self):
        r = self.invoke("--force")
        self.assertEqual(r.returncode, 2)
        self.assertEqual(self.removed(), [])


unittest.main(argv=["image-retention"], verbosity=2)
PY