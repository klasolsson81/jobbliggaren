# Code-review: Steg 5 — Closed beta-disciplin (waitlist + /registrera 308)

**Status:** Approved with Minors
**Granskat:** 2026-05-24
**Agent:** code-reviewer
**Auktoritet:** CLAUDE.md §2–§5, §7, §9.6; ADR 0005 Amendment 2026-05-12
**Scope:** Backend (Domain + Application + Infrastructure + Migration) + Frontend (server-action + RHF-form + page) + tester (Domain/Application/vitest)

## Summa

**0 Blockers · 0 Critical · 0 Major · 5 Minor · 7 Praise**

Klas-GO-besluten (CTO-domarna V1–V7) är troget implementerade. Clean Arch är intakt, Domain har strikta invariants utan public setters, CQRS är ren (Mediator.SourceGenerator, inga MediatR-rester), PII-disciplinen i events är korrekt (`WaitlistEntryRefreshedDomainEvent` bär bara id+timestamp), och migration:en är additive med sentinel-defaults som lämnar legacy-rader identifierbara. Test-coverage är adekvat utan över-testning.

Per §9.6 (fas-regeln) lyfts **inga TDs**. Alla fynd är Minor och fixas in-block eller naturligt i nästa touch.

## Minor (in-block-fix eller naturlig nästa touch — INTE TD)

### Minor 1: `RefreshRequest`-test kunde förstärkas med separat refresh-time

Domain-testet `RefreshRequest_OnPending_UpdatesFieldsAndPreservesRequestedAt` använder samma `Clock.UtcNow` för båda consent-instanserna — verifierar inte att `ConsentedAt` faktiskt stämplas om. Lägg till test som flyttar fram klockan mellan Request och RefreshRequest.

### Minor 2: `Approve`/`Reject` validerings-konsistens (observation, inte fynd)

Befintlig kod — inte introducerat i Steg 5.

### Minor 3: WaitlistForm bygger FormData manuellt + server-action parsar FormData igen

Defensiv dual-path (klient bygger FormData, server-action parsar) är acceptabelt men kunde konsolideras. Rule of Three: vänta tills mönstret upprepas på 3 formulär innan abstraktion.

### Minor 4: `aria-describedby`-spread + override-kollision i WaitlistForm

`fieldA11y`-helpern sätter `aria-describedby` i spread, sedan overrides den explicit på Email-fältet. Subtilt men funktionellt korrekt. Konsolidera vid nästa form-touch.

**FIXAD in-block 2026-05-24** — email-hint borttagen per design-reviewer M3, vilket eliminerar override-fallet.

### Minor 5: WaitlistForm använder both RHF `register({required, maxLength})` AND manuell Zod-parse

Inte zodResolver-baserat. Fungerar korrekt men har dubbel-validation-källa. Byt till `zodResolver(waitlistFormSchema)` vid nästa form-touch (~15 min).

## Praise

1. `ConsentSnapshot` som `record class` med `OwnsOne`-mapping — exakt rätt val för audit-export.
2. Server-side `PrivacyPolicyVersion`-stämpling via `IOptions` — korrekt anti-tamper-design.
3. Idempotens-refresh med email-suppression vid re-signup — bra UX-disciplin + GDPR Art. 7(3)-konformitet.
4. PII-fri `WaitlistEntryRefreshedDomainEvent` med XML-doc som förklarar disciplinen.
5. Migration:en är additive med sentinel-defaults — production-safe.
6. `permanentRedirect("/vantelista"): never` med XML-doc + bevarad `RegisterForm.tsx`.
7. `logga-in/page.tsx` länkar redan till `/vantelista` — konsistent closed-beta-flöde.

## Sammanfattning

**Mergeklar efter Klas-GO.** Inga ändringar krävs innan push.
