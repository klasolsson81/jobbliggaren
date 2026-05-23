# ADR 0053 — Detalj-paradigm: modal default + intercepting/parallel route

**Datum:** 2026-05-19
**Status:** Accepted
**Kontext:** JobbPilot v3 UI-refactor (HANDOVER-v3.md §0.2-veto, §0.5, §5.8, §9). Jobb-/ansökningsdetalj ska öppnas i pop-up-modal vid radklick, inte som egen route — samtidigt som djuplänk/SEO kräver äkta URL per annons.
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-flip-GO 2026-05-19)
**Supersedes:** route-only-detalj-paradigmet (detaljvy enbart som egen sida) — ersatt av modal+route-hybrid
**Relaterad:** ADR 0052 (v3 designsystem — modal-tokens), ADR 0042 (sök-yta-IA), ADR 0046 (Application Management-backbone i Fas 1); HANDOVER-v3.md §0.2/§0.5/§5.8/§9; Next.js docs Intercepting Routes + Parallel Routes

> **Livscykel-/proveniens-not:** Skriven 2026-05-19 av Claude Code (adr-keeper)
> på explicit Klas-begäran — medveten override av CLAUDE.md §9.4
> webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`).
> Besluts-substansen är transkriberad från HANDOVER-v3.md (auktoritativ
> designspec med §0-veto) + senior-cto-advisor-dom Fas 0 (Beslut 2). Inga
> nya beslut konstruerade. Status **Accepted** per Klas explicit
> Accepted-flip-GO 2026-05-19.

---

## Kontext

HANDOVER-v3.md §0.2 är ett veto: jobb- och ansökningsdetalj ska öppnas i en
pop-up-modal vid radklick i listan — **inte** genom navigering till en egen
route. HANDOVER §9 instruerar uttryckligen att modaler ska göras tillgängliga
snarare än att argumenteras bort.

Samtidigt kräver djuplänkning och SEO en äkta, delbar URL per annons (en
användare ska kunna dela en länk till en specifik jobbannons; sökmotorer ska
kunna indexera den). En ren client-state-modal utan URL kan inte uppfylla det.

senior-cto-advisor (Fas 0, Beslut 2) avgjorde route-paradigmet mellan tre
varianter (se Alternativ övervägda).

## Beslut

### Beslut 1 — Modal default vid listklick

Radklick i jobb- och ansökningslistor öppnar detaljen i en modal, inte en
sidnavigering.

### Beslut 2 — Next.js Intercepting + Parallel Routes

- Intercepting Route `(.)jobb/[id]` + Parallel Route `@modal`-slot.
- **Soft-nav** (klick i listan): detaljen renderas i `@modal`-slotten som
  modal.
- **Hard-nav / refresh / share**: samma presentationskomponent renderas
  fullskärm på den äkta URL:en.
- Gäller jobb **och** ansökningar. Befintliga
  `/ansokningar|cv|sokningar/[id]`-routes består för djuplänk.

### Beslut 3 — Modal-presentation (v3-tokens, ADR 0052)

- Max-bredd 760px, max-höjd 86vh, radius 8px (`--jp-r-lg`).
- Scrim `rgba(8, 23, 48, .55)`.
- Animation: fade 140ms + rise 200ms.
- Stängs med ESC **och** klick på scrim.

### Beslut 4 — Tillgänglighet adderas (ej argumenteras bort)

Focus-trap + focus-return läggs till (finns inte i prototyp-JSX). Per
HANDOVER §9: gör modalerna tillgängliga — argumentera inte mot
modal-paradigmet. (Korsref ADR 0047 design-reviewer flödesbegriplighet,
ADR 0041 dark-modal-border.)

### Beslut 5 — Match-score i jobbmodal

> **⚠ AMENDAD 2026-05-19 — se [Amendment 2026-05-19 — Match-presentation Fas-4-gated](#amendment-2026-05-19--match-presentation-fas-4-gated) nedan. Originaltexten nedan bevaras oförändrad; amendment-lagret gäller Fas-tillämpningen (presentation gäller från Fas 4; Fas 3-modalen renderar ingen match-sektion).**

Match-score visas som mono `"92% match"` + 3-nivå-förklaring.
**Aldrig** en rund procent-cirkel (HANDOVER §0.5 / §5.8).

## Amendment 2026-05-19 — Match-presentation Fas-4-gated

> **Amendment 2026-05-19 (Klas-godkänd, CTO-triagead — memory feedback_adr_mechanism_vs_env_phase_triage):** ADR 0053 Beslut 5:s match-score-presentation (mono "92% match" + 3-nivå-förklaring, aldrig procent-cirkel) gäller **från Fas 4** när CV-vs-annons-match-domänen finns. Real `JobAdDto` (id, title, companyName, description, url, source, status, publishedAt, expiresAt, createdAt, isNew) saknar match/requirements/occupation/location — dessa var v3-prototyp-mock. **Fas 3-jobbmodalen renderar ingen match-sektion, inga requirements, ingen occupation/location.** HANDOVER §0.5/§5.8-vetot ("match-score finns kvar, aldrig cirkel") uppfylls genom **frånvaro, inte mock-data** (Evans DDD — UI får ej hallucinera ett domänbegrepp som inte finns; YAGNI). Footer i Fas 3 = **endast** `Öppna annonsen [url]`. `Spara annons` / `Har ansökt` deferrade till fas där FE-action-bryggan (job-ad → saved-search/application-domän) byggs — ingen disabled-knapp-teater (falsk affordance bryter CLAUDE.md §5.2/civic-ton). **Fas 3-modalens fält-set:** title · companyName · status-pill · description · publicerad / sista ansökningsdag (mono) · annons-ID · footer "Öppna annonsen". Match/requirements/Spara/Har-ansökt = explicit fas-deferral (CLAUDE.md §9.6 saknad-funktion-dependency), **EJ TD** (ofödd Fas-4-domän, ej tech-debt-dumpning per §9.7).

> **Amendment-proveniens:** Klas-godkänd amendment-prosa verbatim 2026-05-19 (memory `feedback_klas_can_override_adr_verbatim_source` — explicit Klas-override av §9.4 webb-Claude-verbatim-konvention). Grundad i senior-cto-advisor F3-plan-design-dom + verifierad data-/fas-verklighet (real `JobAdDto` saknar match/requirements/occupation/location; match-scoring = ofödd Fas-4 AI-domän). ADR förblir **Accepted** — amendment, ej supersession; originalt Beslut 5 + route-only-supersession-paradigmet består.

## Amendment 2026-05-23 — Spara/Har-ansökt-knappar Accepted i F6 P5 Punkt 2

> **Amendment 2026-05-23 (Klas-godkänd, CTO-triagead — agentId ad76c06a752275b17):** ADR 0053 Amendment 2026-05-19 deferrade `Spara annons` / `Har ansökt`-knappar till "fas där FE-action-bryggan (job-ad → saved-search/application-domän) byggs". F6 P5 Punkt 2 (2026-05-23) **ÄR den fasen** — FE-action-bryggan byggs nu. Deferringen lyfts: båda knapparna är **Accepted** i modal-footer för F6 P5 Punkt 2.
>
> **Backend (PR1 commit c015918 + PR3 commit a187467):**
> - `SavedJobAd`-aggregat skapas som fullt aggregate root (paritet med `RecentJobSearch` per CTO Val 1 — ADR 0060-mönstret), strongly-typed soft-reference utan DB-FK (ADR 0011) mot JobSeekerId + JobAdId, hard-delete-semantik, ingen audit-bypass (rena `IAuditableCommand`-flöden).
> - `CreateApplicationFromJobAdCommand` ny endpoint `/from-job-ad/{jobAdId}` (per CTO Val 3 SRP — separat från befintlig `CreateApplicationCommand` så manuella och job-ad-drivna flöden inte deluniformeras). `Application.Create` får `JobAdId` som strongly-typed soft-reference; **ingen snapshot** av annonsinnehåll persisteras till Application-aggregatet (ADR 0048 Beslut d respekteras — cross-aggregat-länk löses via read-join i query-vägen, inte via snapshot-duplicering i write-modellen, per CTO Val 2).
>
> **Frontend (PR2 commit 4afc081 + PR4):**
> - `SaveJobAdToggle` i modal-footer (PR2): optimistic UI mot `POST/DELETE /api/saved-job-ads/{jobAdId}`, `aria-pressed`-state, toast vid fel som rullar tillbaka optimistic-mutationen. Knappen ersätter inte "Öppna annonsen"-länken — båda samexisterar i footer.
> - `HarAnsoktButton` i modal-footer (PR4): optimistic-create mot `/from-job-ad/{jobAdId}`, toast på success med länk till `/ansokningar/{id}` (per CTO Val 4 Variant A — ADR 0053 modal-footer + toast, inte sekundär modal/inline-formulär; bevarar "en presentationskomponent två renderingskontexter"-disciplinen från Beslut 2).
>
> **Vad amendment INTE rör:** Match-presentation kvarstår Fas-4-gated per Amendment 2026-05-19 (real `JobAdDto` saknar match/requirements/occupation/location — Fas 3-modalen renderar fortfarande ingen match-sektion). Footer-fält-set i Fas 3 utökas till: `Öppna annonsen [url]` · `Spara annons` (PR2) · `Har ansökt` (PR4). Övriga modal-fält oförändrade.
>
> **Amendment-proveniens:** Klas-godkänd amendment-prosa verbatim 2026-05-23 (memory `feedback_klas_can_override_adr_verbatim_source` — explicit Klas-override av §9.4 webb-Claude-verbatim-konvention för PR4-amends). Grundad i senior-cto-advisor-dom (agentId ad76c06a752275b17 2026-05-23, 4 multi-approach-val: Val 1 SavedJobAd fullt aggregate root, Val 2 Application.Create med JobAdId NO snapshot, Val 3 separat `/from-job-ad/{jobAdId}`-endpoint, Val 4 modal-footer + toast Variant A). ADR förblir **Accepted** — additivt tilläggslager, ej supersession; originalt Beslut 5 + Amendment 2026-05-19 (match-presentation Fas-4-gated) + route-only-supersession-paradigmet består.

## Konsekvenser

### Positiva

- **En** presentationskomponent renderas i två kontexter (modal vid soft-nav,
  fullskärm vid hard-nav) — ingen duplicerad detaljvy.
- Äkta delbar/indexerbar URL per annons bevaras (djuplänk + SEO).
- Modal-UX matchar v3-målbild utan att offra URL-kanonikalisering.

### Negativa + mitigering

- **Störst arkitekturyta** av v3-besluten; RSC/client-boundary-känsligt
  (Intercepting/Parallel Routes har subtil server/client-gräns).
  Mitigering: `pnpm build`-gate är kritisk i F3 (AGENTS.md) — boundary-fel
  fångas av build innan commit.
- Tillgänglighet (focus-trap/-return) måste byggas, finns ej i prototyp.
  Mitigering: explicit Beslut 4 + design-reviewer-mandat (ADR 0047).

## Alternativ övervägda

### Alternativ A — Modal + Intercepting/Parallel Route (valt)

Se Beslut 2.

**Valt:** enda varianten som ger modal-UX + äkta delbar URL utan
URL-dialekt-splittring. (Källa: senior-cto-advisor Beslut 2; Next.js docs
Intercepting/Parallel Routes.)

### Alternativ B — Client-state-modal utan URL

Modalen är ren klient-state, ingen URL ändras.

**Avvisat:** bryter djuplänk-/SEO-intentionen — ingen delbar eller
indexerbar adress per annons. (Källa: senior-cto-advisor Beslut 2.)

### Alternativ C — Query-param-modal + separat path-route

`?modal=<id>` för modal plus separat `/jobb/[id]`-path för djuplänk.

**Avvisat:** två URL-dialekter för samma resurs — bryter DRY och
SEO-kanonikalisering (samma annons nås via två icke-ekvivalenta URL-former).
(Källa: senior-cto-advisor Beslut 2.)

## Implementationsstatus

- **Beslut accepterat 2026-05-19** (Klas Accepted-flip-GO).
- Implementation: JobbPilot v3 UI-refactor F3 (Intercepting/Parallel Routes,
  delad presentationskomponent, focus-trap/-return). `pnpm build`-gate
  obligatorisk per AGENTS.md innan commit.
- Befintliga `/ansokningar|cv|sokningar/[id]`-routes lämnas orörda som
  djuplänk-target.
