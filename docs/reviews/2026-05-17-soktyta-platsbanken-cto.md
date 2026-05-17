# CTO-beslutsunderlag — Sök-yta vs Platsbanken civic-modell

**Datum:** 2026-05-17
**Decision-maker:** senior-cto-advisor (agentId `aa1b666ec06a345ed`)
**Status:** Beslutsunderlag — kräver Klas strategisk GO innan implementation (Fråga 1 + 3). Fråga 2 = in-block, ingen GO.
**Källa:** Klas live-jämförelse /jobb vs arbetsformedlingen.se/platsbanken/annonser (cron-grön-followup-session 2026-05-17). Persisterad av CC (CTO-agenten är read-only).

---

## Sammanfattande ramning

Klas observation är korrekt och allvarlig mätt mot JobbPilots egen identitet. Nuvarande sök-yta exponerar **JobTech-domänens interna vokabulär** (concept-id `MVqp_eS8_kDZ`, font-mono-chips, "OR-bevakning") direkt i användarytan. Detta är ett **läckande bounded context** (Evans 2003, kap. 14 — anticorruption layer): JobTech-taxonomins identifierare är *deras* ubiquitous language, inte JobbPilots, och inte slutanvändarens. CLAUDE.md §1 (civic-utility) och §10.3 (rak svenska) är direkt brutna. ADR 0042 Beslut A löste *layouten* (resultat-först) men inte *vokabulären*.

Detta är inte kosmetisk polish. Det är ett IA-fel en Mastercard-nivå-granskare markerar direkt.

---

## FRÅGA 1 — Yrke/Ort namn-väljare vs concept-id

**Beslut: Approach A** — statisk/cachead taxonomi-snapshot i egen DB/seed, med ACL-mappning namn→concept-id i Infrastructure. Sök-ytan visar svenska namn i hierarkiska väljare (Region: Län→Kommun; Yrke: Yrkesområde→Yrke). Användaren ser/väljer aldrig concept-id. Mappning sker via lokalt persisterad snapshot uppdaterad via befintlig JobTech-sync-väg, **aldrig på sök-vägen per keystroke**.

**Motivering:** Anticorruption Layer (Evans 2003 kap. 14) — JobAdSearch.cs rad 24-25 erkänner redan att JobTech-taxonomi inte är JobbPilots ubiquitous language; UI-ytan bryter samma princip koden respekterar. ADR 0042 rad 21-constraintet ("inget externt taxonomi-API **på sök-vägen**") är **uppfyllt, inte brutet** av A — lokal snapshot är per definition inte på sök-vägen (samma resonemang som C2-avvisningen Beslut C rad 89-90). YAGNI/KISS — taxonomi är referensdata, ändras månads-/kvartalsvis.

**Avvisat:** B (frontend-konstant — DRY-brott, två sanningskällor). C (de-jargonisera endast copy — snabblösning förklädd; "Yrke"-etikett men kräver `MVqp_eS8_kDZ` är *mer* vilseledande; löser symptom ej sjukdom). C2-light (redan avvisat ADR 0042 Beslut C).

**Klas strategisk GO krävs: JA.** Arkitekturell utvidgning (ny snapshot + ACL + sync-integration) + partiell supersession av ADR 0042 Beslut C:s sök-ytas datakälla (Beslut B:s domänform `IReadOnlyList<string>` concept-id **oförändrad** — endast inmatnings-/presentationsyta ändras) + Fas 2 formellt stängd → post-closure redesign = strategisk transition (§9.2).

**ADR-konsekvens:** Ny **ADR 0043** "Taxonomi-ACL för sök-ytan" rekommenderas (nytt arkitekturbeslut, Nygard 2011) alternativt ADR 0042-amendment. Klas avgör granularitet vid GO. Beslut B-domänkontrakt + Beslut C-typeahead-arkitektur ändras inte.

---

## FRÅGA 2 — Sort-modell 5→3

**Beslut: Approach A** — behåll 5-värdes-enum (`JobAdSortBy`), åtgärda endast copy i `JOB_AD_SORT_LABELS`. Klas problem ("Sist/Tidigast sista ansökningsdag otydligt") är lexikalt, inte strukturellt.

**Motivering:** YAGNI/KISS (Beck; Fowler 2018) — B (3 + riktnings-toggle) löser ett icke-existerande problem. Blast-radius (Ford/Parsons/Kua 2017): `JobAdSortBy`→`ListJobAdsQuery`→`JobAdSearch.ApplySort`→`SearchCriteria` VO→**ADR 0039 Beslut 3** (SortBy del av VO-identitet)→jsonb-persistens `saved_searches.criteria`→migrations/bakåtkompat + risk mot SavedSearch jsonb-dedupe (ADR 0042 Beslut B.1-invariant). Massiv kostnad, "två ord tydligare"-nytta. ADR 0039 Beslut 3 (Accepted) väljer medvetet inbakad riktning. Platsbanken-paritet är ton/begriplighet, inte 1:1-funktionsspegling — 5 explicita val med klar copy är *mer* civic-begripliga än 3 + dold riktning.

**Avvisat:** B (enum-ändring — bryter YAGNI + river ADR 0039 Beslut 3 + blast-radius). C (ta bort Asc-varianter — feature-förlust utan användarsignal).

**Klas strategisk GO krävs: NEJ** för Approach A — ren copy, ingen enum-/VO-/migrations-/ADR-påverkan, legitim in-block-fix (§9.6 default). Om Klas ändå vill ha B: JA + ADR 0039 Beslut 3-supersession + ADR 0042 Beslut D-amendment + full blast-radius-batch; CTO rekommenderar emot (medveten override mot YAGNI/beslutsstabilitet om vald).

---

## FRÅGA 3 — Scope / sekvensering

**Beslut:** Egen tracked redesign-batch, separat session, post-Fas-2-stängning. Ej in-block med pågående touch. **Ej TD** (§9.6 — dependency finns; hör till sök-ytan = Fas 2:s domän; TD-dumpning vore exakt anti-patternet §9.6 förbjuder). Strategisk transition (§9.2) → Klas re-opening-beslut. CTO rekommenderar avgränsad Fas 2-reopening redesign-batch (kvalitetsåtgärd på befintlig Fas 2-yta, ej ny feature).

**Agent-ordning vid GO (Fråga 1 Approach A):** plan-design (webb-Claude+Klas, Klas-STOPP) → dotnet-architect FÖRST → senior-cto-advisor (multi-approach inom design) → db-migration-writer (om snapshot=tabell, mot ADR 0032 sync-skrivlast) → security-auditor BLOCKING (ny inmatningsväg, parametriserat+cap) → backend-impl (test-writer FÖRST/TDD) → nextjs-ui-engineer (hierarkisk väljare, läs next-docs först, behåll Beslut A-disclosure + Beslut B URL-multi) → design-reviewer VETO + visual-verify (Klas godkänner skärmbilder). Fråga 2 Approach A buntas i samma batch (CCP, Martin 2017 kap. 13) ELLER fristående liten fix.

**Klas-STOPP:** Fråga 1 fas-reopening + ACL-arkitektur = JA. ADR-granularitet (0043 vs 0042-amendment) = JA. Fråga 2 copy-only = NEJ. db-migration/security-auditor/design-reviewer-rapporter bifogas STOPP.

---

## TL;DR

| Fråga | Beslut | Klas-GO? | ADR |
|---|---|---|---|
| 1 — namn-väljare vs concept-id | Approach A (lokal taxonomi-snapshot + ACL) | **JA** — fas-reopening + ny arkitektur | Ny ADR 0043 (rek.) el. 0042-amendment |
| 2 — sort 5→3 | Approach A (behåll 5-enum, fixa copy) | **NEJ** — in-block copy | Ingen |
| 3 — scope | Egen redesign-batch, separat session, post-closure; ej TD | **JA** — strategisk transition §9.2 | Se Fråga 1 |

**Referenser:** Evans 2003 kap. 14 (ACL); Beck/Fowler YAGNI, Fowler 2018; Ford/Parsons/Kua 2017 (blast-radius); Martin 2017 kap. 13 (CCP) + kap. 7; Hunt/Thomas 1999 (DRY); Nygard 2011 (ADR-numrering); ADR 0042 (rad 21, Beslut B/C/D, notat 2026-05-17), ADR 0039 Beslut 3, ADR 0032 sync-skrivlast; CLAUDE.md §1/§9.2/§9.6/§9.7/§10.3; jobbpilot-design-principles regel 3/7.
