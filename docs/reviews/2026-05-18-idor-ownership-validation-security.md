# Security-audit: IDOR-ownership-validation (pre-FAS-4)

**Status:** ✓ INGEN pre-FAS-4-blocker — externa auditens "potentiella Major" UNDERKÄND
**Granskat:** 2026-05-18
**Agent:** security-auditor (agentId a2c916b2fc9b8cd43)
**On-disk HEAD-kontext:** `ad4758c` (ingen backend-auth-config rörd sedan)
**Auktoritet:** GDPR Art. 32, OWASP API1:2023 (BOLA), CLAUDE.md §5.4/§9.6, ADR 0031, ADR 0008

> CC-not: security-auditor-agentens egen filskrivning landade ej på disk
> (§9.4 paste-verifiering). Denna fil är CC:s trogna transkription av
> agentens verbatim-verdikt till review-trailen — ingen fabrikation,
> ingen omtolkning.

## Severity-räkning

| Severity | Antal |
|---|---|
| Critical | 0 |
| High | 0 |
| GDPR | 0 |
| Medium | 0 |
| Low | 0 (1 dokumentations-not, ej fynd) |

## Dom: (b) — benign konsoliderings-observation, INTE en security-Major

**Pre-FAS-4-blocker: NEJ.** Ingen ny TD (§9.6: ingen fas-dependency, inget hål).

Den externa auditens premiss är dubbelt felaktig:

1. **"IDOR-ownership-pipeline-behavior finns inte" — sant men irrelevant.** Avsaknaden är ett medvetet, ADR-dokumenterat arkitekturbeslut, inte ett gap. ADR 0031 §"Alternativ övervägda" Alt B avvisar explicit en `FailedAccessAuthorizationBehavior` ("Ny pipeline-yta för ett koncept som inte är audit. Bryter CCP, Martin 2017 kap. 13"). Ownership kan strukturellt inte generaliseras till en behavior — den kräver per-aggregat `.Where(... && JobSeekerId == jobSeekerId)` inuti varje query och kan inte uttryckas i ett message-agnostiskt pipeline-steg utan att duplicera domänkunskap dit. Korrekt Clean Architecture, ej teknisk skuld.
2. **"17 [Authorize]-handlers ad-hoc-skydd" — mönstret är de facto komplett, konsekvent, testtäckt.** Inget genuint IDOR-hål.

## Kod-exakt bevis per handler-klass

Identiskt korrekt tvåstegs-mönster överallt: (1) resolvera `currentUser.UserId` → `JobSeekerId` via `db.JobSeekers.Where(js => js.UserId == currentUser.UserId.Value)`; (2) aggregat-access scopas `.Where(x => x.Id == id && x.JobSeekerId == jobSeekerId)`. `jobSeekerId` härleds alltid från autentiserad claim, aldrig request-body/URL → ingen injektions-yta.

- **Applications:** GetApplicationById:38 (+ADR0031-logg:84-92), GetApplications:29, GetPipeline:32, TransitionTo:32, AddNote:32, AddFollowUp:32, RecordFollowUpOutcome:33, CreateApplication:24-31 — alla scopade.
- **Resumes:** GetResumeById:34, GetResumes:28, RenameResume:32, UpdateMasterContent:34, DeleteResume:33, DeleteResumeVersion:33, CreateResume:24-31 — alla scopade.
- **SavedSearches:** GetSavedSearch:35, ListSavedSearches:41, RunSavedSearch:36, UpdateSavedSearch:35, DeleteSavedSearch:35, CreateSavedSearch:22-29 — alla scopade.
- **JobSeeker/Account (claim-baserade, ingen id-param):** GetMyProfile:18, UpdateMyProfile:20, DeleteAccount:33, GetCurrentUser:14-20, Logout:16-22.
- **Invitations/Waitlist (admin-scopade):** Revoke/IssueInvitation via `IAdminRequest` → `AdminAuthorizationBehavior:27` (Roles.Admin); RedactRecruiterPii:40 `IAdminRequest` (Art. 17). Korrekt annan auktorisationsmodell.

## Pipeline-enforcement (ingen [Authorize] utelämnad)

`AuthorizationBehavior:16` kastar `UnauthorizedException` för varje `IAuthenticatedRequest` när ej autentiserad. Grep-verifierat: alla user-data-handlers bär `IAuthenticatedRequest`, alla admin-ytor `IAdminRequest`. Enda omarkerade = medvetet anonyma/system, dokumenterade i kod: MarkGhosted (Hangfire, "Får INTE exponeras via API utan RBAC"), Upsert/ArchiveExternalJobAd (system), RedeemInvitation:13 (anonym registrering, email från aggregat ej body). Inga PII-läckande anonyma ytor. Defense-in-depth-re-verifiering i DeleteAccount:16-26 + CreateResume:19-22 (mot ADR 0008/Fas-6-impersonation).

## Testtäckning

Cross-user-isolation-integrationstester för alla fem user-ägda kluster: ApplicationsCrossUserIsolationTests, ResumesCrossUserIsolationTests, SavedSearchesCrossUserIsolationTests, MeProfileCrossUserIsolationTests + per-handler ADR0031-logg `Received(1)`-assertions. ResumesCrossUserIsolationTests:104-122 (enumeration-attack slumpat versionId → 404 ej 403, ingen existens-läcka), :159-185 (sidoeffekt-frihet); MeProfileCrossUserIsolationTests:71-107 (payload-injection userId/jobSeekerId i body ignoreras). Djup BOLA-täckning.

## FAS 4-riskyte-bedömning

Risk-ytan växer inte oacceptabelt när FAS 4 lägger AI-handlers på samma user-data — förutsatt samma mönster. ADR 0031 §Mitigering noterar redan restrisken (per-handler-disciplin, regression-risk vid utveckling) — utvecklingsdisciplin, ej befintligt hål.

**Framåtblickande åtgärd (ej blocker, ej TD, ej pre-FAS-4-deliverable):** FAS 4:s första AI-handler på user-data ska (a) bära `IAuthenticatedRequest`, (b) scopa via `jobSeekerId`, (c) anropa `IFailedAccessLogger` vid mismatch, (d) få rad i respektive `*CrossUserIsolationTests`. Arkitektur-checklist-post för FAS 4-arbetet, hanteras av code-reviewer. Ownership-pipeline-behavior ska INTE byggas (ADR 0031 Alt B avvisat på CCP-grund — vore regression mot accepterat ADR).

## Sammanfattning

Externa auditens "potentiella pre-FAS-4-Major" UNDERKÄND. Ad-hoc-ownership komplett/konsekvent/claim-härlett/testtäckt (inkl. enumeration + payload-injection). Pipeline-behavior-avsaknad = medvetet ADR 0031-beslut. **Pre-FAS-4-blocker: NEJ. Ingen ny TD.** FAS 3.5/TD-13-scopet ska EJ belastas med ownership-pipeline-behavior.
