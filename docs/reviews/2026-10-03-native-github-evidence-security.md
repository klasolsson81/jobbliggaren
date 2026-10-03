# Security audit: native GitHub review evidence policy

Canonical charter: .claude/agents/security-auditor.md (25260 bytes).

**Status:** Approved
**Scope:** Seven-file working diff on base `9f17dca890f3815e81cb360487cdefb88f7ac72d`.
**Authority:** CLAUDE.md §§9.1/9.2; ADR 0065; native-review runbook; repository threat model.

## Blockers / Major / Minor

None.

Accepting same-PR GitHub evidence for `security_report` preserves authorized-writer checks, explicit completion attestation, current head/base binding, verified test-merge parents and dedicated App publication on both SHAs.

The documented boundary remains accurate: the publisher validates authority and commit identity; the driving session verifies bot attribution, both separate review completions and their actual findings. Silence, stale results and incomplete reviews remain insufficient. Medium+ security findings and P0/P1/P2 code defects retain their closure requirements; local reviews and CI remain required.

Independently ran the validator suite: **55 tests, OK**, including both evidence fields’ URL rejection cases, missing attestation and publication on head/test-merge.

## Praise

- Optional private report access removes a dependency without weakening commit or writer checks.
- Documentation explicitly preserves the session’s responsibility for checking completion and findings.

## Summary

**0 Blockers / 0 Major / 0 Minor.** Report-only audit; no repository changes.

**Escalation to Klas:** No.
