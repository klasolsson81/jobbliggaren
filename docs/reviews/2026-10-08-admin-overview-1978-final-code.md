charter=.claude/agents/code-reviewer.md bytes=9874

## Code-review: Admin overview (PR #2059)

**Status:** ⚠ Changes requested

**Authority:** CLAUDE.md §§4, 8, 9.6

**Scope:** Full 73-file diff, `0b163d1…ba06465`. Covered API, Application, Infrastructure, frontend, tests, i18n, BUILD and ADR0150/0151. Second pass completed for authorization, calendar boundaries, navigation and translation scope; no code-review area remains unexamined.

### Blockers / Major / Minor

1. **Major — Header navigation clears the URL but retains filtered directory state** — File: `web/jobbliggaren-web/src/app/(admin)/admin/anvandare/accounts-directory.tsx:163`

   **Current:** Criteria and listing initialize from props through `useState` but do not reconcile subsequent route props. Clicking the actual header “Användare” from a dated directory produces `/admin/anvandare` while “Rensa period” remains. The equivalent status-filter navigation retains the old empty table. Both failures are confirmed against the frozen production build in `C:/tmp/admin-overview-1978-url-repro.log`.

   **Required:** Synchronize displayed criteria and results with route navigation while preserving intentional local filtering and period-clearing behavior. Add browser regressions for dated/status URLs → header “Användare”.

   **Reason:** CLAUDE.md §§4, 8; the displayed directory must honor the current route and acceptance criteria.

   **Delegate:** nextjs-ui-engineer; test-writer for regression coverage.

   Cross-page overview drilldowns passed the additional reproduction; this finding concerns navigation within the directory.

### Praise

- Shared parameterized registration predicates and Swedish calendar boundaries keep counts and drilldowns consistent.
- Global authorization refusal clears privileged observations; ordinary source failures retain explicitly dated data.

### Summary

**0 Blocker / 1 Major / 0 Minor.** Fix before merge, then invoke this issuer for a report-only recheck scoped to the fix delta under §9.6.

The inherited native Security Low/P3 fanout finding remains unresolved and routed to security/CTO; this review does not regrade or defer it.
