---
session: Post-Fas-3 + pre-migration-discovery (AWS-budget / hosting / AI-provider / ADR-skissning)
datum: 2026-05-19
slug: post-fas3-migration-discovery
status: STOPP 4 — ADR-paket + docs-diff klart, inväntar Klas-granskning innan commit/push
commits: [] # inga commits denna session (STOPP 4 före commit)
---

# Session: Post-Fas-3 migration-discovery

STOPP-driven discovery-session (4 block). Ingen kod. HEAD vid start = `3f22224`
(= origin/main, TD-13 FAS 3.5 stängd). Inga commits gjorda — STOPP 4 är före
commit/push per startprompt.

## Mål

Höj AWS-budget inför MVP-presentation, discovery på billigare hosting +
Bedrock-vs-Anthropic, skissa ADR-paket för deployment-migration. Ingen
migration utförd.

## Vad som hände per block

### Block 1 — AWS-budget $50→$100: SKIPPAD (Klas-beslut)

Discovery + dotnet-architect-review fann tre materiella fel i startpromptens
Block 1-modell: (1) budget-taket lever i **prod/baseline**-stacken
(`environments/prod`), ej dev — en dev-apply rör inte taket; (2) notifierings-
trösklarna är **redan** 50/80/100 (+100 forecast) — tröskeländringen är no-op;
(3) Bedrock-deny APPLY_IAM_POLICY ligger i separat `budget_actions`-modul
(dev), auto-flyttar till $100 om taket höjs. Architect-dom: Approach A
(explicit värde i `prod/terraform.tfvars`), modul-default orörd,
`budget_actions` orörd, targeted apply `-target=module.budgets`.

**Klas-beslut:** skippa Block 1 helt. Motiv (genomgånget med Klas):
budget-action vid $50 attachar endast `JobbPilotBedrockDeny` på dev
api-task-roll — stoppar **inget** annat (ECS/RDS/site rullar). Fas 4 (AI) ej
byggt ⇒ noll funktionell påverkan även om den triggar. AWS rivs i juni ändå.
Höjningen hade bara varit presentations-optik; prod-apply-risk motiverar inte
en icke-funktionell ändring på en stack som avvecklas. Ingen terraform-ändring
gjord.

### Block 2 — Hosting: BESLUTAT (Klas pre-beslutade arkitektur)

Klas hade redan riktning: Hetzner VPS backend + Vercel FE + ev. Cloudflare.
Provider-jämförelsen blev akademisk; enda levande axeln = CX22 vs CX32-sizing.
Discovery av faktisk footprint (AWS dev = hela driften): API 0,5vCPU/1GB,
Worker 0,25vCPU/0,5GB, RDS `db.t4g.micro` (1GB), Redis `cache.t4g.micro`.
Web-verifierade Hetzner-priser (2026-05, efter pris-justering 2026-04-01):
CX22 2vCPU/4GB/40GB €3,79; CX32 4vCPU/8GB/80GB €6,80.

Klas korrigerade en stale-siffra: korpuset är **45k+ annonser live** (ej ~19,8k
som current-work angav). Det härdade sizing-analysen: co-tenant Postgres med
45k+ + raw_payload + ingestion-minnesprofil (ADR 0032-blowout-vektorn) gör
CX22 under-provisionerad (4GB/40GB utan marginal). **Klas-beslut: CX32**
(8GB/80GB) + pg_dump-offload till Cloudflare R2 (ej lokal disk).

### Block 3 — Bedrock vs Anthropic Direct: BESLUTAT

Avgörande discovery: **AI-lagret är 0 rader** (`Grep` *.cs = 0). Greenfield
Fas 4, ingen refaktor-kostnad — startpromptens "abstraktions-port finns redan"
fel. BUILD.md §8 specar redan dual-provider-port. Ingen fristående Bedrock-EU-
ADR finns (startpromptens "ADR 0007 supersession" moot — ADR 0007 =
branch-protection). Web-verifierat: Anthropic Direct ≈10% billigare än
Bedrock EU men US-only (self-serve); EU-residency endast custom enterprise.

Tre agent-domar (rapport: `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md`):
- **dotnet-architect:** ej arkitektur-ändring (port-konfig inom §8); Bedrock-
  adapter byggs aldrig; defer bygg till Fas 4 (LRM/YAGNI); rör CLAUDE.md
  §5.3-spärr → ADR + Klas-GO.
- **security-auditor (GDPR-veto):** US-systemnyckel blockerad som tyst default;
  5 icke-förhandlingsbara kumulativa villkor innan Fas 4-kod (DPIA / SCC+TIA+
  DPA+DPF / versionerad policy före live / Art.25-opt-in / ADR 0049-decrypt-
  interaktion).
- **senior-cto-advisor (decision-maker):** riktning godkänd; §9.6 strategiskt
  fas-skifte → Klas-STOPP; US opt-in även systemnyckel entydigt (ingen
  US-default, Art. 25.2); 5 villkor står fast (GDPR-veto ej override-bar).

**Klas-beslut (verbatim):** "skippa AWS helt, skippa bedrock, kör Anthropic
Direct API — både i FAS 4, samt när vi byter VPS." GO på de tre STOPP-3-
punkterna (US opt-in även systemnyckel = bekräftad, ingen override-signal).

### Block 4 — ADR-skissning

Klas bad CC skriva ADR-prosan (explicit override av §9.4 webb-Claude-verbatim-
konventionen — medveten, dokumenterad i ADR:ernas Livscykel-not + memory).
Substans transkriberad från agent-domarna, inga nya beslut konstruerade.

- `docs/decisions/0050-deployment-migration-aws-exit-hetzner.md` (Proposed)
- `docs/decisions/0051-ai-provider-anthropic-direct-bedrock-retired.md` (Proposed)
- ADR-nummer: nästa lediga = **0050/0051** (startpromptens "0033" fel — taget).

**Kritiskt fynd under skrivning (ingen Block 3-agent ytte det — scope):**
ADR 0049 (TD-13, stängd 2026-05-18) implementerade PII-fält-kryptering via
**AWS KMS**-envelope. Full AWS-exit **tar bort KMS** → load-bearing
GDPR-krypto måste om-hemmas (icke-AWS KMS, bevarad crypto-erasure). Namngiven
som **olöst migrations-blocker** i ADR 0050 (Öppen fråga) — ej löst, ej
papprad över. Kandidat för egen designomgång/TD/amendment — Klas/CTO-triage.

adr-keeper: format/nummer OK, index + "Planerade ADRs" uppdaterat
(`aws-over-azure`/`bedrock-eu-for-system-key`-slottar realiserade m. inverterad
slutsats), 0049-bakåtlänk medvetet uppskjuten till amendment (immutable).
docs-keeper: index/cross-refs konsistenta, 0 auto-fixes; spec-drift +
README-räknedrift (ADR 44→51) flaggade ej applicerade.

## Beslut & avvikelser

- Block 1 skippat (Klas) — disciplinärt YAGNI/blast-radius-korrekt.
- Tech-debt obsolet-stämpel **ej applicerad blint** (§9.6 + memory
  `feedback_td_lifting_discipline`): TD-77/78/27/26 är AWS-mekanism-kopplade
  men kraven överlever migration — "obsolet"-stämpel vore lossy. Flaggat till
  Klas i STOPP 4 istället för blind exekvering av stale startprompt-instruktion.
- ADR 0005 Accepted/immutable → ingen body-edit; relevans-skifte framåt-
  dokumenterat i ADR 0050/0051.
- §9.4-override (CC skrev ADR-prosa) — medveten Klas-begäran, dokumenterad.

## Nästa session

- Klas STOPP 4-granskning av ADR 0050/0051 + docs-diff → commit/push-GO.
- Spec-amendments (BUILD.md/CLAUDE.md §5.3/privacy-policy) = Klas spec-edit-
  approve-mekanism, ej CC.
- KMS-rehoming-blocker (ADR 0050 Öppen fråga) = egen designomgång före migration.
- Fas 4 + faktisk migration = egna strategiska Klas-GO + ren `/clear`.
- 5 GDPR-villkor (ADR 0051) = pre-Fas-4-grind, spåras i current-work/steg-tracker.
