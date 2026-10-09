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

---

charter=.claude/agents/code-reviewer.md bytes=9874

## Code-review: Admin overview scoped recheck (PR #2059)

**Status:** ✓ Approved within recheck scope

**Authority:** CLAUDE.md §§4, 8, 9.6

**Scope:** Commit `7f7e2e8c216507601ef2e01ae796125f6e257982`: five frontend/CSS/regression files and four preserved first-round reports. Working tree clean.

### Blockers / Major / Minor

**Original Major closed — header navigation retains obsolete directory filters.**

File: `web/jobbliggaren-web/src/app/(admin)/admin/anvandare/accounts-directory.tsx:416`.

Changed route filters now reconcile criteria and server-provided results, close the panel and cancel its detail read. Period clearing preserves local search, status and sorting.

The same production-browser measurement that established the defect now passes for both dated and Suspended URLs → actual header “Användare”: plain URL, no period control, unfiltered rows and “Alla (5)”. Verified in [production log](C:/tmp/admin-overview-1978-review-production.log). Directory unit tests also pass **56/56**.

**No new-in-delta findings.**

### Summary

**0 Blocker / 0 Major / 0 Minor outstanding in this issuer’s scope.** The original Major is closed under §9.6. No further code-review repair or delegation required.
