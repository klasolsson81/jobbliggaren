# CTO decision — #1986 performance attribution

**Charter:** `.claude/agents/senior-cto-advisor.md` (17258 bytes read)
**Date:** 2026-10-04
**Scope:** Bounded performance attribution; not a final code-review or merge verdict.

## Decision

Close the bounded #1986 performance measurement pass on documented byte-budget compliance and an unresolved timing signal. Retain the complete candidate/base comparison and quiet-repeat artifacts. Route further timing attribution through existing #1068; make no speculative production performance change and create no duplicate issue.

## Rationale

ADR 0045 Decisions 5/6 and its 2026-07-25 amendment distinguish admissible byte evidence from timing that the instrument cannot adjudicate. The performance runbook §F explicitly classifies local Windows LCP/TBT measurements as hypothesis generation. Candidate privacy initially measured median score 75 / LCP 3326 ms / TBT 712 ms against base 96 / 2552 ms / 53 ms. The unchanged candidate's quiet repeat measured scores 69/81/90, LCP 4038/3784/3591 ms and TBT 682/332/34 ms; its repeat medians are 81 / 3784 ms / 332 ms. The 20-fold TBT spread defeats a causal verdict from this local sample. It does not refute the LCP signal or establish that host load caused it.

All measured byte budgets remain satisfied: JS 216837 versus 212300 B (+4537 B), CSS 34438 versus 33086 B (+1352 B), fonts unchanged at 61908 B. Both prerender manifests have the same static route set; no information page changed from static to dynamic. These are admissible bounded facts, not an explanation of the timing difference.

## Rejected alternatives

A speculative hydration, rendering, font or payload intervention has no established cause and violates runbook §F's no-speculative-fix rule. Repeating runs until one looks green or reporting only the third repeat would manufacture a non-regression claim. Holding this UX scope open until an inherently variable composite score becomes green contradicts the ADR's explicit attribution-based closure rule.

## Accepted limitation

The manual Lighthouse >90 target has not been met. Neither timing non-regression nor performance-budget success for LCP is claimed; the LCP budget remains unchanged. Further attribution remains open in #1068, including confirmation on the CI instrument before a causal code finding or intervention. This decision does not waive unresolved code/design/security findings or manual accessibility obligations.

## References

ADR 0045, Decisions 5/6 and Amendment 2026-07-25; docs/runbooks/performance-measurement.md §F; [#1068](https://github.com/klasolsson81/jobbliggaren/issues/1068); Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017), fitness-function evidence and ratcheting.
