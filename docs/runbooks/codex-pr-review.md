# Codex PR review

This policy applies to work driven by Codex. Create each new Codex PR with the
`codex-review` label and use a fresh `codex/` branch. The label also identifies
Codex work where a different branch convention is explicitly requested.
CC keeps its existing branch conventions, CI and mandatory-agent process.
The label or prefix expresses the trusted owner's policy, not detected authorship.
Keep the scope marker for the entire PR. Do not switch policy on an existing PR
without coordinating with its driving session; existing unmarked PRs predate this policy.
Never reuse a reviewed or exempt commit as the head of a different PR.

## Repository settings

In ChatGPT Settings → Code review → `klasolsson81/jobbliggaren`, use exhaustive
code review and Medium minimum severity for manual Security Review. Configure
`docs/threat-model.md` as the threat-model path after the file reaches main.
Repository-wide automatic triggers must be off for both review types: the
native PR selectors cannot distinguish CC and Codex using the same GitHub user.
The Codex driving session supplies the triggers below. This is session-driven
automation; it does not run an unattended coding worker after the session stops.

## Same-PR loop

1. Push and create the PR with `automerge` and `codex-review`, without `agents-done`.
2. On creation and after each changed head (including a pure base merge), the
   Codex driving session posts `@codex review` and `@codex security review` as
   separate comments. Never interpret an old review as covering a new commit.
3. Read both completed reports and their reviewed SHA. Follow the report links
   in Codex, including the full Security Report. A reaction, empty comment list,
   timeout, rate limit, failed task or missing access is not a clean result.
4. Fix every valid Medium/High/Critical security finding and every reported
   P0/P1/P2 code defect in this PR. Deduplicate overlapping reports. If a finding
   is false, record the evidence and obtain reviewer confirmation; do not simply
   lower its severity. Run relevant tests, push the batch, then repeat step 2.
   Klas's 2026-10-03 directive permits repeated external rounds until clean;
   the local mandatory panel keeps its separate §9.6 process. Never defer a
   valid Medium+ finding to make this PR mergeable.
5. Complete the local mandatory panel, keeping `agents-done`'s existing meaning.
   On the final head, dispatch `codex-review-gate.yml` from **main** with the PR
   number, full head SHA, both report URLs, `verdict=approved` and
   `attestation=both-complete-zero-medium-plus`. This explicitly attests that both
   reports completed on that SHA and no blocking findings remain. The workflow
   verifies the writer's authority and commit, not the reports' contents.
6. Read back the green `codex-review-gate` for that head, then set `agents-done`
   and watch CI/merge as usual. A new SHA needs a new external attestation.
   To revoke a result, first disable auto-merge and remove `agents-done`, then
   dispatch the same workflow for the current SHA with `verdict=blocked`.

Native Code Review documents P0/P1 reporting; it is not a promise of complete
P2 coverage. The local review panel remains necessary. Exhaustive review seeks
additional findings; the driving session still owns fixes and re-review.

## Enforcement and commissioning

Branch protection requires `ci` plus our own `codex-review-gate` check from
GitHub Actions. This is not the name of a native Codex service check.
The trusted default-branch workflow exempts same-repository PRs with neither
the `codex-review` label nor the `codex/` prefix.
It refuses exemptions for forks and for SHAs currently shared with a Codex PR.
Writers must preserve the fresh-branch/unique-head convention above; GitHub
checks are SHA-bound, not cryptographic proof of which tool wrote a PR.

Install the workflow through a normally reviewed PR first. Before requiring
the new check, verify an actual Codex report pair, a clean attestation, rejection
of a stale head, and a CC exemption. Do not claim enforcement is active until
the branch-protection API reads both required checks back. Existing CC PRs need
an exemption run before enabling the requirement. Keep unavailable native
review evidence as an explicit open commissioning dependency.

## References

- [Native code review](https://learn.chatgpt.com/docs/third-party/github)
- [Native security review](https://learn.chatgpt.com/docs/security/security-review)
- [Threat model](../threat-model.md)
- [ADR 0065](../decisions/0065-pr-flow-restoration-with-ci-gate.md)
