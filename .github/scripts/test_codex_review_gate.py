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
        self.pr = {"number": 123, "state": "open", "merge_commit_sha": "e" * 40, "head": {"sha": "a" * 40,
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

    def publication(self, pr=None):
        pr = pr or self.pr
        return [{"parents": [{"sha": pr["base"]["sha"]}, {"sha": pr["head"]["sha"]}]}, {"id": 2}, {"id": 1}]

    def refusal(self):
        return [{"id": 1}, {"id": 2}]

    def approval(self):
        return {"id": 5, "external_id": review_identity(self.repo, self.pr),
                "conclusion": "success", "app": {"id": 98765},
                "details_url": "https://github.com/owner/repo/actions/runs/42",
                "output": {"summary": "Original writer, reports and attestation time"}}

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
        calls = self.run_event([self.pr, {"permission": "write"}, *self.publication(), self.pr, {}, {}])
        self.assertEqual(self.inputs["head_sha"], calls[4][2]["head_sha"])
        self.assertTrue(calls[4][2]["external_id"].startswith("codex:123:" + self.inputs["head_sha"] + ":"))
        self.assertEqual("success", calls[-1][2]["conclusion"])
        self.assertEqual({self.pr["head"]["sha"], self.pr["merge_commit_sha"]},
                         {body["head_sha"] for method, _, body in calls if method == "POST"})
        self.assertTrue(all(token == "Bearer test-checks-token" for token, (_, url, _) in
                            zip(self.tokens, calls) if "check-runs" in url))

    def test_move_between_check_creation_and_completion_fails_check(self):
        moved = copy.deepcopy(self.pr)
        moved["head"]["sha"] = "b" * 40
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, *self.publication(), moved, {}, {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_report_api_failure_never_becomes_success(self):
        with self.assertRaises(OSError):
            self.run_event([self.pr, {"permission": "write"}, *self.publication(), OSError("offline"), {}, {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_cc_branch_exemption_needs_no_native_reports(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        calls = self.run_event([cc, [], [cc], cc, *self.publication()], "pull_request_target")
        self.assertEqual("success", calls[-1][2]["conclusion"])
        self.assertTrue(calls[-1][2]["external_id"].startswith("non-codex:"))

    def test_codex_push_does_not_issue_an_exemption(self):
        calls = self.run_event([self.pr, {"check_runs": []},*self.refusal()], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_codex_label_covers_nonstandard_branch_name(self):
        labeled = copy.deepcopy(self.pr)
        labeled["head"]["ref"] = "refactor/other-convention"
        labeled["labels"] = [{"name": "codex-review"}]
        self.assertTrue(codex_scope(self.repo, labeled))
        calls = self.run_event([labeled, {"check_runs": []},*self.refusal()], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_full_revocation_publishes_failure(self):
        self.inputs = {"head_sha": "a" * 40, "base_sha": "c" * 40, "base_ref": "main", "verdict": "blocked"}
        calls = self.run_event([self.pr, {"permission": "write"}, *self.publication(), self.pr, {}, {}])
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_unrelated_label_event_preserves_current_attestation(self):
        result = self.approval()
        calls = self.run_event([self.pr, {"check_runs": [result]}, self.pr,
                               {"check_runs": [result]}, *self.publication()], "pull_request_target")
        self.assertEqual("success", calls[-1][2]["conclusion"])
        self.assertEqual(result["output"]["summary"], calls[-1][2]["output"]["summary"])

    def test_prior_exemption_or_another_pr_attestation_is_not_codex_approval(self):
        for external_id in ("non-codex:123:", "codex:456:"):
            result = {"id": 5, "external_id": external_id + "a" * 40,
                      "conclusion": "success", "app": {"id": 98765}}
            calls = self.run_event([self.pr, {"check_runs": [result]},*self.refusal()], "pull_request_target")
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
            self.run_event([cc, [], [cc] * 100, [self.pr],*self.refusal()], "pull_request_target")
        self.assertTrue(self.requests[-3][1].endswith("page=2"))
        self.assert_failure_replaces_prior_approval()

    def test_retarget_or_base_advance_refuses_old_attestation(self):
        for key, value in (("ref", "release"), ("sha", "d" * 40)):
            moved = copy.deepcopy(self.pr)
            moved["base"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check(pr=moved)
            result = {"id": 5, "external_id": review_identity(self.repo, self.pr),
                      "conclusion": "success", "app": {"id": 98765}}
            if key == "ref":
                with self.assertRaises(ValueError):
                    self.run_event([moved, *self.refusal()], "pull_request_target")
                self.assert_failure_replaces_prior_approval()
            else:
                calls = self.run_event([moved, {"check_runs": [result]}, *self.refusal()], "pull_request_target")
                self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_base_moves_during_attestation_never_publishes_success(self):
        moved = copy.deepcopy(self.pr)
        moved["base"]["sha"] = "d" * 40
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, *self.publication(), moved, {}, {}])
        self.assertEqual("failure", self.requests[-1][2]["conclusion"])

    def test_nondefault_target_cannot_receive_an_attestation(self):
        other_base = copy.deepcopy(self.pr)
        other_base["base"]["ref"] = "stacked-parent"
        self.inputs["base_ref"] = "stacked-parent"
        with self.assertRaises(ValueError):
            self.run_event([other_base, {"permission": "write"}])
        self.assertTrue(all(method == "GET" for method, _, _ in self.requests))

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
        calls = self.run_event([self.pr, {"check_runs": [spoof]}, *self.refusal()], "pull_request_target")
        self.assertEqual("failure", calls[-1][2]["conclusion"])
        trusted = self.approval()
        calls = self.run_event([self.pr, {"check_runs": [trusted, spoof]}, self.pr,
                               {"check_runs": [trusted, spoof]}, *self.publication()], "pull_request_target")
        self.assertEqual("success", calls[-1][2]["conclusion"])

    def test_missing_publisher_or_actions_identity_fails_before_api_calls(self):
        for settings in ({"CHECKS_TOKEN": ""}, {"CODEX_REVIEW_APP_ID": "15368"},
                         {"CODEX_REVIEW_APP_ID": ""}, {"CODEX_REVIEW_APP_ID": "0"}):
            with self.subTest(settings=settings), self.assertRaises(ValueError):
                self.run_event([], env_overrides=settings)
            self.assertEqual([], self.requests)

    def test_removed_codex_label_remains_scoped_across_history_pages(self):
        removed = copy.deepcopy(self.pr)
        removed["head"]["ref"] = "refactor/nonstandard"
        events = [{"event": "labeled", "label": {"name": "codex-review"}}]
        calls = self.run_event([removed, [{"event": "commented"}] * 100, events,
                                {"check_runs": []}, *self.refusal()], "pull_request_target")
        self.assertIn("page=2", calls[2][1])
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_removed_label_on_same_sha_sibling_blocks_exemption(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        sibling = copy.deepcopy(cc)
        sibling["number"] = 456
        events = [{"event": "labeled", "label": {"name": "codex-review"}}]
        with self.assertRaises(ValueError):
            self.run_event([cc, [], [cc, sibling], events,*self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_unavailable_scope_history_cannot_mint_an_exemption(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        with self.assertRaises(OSError):
            self.run_event([cc, OSError("offline"),*self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def assert_failure_replaces_prior_approval(self):
        method, url, body = self.requests[-1]
        self.assertEqual("POST", method)
        self.assertTrue(url.endswith("/check-runs"))
        self.assertEqual("codex-review-gate", body["name"])
        self.assertEqual({self.pr["head"]["sha"], self.pr["merge_commit_sha"]},
                         {request[2]["head_sha"] for request in self.requests[-2:]})
        self.assertEqual("failure", body["conclusion"])
        self.assertEqual("Bearer test-checks-token", self.tokens[-1])

    def test_unavailable_sibling_scan_replaces_prior_app_approval(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        with self.assertRaises(OSError):
            self.run_event([cc, [], OSError("offline"),*self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_unavailable_prior_checks_replaces_prior_app_approval(self):
        with self.assertRaises(OSError):
            self.run_event([self.pr, OSError("offline"),*self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_closed_codex_pr_still_blocks_same_sha_exemption(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        previous = copy.deepcopy(self.pr)
        previous.update(number=122, state="closed")
        with self.assertRaises(ValueError):
            self.run_event([cc, [], [cc, previous],*self.refusal()], "pull_request_target")
        self.assertIn("pulls?state=all&", self.requests[-3][1])
        self.assert_failure_replaces_prior_approval()

    def test_merge_commit_with_wrong_parents_never_receives_approval(self):
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, {"parents": [{"sha": "f" * 40}]}])
        self.assertTrue(all(method == "GET" for method, _, _ in self.requests))

    def test_uncomputed_merge_is_retried_before_attestation(self):
        pending = self.pr | {"merge_commit_sha": None, "mergeable": None}
        with patch("codex_review_gate.time.sleep") as sleep:
            calls = self.run_event([pending, self.pr, {"permission": "write"},
                                   *self.publication(), self.pr, {}, {}])
        sleep.assert_called_once_with(2)
        self.assertEqual("success", calls[-1][2]["conclusion"])

    def test_missing_merge_after_bounded_retry_never_receives_approval(self):
        pending = self.pr | {"merge_commit_sha": None, "mergeable": None}
        with patch("codex_review_gate.time.sleep") as sleep, self.assertRaises(ValueError):
            self.run_event([pending, pending, pending, {"permission": "write"}])
        self.assertEqual(2, sleep.call_count)
        self.assertTrue(all(method == "GET" for method, _, _ in self.requests))

    def test_merge_changes_during_attestation_fails_both_publications(self):
        moved = self.pr | {"merge_commit_sha": "f" * 40}
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"permission": "write"}, *self.publication(), moved, {}, {}])
        self.assertEqual(2, len([body for method, _, body in self.requests
                               if method == "PATCH" and body["conclusion"] == "failure"]))

    def test_daily_renewal_preserves_old_evidence_on_head_and_merge(self):
        old = self.approval() | {"completed_at": "2020-01-01T00:00:00Z"}
        calls = self.run_event([[self.pr], self.pr, {"check_runs": [old]}, self.pr,
                               {"check_runs": [old]}, *self.publication()], "schedule")
        posts = [body for method, _, body in calls if method == "POST"]
        self.assertEqual({self.pr["head"]["sha"], self.pr["merge_commit_sha"]}, {p["head_sha"] for p in posts})
        self.assertTrue(all(p["output"]["summary"] == old["output"]["summary"] for p in posts))
        self.assertTrue(all(p["conclusion"] == "success" for p in posts))

    def test_daily_renewal_never_resurrects_an_older_success(self):
        old = self.approval()
        blocked = old | {"id": 6, "conclusion": "failure"}
        calls = self.run_event([[self.pr], self.pr, {"check_runs": [old, blocked]},
                               *self.refusal()], "schedule")
        self.assertEqual("failure", calls[-1][2]["conclusion"])

    def test_revocation_during_renewal_publishes_failure_on_both_targets(self):
        old = self.approval()
        blocked = old | {"id": 6, "conclusion": "failure"}
        with self.assertRaises(ValueError):
            self.run_event([self.pr, {"check_runs": [old]}, self.pr,
                            {"check_runs": [blocked]}, *self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_daily_refresh_recalculates_cc_exemption(self):
        cc = copy.deepcopy(self.pr)
        cc["head"]["ref"] = "fix/cc-example"
        calls = self.run_event([[cc], cc, [], [cc], cc, *self.publication()], "schedule")
        self.assertEqual("success", calls[-1][2]["conclusion"])
        self.assertTrue(calls[-1][2]["external_id"].startswith("non-codex:"))

    def test_partial_attestation_completion_failure_revokes_both_checks(self):
        with self.assertRaises(OSError):
            self.run_event([self.pr, {"permission": "write"}, *self.publication(), self.pr,
                            {}, OSError("head update failed"), {}, {}])
        self.assertEqual(["failure", "failure"], [body["conclusion"] for _, _, body in self.requests[-2:]])

    def test_metadata_failure_revokes_existing_approval_without_another_commit_read(self):
        old = self.approval()
        with self.assertRaises(OSError):
            self.run_event([self.pr, {"check_runs": [old]}, self.pr, {"check_runs": [old]},
                            OSError("commit metadata unavailable"), *self.refusal()], "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_failure_on_one_negative_target_still_attempts_the_other(self):
        with self.assertRaises(OSError):
            self.run_event([self.pr, OSError("check read failed"), OSError("head write failed"), {}],
                           "pull_request_target")
        self.assert_failure_replaces_prior_approval()

    def test_global_queue_preserves_pending_revocations_and_other_prs(self):
        workflow = (Path(__file__).parent.parent / "workflows" / "codex-review-gate.yml").read_text(encoding="utf-8")
        concurrency = workflow.split("concurrency:\n", 1)[1].split("\njobs:", 1)[0]
        self.assertIn("queue: max", concurrency)
        self.assertIn("cancel-in-progress: false", concurrency)


if __name__ == "__main__":
    unittest.main()
