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

Match-score visas som mono `"92% match"` + 3-nivå-förklaring.
**Aldrig** en rund procent-cirkel (HANDOVER §0.5 / §5.8).

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
