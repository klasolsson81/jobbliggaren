---
session: d2-search-query-parser
datum: 2026-06-10
slug: d2-search-query-parser
status: levererad (PR mot main, automerge-label, ci-gate pending)
commits: feat (parser + tester) + docs-sync (denna)
bas-HEAD: ed959c0
---

# Session 2026-06-10 — Platsbanken sök-paritet Fas D2

## Mål

Fas D2 per ADR 0067 Beslut 5c: `ISearchQueryParser` för residual-fritext.
Backend-parser + `ParsedSearchQuery`-kontrakt + residual-Q→FTS-väg, unit-testbar
standalone. Chip-komposition (FE-state) = Beslut 5b/Fas E, EJ D2.

## Kärnspänning + reconciliation (architect + CTO INNAN kod)

ADR 0067 Beslut 5c skrevs FÖRE Fas C2 och namngav
`ParsedSearchQuery(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds,
ResidualQ)`. C2 avvecklade Ssyk ur sök-identiteten → post-C2-SPOT =
`JobAdFilterCriteria(OccupationGroup, Municipality, Region, Q)`. Kontraktet
matchade inte verkligheten.

**dotnet-architect** ramade in kärnfrågan: givet chip-driven UX (5b) +
"disambiguering vid input snarare än via gissande backend" (5c) — extraherar
parsern dimensioner alls, eller normaliserar den bara residual? Gav Variant A/B/C
för parser-form + kontrakt-dims; rekommenderade ej (CTO = decision-maker).

**senior-cto-advisor** (VAL 1–6):
1. **Variant A+A** — ren ResidualQ-normalisering, `ParsedSearchQuery(string?
   ResidualQ)`, inga dimensions-fält. Motiv: 5c:s ordalydelse dispositiv,
   YAGNI/Speculative Generality, kraschsäkerhet kompilator-garanterad, SRP,
   recall-anda.
2. **Application-only impl** — ren CPU, inget Infra-beroende (synonymExpander-
   precedensen = IOptions-binding som saknas här).
3. **Reconciliation = implementerings-notat**, ingen amendment, ingen Klas-STOPP
   (mekanik-konkretisering av redan-Accepted-beslut; C2-precedens).
4. **Kombinationssemantik = Klas-STOPP** (förflaggad; GO först vid Fas E-wiring).
5. Allt in-block, inga TD.
6. Scope-vakt: D2 wirar EJ FE-chip-state, kör EJ re-ingest Klass 2.

## Vad som levererades

- `ISearchQueryParser`-port + `ParsedSearchQuery(string? ResidualQ)` (Abstractions).
- `SearchQueryParser` `internal sealed` (Application/JobAds/Internal) — ren CPU.
  Normalisering: whitespace-kollaps (IsWhiteSpace FÖRE Cc/Cf-strip — tab/newline
  är Cc men ordgränser), strip Control/Format, sub-QMinLength→null,
  >QMaxLength→rune-säker trunkering. Kastar ALDRIG.
- `SearchCriteria.QMinLength`/`QMaxLength` private→public const; validator + parser
  refererar (DRY).
- `ListJobAdsQueryHandler`-wiring (query.Q → ResidualQ → JobAdFilterCriteria.Q);
  DI singleton; InternalsVisibleTo (UnitTests + IntegrationTests).
- Tester: 32 parser-fall + 5 handler-fall + 4 Testcontainers-integ
  (`ListJobAdsResidualQueryTests`). 3 integ-filer uppdaterade för 2-arg ctorn.

## Beslut & detours

### Test-writer-miss (1 integ-fil)
test-writer uppdaterade 3 av 4 integ-filer för 2-arg ctorn men missade
`ListJobAdsOccupationGroupFilterTests.cs` (target-typed `new(...)` → grep-miss).
Fixat in-block + `using ...Internal`.

### Bygg-lås (memory `feedback_restart_stack_after_commit_stop`)
Api (PID 29600) + Worker (PID 29792) körde och låste src/Worker/bin-DLL:er →
full-solution-bygget + Api.IntegrationTests-bygget krockade. Stoppade båda,
byggde, testade, **startar om före sessionsslut + verifierar /api/ready**.
(`taskkill //PID` med MSYS-escape; PowerShell-via-Bash blockerat av classifier.)

### code-reviewer Major: surrogat-split (in-block-fix)
Trunkeringen `sb.ToString(0, QMaxLength)` arbetar på UTF-16 code units → kunde
splittra ett surrogatpar på QMaxLength-gränsen → lone surrogate → ogiltig UTF-16
→ Npgsql-krasch nedströms (bröt "kastar ALDRIG"-garantin). Reachable via rå
user-input (≥100-tecken emoji-tung sträng), nuvarande fas → fix in-block (§9.6).
Åtgärd: `if (char.IsHighSurrogate(sb[cut - 1])) cut--;` + 2 surrogat-gränstester.
Re-verifierat grönt.

## Reviews
- dotnet-architect: kontrakts-spänning + lager-design + Variant A/B/C.
- senior-cto-advisor: VAL 1–6 (Variant A+A, Application-only, notat-ej-amendment).
- code-reviewer: 0 Block / 1 Major (surrogat-split — åtgärdad in-block) / 2 Minor
  (binär testfil acceptabel; default!-init OK). Mergeklar efter fix.
- security-auditor: APPROVED, 0 Crit/High/Major. Parsern minskar netto-attack-ytan
  (Cc/Cf-strip + längd-cap före FTS). LIKE-metatecken = oförändrad pre-existing L1.
Rapporter: `docs/reviews/2026-06-10-sok-paritet-d2-*.md`.

## Status & nästa session
- Bygg 0 warn/0 err; format-verify exit 0; Application 728 / Domain 440 /
  Architecture 78 / integ residual 4 + sök-regression 35 gröna.
- ADR 0067 implementerings-notat 2026-06-10 (Fas D2) skrivet.
- **KLAS-STOPP:** chip/residual-kombinationssemantik presenteras för GO innan
  Fas E-wiring (se STOPP-rapport + current-work.md Pending #2).
- Nästa: Fas E (FE-picker + chip-komposition + live-count + ny färg-identitet,
  design-reviewer VETO) — Klas-GO.
- Stacken startas om (Api 5049/Worker) + /api/ready verifierad före sessionsslut.
