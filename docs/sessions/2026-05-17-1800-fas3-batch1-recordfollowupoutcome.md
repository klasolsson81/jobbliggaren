---
session: FAS 3 batch 1 — scope-redefinition + RecordFollowUpOutcome-vertikal
datum: 2026-05-17
slug: fas3-batch1-recordfollowupoutcome
status: levererad, CI-grön, pausad på Klas begäran → återupptagen → docs-synk
commits:
  - 78d3b14 feat(applications) RecordFollowUpOutcome-vertikal + rättad FollowUpOutcome-enum
  - <docs-commit session-end>
---

# FAS 3 batch 1 — Application Management

## Mål (startprompt)
FAS 3 Application Management utan AI. Startprompten antog greenfield TDD-konstruktion
av Application-pipeline-state-machine (domän → handlers → migration → endpoints →
frontend).

## Strategiskt premiss-brott (sessionens kärnhändelse)
Discovery (Explore-agent + grep + filläsning, ej gissning — startprompten varnade
explicit "kartlägg INNAN design, gissa ej") visade att **hela vertikalen redan
byggts i Fas 1** ("fake ansökningar i admin-audit" var en komplett vertikal):
Domain (Application-aggregat, ApplicationStatus 10-state SmartEnum med
AllowedTransitions, FollowUp/ApplicationNote, 6 events), Application (5 commands,
3 queries, 5 DTOs, DetectGhostedApplicationsJob + StaleApplicationSpecification),
Infrastructure (EF-configs + 2 applicerade migrations), Api (7 endpoints), Worker
(Ghosted recurring 03:30 UTC), Frontend (3 rutter + 5 komponenter), 12+ testfiler.

Per CLAUDE.md §9.3 (gissa aldrig om feature ska finnas) + §9.2 (strategiska
sessions-transitioner) eskalerades detta: ingen kod skrevs förrän Klas avgjort
den redefinierade scopen.

## senior-cto-advisor `a49fdd7992b3a7a0a` (decision-maker, CC gav ej egen rek)
Fann **spec-konflikt**: startpromptens påstående "Avslags-analys/trender =
FAS 3-kärna" är felaktigt mot BUILD.md — §18 rad 1607–1613 (Fas 3-milstolpe)
listar INTE Avslags-analys; rad 1638–1643 (Fas 6 Admin & Analytics) fas-allokerar
`Avslags-analys-dashboard` explicit Fas 6. §2.3 = kapabilitets-katalog (vad),
§18 = fas-allokering (när), §18 auktoritativ.

CTO-dom: redefinierad FAS 3 = **A (RecordFollowUpOutcome in-block) + D
(DoD-verifiering av befintlig 95%-vertikal, körs FÖRST)**; **B Påminnelser →
Fas 5** (notifikations-leverans = egen bounded context delad med Calendar-sync +
Gmail-loggning, alla Fas 5; bygga isolerat nu = YAGNI/CCP-brott, Martin Clean
Architecture kap. 13/34, Evans DDD Bounded Contexts; domänlogiken som triggar
påminnelse finns redan via StaleApplicationSpecification + DetectGhostedJob);
**C Avslags-analys → Fas 6** (BUILD.md rad 1641, ej ändring, förtydligande).

## Klas-beslut (AskUserQuestion)
1. **CTO-ramen som den är** — A+D in, B→Fas5, C→Fas6, non-stop efter GO.
2. **ADR + session-log, ingen BUILD.md §18-spec-edit** — fas-omallokeringen
   dokumenteras i ADR 0046 + denna logg som auktoritativ källa; BUILD.md §18
   rad 1610 (listar Påminnelser nominellt Fas 3) lämnas orörd, medveten
   dokumenterad avvikelse tills ev. Fas 5-sync.

## D — DoD-verifiering av befintlig vertikal (KLAR)
- Build 0 warn/0 err. `scripts/coverage.sh` full svit **1160/1160** (0 failed,
  0 skipped), arch-tests gröna.
- ADR 0044 per-lager-golv lokalt replikerat (jq): **alla PASS** — Domain
  95.3/93.1, Application 97.7/91.1, Infra 84, Api 93.7. Worker observe-only.
- Perf ADR 0045 orört (observe-only Fas 1). Frontend lint 0 err (3 pre-existing
  warnings), tsc clean, vitest 387 grön.
- Lighthouse/a11y-rendering + manuell dev-test = Klas fas-stängnings-DoD.

## A — RecordFollowUpOutcome-vertikal (KLAR, commit 78d3b14, CI-grön)
`FollowUp.RecordOutcome(outcome, clock)` fanns i domänen men ingen
command/handler/endpoint/UI wire:ade den. BUILD.md §2.3 listar "utfall" som del
av FollowUp-loggning → ofullständig vertikal i nuvarande fas → in-block (§9.6,
CTO Beslut 5, ej TD).

**dotnet-architect `a1adb06cf1d1e8155` (5 beslut, INNAN kod):**
1. Aggregatroten medierar: `Application.RecordFollowUpOutcome(FollowUpId,
   FollowUpOutcome, IDateTimeProvider)`; handler rör aldrig FollowUp direkt
   (Evans/Vernon aggregate-boundary; `FollowUp.Create` är internal by-design).
2. Saknad child → `Result.Failure(new DomainError("Application.FollowUpNotFound",
   ...))`, ej exception (domän-Result, ej handler-NotFoundException).
3. Nytt `FollowUpOutcomeRecordedDomainEvent(ApplicationId, FollowUpId,
   FollowUpOutcome, OccurredAt)` — audit-paritet med FollowUpAdded (ADR 0022).
4. **Ingen IsClosedForActivity-guard** (medvetet, skiljer från AddFollowUp):
   en Ghosted ansökan måste kunna få pending follow-ups markerade NoResponse,
   annars olösbar invariant. Enda invarianten = Pending-idempotens i
   FollowUp.RecordOutcome. Regressionstest `..._WhenApplicationGhosted_...`.
5. Command `ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>`;
   EventType "Application.FollowUpOutcomeRecorded", retur non-generic Result.

**TDD:** test-writer skrev RÖD svit först (11 domän + 6 handler + validator +
5 API-int + cross-user). Implementation grön.

**Detour 1 — `ApplicationId` ambiguous** mot `System.ApplicationId` → fully-
qualified (samma som AddFollowUp-mallen).

**Detour 2 — API-integration `POST_outcome_..._success` RÖD.** Rotorsak:
handler-query laddade `app` utan `.Include(a => a.FollowUps)`; ingen lazy-
loading i repot → `_followUps` tom vid existing-child-mutation (till skillnad
från AddFollowUp som adderar nytt child via change-tracker). Fix: `.Include`.
Alla 308 API-int gröna.

**Latent Fas 1-bugg rättad in-block (§9.6):** `followUpOutcomeSchema` +
`FOLLOW_UP_OUTCOME_LABELS` var felaktigt `Pending/Positive/Negative/Neutral`;
backend `FollowUpOutcome` SmartEnum är `Pending/Responded/NoResponse`. Aldrig
exponerad förut (outcome aldrig settbar); A gör den settbar → GET-parse skulle
ha kraschat. Synkad i 6 frontend-touchpunkter + vitest.

**Frontend:** `recordFollowUpOutcomeAction` (server-action, paritet
addFollowUp), zod-schema, inline `RecordFollowUpOutcomeForm` (visas endast vid
`fu.outcome === "Pending"`), civic-utility-copy.

## Gates
- security-auditor `a581b9c37cb4e7810`: **GO** 0 Crit/0 High/0 GDPR/0 Med/1 Low
  (IDOR strukturellt stängd — FollowUp via aggregatrot, ej global query;
  cross-user ADR 0031 paritet; ingen ny PII).
- code-reviewer `a6c8732116205c911`: **GO** 0 Block/0 Major/0 Minor.
- design-reviewer (`aa38616d8bccdd7cd` → re-review `aece8eb869c885df1`):
  initial CHANGES-REQUESTED 2 Major (M1 felmeddelande ej aria-kopplat, M2
  text-danger-600→700) + 3 Minor → M1/M2 + m1/m2 in-block-fixade i BÅDA
  formulären (record-follow-up-outcome-form + add-follow-up-form per §9.6) →
  **APPROVED kod-nivå** 0/0/1 (m3 date-fns medvetet uppskjuten). **VETO-villkor
  kvarstår:** rendered-screenshot light+dark = Fas 3-stängnings-gate (som Fas 2,
  ej push-blocker).

Commit `78d3b14` path-scoped (memory `feedback_pathspec_commit_parallel_cc` —
explicit pathspec, `.lock` + agent-roster-doc korrekt exkluderade), pre-push
gröna, CI run `25998180368` **success** (2m40s). Klas pausade efter push;
återupptog för ADR + docs-synk.

## ADR 0046 (Proposed)
`docs/decisions/0046-fas3-scope-redefinition-application-management-backbone-in-fas1.md`
— FAS 3 scope-redefinition + B→Fas5/C→Fas6, dokumenterar medveten avvikelse
mot BUILD.md §18 rad 1610. Index uppdaterat. **Accepted-flip = Klas-STOPP.**

## Återstår (nästa session / Klas)
1. **ADR 0046 Proposed → Accepted** = explicit Klas-GO.
2. **design-reviewer VETO-villkor:** rendered-screenshot-granskning (light+dark,
   `pnpm visual-verify` — kräver deploy = Klas-GO, som Fas 2) före Fas 3-stängning.
3. **Fas 3-stängning** = separat Klas-DoD-verifiering (steg-tracker rad 32
   → "Pågående", stängs vid Klas-verifiering).
4. Startprompt-fel uppströms: C var ej FAS 3-kärna (BUILD.md rad 1641).
5. Pending från tidigare (oförändrat): Dependabot-PR #5 re-merge; valfri
   docs-keeper hook-bypass-skärpning; §9.2-spec-edits kräver Klas
   approve-spec-edit.sh.

## Beslut & lärdomar
- Strategiskt premiss-brott hanterat per §9.3/§9.2 — CTO-framing före Klas-STOPP,
  CC gav ej egen rek (memory `feedback_cto_decides_multi_approach`).
- En "stor fas" kan vara nästan tom om dess substans byggdes tidigare;
  korrekt fas-allokering > fas-storlek (Ford/Parsons/Kua).
- Latent enum-desync fångad just för att A gjorde fältet settbart — pre-existing
  cross-ref-risk (MEMORY badge-text) nu testtäckt.
- Inga nya TDs (§9.6 — allt in-fas/in-block).
