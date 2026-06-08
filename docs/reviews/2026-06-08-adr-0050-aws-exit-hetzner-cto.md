# CTO-beslutsrapport — ADR 0050 (AWS-exit → Hetzner) strategisk flip

**Roll:** senior-cto-advisor som decision-maker (§9.6). CC ger ingen egen rek vid multi-approach (memory `feedback_cto_decides_multi_approach`).
**Underlag:** dotnet-architect-dom + security-auditor-dom (separata reviews samma datum) + on-disk-verifiering 2026-06-08 av ADR 0050, ADR 0049 (2026-06-06-not), `ForwardedHeadersConfig.cs`, `AlbOptions.cs`, `JobbPilot.Migrate`, TD-101/102/104/105, döda workflows.
**Klas-policy:** kvalitet > tempo, studentbudget.
**Grund-fynd:** Hetzner-fas-arbetet är **redan TD-täckt** (TD-101 mejl, TD-102 master-nyckel+rotation, TD-104 logg-sink, TD-105 Migrate-re-home). Skapar **inte** 8 nya TDs — det vore TD-bloat (§9.7, `feedback_td_lifting_discipline`).

**Markering:** **[ENTYDIG]** = CC följer auto utan extra Klas-GO (§9.6 p.5). **[KLAS-STOPP]** = Klas-GO (fas-skifte/ADR-flip/deploy/spec-edit/GDPR-Blocker).

---

## Axel 1 — Sizing: **CAX31 (ARM, 8 vCPU/16 GB/160 GB, ~€16/mån)** — [ENTYDIG; pris-edit i ADR-prosa via M-1 = KLAS-STOPP]

ADR 0050:s Beslut 2 (CX32) avvisas/uppdateras.

**Motivering:** Bulkhead/Steady State (Nygard 2018) — single-box river AWS managed-isolering; kompensera med headroom i delad resurs. Kod-bevisat: `JobTechStreamClient` `MaxResponseContentBufferSize=500MB` = OOM-vektor som konkurrerar med PG hot-index (46k+raw_payload+generated cols+FTS-GIN) på samma RAM. På 8 GB (CX32) farligt nära taket. YAGNI vänd rätt (Hunt/Thomas 1999): behovet är inte spekulativt — kod-mätt OOM-vektor mot verifierat 46k-korpus. €9/mån köper bort största single-box-risken. Mastercard-testet.

**ARM-risk låg:** stacken ARM64-ren 2026; enda fällan (System.Drawing/libgdiplus) Fas-4-PDF-gated (`project_cv_pdf_features_gated_on_fas4_ai`), ej aktuell vid cutover.

**Avvisat:** CX32 (8 GB under-provisionerat, prisdelta trivialt mot OOM); CAX21 (ARM utan RAM-vinst löser ej feldomän). **Trade-off:** €16 vs €7, fortfarande ~80% reduktion mot AWS ~$45/mån. 160 GB disk för PG+WAL+images+tillväxt.

## Axel 2 — mem_limit: **Hybrid — hård på Worker+Redis (skydda PG), generös/osatt PG, inom 16 GB-headroom** — [ENTYDIG princip; mekanik i Hetzner-fas]

**Motivering:** Bulkhead (Nygard) — cappa angriparen (Worker-spik), inte offret (PG = data-durabilitet; hård PG-cap kan OOM-killa mitt i query). SRP på resurspolicy (Martin kap. 7): Worker = "burst, kan dödas/retryas" (Hangfire-Postgres-storage → dödad spik förloras ej); PG = "får aldrig dödas". CAX31:s 16 GB upplöser architect-delfrågans falska dilemma (på 8 GB hade det varit nollsumma).

## Axel 3 — Logg-sink: **Defer VAL till TD-104 (finns) m. dokumenterad beslutsaxel; riktning Serilog>OTel, Seq-self-hosted-lutning** — [ENTYDIG]

**Motivering:** DIP redan ren (Martin kap. 11) — `ILogger`-seam intakt; Serilog top-level-dep nu bryter §9.2 för feature ej behövd i lokal-fas. YAGNI — sink-val (Seq/Loki/managed) kopplat till sizing+PII-residens, blir verkligt först med prod-trafik/PII. TD-104 dokumenterar redan "Designvalet fattas DÅ". Serilog>OTel = riktning (reversibel via `ILogger`-seam), ej låst nu. **Åtgärd:** uppdatera TD-104:s beslutsaxel med Serilog-vs-OTel-domen (in-block TD-update, ej ny TD).

## Axel 4 — Secrets: **sops+age; master-nyckel (a) systemd-credentials primär / (b) sops+age fallback — bindande val = TD-102; B-1/B-2 eskaleras** — [KLAS-STOPP]

**Motivering:** 12-Factor III korrekt — config i miljön, men ej plaintext-secrets på prod-disk. sops+age = git-spårbar, krypterad-at-rest, rotations-ergonomi (stödjer M-3). Defense in depth (OWASP): master-nyckel ≠ vanlig secret; B-1 kod-grundad (process-minne-laddning). Båda alt ärligt namngivna nedgraderingar vs KMS-HSM; M-2 = accepterad beta-restrisk + skala-trigger. Val fattas ej nu — B-1/B-2 är gates **före real-PII** (waitlist tom), redan TD-102-scope. **Eskalering till Klas:** B-1 (aldrig plaintext-på-disk före real-PII) + B-2 (gitleaks/historik-scan + rotation om läckt). B-2 billig/fas-oberoende — rek: kör tidigt.

## Axel 5 — Backup: **Hetzner-EU Storage Box primärt + klient-side-krypterad dump oavsett mål; R2 avvisas** — [ENTYDIG dom]

ADR 0050 Beslut 4 (R2) avvisas/uppdateras.

**Motivering:** GDPR-residens (M-4 kod-grundad) — pg_dump bär icke-krypterad PII; Cloudflare R2 = US-bolag (CLOUD Act) även EU-lagrad → Schrems II-territorium. Hetzner Storage Box EU-only (~€3,20/mån/1TB) = samma jurisdiktion som boxen, ingen överförings-yta. KISS + minska blast-radius + leverantörer. Klient-side-kryptering (age) obligatorisk oavsett + retention/rotation (ADR 0024:s 14d finns ej gratis på Hetzner). **Trade-off:** Storage Box egress-cost vid restore (sällan-händelse) < R2 gratis-egress; jurisdiktions-renhet vinner. ADR 0050:s disk-budget-motiv bevaras, bara leverantör byts.

## Axel 6 — AWS-hygien: **Rensa 2 döda workflows (Klas-GO för .github/); behåll KMS; SecretsManager via TD-105** — [KLAS-STOPP för .github/; resten ENTYDIG]

**Motivering:** Dead Code (Martin *Clean Code* kap. 17 G9) — `deploy-dev.yml` (riven AWS-stack) + `rds-ca-bundle-check.yml` (ingen RDS) obetingat död, hör ej till framtida fas → in-block-fix, ej TD. Reversibilitets-policy (ADR 0066) — `AWSSDK.KeyManagementService` behålls (KMS referens-impl levande). `AWSSDK.SecretsManager` fas-bunden (§9.6 krit. 2) — rensas NÄR Migrate re-homas (TD-105 p.3), annars broken state. `.github/`-touch rör CI-gate (ADR 0065) → Klas-GO, egen `chore(infra)`-commit.

## Axel 7 — ADR-revision: **Targeted amendment in-place + Accepted-flip (ej omskrivning/superseder)** — [KLAS-STOPP]

**Motivering:** Granskningstrail-integritet (Ford/Parsons/Kua 2017) — ADR 0050:s substans korrekt+oförändrad; verkligheten (ADR 0066) bekräftade riktningen. Immutability skyddar *fattade* beslut; ADR 0050 är **Proposed** (aldrig låst) → amendment bryter ingen immutability. Livscykel-not (rad 9-14) säger CC skrev prosan på Klas-begäran (§9.4-override) → CC får revidera grundad i agent-domar (`feedback_klas_can_override_adr_verbatim_source`). DRY (Hunt/Thomas) — superseder-ADR duplicerar ~90%.

**Amendment-scope (besluts-substans):**
1. Beslut 2 sizing CX32→**CAX31** + ARM-motivering + single-box-RAM-argument; behåll CX22-avvisning, lägg CX32/CAX21-avvisning.
2. Beslut 4 backup R2→**Hetzner-EU Storage Box** + klient-kryptering + retention.
3. "Öppen fråga — KMS-beroende" (rad 100-117) FÖRÅLDRAD → ersätt: "KMS-beroendet löst 2026-05-26 via ADR 0066 `LocalDataKeyProvider`... kvarvarande Hetzner-härdning = TD-102."
4. Validering/Rollback (rad 156-161) FÖRÅLDRAD ("behåll AWS körande" = ogiltig, AWS rivet) → lokal-dev→Hetzner-cutover-modell (lokal Compose = paritets-baseline; rollback = återgå lokal + ej-cutad DNS).
5. Lägg **"Pre-beta-data-gates"-sektion** (B-1/B-2/M-1–M-6) m. TD-102/107-cross-ref.
6. Lägg mem_limit-hybrid-dom (axel 2) som konsekvens-not.
7. Uppdatera Livscykel-not (2026-06-08 CTO-grundad revision, inga nya beslut utöver CTO-domarna).

## Axel 8 — Accepted-flip DENNA session: **JA efter M-1-amendment** — [KLAS-STOPP]

**Motivering:** Reversibelt riktnings-beslut (Ford/Parsons/Kua two-way-door) — binder ingen infra (ingen provisionering denna session); DNS-cutover är enda irreversibla, ej nu. Auditor-distinktion avgörande: 2 Blockers + 4 Majors är gates **före real-PII** (beta), ej före Accepted-flip av strategin (waitlist tom). Strategin har inga GDPR-blockers. **Pre-condition:** M-1 (föråldrad KMS-prosa) MÅSTE amenderas FÖRE flip (Accepted-ADR får ej bära falsk blocker). Sekvens: amendment → Accepted. Klas-STOPP (strategiskt fas-beslut; Klas har sista ordet).

## Axel 9 — TD-triage: **0 dup-TDs; skapa TD-106 (Compose-stack) + TD-107 (backup); uppdatera TD-104** — [ENTYDIG]

Hetzner-fas redan TD-täckt → mappa architect/auditor mot befintliga:

| Förslag | Dom | Hemvist |
|---|---|---|
| Migrate-re-home | Redan TD | **TD-105** |
| Compose-stack+proxy+Caddy | **NY** | **TD-106** |
| AlbOptions-rename | In-block i TD-106 | (§9.7: ej Minor-Fas-Nu) |
| pg_dump+restore-runbook | **NY** (M-4 GDPR-substans) | **TD-107** |
| Logg-sink | Redan TD | **TD-104** (uppdatera axel) |
| Mejl-väg | Redan TD | **TD-101** |
| Conn-pool-budget | In-block i TD-106 | |
| Hetzner-deploy-workflow | In-block i TD-106 | |
| Master-nyckel+rotation | Redan TD | **TD-102** |
| Security-gates B-1–M-6 | ADR-amendment + TD-102 | |

**TD-106 (Major, Hetzner-deploy):** Compose all-in-one (API+Worker+PG+Redis+Caddy), mem_limit-hybrid, ForwardedHeaders Docker-bridge-CIDR-overlay, AlbOptions→ReverseProxyOptions, Caddy CF "Full strict"+DNS-01-ACME, oneshot-migrate-container-sekvens, conn-pool-budget, Hetzner-deploy-workflow, VPS-härdning (M-6). Källa: architect + CTO 2026-06-08.

**TD-107 (Major, Hetzner-deploy, GDPR):** Klient-side-krypterad pg_dump (age) → Hetzner-EU Storage Box (R2 avvisad, M-4), retention/rotation, restore-drill som DoD-grind. Källa: security-auditor M-4 + CTO 2026-06-08.

**Varför 2 ej 8:** §9.6 TD-bloat-förbud; rename/conn-pool/deploy-workflow/härdning = mekanik inom Compose-bygget (CCP, Martin kap. 13: "things that change together belong together"). Backup egen TD (distinkt GDPR-substans + leverantör).

## Axel 10 — Sekvensering: **Fas 4 (AI Layer, ADR 0051) NÄST, INTE Hetzner-provisionering nu** — [KLAS-STOPP roadmap; CTO-rek]

**Motivering:** YAGNI på infra + two-way-door — Hetzner löser ett kostnadsproblem som inte finns (AWS rivet, €0, dev lokalt, waitlist tom). Deploy nu = €16/mån + single-box-ops + alla security-gates för **noll användare** = premature deployment. Value over activity (SWE at Google, Winters 2020) — nästa värde = AI Layer (gör JobbPilot till assistent ej tracker); CV-PDF Fas-4-AI-gated. Fas 4 byggs/testas lokalt utan Hetzner. Hetzner-deploy när det finns något att deploya för + security-gates lösta. ADR 0050 Accepted dokumenterar riktningen (redo), exekvering väntar på produktbehov.

**Rek sekvens:** (1) nu: ADR 0050 amendment+Accepted, rensa döda workflows, B-2 gitleaks-scan tidigt, TD-106/107 + TD-104-update; (2) näst: Fas 4 lokalt; (3) när Fas 4 beta-värdig + gates lösta: Hetzner-provisionering (TD-106/107/102/101/105/104 + B-1–M-6 + andra security-granskning TD-102 p.3).

---

## Beslutsmatris

| Axel | Beslut | Status |
|---|---|---|
| 1 Sizing | **CAX31** (16 GB ARM) över CX32 | ENTYDIG (pris-edit = Klas-STOPP) |
| 2 mem_limit | Hybrid (hård Worker/Redis, generös PG) | ENTYDIG |
| 3 Logg-sink | Defer→TD-104; Serilog>OTel, Seq-lutning | ENTYDIG |
| 4 Secrets | sops+age; master-nyckel=TD-102; **B-1/B-2 eskaleras** | KLAS-STOPP |
| 5 Backup | **Hetzner-EU Storage Box** + klient-kryptering (R2 avvisad) | ENTYDIG |
| 6 AWS-hygien | Rensa 2 döda workflows; behåll KMS; SecretsMgr via TD-105 | KLAS-STOPP (.github/) |
| 7 ADR-revision | **Targeted amendment in-place** | KLAS-STOPP |
| 8 Accepted-flip | **JA efter M-1-amendment** | KLAS-STOPP |
| 9 TD-triage | 0 dup; TD-106+TD-107; uppdatera TD-104 | ENTYDIG |
| 10 Sekvens | **Fas 4 näst, ej Hetzner nu** | KLAS-STOPP roadmap |

**Avvisade snabblösningar:** CX32 "billigare" (€7 ej design-värde; OOM-risk är); 8 nya TDs (TD-bloat); superseder-0050b (DRY); Hetzner-provisionering nu (premature); R2 "gratis egress" (< GDPR-renhet).

**Eskaleras till Klas:** B-1+B-2 (axel 4); ADR-amendment+Accepted-flip (7-8); .github/-rensning (6); sekvens Fas 4 vs Hetzner (10).

**Klas-override-prompt:** (1) CAX31 €16 vs CX32 €7 — jag väljer headroom mot kod-bevisad OOM, kostnadskänslighet är din; (2) Fas 4 före Hetzner — teknisk logik säger vänta, MVP-timing är din kontext; (3) Accepted-flip förutsätter M-1-amendment först (icke-förhandlingsbart).

**Referenser:** Martin *Clean Architecture* (SRP/DIP/CCP) + *Clean Code* (G9 Dead Code); Nygard *Release It!* (Bulkheads/Steady State); Hunt/Thomas (DRY/YAGNI); Ford/Parsons/Kua (ADR-trail/two-way-door); Winters et al. *SWE at Google* (value over activity); 12-Factor III; OWASP. On-disk 2026-06-08: ADR 0050, ADR 0049 (rad 17-28), `ForwardedHeadersConfig.cs`, `AlbOptions.cs`, TD-101/102/104/105, `deploy-dev.yml`+`rds-ca-bundle-check.yml`.
