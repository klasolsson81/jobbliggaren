# senior-cto-advisor — Platsbanken sök-paritet Fas D2 (ISearchQueryParser)

**Datum:** 2026-06-10
**Roll:** Decision-maker (§9.6). CC ger ingen egen rekommendation. Beslut grundade i dotnet-architect-rapport (`2026-06-10-sok-paritet-d2-architect.md`).

## VAL 1 — Parser-form + kontrakt: **Variant A+A (ren ResidualQ-normalisering)**

`ParsedSearchQuery(string? ResidualQ)` — inga dimensions-fält. CC kör direkt.

Motivering: (1) **5c:s egen ordalydelse är dispositiv** — "snarare än via gissande backend"; Variant B/C gissar dimensioner. 5b lade disambiguering på FE-chip (Fas E); dimension-extraktion i D2 dubblerar ett ansvar ADR:n redan placerat. (2) **YAGNI/Speculative Generality** (Beck 1999; Fowler 2018) — vestigiala alltid-tomma dimensions-fält är code smell. (3) **Kraschsäkerhet kompilator-garanterad** (Martin 2017 kap. 22; make illegal states unrepresentable) — utan dimensions-fält är AND-recall-krympning fysiskt omöjlig. (4) **SRP** (Martin kap. 7) — normaliserare har EN change-reason. (5) **Recall-anda** (ADR 0062/0042 B) — Variant B:s tysta dimensions-AND krymper resultat.

## VAL 2 — Impl-lager: **Application-only, internal sealed**

Port i Abstractions, impl i Application/JobAds/Internal. Följer av VAL 1: ren CPU, ingen IOptions/taxonomi/Npgsql → inget Infra-beroende (Martin kap. 22). synonymExpander-precedensen (Infra) gäller inte — den splitten motiveras av IOptions-binding som saknas här. Testbarhet utan DB.

## VAL 3 — Reconciliation-natur: **implementerings-notat, CC skriver, INGEN Klas-STOPP**

Med VAL 1 (bara ResidualQ) kollapsar ADR 5c-kontraktet trivialt. Ssyk→(bort)/+Municipality/−EmploymentType = mekanik-konkretisering av redan-Accepted-beslut (C2 avvecklade Ssyk; NULL-gate=D1). Att tömma kontraktet till bara ResidualQ STÄRKER 5c:s "inte gissande backend"-intent → ingen amendment. CC skriver notatet i ADR 0067 (C2-precedens rad 111-119).

## VAL 4 — Chip/residual-kombinationssemantik: **Klas-STOPP (förflaggad) — vad CC presenterar**

Den ENDA punkten som kräver Klas-GO (ADR 0067 Beslut 5 mildrad Klas-STOPP), och GO:n behövs först vid Fas E-wiring. CC presenterar:

1. **Mellan dimensioner: AND** (ADR 0042 B) — yrkesgrupp ∧ kommun ∧ region.
2. **Inom dimension: OR** (ADR 0042 B) — region=Sthlm ∨ Göteborg.
3. **Residual-Q: recall-bevarande OR-FTS** (ADR 0062) — Q når sök-kompositionen ENBART via JobAdFilterCriteria.Q, OR-additiv q-gren (FTS ∨ title-LIKE ∨ synonym).
4. **Mellan dimensioner och residual-Q: AND-mellan-block, OR-inom-Q-blocket:** `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)`. Q smalnar additivt mot dimensionerna men breddar inom sig själv; aldrig eget AND-fält utanför q-grenen.

Klas bekräftar: residual-Q som AND-block bredvid dimensionerna (ej OR-sammanblandat, vilket gjorde chips meningslösa), Q internt recall-OR. D2 implementerar INTE chip-wiring (Fas E); VAL 1:s kompilator-garanti gör punkt 3-4 strukturellt obrytbara.

## VAL 5 — In-block-fix vs TD: **allt in-block, inga TD**

DoS-yta (längd-cap via SearchCriteria.QMaxLength-referens — duplicera ej, Hunt/Thomas 1999 DRY/SPOT; kontrolltecken-strip; whitespace-kollaps). Ingen tsquery-escape (websearch robust) / ingen SQL-injection (EF parametriserar) — dokumentera varför. EmploymentType/WorktimeExtent utesluts (NULL-gate, ej TD — explicit scope-exkludering inom D2).

## VAL 6 — Scope-vakt

D2 wirar EJ FE-chip-state (5b=Fas E). D2 kör EJ re-ingest Klass 2. Falsk-klar-mitigering: VAL 1 (bara ResidualQ) eliminerar vestigialt-fält-fällan; Q-AND-fällan kompilator-omöjlig; EmploymentType-uteslutning hård.

## Vad Klas måste godkänna vs CC kör direkt

| Punkt | Status |
|---|---|
| VAL 1/2/3/5/6 | CC kör direkt (entydiga domar) |
| VAL 4 kombinationssemantik | Klas-STOPP (förflaggad; GO först vid Fas E-wiring; D2-bygget kör nu) |

**Referenser:** Martin 2017 (kap. 7 SRP, 22 Dependency Rule), Fowler 2018 (Speculative Generality), Beck 1999 (YAGNI), Hunt/Thomas 1999 (DRY/SPOT), ADR 0067 5b/5c/5, ADR 0062, ADR 0042 B.
