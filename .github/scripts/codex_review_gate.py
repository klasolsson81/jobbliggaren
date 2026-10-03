"""Publish a SHA-bound owner attestation; never infer approval from silence."""

from datetime import datetime, timezone
import hashlib
import json
import os
import re
import time
import urllib.request
from urllib.parse import urlparse


CHECK_NAME = "codex-review-gate"


def review_identity(repository, pr):
    target = json.dumps([repository, pr["base"]["ref"], pr["base"]["sha"]])
    return f"codex:{pr['number']}:{pr['head']['sha']}:{hashlib.sha256(target.encode()).hexdigest()}"


def codex_scope(repository, pr):
    return (pr["head"]["repo"] is not None and
            pr["head"]["repo"]["full_name"] == repository and
            (pr.get("codex_review_seen", False) or pr["head"]["ref"].startswith("codex/") or
             any(label["name"] == "codex-review" for label in pr.get("labels", []))))


def validate(inputs, repository, pr, permission, ref, default_branch):
    if ref != f"refs/heads/{default_branch}":
        raise ValueError("Run the trusted default-branch workflow only")
    if permission not in ("admin", "maintain", "write"):
        raise ValueError("A repository writer must attest the reports")
    if not re.fullmatch(r"[0-9a-f]{40}", inputs.get("head_sha", "")):
        raise ValueError("Supply the full reviewed head SHA")
    if pr["state"] != "open" or pr["head"]["sha"] != inputs["head_sha"]:
        raise ValueError("PR is closed or the reviewed head is stale")
    if pr["base"]["repo"]["full_name"] != repository:
        raise ValueError("Wrong repository")
    if pr["base"]["ref"] != default_branch:
        raise ValueError("The review gate only attests PRs targeting the default branch")
    if (not re.fullmatch(r"[0-9a-f]{40}", inputs.get("base_sha", "")) or
            inputs["base_sha"] != pr["base"]["sha"] or
            inputs.get("base_ref") != pr["base"]["ref"]):
        raise ValueError("Reviewed base branch or SHA is stale")
    if not codex_scope(repository, pr):
        raise ValueError("Attestation requires a same-repository Codex PR")
    if inputs.get("verdict") not in ("approved", "blocked"):
        raise ValueError("Verdict must be approved or blocked")
    if inputs["verdict"] == "approved":
        if inputs.get("attestation") != "both-complete-zero-medium-plus":
            raise ValueError("Explicit completed-report attestation required")
        for key in ("code_report", "security_report"):
            url = urlparse(inputs.get(key, ""))
            codex_report = url.hostname in ("chatgpt.com", "app.chatgpt.com") and url.path.startswith("/codex/")
            github_review = (key == "code_report" and url.hostname == "github.com" and
                             url.path == f"/{repository}/pull/{pr['number']}" and
                             re.fullmatch(r"(?:pullrequestreview|issuecomment)-[0-9]+", url.fragment))
            if (url.scheme != "https" or not (codex_report or github_review) or
                    url.username or url.password or url.port):
                raise ValueError(f"Supply a Codex report link: {key}")


def main():
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as event_file:
        event = json.load(event_file)
    repository = os.environ["GITHUB_REPOSITORY"]
    inputs = event.get("inputs", {})
    number = inputs.get("pr_number", str(event.get("pull_request", {}).get("number", "")))
    scheduled = os.environ["GITHUB_EVENT_NAME"] == "schedule"
    if not scheduled and not re.fullmatch(r"[1-9][0-9]*", number):
        raise ValueError("Invalid PR number")
    token = os.environ["GH_TOKEN"]
    checks_token = os.environ["CHECKS_TOKEN"]
    app_id = int(os.environ["CODEX_REVIEW_APP_ID"])
    if not checks_token or app_id <= 0 or app_id == 15368:
        raise ValueError("A dedicated GitHub App must publish the review gate")

    def api(path, data=None, method=None):
        request = urllib.request.Request(
            f"https://api.github.com/repos/{repository}/{path}",
            data=None if data is None else json.dumps(data).encode(),
            headers={"Authorization": f"Bearer {checks_token if 'check-runs' in path else token}",
                     "Accept": "application/vnd.github+json",
                     "X-GitHub-Api-Version": "2022-11-28"},
            method=method,
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)

    history_scopes = {}

    def include_scope_history(pr):
        if codex_scope(repository, pr) or not pr["head"]["repo"] or pr["head"]["repo"]["full_name"] != repository:
            return pr
        pr_number = pr["number"]
        if pr_number not in history_scopes:
            page = 1
            while True:
                events = api(f"issues/{pr_number}/events?per_page=100&page={page}")
                marked = any(event.get("event") == "labeled" and
                             event.get("label", {}).get("name") == "codex-review" for event in events)
                if marked or len(events) < 100:
                    history_scopes[pr_number] = marked
                    break
                page += 1
        pr["codex_review_seen"] = history_scopes[pr_number]
        return pr

    def read_current(pr_number):
        for attempt in range(5):
            pr = api(f"pulls/{pr_number}")
            if (pr["state"] != "open" or pr.get("mergeable") is False or
                    (pr.get("mergeable") is True and pr.get("merge_commit_sha")) or attempt == 4):
                return pr
            time.sleep(3)

    def read_pr():
        return include_scope_history(read_current(number))

    def targets(pr):
        merge_sha = pr.get("merge_commit_sha")
        if pr.get("mergeable") is not True or not isinstance(merge_sha, str) or not re.fullmatch(r"[0-9a-f]{40}", merge_sha):
            raise ValueError("Test merge is unavailable; retry after GitHub computes it")
        commit = api(f"commits/{merge_sha}")
        if [parent["sha"] for parent in commit["parents"]] != [pr["base"]["sha"], pr["head"]["sha"]]:
            raise ValueError("Test merge does not match current base and head")
        return [merge_sha, pr["head"]["sha"]]

    def publish(pr, body):
        if body.get("conclusion") != "failure":
            return [api("check-runs", body | {"name": CHECK_NAME, "head_sha": sha}) for sha in targets(pr)]
        # Negative results revoke known PR targets even when commit metadata is unavailable.
        shas = []
        merge_sha = pr.get("merge_commit_sha")
        if isinstance(merge_sha, str) and re.fullmatch(r"[0-9a-f]{40}", merge_sha) and merge_sha not in shas:
            shas.append(merge_sha)
        if pr["head"]["sha"] not in shas:
            shas.append(pr["head"]["sha"])
        checks, errors = [], []
        for sha in shas:
            for attempt in range(3):
                try:
                    checks.append(api("check-runs", body | {"name": CHECK_NAME, "head_sha": sha}))
                    break
                except Exception as error:
                    if not isinstance(error, OSError) or attempt == 2:
                        errors.append(error)
                        break
                    time.sleep(1)
        if errors:
            raise errors[0]
        return checks

    def require_same_snapshot(pr):
        current = include_scope_history(read_current(pr["number"]))
        if (codex_scope(repository, current) != codex_scope(repository, pr) or current["state"] != "open" or review_identity(repository, current) != review_identity(repository, pr) or
                current.get("merge_commit_sha") != pr.get("merge_commit_sha")):
            raise ValueError("PR changed while reconciling checks")

    def latest_attestation(sha):
        results = api(f"commits/{sha}/check-runs?check_name={CHECK_NAME}&per_page=100")
        trusted = [result for result in results["check_runs"] if result.get("app", {}).get("id") == app_id]
        return max(trusted, key=lambda result: result["id"], default=None)

    def handle_pr_event(pr):
        pr = include_scope_history(pr)
        if pr["state"] != "open":
            return
        if pr["base"]["ref"] != "main":
            raise ValueError("Only main-target PRs are supported")
        if codex_scope(repository, pr):
            latest = latest_attestation(pr["head"]["sha"])
            merge_sha = pr.get("merge_commit_sha")
            merge_latest = latest_attestation(merge_sha) if merge_sha else None
            expected = review_identity(repository, pr)
            if (all(result and result.get("external_id") == expected and result.get("conclusion") == "success" and
                    result.get("output", {}).get("summary") and result.get("details_url")
                    for result in (latest, merge_latest)) and
                    latest["output"]["summary"] == merge_latest["output"]["summary"] and
                    latest["details_url"] == merge_latest["details_url"]):
                require_same_snapshot(pr)
                current_results = (latest_attestation(pr["head"]["sha"]), latest_attestation(merge_sha))
                if current_results != (latest, merge_latest):
                    raise ValueError("Attestation changed during renewal")
                publish(pr, {"status": "completed", "conclusion": "success", "external_id": expected,
                             "details_url": latest["details_url"],
                             "output": {"title": "Existing review attestation renewed",
                                        "summary": latest["output"]["summary"],
                                        "text": f"Republished at {datetime.now(timezone.utc).isoformat()}; no new review was inferred."}})
                return
            publish(pr, {"status": "completed", "conclusion": "failure", "external_id": expected,
                         "output": {"title": "Waiting for Codex reports",
                                    "summary": "Owner must attest completed reports for the current head and base."}})
            return
        page = 1
        while True:
            siblings = api(f"pulls?state=all&per_page=100&page={page}")
            if any(sibling["head"]["sha"] == pr["head"]["sha"] and
                   codex_scope(repository, include_scope_history(sibling)) for sibling in siblings):
                raise ValueError("SHA is shared with a Codex PR; no exemption")
            if len(siblings) < 100:
                break
            page += 1
        require_same_snapshot(pr)
        publish(pr, {"status": "completed", "conclusion": "success",
                     "external_id": f"non-codex:{pr['number']}:{pr['head']['sha']}",
                     "output": {"title": "Not applicable: non-Codex branch",
                                "summary": "Existing CI and agents-done policy applies."}})

    def reconcile(pr):
        # Untrusted forks must not overwrite a same-repository commit's App status.
        if not pr["head"]["repo"] or pr["head"]["repo"]["full_name"] != repository:
            raise ValueError("Fork PRs need explicit review policy; no automatic exemption")
        try:
            handle_pr_event(pr)
        except Exception:
            # An Actions failure cannot replace this App's prior success on a reused SHA.
            publish(pr, {"status": "completed", "conclusion": "failure",
                         "external_id": review_identity(repository, pr),
                         "output": {"title": "Review policy could not be satisfied",
                                    "summary": "Resolve the workflow error before attesting current reports."}})
            raise

    if scheduled:
        page, failed = 1, []
        while True:
            prs = api(f"pulls?state=open&base=main&per_page=100&page={page}")
            for item in prs:
                if not item["head"]["repo"] or item["head"]["repo"]["full_name"] != repository:
                    continue
                try:
                    reconcile(read_current(item["number"]))
                except Exception:
                    failed.append(item["number"])
                    try:
                        publish(item, {"status": "completed", "conclusion": "failure",
                                       "external_id": review_identity(repository, item),
                                       "output": {"title": "Reconciliation failed",
                                                  "summary": "Current PR policy could not be verified."}})
                    except Exception:
                        print(f"Could not revoke every target for PR #{item['number']}")
            if len(prs) < 100:
                break
            page += 1
        if failed:
            raise ValueError(f"Reconciliation failed for PRs: {failed}")
        return
    try:
        pr = read_current(number)
    except Exception:
        snapshot = event.get("pull_request", {})
        if (os.environ["GITHUB_EVENT_NAME"] == "pull_request_target" and snapshot.get("state") == "open" and
                (snapshot.get("head", {}).get("repo") or {}).get("full_name") == repository and
                snapshot.get("base", {}).get("repo", {}).get("full_name") == repository):
            publish(snapshot, {"status": "completed", "conclusion": "failure",
                               "external_id": review_identity(repository, snapshot),
                               "output": {"title": "Current PR could not be read",
                                          "summary": "The event snapshot is revoked; retry current policy evaluation."}})
        raise
    if os.environ["GITHUB_EVENT_NAME"] == "pull_request_target" or inputs.get("verdict") == "refresh":
        reconcile(pr)
        return
    pr = include_scope_history(pr)
    permission = api(f"collaborators/{os.environ['GITHUB_ACTOR']}/permission")["permission"]
    validate(inputs, repository, pr, permission, os.environ["GITHUB_REF"],
             event["repository"]["default_branch"])
    run_url = f"https://github.com/{repository}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    checks = publish(pr, {"status": "in_progress", "details_url": run_url,
                          "external_id": review_identity(repository, pr)})
    try:
        # Re-read after creating both checks: moved head/base/test-merge never receives approval.
        current = read_pr()
        validate(inputs, repository, current, permission,
                 os.environ["GITHUB_REF"], event["repository"]["default_branch"])
        if current.get("merge_commit_sha") != pr.get("merge_commit_sha"):
            raise ValueError("Test merge changed during attestation")
        approved = inputs["verdict"] == "approved"
        summary = (f"PR #{number}, head {inputs['head_sha']}. "
                   f"Base {inputs['base_ref']} at {inputs['base_sha']}. "
                   f"Attested by {os.environ['GITHUB_ACTOR']} at {datetime.now(timezone.utc).isoformat()}. "
                   "This validates an authorized attestation, not report contents.\n\n"
                   f"Code report: {inputs.get('code_report', '')}\n\n"
                   f"Security report: {inputs.get('security_report', '')}")
        for check in checks:
            api(f"check-runs/{check['id']}", {
                "status": "completed", "conclusion": "success" if approved else "failure",
                "output": {"title": "Reports attested clean" if approved else "Review blocked",
                           "summary": summary}}, "PATCH")
    except Exception:
        for check in checks:
            api(f"check-runs/{check['id']}", {
                "status": "completed", "conclusion": "failure",
                "output": {"title": "Attestation failed", "summary": "Recheck current PR and reports."}}, "PATCH")
        raise


if __name__ == "__main__":
    main()
