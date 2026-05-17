# Retroaktiv security-audit — Resume.SoftDelete idempotens-guard (commit 62c9dc7)

**Datum:** 2026-05-17
**Agent:** security-auditor (agentId a206815b2788780c2)
**Scope:** PII-aggregat (Resume/ResumeVersion), GDPR Art. 17-cascade-kontext (ADR 0024)
**Anledning:** Commit `62c9dc7` buntade av misstag CC B:s Resume.SoftDelete-idempotens-arbete via delat git-index. Agent-invocation-pre-gaten (CLAUDE.md §6.3.3 / §9.2) kringgicks — denna review körs retroaktivt på redan pushad kod.

## Verdikt

**✓ APPROVED — GO. 0 Blockers / 0 Majors / 0 Minors. Koden är säker att stanna på main. Ingen kod-åtgärd krävs.**

## Granskad diff

- `src/JobbPilot.Domain/Resumes/Resume.cs:163-171` — `SoftDelete`: `if (DeletedAt.HasValue) return;` tillagt före `DeletedAt = clock.UtcNow` + `_versions`-cascade.
- `src/JobbPilot.Domain/Resumes/ResumeVersion.cs:40-44` — barn-`SoftDelete` block-bodied med samma guard.
- `tests/JobbPilot.Domain.UnitTests/Resumes/ResumeTests.cs` — +2 tester (first-timestamp-wins, exakt ett `ResumeDeletedDomainEvent` över två anrop).

## Fynd

**(a) Alignment:** ALIGN, inte DIVERGE. `JobSeeker.SoftDelete` (`JobSeeker.cs:77-83`) och `Application.SoftDelete` (`Application.cs:129-137`) har redan identisk guard + event-efter-guard-mönster. Resume var det enda PII-primäraggregatet som saknade guarden — ändringen tar bort en konsistens-avvikelse.

**(b) GDPR erasure-regression:** Guarden **stänger** en latent regression och inför ingen. Före ändringen kunde en andra `SoftDelete` på en redan soft-deletad Resume (a) skriva om `DeletedAt` → nollställa ADR 0024 D6 30-dagars-anonymiseringsklockan (`HardDeleteAccountsJob` selekterar `WHERE deleted_at < UTC.Now - 30d`) → fördröjd Art. 17-anonymisering; (b) raisa ett andra `ResumeDeletedDomainEvent` → falsk historisk raderings-fakta. Guarden eliminerar båda. Tidig retur sker endast när `DeletedAt.HasValue` redan sant (raderingen har redan propagerat) — ingen väg där en faktisk första radering tystas.

**(c) Undertryckt andra event:** GDPR-korrekt. `ResumeDeletedDomainEvent` har noll handlers i Application-lagret (verifierat). Audit-rad vid kontoradering kommer från ett enda `Account.Deleted`-event via AuditBehavior (ADR 0024 D4), ej från `ResumeDeletedDomainEvent`. Inget downstream-beteende går förlorat.

## Eskalering till Klas (process, ej kod)

Pre-gaten för agent-invocation kringgicks p.g.a. delat git-index mellan parallella CC-instanser. Koden friades retroaktivt, men flödet lät GDPR-kritisk PII-aggregat-kod nå main utan föreskriven gate (CLAUDE.md §6.3.3 / §9.2). Disciplin-notering: parallell-CC-arbete bör inte dela git-index med pågående batch. Klas kvitterar retroaktivt och avgör om parallell-CC-isolering ska härdas.
