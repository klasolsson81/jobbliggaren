# security-auditor — Platsbanken sök-paritet Fas D2 (ISearchQueryParser)

**Datum:** 2026-06-10
**Status:** ✓ APPROVED — inga blockers, inga Critical/High/Major. Ingen GDPR-eskalering.
**Auktoritet:** CLAUDE.md §5.1/§5.4, GDPR Art. 5/32, ADR 0062/0067, precedens `docs/reviews/2026-05-21-f6-p4-fts-security-audit.md` (L1).

## Dom
`SearchQueryParser` är en normaliserande grind framför q-FTS-hybriden som **minskar** attack-ytan: tar bort mega-whitespace-stuffing, kontroll-/format-tecken och sub-min-längd innan de når SQL-lagret. Kraschsäkerhet testbevisad (10000-tecken-whitespace → null, 5000 → trunkerat, RTL/ZWJ/emoji → kastar aldrig). Mergeklar.

## Genomgång

**1. Injection (title-LIKE-wildcard):** `JobAdSearchQuery.cs` bygger `pattern = $"%{q.ToLowerInvariant()}%"` utan att escapa `%`/`_` — IDENTISKT med pre-D2 och redan dokumenterat L1 (Low, pre-existing) i 2026-05-21-auditen. EJ SQL-injektion (`EF.Functions.Like` parametriserar `LIKE @pattern`). D2 förbättrar marginellt: parsern strippar Cc/Cf + cap:ar längd innan q når pattern-bygget. `%...%`-substring-grenen har inget btree-anchor att skydda (till skillnad från SuggestJobAdTerms left-anchored prefix där LikePattern.EscapePrefix används) → escaping ger ingen DoS-vinst. **Ej TD, ej in-block** (§9.6 — ingen ny exponering, redan avfärdat).

**2. DoS:** validator-cap (2-100, pre-handler) + parser-cap (QMaxLength-trunkering) + parser-floor (sub-2→null skyddar mot `%a%`-near-full-scan) + GIN-trigram på kort title-kolumn. Whitespace materialiseras bara vid pending+följande icke-whitespace → ingen kvadratisk allokering. StringBuilder pre-sizad, O(n)-pass. Täckt.

**3. PII/GDPR (§5.1):** ingen ILogger i SearchQueryParser/ISearchQueryParser/ParsedSearchQuery/ListJobAdsQueryHandler/JobAdSearchQuery (grep Log* i JobAds-trädet: noll). Residual-Q loggas aldrig i klartext. EF-command binder `@q` (maskeras). Ingen ny PII-kategori, ingen privacy-policy-påverkan.

**4. Konstant-exponering (QMinLength/QMaxLength private→public):** värden 2/100 = rena gränsvärden, redan externt observerbara via validator-felmeddelande. Inget info-läckage; eliminerar literal-duplicering (DRY).

**5. InternalsVisibleTo (Application → testprojekt):** compile-time-synlighet för två namngivna testassemblier, ändrar EJ runtime-synlighet i prod. SearchQueryParser förblir internal sealed.

## Praise
- Cc/Cf-strip före FTS = proaktiv härdning mot null-byte/zero-width/RTL-stuffing
- Kraschsäkerhet testbevisad med patologisk input
- DI samma commit (`feedback_di_with_handlers_same_commit`)
- Testcontainers ej InMemory (`feedback_ef_strongly_typed_vo_contains_translation`)
- SPOT bevarad (RunSavedSearch parsar ej om redan-validerat Q)

**Slutsats:** Inga blockers. LIKE-metatecken = oförändrad pre-existing L1 (Low). Parsern minskar netto-attack-ytan. Mergeklar.
