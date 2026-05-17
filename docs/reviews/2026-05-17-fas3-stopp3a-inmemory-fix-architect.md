# dotnet-architect — STOPP 3a fix-beslut: EF InMemory-brott i read-join

**Datum:** 2026-05-18
**Agent:** dotnet-architect (agentId a58de31fb7d3c6735), design-ägare för 2026-05-17-fas3-stopp3a-architect-design.md
**Status:** Read-only-pass — fix-beslut. Rapport applicerad av CC (architect write-restricted).

## Fynd (kritiskt — testbarhetsregression)

`x.a.JobAdId == null ? (Guid?)null : x.a.JobAdId.Value.Value` i pre-materialiserings-projektionen (3 read-handlers). `Application.JobAdId` = `Nullable<JobAdId>`, `JobAdId = readonly record struct` med EF `HasConversion`. EF InMemory-provider kör uttrycket som LINQ-to-objects mot rå property (ej via converter-pipeline) → `Nullable<JobAdId>.Value` kastar `InvalidOperationException: Nullable object must have a value`. Ursprungskoden gjorde samma ternär men POST-`ToListAsync()` (materialiserade entiteter, converter redan applicerad). §5b-designen flyttar projektionen in i provider-trädet (korrekt för Npgsql, ADR 0048 EN LEFT JOIN + CTO Beslut 2 SQL-verifiering) → InMemory straffar uttrycksformen. Ej designfel — uttrycksformulering.

## Beslut: Väg (D) — join-härledd JobAdGuid

Behåll pre-materialiserings-`GroupJoin`/`DefaultIfEmpty` (Npgsql-optimal, EN LEFT JOIN, ADR 0048 + CTO Beslut 2 intakt). Ersätt converter-tunga `.Value.Value` med join-härledd Guid via mellansteg-projektion: `JobAdGuid = j != null ? (Guid?)j.Id.Value : null`. Form (alla 3 handlers identiskt):

```csharp
.SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new { x.a, j,
    JobAdGuid = j != null ? (Guid?)j.Id.Value : null })
.Select(r => new ApplicationDto(
    r.a.Id.Value, r.a.JobSeekerId.Value, r.JobAdGuid, r.a.Status.Name,
    r.a.CreatedAt, r.a.UpdatedAt,
    r.j != null
        ? new JobAdSummaryDto(r.j.Id.Value, r.j.Title, r.j.Company.Name,
            r.j.Url, r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt)
        : r.a.ManualPosting != null
            ? new JobAdSummaryDto(null, r.a.ManualPosting.Title,
                r.a.ManualPosting.Company, r.a.ManualPosting.Url, "Manual",
                (DateTimeOffset?)null, r.a.ManualPosting.ExpiresAt)
            : null))
```

**Semantik (önskad, ADR 0048):** `JobAdGuid` från `j.Id` → `null` när JobAd soft-deletad (query-filter → `j == null`) ÄVEN om `Application.JobAdId` har värde. Korrekt — FK exponeras ej mot rad användaren ej får se; matchar JobAdSummary-fallback i samma projektion.

## Avvisade alternativ (källciterat)

- **(B) flytta unit→Testcontainers:** bryter test-pyramiden (Fowler 2018 — handler-logiken ÄR isolerbar; bara uttrycks-syntaxen fallerar); rör Fas-1-testfiler i onödan (mot J3-blast-radius).
- **(C) SQLite-in-memory i factory:** `TestAppDbContextFactory` delas av 43 testfiler — relationell semantik-skift (query-filter/owned/DateTimeOffset/converter) = regressionsrisk i 40 orelaterade filer. Legitim separat ratchet, ej denna touch.
- **(A) materialisera-först-projicera-sen:** återinför exakt det ADR 0048/CTO Beslut 2 förbjuder (döljer EN-join-vs-N+1).

## Verifieringskrav innan GRÖN (J3)

1. Full Release-svit: 18 unit-fel → 0.
2. `git diff` = exakt 3 handler-filer; `TestAppDbContextFactory.cs` 0 ändring (bevisar (C) ej smyger).
3. Npgsql-integrationstest: fortfarande EN LEFT JOIN, ingen N+1 (CTO Beslut 2).
4. `ReadHandlerManualPostingFallbackTests` grön — assert som kräver `JobAdId != null` vid soft-deletad JobAd = test-defekt (samma kategori), ej designändring.

## Separat: integrationstest-defekt (ej design)

`ReadJoin_WithSoftDeletedJobAd_FallsBackToNullViaQueryFilter` använder `jobAd.Archive(clock)` som soft-delete. `JobAd.Archive` sätter `Status=Archived` + event, **inte** `DeletedAt`. JobAd har ingen domän-`SoftDelete` som sätter `DeletedAt` (query-filter defensivt, matas aldrig av domänkod). Test-writer-fel — fixas i test-spåret (sätt `DeletedAt` via EF i testet). **Arkitekturobservation (separat TD-kandidat, annan fas §9.6):** om JobAd-soft-delete är ett verkligt krav saknas domänmetod — triageras separat, ej denna touch.

## Klas-STOPP?

**Nej — entydigt.** (D) rör endast de 3 handler-filer architect ägde i §5b; bevarar ADR 0048/CTO Beslut 2/J3/test-pyramid/§3.6/§7. Ingen test-omflytt, ingen delad-infra-ändring, ingen fas-skift. Klas-STOPP endast om join-härledd form mot förmodan ej ger GRÖN i båda providers (äkta provider-divergens → (B)/(C) blir strategiskt).

## Referenser
ADR 0048; CTO Beslut 2 (rev2); CLAUDE.md §3.6/§7/§9.6; Fowler *The Practical Test Pyramid* (2018). Kod: GetApplications/GetPipeline/GetApplicationByIdQueryHandler.cs, JobAdId.cs (readonly record struct), ApplicationConfiguration.cs:26, TestAppDbContextFactory.cs (43 filer — rör ej).
