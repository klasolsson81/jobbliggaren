import copy
import io
import json
from pathlib import Path
import re
import unittest
from unittest.mock import patch

from codex_review_gate import codex_scope, main, review_identity, validate


class ReviewAttestationTests(unittest.TestCase):
    def setUp(self):
        self.repo = "owner/repo"
        self.inputs = {"head_sha": "a" * 40, "base_ref": "main", "base_sha": "c" * 40, "verdict": "approved",
                       "attestation": "both-complete-zero-medium-plus",
                       "code_report": "https://chatgpt.com/codex/tasks/code",
                       "security_report": "https://chatgpt.com/codex/tasks/security"}
        self.pr = {"number": 123, "state": "open", "head": {"sha": "a" * 40,
                   "ref": "codex/fix/example", "repo": {"full_name": self.repo}},
                   "base": {"ref": "main", "sha": "c" * 40, "repo": {"full_name": self.repo}}}

    def check(self, inputs=None, pr=None, permission="write", ref="refs/heads/main"):
        validate(self.inputs if inputs is None else inputs, self.repo,
                 self.pr if pr is None else pr, permission, ref, "main")

    def test_explicit_current_head_writer_attestation_is_admitted(self):
        self.check()

    def test_new_content_and_pure_base_merge_both_invalidate_old_sha(self):
        # Both GitHub push forms change head.sha; neither inherits old evidence.
        changed = copy.deepcopy(self.pr)
        changed["head"]["sha"] = "b" * 40
        with self.assertRaises(ValueError):
            self.check(pr=changed)

    def test_missing_completion_or_report_is_not_approval(self):
        for key in ("attestation", "code_report", "security_report", "head_sha", "base_ref", "base_sha"):
            with self.subTest(key=key):
                incomplete = self.inputs.copy()
                incomplete.pop(key)
                with self.assertRaises(ValueError):
                    self.check(inputs=incomplete)

    def test_untrusted_actor_and_workflow_ref_are_refused(self):
        for permission in ("read", "triage", "none"):
            with self.subTest(permission=permission), self.assertRaises(ValueError):
                self.check(permission=permission)
        with self.assertRaises(ValueError):
            self.check(ref="refs/heads/codex/fix/example")

    def test_fork_and_cc_cannot_use_codex_attestation(self):
        for ref, repository in (("fix/example", self.repo), ("codex/fix/x", "fork/repo")):
            changed = copy.deepcopy(self.pr)
            changed["head"].update(ref=ref, repo={"full_name": repository})
            self.assertFalse(codex_scope(self.repo, changed))
            with self.assertRaises(ValueError):
                self.check(pr=changed)

    def test_closed_pr_and_wrong_repository_refused(self):
        changed = copy.deepcopy(self.pr)
        changed["state"] = "closed"
        with self.assertRaises(ValueError):
            self.check(pr=changed)
        changed = copy.deepcopy(self.pr)
        changed["base"]["repo"]["full_name"] = "another/repo"
        with self.assertRaises(ValueError):
            self.check(pr=changed)

    def test_invalid_verdict_and_untrusted_links_refused(self):
        for key, value in (("verdict", "unknown"), ("code_report", "https://evil.test/report"),
                           ("security_report", "https://chatgpt.com@evil.test/report"),
                           ("code_report", "https://user:pass@chatgpt.com/report")):
            changed = self.inputs | {key: value}
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.check(inputs=changed)

    def test_revocation_needs_no_clean_report_claim(self):
        self.check(inputs={"head_sha": "a" * 40, "base_sha": "c" * 40, "base_ref": "main", "verdict": "blocked"})

    def run_event(self, responses, event_name="workflow_dispatch", env_overrides=None):
        event = {"inputs": self.inputs | {"pr_number": "123"},
                 "repository": {"default_branch": "main"}, "pull_request": {"number": 123}}
        requests = []
        tokens = []

        def request_api(request, timeout):
            tokens.append(request.get_header("Authorization"))
            requests.append((request.get_method(), request.full_url,
                             None if request.data is None else json.loads(request.data)))
            result = responses.pop(0)
            if isinstance(result, Exception):
                raise result
            return io.BytesIO(json.dumps(result).encode())

        env = {"GITHUB_EVENT_PATH": "event.json", "GITHUB_REPOSITORY": self.repo,
               "GH_TOKEN": "test-token", "CHECKS_TOKEN": "test-checks-token",
               "CODEX_REVIEW_APP_ID": "98765", "GITHUB_ACTOR": "owner",
               "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ID": "42",
               "GITHUB_EVENT_NAME": event_name}
        env.update(env_overrides or {})
        with patch.dict("os.environ", env), patch("builtins.open", return_value=io.StringIO(json.dumps(event))), \
                patch("urllib.request.urlopen", side_effect=request_api):
            try:
                main()
            finally:
                self.requests = requests
                self.tokens = tokens
        self.assertEqual([], responses)
        return requests

    def test_full_attestation_writes_check_to_reviewed_head(self):
        calls = self.run_event([self.pr, {"permission": "write"}, {"id": 1}, self.pr, {}])
        self.assertEqual(self.inputs["head_sha"], calls[2][2]["head_sha"])
        self.assertTrue(calls[2][2]["external_id"].startswith("codex:123:" + self.inputs["head_sha"] + ":"))
        self.assertEqual("success", calls[4][2]["conclusion"])
        self.assertEqual(["Bearer test-token", "Bearer test-token", "Bearer test-checks-token",
                          "Bearer test-token", "Bearer test-checks-token"], self.tokens)

    def test_move_between_check_creation_and_completion_fails_check(self):
        moved = copy.deepcopy(self.pr)
        moved["head"]["sha"] = "b" * 40
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, {"id": 1}, moved, {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_report_api_failure_never_becomes_success(self):
        with self.assertRaises(OSError):
            self.run_event([self.pr, {"permission": "write"}, {"id": 1}, OSError("offline"), {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_cc_branch_exemption_needs_no_native_reports(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        calls = self.run_event([cc, [cc], {}], "pull_request_target")
        self.assertEqual("success", calls[-1][2]["conclusion"])
        self.assertTrue(calls[-1][2]["external_id"].startswith("non-codex:"))

    def test_codex_push_does_not_issue_an_exemption(self):
        calls = self.run_event([self.pr, {"check_runs": []}, {}], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_codex_label_covers_nonstandard_branch_name(self):
        labeled = copy.deepcopy(self.pr)
        labeled["head"]["ref"] = "refactor/other-convention"
        labeled["labels"] = [{"name": "codex-review"}]
        self.assertTrue(codex_scope(self.repo, labeled))
        calls = self.run_event([labeled, {"check_runs": []}, {}], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_full_revocation_publishes_failure(self):
        self.inputs = {"head_sha": "a" * 40, "base_sha": "c" * 40, "base_ref": "main", "verdict": "blocked"}
        calls = self.run_event([self.pr, {"permission": "write"}, {"id": 1}, self.pr, {}])
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_unrelated_label_event_preserves_current_attestation(self):
        result = {"id": 5, "external_id": review_identity(self.repo, self.pr),
                  "conclusion": "success", "app": {"id": 98765}}
        calls = self.run_event([self.pr, {"check_runs": [result]}], "pull_request_target")
        self.assertTrue(all(method == "GET" for method, _, _ in calls))

    def test_prior_exemption_or_another_pr_attestation_is_not_codex_approval(self):
        for external_id in ("non-codex:123:", "codex:456:"):
            result = {"id": 5, "external_id": external_id + "a" * 40,
                      "conclusion": "success", "app": {"id": 98765}}
            calls = self.run_event([self.pr, {"check_runs": [result]}, {}], "pull_request_target")
            self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_fork_cannot_issue_an_exemption(self):
        fork = copy.deepcopy(self.pr)
        fork["head"]["repo"]["full_name"] = "fork/repo"
        with self.assertRaises(ValueError):
            self.run_event([fork], "pull_request_target")
        self.assertTrue(all(method == "GET" for method, _, _ in self.requests))

    def test_same_sha_codex_on_second_page_blocks_cc_exemption(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        with self.assertRaises(ValueError):
            self.run_event([cc, [cc] * 100, [self.pr]], "pull_request_target")
        self.assertTrue(self.requests[-1][1].endswith("page=2"))
        self.assertTrue(all(method == "GET" for method, _, _ in self.requests))

    def test_retarget_or_base_advance_refuses_old_attestation(self):
        for key, value in (("ref", "release"), ("sha", "d" * 40)):
            moved = copy.deepcopy(self.pr)
            moved["base"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check(pr=moved)
            result = {"id": 5, "external_id": review_identity(self.repo, self.pr),
                      "conclusion": "success", "app": {"id": 98765}}
            calls = self.run_event([moved, {"check_runs": [result]}, {}], "pull_request_target")
            self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_base_moves_during_attestation_never_publishes_success(self):
        moved = copy.deepcopy(self.pr)
        moved["base"]["sha"] = "d" * 40
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, {"id": 1}, moved, {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_native_github_code_report_is_scoped_to_this_pr(self):
        self.check(inputs=self.inputs | {"code_report": "https://github.com/owner/repo/pull/123#pullrequestreview-1"})
        self.check(inputs=self.inputs | {"code_report": "https://github.com/owner/repo/pull/123#issuecomment-1"})
        for url in ("https://github.com/owner/repo/pull/456#pullrequestreview-1",
                    "https://github.com/other/repo/pull/123#pullrequestreview-1",
                    "https://github.com/owner/repo/pull/123#discussion-1"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                self.check(inputs=self.inputs | {"code_report": url})

    def test_workflow_receives_retarget_events(self):
        workflow = (Path(__file__).parent.parent / "workflows" / "codex-review-gate.yml").read_text(encoding="utf-8")
        trigger = re.search(r"pull_request_target:\s+types:\s*\[([^]]+)\]", workflow)
        self.assertIsNotNone(trigger)
        self.assertIn("edited", {event.strip() for event in trigger.group(1).split(",")})

    def test_actions_check_cannot_impersonate_the_publisher(self):
        spoof = {"id": 99, "external_id": review_identity(self.repo, self.pr),
                 "conclusion": "success", "app": {"id": 15368}}
        calls = self.run_event([self.pr, {"check_runs": [spoof]}, {}], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])
        trusted = spoof | {"id": 5, "app": {"id": 98765}}
        calls = self.run_event([self.pr, {"check_runs": [trusted, spoof]}], "pull_request_target")
        self.assertTrue(all(method == "GET" for method, _, _ in calls))

    def test_missing_publisher_or_actions_identity_fails_before_api_calls(self):
        for settings in ({"CHECKS_TOKEN": ""}, {"CODEX_REVIEW_APP_ID": "15368"},
                         {"CODEX_REVIEW_APP_ID": ""}, {"CODEX_REVIEW_APP_ID": "0"}):
            with self.subTest(settings=settings), self.assertRaises(ValueError):
                self.run_event([], env_overrides=settings)
            self.assertEqual([], self.requests)


if __name__ == "__main__":
    unittest.main()
