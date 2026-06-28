# ADR 0040 — Smart CV-härlett filter ovanpå SavedSearch (framtida fas)

**Datum:** 2026-05-16
**Status:** Superseded by ADR 0076 (matchnings-pivot "tänd matchningen" subsumerar det smarta CV-härledda filtret; reconcile 2026-06-28). AI-premissen (`IAiProvider` + Bedrock) är dessutom upplöst av ADR 0071 (NOLL AI). _Historiskt:_ Proposed 2026-05-16 (framtida fas — Fas 4+; senior-cto-advisor-vägning 2026-05-16, Klas-bekräftad produktinriktning 2026-05-16)
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0039 (SavedSearch-aggregat — detta bygger ovanpå, supersederar ej), ADR 0032 (JobTech occupation-taxonomi), BUILD.md §18 Fas 4 (AI-lager), CLAUDE.md §5.3 (AI EU-routing/GDPR), Resume-domän (Fas 1)

---

## Kontext

Klas grundtanke (2026-05-16): utöver det vanliga manuella sparbara filtret (ADR 0039, Fas 2) vill JobbPilot ett **smart CV-baserat filter**. Användaren skapar ett filter enkelt utifrån sitt CV — AI härleder vilka yrken (JobTech occupation-concept-ids) som ska filtreras, med enkla klargörande frågor om något är oklart och en användarbekräftelse ("bekräftar du dessa val / vill du lägga till något?"). En användare har flera CV-spår (t.ex. ett CV för systemutveckling, ett för tid som industriarbetare) → ett smart filter per spår.

`ResumeContent` (Fas 1) innehåller idag **ingen** occupation-taxonomi — bara fritext (Experiences, Skills, Summary). Smart filter kräver alltså ett AI-steg som *översätter* CV-fritext → JobTech occupation-concept-ids. Det är en Anti-Corruption Layer + härlednings-pipeline (Evans 2003 kap. 14), inte en formvariant på `SearchCriteria`.

## Beslut (inriktning — detaljdesign vid faststart)

1. **Båda filtertyperna, sekventiellt.** Vanligt sparbart filter levereras i Fas 2 (ADR 0039, oförändrat). Smart CV-filter byggs **ovanpå samma `SavedSearch`-aggregat** — det producerar en `SavedSearch` vars criteria är AI-härledd, inte en parallell andra-modell. Additiv evolution (Ford/Parsons/Kua 2017).
2. **Fas 4+.** Smart filter förutsätter hela AI-stacken: `IAiProvider` + Bedrock EU-routing, prompts i `/prompts/*.prompt.md`, token-tracking, GDPR-samtycke (CLAUDE.md §5.3) + Resume-domänen (Fas 1, finns). Levereras tidigast Fas 4.
3. **ADR 0039 amendas INTE.** `SearchCriteria` förblir låst som ADR 0039 Beslut 3 (singel `Ssyk`, jsonb, värde-equality). Smart-filter-domänens rätta form (sannolikt separat aggregat-koncept med `DerivedFromResumeId`-referens + occupation-set, med egna invarianter — en härledd sökning ogiltigförklaras när källan-CV:t ändras; en manuell ändras av användaren) avgörs när Fas 4 ger den konkreta AI-designen. Form-evolution sker via supersession-ADR, inte retroaktiv VO-mutation.
4. **Transparens som civic-utility-krav.** AI:ns yrkesurval måste visas och bekräftas av användaren innan filtret sparas (Klas grundtanke + CLAUDE.md §1 — seriöst/pålitligt; ingen svart låda).

## Konsekvenser

**Positiva:** Hög användarnytta (ett CV-spår = ett färdigt filter); delar fundament med Fas 2 (ingen kod-dubblering); produktinriktningen är beslutad och dokumenterad så den inte tappas.

**Negativa + mitigering:** Smart filter levereras inte nu. *Mitigering:* grundtanken bevarad här + BUILD.md §18 Fas 4-backlog. Trolig framtida `SavedSearch`/`SearchCriteria`-evolution. *Mitigering:* normal ADR-supersession-livscykel (Nygard 2011), inte skuld — billigare än att gissa formen nu och baka in den i jsonb-migration.

## Alternativ som övervägdes

- **Forward-compat multi-occupation i `SearchCriteria` nu (`IReadOnlyList<string> Ssyk`):** Avvisat. Bryter VO-värde-equality-invarianten ADR 0039 Beslut 3 vilar på (referens-equality i jsonb-collection); spekulativ generalisering vars slutform är okänd utan Fas 4-design (Beck/Fowler YAGNI); sparar en migration man sannolikt inte vill ha (rätt form är troligen separat aggregat-koncept, SRP — Martin 2017 kap. 7).
- **Smart filter i Fas 2:** Avvisat. Vore fas-skifte (CLAUDE.md §9.2) och skulle blockera sista Fas 2-leverabeln på en hel oimplementerad AI-fas.
- **Bara ett filter (skippa det vanliga eller det smarta):** Avvisat. Båda har distinkt användarvärde; Klas grundtanke (CV-spår-baserade filter) är hög nytta och förenlig med civic-utility när AI-valet är användarbekräftat.
- **ADR 0039-amendment / TD-post:** Avvisat. ADR-immutabilitet (Nygard 2011); detta är oformad framtida feature, inte uppskjutet arbete inom känd form (CLAUDE.md §9.6/§9.7 — tech-debt-matrisen är ingen dumpningsplats).

## Implementationsstatus

Ej påbörjad. Proposed — inriktning Klas-bekräftad 2026-05-16, men detaljdesign (aggregat-form, härlednings-pipeline, re-derivation-trigger vid CV-ändring, prompt-design) görs när Fas 4 startar. Flippas till Accepted först då, efter Klas-GO på den konkreta designen.

---

*Referenser: Evans, DDD (2003) kap. 5, 14; Vernon, IDDD (2013) kap. 6; Martin, Clean Architecture (2017) kap. 7; Beck/Fowler — YAGNI; Ford/Parsons/Kua, Building Evolutionary Architectures (2017); Nygard, Documenting Architecture Decisions (2011). ADR 0039; CLAUDE.md §1, §5.3, §9.2, §9.6, §9.7.*
