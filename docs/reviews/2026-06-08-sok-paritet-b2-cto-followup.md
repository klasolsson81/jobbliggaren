# CTO-followup — Backfill-trigger: generalisera vs klona (Fas B2 Beslut (a) uppföljning)

**Datum:** 2026-06-08
**Agent:** senior-cto-advisor (agentId `a6e8d247d857d0116`) — decision-maker, fokuserad sub-fråga till Beslut (a) Variant B.
**Underlag:** `BackfillJobAdSsykJob.cs` + `BackfillJobAdSsykWorker.cs` + `AdminJobAdsEndpoints.cs` + `docs/tech-debt.md:54` (TD-97 — ssyk-backfill öppen test-skuld, ingen pågående refaktorering).

## Fråga
Beslut (a) Variant B kräver en idempotent re-ingest-trigger för Klass 2 enligt backfill-ssyk-precedensen. Nyckelinsikt: skillnaden mellan ssyk-jobbet och ett Klass2-jobb är BARA WHERE-predikatet (vilken NULL-kolumn som styr re-fetch); per-ID-refetch re-skriver hela raw_payload → alla STORED-kolumner re-evalueras. Fork: **K** (klona ~200 rader), **G** (generalisera publikt ssyk-jobb), **H** (extrahera delad privat kärna, ssyk publik yta orörd, tunn wrapper).

## Beslut: **Variant H — extrahera delad re-ingest-kärna; ssyk-jobbet behåller orört publikt beteende, Klass 2-jobbet är tunn wrapper.**

**Motivering:**
- **DRY rätt tillämpad (Hunt/Thomas 1999 kap. 7):** Iterations-loop, RefetchByExternalIdAsync, throttle, child-scope, MaxItemsPerRun-cap är ETT knowledge piece ("idempotent restart-säker re-ingest via deterministisk refetch"). NULL-kolumn-predikatet är enda äkta variationspunkten.
- **OCP (Martin 2017 kap. 8):** Delad kärna med predikat-parameter = öppen för extension (nytt backfill-behov = ny tunn wrapper), stängd för modifikation (kärnan rörs ej). Variant K bryter detta (klonar ~200 rader per STORED-tillägg).
- **Blast-radius (Ford/Parsons/Kua 2017):** Variant H överlägsen G. G **modifierar** det körda ssyk-jobbets publika signatur/beteende (deployad historisk kod + TD-97:s kommande test). H **adderar** privat helper, ssyk-jobbet delegerar utan att ändra publikt kontrakt/endpoint. Additiv över muterande.
- **Rule of Three (Fowler 2018 kap. 1):** Andra instansen, ej tredje — men Fowlers tröskel gäller spekulativ generalisering. Här är variationsaxeln (predikatet) empiriskt fastställd och endimensionell; att faktorisera ut en redan-känd identisk mekanik är inte premature abstraction. H är minst spekulativ (helper, ej fullt parametriserat publikt jobb).

**Avvisat:** K (DRY-brott på äkta knowledge-piece, klonings-precedens). G (korrekt DRY-instinkt men onödig blast-radie på kört "engångs"-jobb + TD-97-test; symmetrin nås via delad privat kärna).

**Trade-offs:** Två tunna publika jobb/workers/endpoints (ssyk + Klass2) ovanpå delad kärna — acceptabelt (semantiskt distinkta admin-operationer, endpoint-count ej en minimerings-axel). Loop-kroppen rör sig i ssyk-jobbet men publikt beteende/kontrakt oförändrat.

**Implementationsram (bindande):** Kärnan i Application (`Jobs/`), tar NULL-kolumn-predikat som Expression, **ingen Hangfire-referens** (`[DisableConcurrentExecution]` stannar i Worker-wrappern, §2.1). Klass2-predikat: `EmploymentTypeConceptId == null`. Ny endpoint `POST /backfill-klass2`, 202 + jobId, "engångs, INTE i RecurringJobRegistrar".

**Klas-GO: Nej — CC går direkt till implementation.** Entydigt designval mot principer (DRY + OCP + additiv-över-muterande), ej fas-strategiskt/ADR-amendment/deploy (§9.6 punkt 5).

**Referenser:** Hunt/Thomas (1999) kap. 7; Martin (2017) kap. 8 + §2.1; Fowler (2018) kap. 1; Ford/Parsons/Kua (2017); on-disk BackfillJobAdSsykJob/Worker + AdminJobAdsEndpoints + TD-97.
