# Code review — #1864 / #1872

Issuer: code-reviewer, /root/detail_code_review.
Canonical charter: .claude/agents/code-reviewer.md, 9,874 bytes.
Authority: CLAUDE.md §§5, 7–8, 9.2 and 9.6.

## Findings and closure

1. Major: two endpoint-test comments made unsupported claims about NotAssessed and confirmed-skill/CV prerequisites. The claims were deleted. The issuing reviewer closed both in its one scoped re-check.
2. New-in-delta Major in that re-check: four modal-focus E2Es assumed an authenticated ad corpus that the default CI fixture does not create. The CTO bound the existing guest-ad producer as the remedy. The tests now execute GUEST_MOCK → guest list → intercepted detail → the shared JobAdModalShell, pinning the rendered title and destination. All four dismissal tests executed and passed locally against the production build.

The second finding is closed through the code-only correction and rerun of its measurement under §9.6. No additional local re-check is claimed. The authenticated 40-case keyboard measurement remains separate evidence and is not a default-CI corpus claim.

## Summary

No unresolved local Blocker/Major. Native code/security review and CI remain separate delivery gates. No severity was re-graded, no follow-up issue was created and no escalation to Klas remains.
