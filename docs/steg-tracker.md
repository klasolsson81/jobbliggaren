# JobbPilot — STEG-tracker

> **Version:** 1.1
> **Senast uppdaterad:** 2026-05-08
> **Roll:** permanent översikt över STEG- och fas-progression.

Kompletteras av:
- `docs/current-work.md` — aktiv session-state
- `docs/sessions/` — per-session-loggar
- `docs/decisions/` — arkitekturbeslut
- `docs/tech-debt.md` — teknisk skuld

---

## 1. Översikt

JobbPilot:s utvecklingsbana spårar två dimensioner:

- **Faser** — strategiska tidsblock per BUILD.md §18. Nio faser plus Fas 9+ (efter klass-launch).
- **STEG** — tekniska arbetsenheter inom faser. Numrering är historisk och behålls oförändrad även när faserna inte mappar 1:1 mot STEG-gränserna.

Mellan-arbete (upptakter, cleanup-passningar, disciplin-uppgraderingar) är inte STEG men dokumenteras i §4.

## 2. Fas-översikt (per BUILD.md §18)

| Fas | Namn | Tidsuppskattning | Milstolpe | Status |
|-----|------|------------------|-----------|--------|
| Fas 0 | Foundation | ~2 v | Registrera + logga in på dev.jobbpilot.se | Lokalt klar¹ |
| Fas 1 | Core Domain | ~3 v | CV manuellt + "fake" ansökningar i admin-audit | Pågående |
| Fas 2 | JobTech Integration | ~2 v | Söka jobb på Platsbanken via appen, spara sökningar | Planerad² |
| Fas 3 | Application Management | ~2 v | Fullständig ansökningshantering (utan AI) | Planerad |
| Fas 4 | AI Layer | ~3-4 v | Alla AI-features end-to-end + 14 dagar dogfood | Planerad |
| Fas 5 | Integrationer | ~2 v | Gmail auto-loggar, intervjuer i Google Calendar | Planerad |
| Fas 6 | Admin & Analytics | ~2 v | Admin-panel komplett | Planerad |
| Fas 7 | Internal Beta | ~2 v | 3 användare aktivt 14 dagar | Planerad |
| Fas 8 | Klass-launch | ~1 v | 20 klasskamrater onboardade — v1 klar | Planerad |
| Fas 9+ | Efter klass-launch | — | Mobil, Kanban, intervjuträning, LinkedIn, Stripe, Chrome ext m.m. | Framtid |

**Totalt:** ~20 veckor till klass-launch (mjuk uppskattning, inga hårda deadlines).

¹ Fas 0:s lokala milstolpar uppfyllda (Clean Arch-solution, Identity, Next.js, design system). Kvarvarande för full Fas 0-stängning enligt BUILD.md §18: första deploy till dev.jobbpilot.se, GitHub Actions CI/CD verifierad, bootstrap-IAM-user raderad.

² Fas 2 är blockerad till ADR 0005 (go-to-market) är beslutad och kostnadsskydd implementerat (Budget Actions, `registrations_open`-flagga, rate limiting, runbook `docs/runbooks/aws-cost-recovery.md`) per BUILD.md §18.

## 3. STEG-historik

STEG-numrering följer faktisk arbetsutveckling och mappar inte exakt mot fas-gränserna i BUILD.md §18. Se §7 för numreringsfotnot.

### Klara

| STEG | Fas | Beskrivning | Sessions |
|------|-----|-------------|----------|
| Pre-STEG | Fas 0 | Research, plan, design-research | Sessions 1, 2, 2.5 |
| Pre-STEG | Fas 0 | AWS, Docker, agents, skills, hooks, GitHub, docs | Sessions 3-5 |
| STEG 1 | Fas 0 | .NET Solution-uppsättning + ADR 0008-0011 | Session 6 |
| STEG 2 | Fas 0 | Domain/Infrastructure/Application/API — JobAd aggregate (35 tester) | Session 7 |
| STEG 3 | Fas 1 | Auth-stack (Identity + JWT, ADR 0012-0014) + JobSeeker aggregate (75 tester) | Session 8 |
| STEG 4a | Fas 0 | Frontend bootstrap (Next.js 16, civic-tokens, shadcn nova, ADR 0015-0016) | Session 9 |
| STEG 4b | Fas 1 | Session-auth backend (ISessionStore, SessionAuthenticationHandler, IAuthAuditLogger, ADR 0017-0018) + frontend auth (login/register/me-sidor, /(app)-layout, 153 tester) | Session 4b.1, 4b.2 |
| STEG 5 | Fas 1 | Application-aggregat — domän (SmartEnum state machine, FollowUp, ApplicationNote), EF Core, 5 commands, 3 queries, 7 API-endpoints, 280 tester (53 nya) | 2026-05-07 |
| STEG 6 | Fas 1 | Frontend /ansokningar — pipeline-tabell, ny-ansökan, detaljvy, transitionsformulär, Server Actions, Zod v4, 28 Vitest + 13 Playwright E2E | 2026-05-08 |

### Pågående

(inga aktiva STEG just nu — se §5)

### Planerade

| STEG | Fas | Beskrivning | Status |
|------|-----|-------------|--------|
| STEG 7 | Fas 1 | Ej planerat — kräver plan-design med webb-Claude. Kandidater: Resume-aggregat (domain + EF + CQRS + API) eller Hangfire-setup + GhostedDetectionJob | Behöver planeras |

STEG 7 fastställs i dedikerad plan-design-chatt med webb-Claude.

## 4. Mellan-arbete

Cleanup-passningar, disciplin-uppgraderingar och dokumentations-arbete som inte hör till någon enskild STEG. Klas använder begreppet "Fas 0.x" för cleanup-arbete mellan officiella faser.

| Period | Beskrivning | Källor | Status |
|--------|-------------|--------|--------|
| 2026-05-07 | Upptakt: ADR 0019 etablerad (solo direct-push), CLAUDE.md uppgraderad (§9.4 discovery, §9.5 web-search, §9.2 utökad), tech-debt.md etablerad, hook-vakt fix:ad för Agent SDK-läget, precompact-rapporter exkluderade från versionshantering | Webb-chats: ADR 0019-chatt + Moment 1-5-chatt | Pågående (Moment 5 = denna tracker) |

## 5. Aktuellt

**STEG-fokus:** STEG 5 och STEG 6 klara (2026-05-07/08). Inga aktiva STEG.

**STEG 5** (Application-aggregat): Komplett — domain, EF Core, CQRS, 7 API-endpoints, 280 tester.
Discovery 2026-05-08 bekräftade att inget arbete avbröts eller saknas.

**STEG 6** (Frontend /ansokningar): Komplett — pipeline-tabell, ny-ansökan, detaljvy, 28 Vitest + 13 Playwright E2E. Code review + security audit genomförda. TD-10/TD-11/TD-12 dokumenterade.

**Nästa:** STEG 7 kräver plan-design med webb-Claude. Se §6.

För session-detaljer och commit-historik, se `docs/current-work.md`.

## 6. Nästa STEG

**STEG 7 — kräver plan-design**

Beslutas i dedikerad webb-Claude-chatt. Kandidater baserade på BUILD.md §18 Fas 1:

**Alt A — Resume-aggregat (sannolikt)**
- Resume-aggregat + ResumeVersion-entitet (domain + EF + migrations)
- Commands: CreateResume, UploadResumeVersion, DeleteResume
- Queries: GetResumes, GetResumeById
- API-endpoints under `/api/v1/resumes`
- Frontend: `/cv`-sidor (lista, uppladdning, detaljvy)
- Tests: domain + application + integration + Playwright

**Alt B — Hangfire-setup + GhostedDetectionJob**
- Hangfire-infrastruktur (Worker-projekt)
- `GhostedDetectionJob` som kör `MarkGhostedCommand` för stale applications
- Konfiguration: `ghosted_threshold_days` per JobSeeker (default 21)
- Tests: Hangfire-testinfrastruktur

**Rekommendation:** Alt A (Resume) ger mer värde för Fas 1-milstolpen ("CV manuellt"), men Klas beslutar i plan-design-chatten.

## 7. Numreringsfotnot

Faktisk historisk numrering följer projektets utveckling, inte BUILD.md §18:s fas-indelning. Sammanfattning:

- **Sessions 1-5:** pre-STEG infrastrukturarbete. Hör till Fas 0.
- **STEG 1-2:** kärnkod-grundläggning (.NET solution, JobAd domain). Hör till Fas 0.
- **STEG 3+:** post-bootstrap arbete. Hör delvis till Fas 0 (frontend bootstrap STEG 4a — design system-baseline) och delvis till Fas 1 (auth + Core Domain).
- **STEG 4a/4b sub-numrering:** "a/b/c"-suffix används när ett STEG sträcker sig över flera sessioner med substantiellt distinkt scope.
- **Moment-numrering** (1-5 i upptakter): separat axel för mellan-arbete, inte STEG.

Renumrering har övervägts och avvisats — bryter audit-trail mot commits, sessions/-loggar och ADR-referenser.
