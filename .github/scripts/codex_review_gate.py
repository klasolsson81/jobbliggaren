"""Publish a SHA-bound owner attestation; never infer approval from silence."""

import hashlib
import json
import os
import re
import urllib.request
from urllib.parse import urlparse


CHECK_NAME = "codex-review-gate"


def review_identity(repository, pr):
    target = json.dumps([repository, pr["base"]["ref"], pr["base"]["sha"]])
    return f"codex:{pr['number']}:{pr['head']['sha']}:{hashlib.sha256(target.encode()).hexdigest()}"


def codex_scope(repository, pr):
    return (pr["head"]["repo"] is not None and
            pr["head"]["repo"]["full_name"] == repository and
            (pr["head"]["ref"].startswith("codex/") or
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
                             re.fullmatch(r"pullrequestreview-[0-9]+", url.fragment))
            if (url.scheme != "https" or not (codex_report or github_review) or
                    url.username or url.password or url.port):
                raise ValueError(f"Supply a Codex report link: {key}")


def main():
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as event_file:
        event = json.load(event_file)
    repository = os.environ["GITHUB_REPOSITORY"]
    inputs = event.get("inputs", {})
    number = inputs.get("pr_number", str(event.get("pull_request", {}).get("number", "")))
    if not re.fullmatch(r"[1-9][0-9]*", number):
        raise ValueError("Invalid PR number")
    token = os.environ["GH_TOKEN"]

    def api(path, data=None, method=None):
        request = urllib.request.Request(
            f"https://api.github.com/repos/{repository}/{path}",
            data=None if data is None else json.dumps(data).encode(),
            headers={"Authorization": f"Bearer {token}",
                     "Accept": "application/vnd.github+json",
                     "X-GitHub-Api-Version": "2022-11-28"},
            method=method,
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)

    pr = api(f"pulls/{number}")
    if os.environ["GITHUB_EVENT_NAME"] == "pull_request_target":
        if pr["state"] != "open":
            return
        if codex_scope(repository, pr):
            results = api(f"commits/{pr['head']['sha']}/check-runs?check_name={CHECK_NAME}&per_page=100")
            latest = max(results["check_runs"], key=lambda result: result["id"], default=None)
            expected = review_identity(repository, pr)
            if (latest and latest.get("external_id") == expected and
                    latest.get("conclusion") == "success" and
                    latest.get("app", {}).get("slug") == "github-actions"):
                return
            api("check-runs", {"name": CHECK_NAME, "head_sha": pr["head"]["sha"],
                              "status": "completed", "conclusion": "failure",
                              "external_id": expected,
                              "output": {"title": "Waiting for Codex reports",
                                         "summary": "Owner must attest completed reports for the current head and base."}})
            return
        # A fork cannot mint an exemption for a commit also used by this repo.
        if not pr["head"]["repo"] or pr["head"]["repo"]["full_name"] != repository:
            raise ValueError("Fork PRs need explicit review policy; no automatic exemption")
        page = 1
        while True:
            siblings = api(f"pulls?state=open&per_page=100&page={page}")
            if any(codex_scope(repository, sibling) and
                   sibling["head"]["sha"] == pr["head"]["sha"] for sibling in siblings):
                raise ValueError("SHA is shared with a Codex PR; no exemption")
            if len(siblings) < 100:
                break
            page += 1
        api("check-runs", {"name": CHECK_NAME, "head_sha": pr["head"]["sha"],
                          "status": "completed", "conclusion": "success",
                          "external_id": f"non-codex:{number}:{pr['head']['sha']}",
                          "output": {"title": "Not applicable: non-Codex branch",
                                     "summary": "Existing CI and agents-done policy applies."}})
        return
    permission = api(f"collaborators/{os.environ['GITHUB_ACTOR']}/permission")["permission"]
    validate(inputs, repository, pr, permission, os.environ["GITHUB_REF"],
             event["repository"]["default_branch"])
    run_url = f"https://github.com/{repository}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    check = api("check-runs", {"name": CHECK_NAME, "head_sha": inputs["head_sha"],
                              "status": "in_progress", "details_url": run_url,
                              "external_id": review_identity(repository, pr)})
    try:
        # Re-read after creating the check: a moved head never receives approval.
        validate(inputs, repository, api(f"pulls/{number}"), permission,
                 os.environ["GITHUB_REF"], event["repository"]["default_branch"])
        approved = inputs["verdict"] == "approved"
        summary = (f"PR #{number}, head {inputs['head_sha']}. "
                   f"Base {inputs['base_ref']} at {inputs['base_sha']}. "
                   f"Attested by {os.environ['GITHUB_ACTOR']}. "
                   "This validates an authorized attestation, not report contents.\n\n"
                   f"Code report: {inputs.get('code_report', '')}\n\n"
                   f"Security report: {inputs.get('security_report', '')}")
        api(f"check-runs/{check['id']}", {
            "status": "completed", "conclusion": "success" if approved else "failure",
            "output": {"title": "Reports attested clean" if approved else "Review blocked",
                       "summary": summary}}, "PATCH")
    except Exception:
        api(f"check-runs/{check['id']}", {
            "status": "completed", "conclusion": "failure",
            "output": {"title": "Attestation failed", "summary": "Recheck current PR and reports."}}, "PATCH")
        raise


if __name__ == "__main__":
    main()
