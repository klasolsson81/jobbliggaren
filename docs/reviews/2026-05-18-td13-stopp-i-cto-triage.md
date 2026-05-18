# CTO-triage — TD-13 FAS 3.5 STOPP I, 3 öppna designfrågor

**Datum:** 2026-05-18
**Roll:** senior-cto-advisor (decision-maker, §9.6) — agentId `a2d22a64081feafe2`
**On-disk HEAD:** `9952a0c` (read-only)
**Uppdrag:** triage av dotnet-architects 3 flaggade pre-code-frågor innan
implementation (test-writer TDD → C1–C6).

> CC-not: agentens filskrivning landade ej på disk (§9.4, samma mönster som
> IDOR-auditen + CTO-5-besluts-domen). Trogen verbatim-transkription till
> review-trailen. Agenten var read-only (web-search för EF Core-doktrin-
> verifiering; inga on-disk-ändringar).

---

## FRÅGA 1 (KRITISK) — interceptor vs ADR 0049 Beslut 4:s "ValueConverter"

**Beslut: (A) Interceptor ÄR inom Beslut 4:s intention. INGEN ADR-amendment.
INGEN Klas-STOPP. CC fortsätter.**

Verifierat mot Microsoft Learn — *Value Conversions (EF Core)*: VC kan ej
referera DbContext, single-column (dotnet/efcore #13947, #31234). En ren
`ValueConverter` kan **inte** nå radens `JobSeekerId` för per-användare-DEK
(Beslut 1). Beslut 4:s ordalydelse "ValueConverter" tekniskt ogenomförbar mot
Beslut 1 — två accepterade ADR-beslut i mekanisk konflikt; Beslut 1 är substans
(DDD aggregate-ägande, crypto-erasure-beroende), Beslut 4:s ord ger vika.

**Substans vs precisering (§9.6):** Beslut 4:s fyra substans-invarianter — (1)
lazy encrypt-on-write, (2) sentinel-/versionsprefix, (3) bounded idempotent
backfill, (4) legacy-tolerans på read-path — överlever **alla oförändrade** i
`ISaveChangesInterceptor`+`IMaterializationInterceptor`. Det som ändras är
EF-konstruktionen, ej beslutsvärdet → mekanik-precisering, ej substansskifte.
Avvisade alternativ (korrekt av architect): (a) VC+`AsyncLocal<DEK>` =
§5.1-antipattern + cross-user-batch-läcka + otestbart; (b) manuell encrypt per
handler = DRY-brott på knowledge-nivå + Clean Arch-dependency-brott. Interceptor
är den principrena lösningen (SRP: write/read separata change-reasons; ADR 0009
EF-bridge bevarad; Domain orört).

**Konsekvens:** ingen formell amendment (substans oförändrad), ingen Klas-STOPP
(ej fas-skifte/deploy/substans-ADR). MEN obligatorisk **mekanik-not** i ADR 0049
Beslut 4+5 vid Accepted-commit (annars ljuger ADR om sin egen mekanik) —
CC-direkt verbatim-not under §9.4, ej adr-keeper-amendment-cykel.

## FRÅGA 2 — `user_data_keys`-placering

**Beslut: bekräftad — `AppDbContext.Set<UserDataKey>()` keyless, EJ exponerad
via `IAppDbContext`, + architecture-test som spärr. CC-direkt.**

ISP/Clean Arch (Martin 2017 kap. 10/22; ADR 0009): Application-handlers ska
aldrig kunna que45ra nyckelmaterial. AppDbContext-placering ger
migrations-/transaktionell-konsistens (interceptorn i FRÅGA 1 behöver wrapped-DEK
+ ciphertext atomärt i samma `SaveChanges`) utan att läcka ytan uppåt. Separat
mini-DbContext avvisad (splittrar DEK/ciphertext i två transaktioner →
konsistensrisk för noll vinst, KISS-brott). Arch-test icke-förhandlingsbart
(C2 in-scope-leverabel, ej TD): (a) `IAppDbContext` exponerar ej `UserDataKey`;
(b) `JobbPilot.Application` refererar ej `UserDataKey`. Inom ADR 0049 Beslut 1:s
redan accepterade lösningsrymd ("user_data_keys-tabell eller på JobSeeker") →
ingen amendment.

## FRÅGA 3 — `resume_versions.content` JSON-VC-ordning + fallback

**Beslut: bekräftad MED skärpt fallback. test-gate→fallback-i-C4 acceptabel.
CC-direkt.**

`IMaterializationInterceptor.InitializedInstance` post-property-init men
VC↔materialization-ordningen **ej normativt garanterad** i Microsoft Learn →
empirisk gate mot Npgsql/Testcontainers (ej InMemory), test 11+12 blockerande
i C4, är korrekt epistemisk hållning (§9.5 tillämpat på EF-internt beteende).
**Architectens fallback FELAKTIG:** "krypto-VC ovanpå JSON-VC med service-locator
i converter-factory" återinför exakt FRÅGA 1:s avvisade ambient-state-antipattern
(factory instansieras model-build-tid, ingen rad-kontext). **Korrekt fallback:**
om gaten röd → `resume_versions.content` behandlas mekaniskt identiskt med de 3
TEXT-kolumnerna: JSON-serialiseringen flyttas IN i interceptor-paret
(entitet→`ResumeContent`→JSON→ciphertext→`content_enc` och omvänt); JSON-VC tas
bort ur krypto-vägen helt (DRY på mekanik-nivå, en krypto-mekanik ej två
divergerande). `ValueComparer` på klartext-`ResumeContent` bevaras oavsett
(change-tracking). Expand/contract-sekvensen (Beslut 5 steg 1–4) mekanik-oberoende,
oförändrad. Beslutet redan fattat här (binär gate, båda utfall förspecificerade)
— C4 aktiverar bara vilken väg.

---

## Klas-GO-matris (§9.6 p.5)

| Fråga | CTO-dom | Klas-STOPP / amendment? |
|---|---|---|
| 1 | (A) interceptor inom Beslut 4 | Nej / Nej — CC-direkt |
| 2 | AppDbContext.Set keyless + arch-test | Nej / Nej — CC-direkt |
| 3 | bekräftad, skärpt fallback | Nej / Nej — CC-direkt |
| ADR 0049 mekanik-not (Beslut 4+5) | obligatorisk housekeeping vid Accepted-commit | Nej — verbatim §9.4, ej adr-keeper |

Enda kvarvarande Klas-grind = ADR 0049 Proposed→Accepted (redan GO:dd
2026-05-18). Implementation får starta non-stop efter mekanik-noten i samma
commit-batch som Accepted-status.

**In-block-leverabler (ej TD):** (1) interceptor-paret + scoped DEK-cache per
SaveChanges, ingen ambient state; (2) arch-test-par UserDataKey-isolering;
(3) test 11+12 Npgsql/Testcontainers-gate → röd ⇒ interceptor äger JSON+krypto.

## Referenser

Microsoft Learn *Value Conversions* / *Interceptors* / *IMaterializationInterceptor.
InitializedInstance* (verifierade 2026-05-18) · dotnet/efcore #13947/#31234 ·
Martin *Clean Architecture* (2017) kap. 7/10/13/22 · Evans *DDD* (2003) ·
Fowler *Refactoring* 2e (2018) ParallelChange · Ford/Parsons/Kua (2017)
fitness functions · Hunt/Thomas (1999) DRY · ADR 0049 (Accepted) ·
`docs/reviews/2026-05-18-td13-design-decisions-cto.md` · ADR 0009 ·
CLAUDE.md §2.1/§5.1/§9.4/§9.6 p.5
