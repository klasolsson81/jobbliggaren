# CTO decisions — #1864 / #1872

Issuer: senior-cto-advisor, /root/detail_dedupe_routing.
Canonical charter: .claude/agents/senior-cto-advisor.md, 17,258 bytes.

## Binding decisions

- Preserve ConceptId and original Display beside frozen scores. Reuse ISkillResolver.GroupConceptIds independently within each dimension and matched/missing side. Retain the legacy display arrays and grade semantics; absent/null additive evidence remains compatible, present malformed evidence rejects.
- The measured label collisions do not establish an ingest defect. Do not infer one or file an unsupported issue. Existing taxonomy grouping determines justified presentation groups.
- Repair the pre-existing job modal focus defect in PR A, bounded to JobAdModalShell and its actual dismissal paths.
- Correct the default-suite focus tests through the existing guest-ad producer and shared modal shell. No seed, conditional skip or new harness is required. Keep authenticated local evidence separate and close through a code-only fix plus rerun of the finding's measurement.

## Escalations

None. These decisions change neither grading nor matching/extraction policy and were executed in the authorized feature scope.
