# senior-cto-advisor — STOPP 2 read-only-granskning: /ansokningar-redesign-plan

**Datum:** 2026-05-17
**Agent:** senior-cto-advisor (agentId ac00cccfcd6962a67)
**Granskat:** docs/design/ansokningar-redesign-plan.md (STOPP 2, read-only)
**Verdikt:** Plan godkänd med 3 bindande ändringar + 1 Klas-STOPP (ADR-precedens).

## Beslut 1 — Save-strategi: VARIANT A (entydigt)

Globalt top-save för status (+ framtida metadata) i StatusEditCard; uppföljningar/noteringar behåller self-contained add-flows. Motivering: YAGNI/KISS (status = single `transitionStatusAction`-write, ingen metadata-edit existerar — Variant B vore spekulativ generalitet, Fowler 2018 kap. 3); SRP/SoC (uppföljningar/noteringar = append-only-listor, "save" är fel verb — Variant B påtvingar formulär-semantik, Martin 2017 kap. 7); regressionsskydd (Variant B återinför ADR 0047 defekt-4-mönstret "sammanflätade sparbara delmoment"). Variant B avvisad: artificiell konceptuell enhetlighet maskerar att append ≠ edit (Norman).

## Beslut 2 — Backend-join: Clean Arch/CQRS-korrekt, MEN 1 blocker + ADR krävs

Arkitektur-ansatsen korrekt: read-projektion av cross-aggregat-join i query-handler, `JobAdSummaryDto?` över Application-gränsen, ingen Domain-koppling införd, AsNoTracking bevarad, ingen migration. CQRS-read-model-projektion per CLAUDE.md §2.3/§3.6 — godkänt.

**BLOCKER (in-block, lös i STOPP 3-design före kod):** `JobAdConfiguration.cs` rad 82 = `builder.HasQueryFilter(j => j.DeletedAt == null)` global query filter. Planens §1.2 "soft-deleted JobAd → null"-mekanism är felbeskriven: soft-deletade exkluderas automatiskt av query-filtret FÖRE joinen; `DefaultIfEmpty()` ger då null. Risker: (1) `IgnoreQueryFilters()` som join-debug-reflex exponerar soft-deletad annons-metadata (regression mot ADR 0032); (2) manuell `DeletedAt`-predikat i handler dubblerar query-filter-invarianten (DRY/SPOT-brott, Hunt/Thomas 1999). Krav: §1.2/§1.4 skärps — query-filter-mekanism explicit, `IgnoreQueryFilters()` förbjudet i de 3 handlers, ingen manuell DeletedAt-predikat, test-writer-krav: explicit test att default-join (utan IgnoreQueryFilters) ger fallback för soft-deletad JobAd.

**N+1-bedömning:** GetPipelineQueryHandler materialiserar + grupperar in-memory. N+1-säkert ENDAST om joinen uttrycks som single LINQ-join projicerad FÖRE ToListAsync (EF genererar en LEFT JOIN). dotnet-architect-gaten ska verifiera genererad SQL = en query med en LEFT JOIN job_ads. ADR 0045 perf-budget-relevant (CLAUDE.md §2.5).

**ADR krävs (avvisar planens "ingen ADR"):** Första cross-aggregat-joinen i Application-läsvägen. ADR 0043 Beslut C löste cross-context-läsning via dedikerad `ITaxonomyReadModel`-port specifikt för att INTE införa cross-aggregat-koppling. Att nu välja in-handler-join är ett medvetet precedensval i kontrast mot ADR 0043 → ADR-värt (Nygard 2011). Designvalet är ändå rätt (YAGNI — enkel 1:0..1 samma-DbContext-länk, ingen anti-corruption behövs som med taxonomins jargong; ny port vore spekulativ inkapsling). **ADR 0048 (Proposed)** krävs: (a) join-i-handler som mönster för enkla samma-DbContext-aggregatlänkar, (b) kontrast mot ADR 0043 port-val, (c) query-filter-disciplin. Accepted-flip = Klas-GO. In-block (§9.6 + §8.9 DoD), ej scope-creep.

## Beslut 3 — Scope: inom ram

Allt i §1–§7 tjänar direkt de två ytorna. Ingen /jobb-läcka, inga token-ändringar, ingen migration. Dead-code-radering (application-card/status-card om orphaned) korrekt in-block per §9.6 + transition-form-precedens (a870292905edc4943) — villkor: grep-bevis 0 referenser i STOPP 3-rapport (§9.4). Memory `feedback_dont_delete_auto_files` gäller scaffolding, ej refactor-föräldralösta appkomponenter — ingen konflikt. Ny `components/ui/radio-group.tsx` inom Klas-GO. Enda scope-tillägg: ADR 0048 (artefakt, ej kod-scope).

## Beslut 4 — Klas-STOPP krävs (ADR-precedens)

3 plan-ändringar: (1) §4 ← Variant A + motivering; (2) §1.2/§1.4 ← query-filter-skärpning; (3) §1.4 "Ingen ADR" → "ADR 0048 Proposed krävs". Ändring 3 motsäger planens egen bedömning och etablerar arkitektur-precedens i kontrast mot ADR 0043 → §9.6 pt 5 "större strategisk fråga" → Klas-STOPP, ej autonomt CC-vidare. Sekvens: Klas läser CTO+design-reviewer-rapport → GO:ar plan-ändringar inkl. ADR 0048-Proposed → plan + ADR 0048-skelett uppdateras → STOPP 3 TDD per §1.4-gater. Klas-override-utrymme: argument mot ADR = "rent additiv projektion" (svagt — första cross-aggregat-joinen, medveten ADR 0043-avvikelse); dokumenteras som Klas-override om Klas avviker.

## Referenser
Martin *Clean Architecture* (kap. 7 SRP, 13 REP/CCP); Evans *DDD* (kap. 14 ACL — ADR 0043-kontrast); Fowler *Refactoring* 2e (kap. 3); Beck (YAGNI); Hunt/Thomas *Pragmatic Programmer* (DRY/SPOT); Nygard *Documenting ADR* (2011); Norman/Krug (ADR 0047). ADR 0009/0032/0043/0046/0047. CLAUDE.md §1/§2.3/§2.5/§3.6/§8.9/§9.6. Kod: JobAdConfiguration.cs:82, GetPipelineQueryHandler.cs:30–44, GetApplicationByIdQueryHandler.cs:31–73.
