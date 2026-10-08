charter=.claude/agents/security-auditor.md bytes=25260

## Security-audit: admin overview #1978 (PR #2059)

**Status:** ✓ Approved — local final security review

**Auktoritet:** GDPR Articles 5/25/32; ADR 0049/0066/0150/0151/0154; AGENTS.md §§5/12; CLAUDE.md §§9.2/9.6.

Reviewed HEAD `ba06465bb594f08f750741da1cd043974c84b8b1` against actual merged #1977 baseline `0b163d1b48fafe74a60ea497392142a461aa449f`: 73 files, +2901/−118.

### Blockers / Major / Minor

No local Blocker or Major.

1. **Inherited native Low/P3: technical condition remains unfixed; severity unchanged.**

   Evidence: `web/jobbliggaren-web/src/lib/api/admin-overview.ts:55`, `src/app/api/admin/oversikt/route.ts:7`, and `src/Jobbliggaren.Api/Program.cs:423–427`.

   With admission open, a supplied cookie starts three parallel backend calls before authorization results are examined. API authentication/authorization precede rate limiting, so rejected requests retain the bounded three-call amplification.

   Independent backend authorization prevents privileged data disclosure; this does not close the resource-amplification finding. The native remedy remains authorization once or BFF rate limiting before fan-out.

   [Original native finding](https://github.com/klasolsson81/jobbliggaren/pull/2059#discussion_r4217344641). Live GitHub resolution state was unavailable to this reviewer; the supplied historical evidence and current source establish the surviving condition.

   Disposition belongs to the already scheduled CTO checkpoint. This review neither closes, regrades nor defers it.

### Praise

- Backend Admin policy, direct-query authorization and fresh account/role checks remain enforced.
- Private/no-store responses, server-side projections and global refusal erasure protect browser-visible observations.
- Parameterized filters and retained-account classification preserve deletion behavior and minimize exposed data.

### Sammanfattning

**0 Blockers / 0 Major; one inherited native Low/P3 condition remains outstanding.** No new security repair is required by this local review.

Inspected authorization, privacy, cancellation, partial-failure and fixture-provenance tests. The test-runner report records 1,046 passing backend cases; verified zero backend/test delta from its tested head to this head. No tests rerun or files changed here. Current-head native reviews and CI remain separate pending attestations.

**Eskalering till Klas:** No additional human approval is required by this local review. Driving session: carry the still-unfixed native Low/P3 finding and its original remedy verbatim to the already scheduled CTO checkpoint for disposition. This report supplies no closure, deferral, merge permission or deployment approval.
