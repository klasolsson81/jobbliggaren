---
session: F-Pre Steg 5 — closed-beta-väntelista + /registrera 308-redirect
datum: 2026-05-24
slug: closed-beta-vantelista
status: levererad
commits:
  - afd8467 feat(web,api): F-Pre Steg 5 — closed-beta-väntelista (Acceptance-modell) + /registrera 308-redirect
tags:
  - v0.2.69-dev (på `afd8467`)
---

# F-Pre Steg 5 — closed-beta-väntelista + /registrera 308-redirect

## Sammanfattning

Klas-prompt 2026-05-24: utvidga gästlistan (väntelistan) med acceptance-fält
och stäng den öppna registreringen. Closed beta löper tills ~aug 2026.
`/registrera` blir 308 permanent redirect till `/vantelista`; `RegisterForm.tsx`
bevarad för senare återöppning. Backend-domänen `WaitlistEntry` utökades med
acceptance-snapshot per **EDPB Guidelines 03/2022** och **Planet49 C-673/17**
— bara MarketingEmail kvarstår som separat consent (Art. 6(1)(a)); användarvillkor
+ cookies behandlas under Art. 6(1)(b) "performance of contract" via disclaimer-
text, inte separat consent-checkbox.

Pushad på `origin/main`. Tag `v0.2.69-dev` på `afd8467` → deploy-dev-workflow
triggad. EF-migration `20260524183703_ExtendWaitlistEntryWithAcceptance` är
additiv (ALTER TABLE med sentinel-defaults för legacy-rader) och appliceras
automatiskt av deploy-workflow vid tag-push.

## Mål

1. Utvidga `WaitlistEntry`-aggregatet (bara `Email` innan) med Name, Motivation,
   acceptance-snapshot, RejectedAt + audit-portar.
2. /vantelista FE: utbyggt formulär med tre input-fält + disclaimer-text +
   en marketing-checkbox.
3. /registrera bytas mot 308 permanent redirect till /vantelista; bevara
   RegisterForm.tsx för framtida återöppning.
4. Domain/Application/Infrastructure/API end-to-end. Migration. Reviews.

## Fasindelning under sessionen

1. **Discovery** — kartlägga befintlig WaitlistEntry-domän (`Email`-only),
   ADR 0005 `registrations_open`-flag, `/registrera`-routing.
2. **STOPP A — CTO + dotnet-architect-rond** (multi-approach-val × 5):
   acceptance-modell, name-VO, motivation-policy, rejection-semantik,
   audit-strategi.
3. **BE-batch** — Domain + Application + EF-mapping + handlers + endpoint
   + first-cut-migration.
4. **STOPP B — migration-review** (db-migration-writer + dotnet-architect).
5. **FE-batch** — /vantelista-formuläret + /registrera→/vantelista 308-redirect.
6. **STOPP C — reviews** (code-reviewer + security-auditor + design-reviewer
   parallellt).
7. **CTO-triage på 2 policy-fynd** (design-reviewer Maj-5 GDPR Art. 7(1) +
   security-auditor Maj-1 GDPR Art. 17).
8. **Refactor-pass** — ConsentSnapshot → AcceptanceSnapshot (CTO Fynd 1 B);
   migration regenererad (gammal raderad, 5-kolumners ny).
9. **STOPP D — commit + tag-push**.

## Domänändringar

### Aggregatet `WaitlistEntry`

Tidigare: bara `Email`. Nu:

- `Email` (oförändrad)
- `Name` (VO eller string, krävs)
- `Motivation` (string, krävs)
- `AcceptanceSnapshot` value object: `MarketingEmailAccepted` (bool),
  `AcceptedAt` (UTC), `PrivacyPolicyVersion` (string)
- `RejectedAt` (UTC?, null tills Reject-beslut)
- Reject-metod sentinel-erasar `Name`, `Motivation` och `Acceptance` (GDPR Art. 17)
  men bevarar `Email` + `RejectedAt` + `AcceptedAt` för audit-spår

### EF-migration `20260524183703_ExtendWaitlistEntryWithAcceptance`

Additiv ALTER TABLE — alla nya kolumner NOT NULL med sentinel-defaults för
legacy-rader (`legacy` för PrivacyPolicyVersion, `epoch` UTC för AcceptedAt,
tom-sträng för Name/Motivation). Inga DROP, inga renames.

## CTO-triage på två policy-fynd

### Fynd 1 — design-reviewer Maj-5: Pre-ifyllt consent bryter GDPR Art. 7(1)

CC presenterade 3 approaches → CTO valde **Approach B** (legal-framing).

Källor: EDPB Guidelines 03/2022 on Consent §38–40 (samtycke kräver aktiv
handling, inte pre-tick) + EUD Planet49 C-673/17 (förmarkerade rutor är inte
giltigt samtycke).

**Konsekvens (stor refactor):**
- `ConsentSnapshot` döpt om till `AcceptanceSnapshot` (acceptance, inte
  consent — separerar rättsligt språk).
- `PoliciesAccepted` och `CookiesAccepted` borttagna som obligatoriska
  consent-checkboxar. Användarvillkor + cookies behandlas under Art. 6(1)(b)
  "performance of contract" via disclaimer-text ovanför formuläret.
- Bara `MarketingEmailAccepted` (Art. 6(1)(a) opt-in, kvar som checkbox).
- Sparar `PrivacyPolicyVersion` (string) per submission för audit (vilken
  version disclaimern refererade till).

### Fynd 2 — security-auditor Maj-1: GDPR Art. 17 (right to erasure) efter Reject

CC presenterade 2 approaches → CTO valde **Approach A** (sentinel-PII-erasure).

När en gästlista-entry "Rejectas" av admin: sentinel-erasera Name + Motivation
+ Acceptance, men bevara Email + RejectedAt + AcceptedAt så audit-spår finns
för "ansökan inkom + bedömdes" utan att hålla kvar PII utöver legitimt syfte.

## FE-leveranser

- `/vantelista`: utbyggt formulär (Name + Email + Motivation + en
  marketing-checkbox). Disclaimer-text ovanför formuläret refererar till
  `/villkor`, `/cookies` och `/integritet` (link-routes deferrade — Klas levererar
  privacy-policy-prosan).
- `/registrera`: bytt mot 308 permanent redirect till `/vantelista`.
  `RegisterForm.tsx` bevarad (ej raderad) för framtida återöppning av öppen
  registrering.

## Reviews (sparade per CLAUDE.md §9.2)

| Fil | Reviewer | Utfall |
|-----|----------|--------|
| `docs/reviews/2026-05-24-steg5-code-reviewer.md` | code-reviewer | APPROVED 0 Block / 5 Minor |
| `docs/reviews/2026-05-24-steg5-security-auditor.md` | security-auditor | APPROVED 1 Major (åtgärdat in-block) + 2 Minor |
| `docs/reviews/2026-05-24-steg5-design-reviewer.md` | design-reviewer | NEEDS_REWORK → APPROVED efter 3 Major + 5 Minor in-block-fixade |

CTO-triage utfärdades som **text-only-retur** (inte sparad som review-fil)
för Fynd 1 + Fynd 2.

## Disciplin-noteringar

- **Memory `feedback_subagent_hook_bypass_watch`:** ingen sub-agent rörde
  `.claude/settings.json` eller bypassade hooks.
- **Memory `feedback_pathspec_commit_parallel_cc`:** explicit pathspec på
  `git add` (24 specifika filer/paths).
- **Memory `feedback_spec_edit_approve_classifier_block`:** `dotnet ef database
  update` lokalt blockerad av classifier (förväntat) — deploy-workflow tar
  migrationen automatiskt vid tag-push.
- **Memory `feedback_cto_decides_multi_approach`:** CC gav ingen egen
  rekommendation vid 4 multi-approach-val (STOPP A 5 val + STOPP C-triage 2
  val) — CTO decision-maker.

## TDs

**Inga TDs lyfta.** Alla fynd från reviews (3 Maj + 12 Min totalt) pressade
mot §9.6 fas-regeln och fixade in-block i samma commit.

## ADRs

**Inga nya ADRs skapade.** ADR 0005 Amendment 2026-05-12 var redan auktoritativ
för väntelista-domänen; våra ändringar är **implementation** av spec +
CTO-tolkningstillämpning, inte nya arkitekturella beslut.

## Tester

- Domain **422/422 PASS** (+18 nya WaitlistEntry-tester)
- Application **591/591 PASS** (+3 nya: handler happy/validation/erasure)
- Architecture **78/78 PASS** (oförändrat)
- vitest **686/686 PASS** (+3 nya: vantelista-form + redirect-test)
- pnpm build PASS
- gitleaks: no leaks

## Commits

| Commit | Typ | Beskrivning |
|--------|-----|-------------|
| `afd8467` | feat(web,api) | F-Pre Steg 5 — closed-beta-väntelista (Acceptance-modell) + /registrera 308-redirect |

30 filer ändrade, **+2574 / -168** rader. Tag `v0.2.69-dev` på `afd8467` →
deploy-dev-workflow triggad. Docs-sync kommer som **separat commit** efter
detta agent-jobb (per CLAUDE.md §1.5 — docs-uppdateringar bundlas inte med
feature-commits).

## Pending Klas-operativt

1. **deploy-dev-workflow stable-verify** — invänta att workflow för
   `v0.2.69-dev` går grönt; migration appliceras automatiskt.
2. **Visual-verify dev `/vantelista`** light + dark + Lighthouse a11y ≥ 95
   + axe 0 violations.
3. **/registrera → 308 → /vantelista**-test (`curl -I` + browser-redirect-check).
4. **BUILD.md §20 öppna frågor** — privacy-policy-text + cookies-text för
   `/villkor` + `/cookies` routes. Länkarna i disclaimern 404:ar tills Klas
   levererar prosan.
5. **Pre-prod migration-check** (när prod-deploy närmar sig): kör `SELECT
   COUNT(*) FROM waitlist_entries WHERE privacy_policy_version='legacy'` mot
   prod-DB för att verifiera att sentinel-defaults är tomma (väntelistan har
   ännu inte använts i prod).

## Nästa session

Per Klas pending-lista 2026-05-23 (sekvenseringen Steg 1→5):

- **Steg 1** Snapshot-retention: ✅ Klar 2026-05-23 (v0.2.57-dev)
- **Steg 2** Spara + Har-ansökt: ✅ Klar 2026-05-23 (v0.2.59-dev)
- **Steg 3** Landing live-stats: ✅ Klar 2026-05-24 (v0.2.61-dev)
- **Steg 4** Översiktssida `/oversikt`: ✅ Klar 2026-05-24 (svans-PRs t.o.m.
  v0.2.65-dev)
- **Steg 5 (denna)** Closed-beta-väntelista + /registrera 308: ✅ Klar
  2026-05-24 (v0.2.69-dev)
- **Steg 6** MVP-recall-fix: ✅ Klar 2026-05-24 (v0.2.67-dev — tidigare i
  samma dag, separat session)

**Observera:** "Steg 5" i Klas pending-lista 2026-05-23 (rad nr 5 — "Stängd
registrering + gäst-mockdata") refererar till en **annan**, separat session
om gäst-mockdata. Denna F-Pre Steg 5 levererade **registreringsdelen**
(closed-beta-väntelistan + 308-redirect). Gäst-mockdata-delen (read-only
gäst-mode på alla sidor med "Exempel TEST-data") kvarstår som **lägst prio**
per Klas-direktiv 2026-05-23, fristående session.

**Övriga öppna ärenden** (parkerat, inte denna sessions ansvar):
- TD-94 + TD-95 från grunden (perf-rotorsak, separat session per F6 P5 P4
  svans-direktivet 2026-05-24 1050)
- TD-86 sök/filter-hardening (pausat per Klas-direktiv 2026-05-23)
- Fas 4 AI-pre-reqs (ADR 0051 + 5 GDPR-villkor)
