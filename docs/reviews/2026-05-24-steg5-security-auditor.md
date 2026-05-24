# Security-audit: Steg 5 — Closed beta-disciplin (waitlist-utvidgning)

**Status:** APPROVED med 1 Major + 2 Minor + 4 Praise — INGA Blockers/Criticals/Highs/Mediums
**Granskat:** 2026-05-24
**Agent:** security-auditor
**Auktoritet:** GDPR Art. 5, 6, 7, 17, 32 + ePrivacy Art. 5(3) + CLAUDE.md §5.1/§5.4
**Scope:** Areas 1 (PII), 3 (auth/IDOR), 4 (GDPR-spec), 6 (logghygien), 7 (CSRF/SQLi)

## 0 Blockers / 0 Critical / 0 High / 0 Medium

GDPR Art. 7-mekaniken är substantiellt korrekt implementerad: server-side `PrivacyPolicyVersion`-stämpel (tamper-resistent), granulära samtycken (3 separata bool-fält), `ConsentedAt` per-event-tidsstämpel, Art. 7(3)-mekanik via `RefreshRequest`.

## 1 Major

### Maj-1 — `RejectWaitlistEntryCommandHandler` saknar PII-erasure efter rejection (GDPR Art. 17 + Art. 5(1)(c)/(e))

**Fil:** `src/JobbPilot.Application/Waitlist/Commands/RejectWaitlistEntry/RejectWaitlistEntryCommandHandler.cs` + `src/JobbPilot.Domain/Waitlist/WaitlistEntry.cs`

`Reject(...)` bevarar Email, Name, Motivation, ConsentSnapshot obegränsat efter rejection. Steg 5 ökar PII-ytan från {email} till {email + name + motivation-fritext + 5 consent-fält}. När admin avvisar entry försvinner rättslig grund för Name + Motivation — Art. 5(1)(c) data-minimization + Art. 5(1)(e) storage-limitation kräver att lagring upphör.

Två alternativ:
- **A) Nullify fields on reject** (defensiv)
- **B) Hard-delete via scheduled cleanup**

**Eskalera till senior-cto-advisor för fas-triage (in-block-fix vs TD per §9.6).** Default-rec: in-block-fix nu eftersom Steg 5 *introducerar* fritext-PII-fältet.

## 2 Minor

### Min-1 — Sentinel-defaults i migration läcker "implicit non-consent"

Legacy-rader (pre-2026-05-24) får `consent_*=false`, `privacy_policy_version='legacy'`. Compliance-säkert markering men retention-problem på sikt. Pre-deploy: verifiera att inga riktiga rader finns i prod (`SELECT COUNT(*) WHERE privacy_policy_version='legacy'`).

### Min-2 — `PrivacyPolicy`-sektionen saknas i `appsettings.json`

Server-side stämpel default:ar till `"1.0"` via class-init. Versionsbumpar blir tysta. Lägg till explicit sektion.

**FIXAD in-block 2026-05-24** — `"PrivacyPolicy": { "CurrentVersion": "1.0" }` tillagd i `appsettings.json`.

## 4 Praise

1. Server-side privacy-policy-version-stämpel via `IOptions` — CLAUDE.md §5.4-konform.
2. Refresh-domain-event bär INGEN PII — CLAUDE.md §5.1 PII-disciplin följd.
3. `LoggingBehavior` loggar bara `MessageName` + `ElapsedMs` — Motivation-fritext loggas INTE.
4. Granular consent-mekanik per Art. 7(2) + ePrivacy Art. 5(3) — exakt vad granularitet kräver.

## Q&A från uppdraget

- **Q1 PII-loggar:** OK — inga `LogInformation`/`LogDebug` på Motivation/Name.
- **Q2 Art. 7-compliance:** OK — granularitet + tamper-resistens.
- **Q3 Art. 17:** **Maj-1** — manuell email-baserad process, ej automatiserad.
- **Q4 CSRF:** OK för anonym endpoint (ADR 0018-trustmodell).
- **Q5 Migration sentinel:** OK pre-launch, retention-skuld post-launch (**Min-1**).
- **Q6 Admin-DTO:** OK — `RequireAuthorization(AuthorizationPolicies.Admin)` på group-nivå.
- **Q7 Frontend dual-validering:** OK — backend auktoritativ.
- **Q8 308-disclosure:** OK.

## Sammanfattning

**Säkerhetsmässigt mergeklar EFTER Maj-1 + Min-2 är adresserade.** GDPR-grundpussel-bitarna sitter. Maj-1 är retention-svans; Min-2 fixad in-block.
