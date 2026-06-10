# security-auditor — Platsbanken sök-paritet Fas D1

**Datum:** 2026-06-10
**Agent:** security-auditor (agentId `a8d51280b806479ee`)
**Verdikt:** ✓ Approved — säkerhetsmässigt mergeklar. **0 Critical / 0 High / 0 Major / 1 Minor**
**Auktoritet:** OWASP API Top 10 (2023), GDPR Art. 5/32, CLAUDE.md §5.3/§5.4

## Fokuspunkter

1. **DoS / OWASP API4:2023 — RENT.** `FacetCountsAsync` (~44k-rad GROUP BY) når ingen otaglad yta i D1 (port-only — symbol endast i interface/impl/enum-doc + direkt-DI-test, ingen route/Mediator-query). NBomber-scenariot inert (ej i Program.cs). Suggest-DoS-floor bevarad (min prefix ≥2, limit 1–20, SuggestPolicy 30/10s). Taxonomi-prefix-scan in-memory + `.Take(limit)`; union cappar totalen över båda grenar (bevisat av cap-test).
2. **Injection — RENT.** Titel-LIKE-escape (`LikePattern` + explicit ESCAPE) oförändrad. GROUP BY via `EF.Property` parametriserad; `column` från sluten enum-switch, aldrig user-input. Ingen rå SQL/concat.
3. **Informationsläckage — RENT.** Concept-id (facet-count-nycklar + `SuggestionDto.ConceptId`) = publik referensdata, EJ PII. ADR 0043 ACL ej bruten (concept-id flödar redan över Application-gränsen; förbudet gäller UI-ytan — Fas E). ACL t.o.m. förstärkt (internal `TaxonomyConceptKind` → publik `SuggestionKind`). Ingen känslig logging.
4. **Auth — RENT.** `/suggest` fortsatt auth-gated (RequireAuthorization via grupp); SuggestPolicy-partition (sub-claim) oförändrad.
5. **Secrets/PII i nya filer — RENT.** `FacetCountsScenarios.cs` läser `LOADTEST_BEARER_TOKEN` ur env (ingen inbäddad cred); testfiler rena (Guid-prefix-fixtures, ephemeral Testcontainers-connstring).

## Minor (icke-blockerande)
1. Dedup-kommentar i `SuggestJobAdTermsQueryHandler.cs` beskrev `(Kind, ConceptId ?? Label)` men koden använder `(hit.Kind, hit.ConceptId)` / `(Title, title)`. Funktionellt korrekt, kommentar-hygien. **→ Åtgärdat in-block.**

## Sammanfattning
0 Critical / 0 High / 0 Major / 1 Minor (åtgärdad). NOLL GDPR-brott. Mergeklar.
