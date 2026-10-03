# Codex PR review

This policy applies to work driven by Codex. Create each new Codex PR with the
`codex-review` label and use a fresh `codex/` branch. The label also identifies
Codex work where a different branch convention is explicitly requested.
CC keeps its existing branch conventions, CI and mandatory-agent process.
The label or prefix expresses the trusted owner's policy, not detected authorship.
Keep the scope marker for the entire PR. Do not switch policy on an existing PR
without coordinating with its driving session; existing unmarked PRs predate this policy.
The gate also reads label-event history: once labeled `codex-review`, a PR stays
in scope even after label removal. Unavailable history cannot grant an exemption.
Never reuse a reviewed or exempt commit as the head of a different PR.
The enforced target is `main`, which must remain the default branch. Stacked
PRs may be reviewed earlier, but receive a merge attestation only after targeting
main and completing reviews against that head/base. Other target branches do not
run the publisher. Invalidation is event-driven; GitHub does not atomically
compare our base fingerprint at the instant of merge. Strict up-to-date protection
remains required alongside the publisher check.

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
2. On creation, each changed head (including a pure base merge), or changed base, the
   Codex driving session posts `@codex review` and `@codex security review` as
   separate comments. Never interpret an old review as covering a new commit.
3. Record the head SHA, base branch and base SHA before requesting review; verify
   they are unchanged when reading both completed reports. Follow the report links
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
   number, full head SHA, `base_ref`, `base_sha`, both report URLs, `verdict=approved` and
   `attestation=both-complete-zero-medium-plus`. This explicitly attests that both
   reports completed against that head/base and no blocking findings remain. A
   code report can use its GitHub review or no-findings comment URL; the Security Report
   uses its Codex task URL. The workflow
   verifies the writer's authority and commit, not the reports' contents.
6. Read back the green `codex-review-gate` for that head, then set `agents-done`
   and watch CI/merge as usual. A new head or base needs a new external attestation.
   To revoke a result, first disable auto-merge and remove `agents-done`, then
   dispatch the same workflow for the current SHA with `verdict=blocked`.

Native Code Review documents P0/P1 reporting; it is not a promise of complete
P2 coverage. The local review panel remains necessary. Exhaustive review seeks
additional findings; the driving session still owns fixes and re-review.

## Enforcement and commissioning

Branch protection requires `ci` from GitHub Actions plus our own
`codex-review-gate` check from a dedicated GitHub App, pinned by numeric App ID.
This is not the name of a native Codex service check. A PR-controlled Actions job
can copy a check name, so `github-actions` must never be its accepted publisher.
The trusted default-branch workflow exempts same-repository PRs with neither
the `codex/` prefix nor a current or historical `codex-review` label.
It refuses exemptions for forks and for SHAs shared with an open or closed Codex PR.
Writers must preserve the fresh-branch/unique-head convention above; GitHub
checks are SHA-bound, not cryptographic proof of which tool wrote a PR.

Install the workflow through a normally reviewed PR first. Before requiring
the new check, verify an actual Codex report pair, a clean attestation, rejection
of a stale head, and a CC exemption. Also verify that a successful Actions job
with the same name cannot satisfy the requirement and that a PR job requesting
the publisher environment is denied access. Do not claim enforcement is active until
the branch-protection API reads both required checks back. Existing CC PRs need
an exemption run before enabling the requirement. Keep unavailable native
review evidence as an explicit open commissioning dependency.

### Dedicated publisher setup

Register a private GitHub App owned by the repository owner, installed only on
`jobbliggaren`. Disable its webhook; it needs no server or OAuth callback.
Grant only repository **Checks: read and write**, with the mandatory Metadata
read permission. The workflow's ordinary `GITHUB_TOKEN` reads PRs and writer
permissions; the App installation token reads and publishes checks only.

Create environment `codex-review-publisher` with **Selected branches and tags**
and exactly one **branch** rule: `main`. Add no tag or PR-ref rules; do not use
the broader protected-branches option. Configure this restriction before storing
any key. Store environment variables `CODEX_REVIEW_APP_ID` (numeric App ID) and
`CODEX_REVIEW_APP_CLIENT_ID`, and environment secret
`CODEX_REVIEW_APP_PRIVATE_KEY`. Never store the key as a repository secret,
commit it, log it, or paste it into chat. Generate and install credentials through
the owner's secure GitHub setup flow.

The SHA-pinned token action scopes its short-lived token to this repository and
revokes it after the job. Only trusted default-branch code runs with the key;
no PR checkout, artifacts, caches, or scripts enter the publisher job. Repository
administrators and existing main workflows remain trusted. Missing credentials
must fail without publishing a fallback success.

Before activation, read back the environment's exact branch policy and verify
the check response's `app.id` equals `CODEX_REVIEW_APP_ID`. Configure required
checks as `ci` with App ID `15368` and `codex-review-gate` with the dedicated
App's ID, preserving strict up-to-date checks and all other protections.

## References

- [Native code review](https://learn.chatgpt.com/docs/third-party/github)
- [Native security review](https://learn.chatgpt.com/docs/security/security-review)
- [Threat model](../threat-model.md)
- [ADR 0065](../decisions/0065-pr-flow-restoration-with-ci-gate.md)
