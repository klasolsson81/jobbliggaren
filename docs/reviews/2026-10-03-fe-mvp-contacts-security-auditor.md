# Security audit — #1944 scoped re-check

Issuer: security-auditor, /root/detail_contacts_security.
Canonical charter: .claude/agents/security-auditor.md, 25,260 bytes.
Status: APPROVED. HEAD: 2182041a0b00fbb37e19e74e5c5cfe2c5e841e0d.
Authority: CLAUDE.md §9.6; AGENTS.md §§5/12; ADR 0106; ADR 0144 D4 rows 10/18.

## Findings

M1 closed. The Ghosted fixture now uses separate derived email-only and phone-only contacts, asserts two provenance labels and checks the actual phone lead name. This matches the unchanged AdContacts.From producer. The reviewer independently reran the four affected suites: 104 passed, 0 failed.

Reviewed B's correction delta plus the inherited shared link-style seam. Computed-style evidence contains 64 views / 256 targets / 0 failures, successful Tab traversal, edge/centre hit tests and no overlapping targets. Notices remain 14px, permanently underlined, Source Sans 3, minimum measured contrast 7.5626:1. Contact targets meet the 32px desktop and 44px touch floors.

No new security findings. ADR 0106's final change is whitespace-only; legal strings, routes, presence gates and provenance remain unchanged.

## Praise

- The corrected test verifies actual derived-contact provenance.
- Shared target sizing preserves the notice's legal presentation constraints.

## Summary

0 Blocker / 0 Major / 0 Minor. The issuer's one report-only scoped re-check completed against the stated head. Escalation to Klas: No.
