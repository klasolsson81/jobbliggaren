# ADR 0046 — FAS 3 scope-redefinition: Application Management-backbone byggd i Fas 1

**Datum:** 2026-05-17 (Accepted-flip 2026-05-18)
**Status:** Accepted — beslutsinnehållet är låst (senior-cto-advisor + Klas-GO 2026-05-17); Accepted-flip utförd 2026-05-18 på explicit Klas-GO vid den handoff-mandaterade FAS 3 Grind 1-STOPP-grinden efter godkänd live-verify av /ansokningar
**Kontext:** FAS 3-startpromptens arbetspremiss (greenfield TDD-konstruktion av Application-pipeline-state-machine) motbevisades av discovery 2026-05-17 — hela vertikalen byggdes redan under Fas 1. senior-cto-advisor identifierade dessutom en spec-konflikt i startpromptens FAS 3-kärna-påstående mot BUILD.md §18. Denna ADR redefinierar FAS 3-scope och dokumenterar en medveten fas-omallokering utan spec-edit.
**Beslutsfattare:** senior-cto-advisor (agentId a49fdd7992b3a7a0a — scope-redefinition, B/C/D-triage, spec-konflikt-fynd 2026-05-17); Klas Olsson (godkänd 2026-05-17 — valde "ADR + session-log, ingen spec-edit nu"; Accepted-flip 2026-05-18); Claude Code (A-leverans, implementation)
**Relaterad:** ADR 0001 (Clean Architecture med DDD), ADR 0022 (audit log-pipeline + marker-interface), ADR 0031 (failed cross-user access detection / cross-user-scoping), ADR 0032 + ADR 0023 (Hangfire-infrastruktur + Worker-pipeline — Ghosted recurring-jobb), ADR 0044 (test-coverage non-regression-ratchet — DoD-gate-mönster), ADR 0045 (performance-budgetar). Relaterade: BUILD.md §2.3 (kapabilitets-katalog) / §18 (fas-allokering, rad 1607–1613 + 1638–1643), CLAUDE.md §9.6 (in-block-fix vs TD), session-log 2026-05-17 FAS 3.

---

## Kontext

FAS 3-startpromptens arbetspremiss antog **greenfield TDD-konstruktion** av Application-pipeline-state-machine. Discovery 2026-05-17 (Explore-agent + `grep` + filläsning — ej gissning) visade att **hela vertikalen redan byggts under Fas 1**. Det som i sessionsspråket kallats "fake ansökningar i admin-audit" var i själva verket en komplett vertikal:

- **Domain** — Application-aggregat, ApplicationStatus (10-state SmartEnum med `AllowedTransitions`), FollowUp/ApplicationNote, 6 domain events
- **Application** — 5 commands, 3 queries, 5 DTOs, `DetectGhostedApplicationsJob` + `StaleApplicationSpecification`
- **Infrastructure** — EF-configs + 2 applicerade migrations
- **Api** — 7 endpoints
- **Worker** — Ghosted recurring-jobb (03:30 UTC)
- **Frontend** — 3 rutter + 5 komponenter
- **Test** — 12+ testfiler

senior-cto-advisor (agentId a49fdd7992b3a7a0a) fann en **spec-konflikt** i startpromptens FAS 3-kärna-påstående:

- Startpromptens påstående att "Avslags-analys/trender = FAS 3-kärna" är **felaktigt** mot BUILD.md.
- BUILD.md rad 1607–1613 (§18 Fas 3-milstolpe) listar **INTE** Avslags-analys.
- BUILD.md rad 1638–1643 (§18 Fas 6 Admin & Analytics) fas-allokerar `Avslags-analys-dashboard` **explicit till Fas 6**.
- Tolkningsregel: §2.3 är kapabilitets-katalog (*vad*), §18 är fas-allokering (*när*). **§18 är auktoritativ för när.**

## Beslut

> Beslut fattat av senior-cto-advisor (agentId a49fdd7992b3a7a0a), Klas-godkänt 2026-05-17. Status **Accepted** — Accepted-flip utförd 2026-05-18 på explicit Klas-GO.

### Beslut 1 — Redefinierad FAS 3-scope

FAS 3-scope = **A (RecordFollowUpOutcome-vertikal, in-block) + D (DoD-verifiering av befintlig 95%-vertikal, körs först)**.

### Beslut 2 — B (Påminnelser / notifikations-infra) deferras till Fas 5

Notifikations-leverans är en egen bounded context delad av Reminders / Calendar-sync / Gmail-loggning (alla Fas 5). Att bygga isolerat i Fas 3 med **en** konsument = YAGNI/CCP-brott (Martin, *Clean Architecture* kap. 13/34; Evans, *DDD* Bounded Contexts). Domänlogiken som **triggar** påminnelse finns redan (`StaleApplicationSpecification` + `DetectGhostedApplicationsJob`).

BUILD.md §18 rad 1610 listar "Påminnelser (Hangfire + notifikations-UI)" nominellt under Fas 3 — detta är en spec-inkonsekvens. **Klas valde att INTE editera BUILD.md §18 nu** (ingen spec-edit denna session). Fas-omallokeringen dokumenteras i denna ADR + session-log som **auktoritativ källa** tills BUILD.md ev. synkas i Fas 5.

> **Medveten dokumenterad avvikelse:** ADR 0046 (denna) och BUILD.md §18 rad 1610 är i konflikt om Påminnelser-fasen. Denna ADR är auktoritativ tills BUILD.md §18 rad 1610 synkas (flytta Fas 3→Fas 5) i Fas 5 — vilket kräver Klas approve-spec-edit.

### Beslut 3 — C (Avslags-analys/trender) bekräftas Fas 6

Per BUILD.md rad 1641 (redan spec-allokerad). Detta är **ej** en TD, **ej** denna ADR:s ändring — endast ett förtydligande att startpromptens FAS 3-kärna-påstående var felaktigt. BUILD.md §18 och denna ADR är överens om Fas 6 här.

### Beslut 4 — A levererad (FAS 3-leveranssession 2026-05-17)

Commit 78d3b14, CI grön (run 25998180368):

- `Application.RecordFollowUpOutcome` + `FollowUpOutcomeRecordedDomainEvent` + command/handler/validator + endpoint + frontend
- Rättade latent Fas 1-bugg: followUpOutcome-enum `Pending/Positive/Negative/Neutral` → korrekt `Pending/Responded/NoResponse`

Gates: dotnet-architect (agentId a1adb06cf1d1e8155), security-auditor GO, code-reviewer GO, design-reviewer APPROVED på kod-nivå.

## Alternativ som övervägdes

### Alt A — Redefiniera scope till A + D, deferra B till Fas 5 (VALT)
**För:**
- Respekterar att vertikalen byggdes i Fas 1 — ingen greenfield-dubblering
- B byggs där dess bounded context faktiskt bor (Fas 5, ≥1 konsument)
- C bekräftas Fas 6 enligt redan auktoritativ §18 rad 1641
- A är en äkta Fas 3-leverans (RecordFollowUpOutcome komplettering)
**Emot:**
- FAS 3 blir liten — kan ge intryck av "tom fas"
- Skapar en dokumenterad ADR↔BUILD.md-avvikelse (rad 1610) tills Fas 5

### Alt B — Bygg B (notifikations-infra) i Fas 3 enligt startpromptens bokstav
**För:**
- Ingen ADR↔BUILD.md-avvikelse — följer §18 rad 1610 bokstavligt
- "Större" Fas 3
**Emot:**
- YAGNI/CCP-brott: isolerad notifikations-infra med en konsument (Martin kap. 13/34)
- Bounded context (Reminders/Calendar/Gmail) splittras mot DDD-gränsen (Evans)
- Trigger-domänlogiken finns redan — bara leverans saknas, och leverans hör Fas 5

### Alt C — Bygg C (Avslags-analys) i Fas 3 enligt startpromptens premiss
**För:**
- Följer startpromptens FAS 3-kärna-påstående
**Emot:**
- Direkt motstridig BUILD.md rad 1641 (§18 Fas 6 auktoritativ för när)
- Startpromptens premiss var faktafel — inte ett designval att hedra

## Konsekvenser

### Positiva
- Scope speglar verkligheten (Fas 1-bygget) i stället för en felaktig greenfield-premiss
- B byggs i rätt bounded context med faktiska konsumenter — undviker YAGNI/CCP-skuld
- Fas-allokering korrigerad mot auktoritativ §18-tolkning (§2.3 vad / §18 när)
- A-leverans stänger en latent Fas 1-bugg (followUpOutcome-enum) som annars kvarstått

### Negativa
- **Dokumenterad ADR↔BUILD.md-avvikelse** (rad 1610) lever tills Fas 5-sync — risk att framtida läsare litar på §18 rad 1610 utan att se denna ADR. Mitigering: explicit avvikelse-not i Beslut 2 + session-log-cross-ref + steg-tracker-not vid Fas 3-stängning.
- FAS 3 blir liten (A+D). Fas-storlek är **inte** ett designvärde; korrekt fas-allokering är (Ford/Parsons/Kua, *Building Evolutionary Architectures*). Ej en reell negativ — noteras för transparens.
- Spec-sync skjuts till Fas 5 och kräver Klas approve-spec-edit då — en framtida åtgärd som kan glömmas. Mitigering: noterad som öppen punkt nedan.

## Implementation

- **A:** levererad commit 78d3b14, CI grön run 25998180368 (FAS 3-leveranssession 2026-05-17).
- **D:** DoD-verifiering av befintlig 95%-vertikal körs **först** vid Fas 3-stängning — separat Klas-DoD-verifiering.
- **design-reviewer VETO-villkor kvarstår:** rendered-screenshot-granskning (light+dark) = Fas 3-stängnings-gate (samma mönster som Fas 2), **ej** push-blocker.
- **Fas 3-stängning:** steg-tracker rad 32 uppdateras vid stängning.
- **Öppen för Fas 5:** BUILD.md §18 rad 1610 bör synkas (flytta Påminnelser-raden Fas 3→Fas 5) — kräver Klas approve-spec-edit.

## Referenser

- BUILD.md §2.3 (kapabilitets-katalog — *vad*) / §18 rad 1607–1613 (Fas 3-milstolpe), rad 1610 (Påminnelser-rad — avvikelse-punkt), rad 1638–1643 (Fas 6 Admin & Analytics), rad 1641 (Avslags-analys-dashboard Fas 6)
- CLAUDE.md §9.6 (in-block-fix vs TD — fas-regeln)
- ADR 0001 (Clean Architecture med DDD), ADR 0022 (audit log-pipeline), ADR 0031 (cross-user-scoping), ADR 0032 + ADR 0023 (Hangfire / Worker-pipeline — Ghosted-jobb), ADR 0044 (DoD non-regression-ratchet-mönster), ADR 0045 (performance-budgetar)
- session-log 2026-05-17 FAS 3
- Martin, *Clean Architecture* (kap. 13 — CCP, kap. 34 — gränser)
- Evans, *Domain-Driven Design* (Bounded Contexts)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (fas-allokering > fas-storlek)
