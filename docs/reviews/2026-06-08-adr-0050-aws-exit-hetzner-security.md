# Säkerhets-/GDPR-dom: ADR 0050 (AWS-exit → Hetzner VPS)

**Status:** ⚠️ VILLKORAT GODKÄNNANDE av exit-strategin — med **2 Blockers** och **4 Majors** som hård grind före första real-PII (beta-testare). Inga GDPR-Blockers mot strategin *som riktning*; Blockers gäller specifika kontroller som måste vara gröna före beta-data.
**Granskat:** 2026-06-08
**Auktoritet:** GDPR Art. 5, 17, 25, 32, 44–46 (Kap. V); EDPB Guidelines 4/2019 + CEF 2025; Schrems II (C-311/18); OWASP; CLAUDE.md §5.4, §9.2, §9.6, §12
**Källor:** ADR 0049/0050/0051/0066; TD-101/102/104/105; `LocalDataKeyProvider.cs`, `FieldEncryptionOptions.cs`, `FieldEncryptionOptionsValidator.cs`; web 2026-06-08 (Hetzner EU-residens, sops+age/systemd-credentials)

**Strategisk dom på omframingen KMS-blocker → TD-102: BEKRÄFTAD KORREKT** (Avsnitt 0).

---

## 0. Omframing: är "KMS-blockern" fortfarande en oläst migrations-blocker?

ADR 0050 (2026-05-19): *"Den load-bearing GDPR-krypto-mekanismen kan inte migreras genom att bara flytta containrar... oläst migrations-blocker."* **Föråldrad, ska amenderas.**

On-disk: `LocalDataKeyProvider.cs` **kör** och implementerar `IDataKeyProvider` identiskt med KMS-grenen. Envelope-strukturen (per-JobSeeker DEK i `user_data_keys`), owner-AAD-bindningen (paritet med KMS `EncryptionContext`), fail-closed-invarianten och `IFieldEncryptor`-primitiven (ren BCL `AesGcm`, noll AWS-import) är **oförändrade**. Bara DEK-wrap-mekanismen bytte. ADR 0049:s besluts-substans (Beslut 1–5) bevarad.

**Dom:** KMS-borttagningen är INTE längre en oläst blocker — den är en känd, scopead, **kod-bevisad TD-102**. Kvarvarande arbete = "härda självhanterad master-nyckels prod-skyddsmodell + rotation", inte "om-hemma krypto-mekanismen" (gjort).

**Major M-1:** ADR 0050:s "Öppen fråga — KMS-beroende" + Mitigering-punkten måste amenderas innan Accepted-flip (granskningstrail-hygien, Ford/Parsons/Kua). Delegeras till adr-keeper/CC.

---

## 1. Master-nyckel-skyddsmodell på VPS (TD-102, kärnfrågan)

**GDPR-bindande baseline (Art. 32):** proportionalitetsstandard, inte absolut HSM-krav. För beta-skala (opt-in-testare, låga volymer) är HSM **inte** rättsligt krav. Men master-nyckeln är load-bearing för ADR 0049:s crypto-erasure (Art. 17).

**Restrisk vs AWS-KMS — ärligt:** KMS: CMK lämnade aldrig HSM/app-minne; komprometterad box kunde *anropa* Decrypt men ej *exfiltrera CMK*. `LocalDataKeyProvider`: master-nyckel i process-minne ur config. Box-kompromiss med minnesläsning (root/core-dump/swap) → master-nyckel exponerad → alla wrapped-DEK unwrap:bara → all fält-PII läsbar. **Genuin nedgradering, ska inte bortförklaras.**

**Fyra alternativ (multi-approach → CTO; säkerhetsrangordning):**

| Alt | At-rest-skydd master-nyckel | Restrisk vs KMS | Beta-lämplig? |
|---|---|---|---|
| (a) systemd-credentials (TPM-bunden, decrypt→/run tmpfs) | **Bäst self-managed** — plaintext aldrig på persistent disk; host-bunden | Närmast KMS | **Ja — rekommenderad** |
| (b) sops+age encrypted-i-git → /run | Bra; age-privatnyckel på box = ny nyckel-till-nyckel | Bra; rotation-plus | Ja |
| (c) plain `.env` gitignored | **Svagast** — plaintext på persistent disk (snapshot/stulen disk) | Sämst | **Nej för PII-master-nyckel (B-1)** |
| (d) extern KV (Vault/Infisical)/HSM | Bäst totalt | Eliminerar nedgradering | Överkill beta (YAGNI); skala-trigger |

**Fail-closed VERIFIERAT GRÖNT:** `FieldEncryptionOptionsValidator.ValidateLocal` hård-failar tom/icke-32-byte master-nyckel i alla miljöer (rad 96-129); `LocalDataKeyProvider`-ctor re-guardar (rad 71-105). Owner-AAD (rad 220-222) bevarar cross-user-isolering. **Välbyggt, ingen ändring.**

**🔴 Blocker B-1 (Art. 32):** Master-nyckel får **inte** ligga plaintext på persistent disk (alt c) när real-PII finns. Plaintext-nyckel + ciphertext-PII i samma DB på samma box → disk-snapshot exponerar båda → kryptering ger noll skydd mot exakt det hot ADR 0049 byggdes mot. **Krav:** minst (a) eller (b) före beta-VPS. Plaintext OK **endast lokalt (ingen real-PII)**.

**🟠 Major M-2 (Art. 32, härdning):** Minne-exponering kan ej elimineras på single-box utan HSM. **Accepterad beta-restrisk** men måste dokumenteras i ADR 0049-amendment (TD-102 p.1) med namngiven skala-trigger för (d). Härdning: swap av / `vm.swappiness=0` / krypterad swap; core-dumps av (`LimitCORE=0`). Deploy-baseline (Avsnitt 6).

---

## 2. Master-nyckel-ROTATION (TD-102)

**Krypto-dom: designen SUND.** Envelope bevarad för avgränsad re-wrap (unwrap rad med gammal master → re-wrap med ny; fältdata orörd). O(användare), ej O(rader). Wire-formatets versions-byte (`0x01`) ger crypto-agility. **Välbyggt.**

**🟠 Major M-3 (Art. 32):** Rotation existerar som *design* men ej som *körbar operation*. KMS gav automatisk årlig rotation; lokal master-nyckel roterar aldrig själv → monotont växande exfiltrerings-fönster. **Krav före beta:** körbar idempotent batchad re-wrap-operation (samma Hangfire-chassi som backfill) + kadens:
- **Schemalagd:** minst årlig (KMS-paritet).
- **Händelse-driven (obligatorisk, omedelbar):** vid misstänkt box-kompromiss / offboarding av box-access / känd exponering.
- **Versions-byte:** `0x01`→`0x02`-progression för entydiga blandtillstånd under rullande re-wrap.

TD-102 p.2 = rätt scope/fas. **Major, ej Minor** (bär Art. 32-rotations-egenskapen KMS gav gratis).

---

## 3. Secrets-modell som ersätter AWS Secrets Manager

Omfattar: DB-creds, master-nyckel, framtida mejl-API-nyckel (TD-101), framtida BYOK (Fas 4).

**Rekommendation (multi-approach → CTO; säkerhetsrangordning):** sops+age **eller** systemd-credentials, INTE plain `.env` för PII-skyddande hemligheter.
- **sops+age** vinner på **rotation + granskningstrail** (versionerade krypterade secrets i git; rotation = recipient-change + re-encrypt = en commit; audit-trail). Plus för master-nyckel-rotation (Avsnitt 2).
- **systemd-credentials** vinner på enklast disk-/minne-hygien (TPM-host-bunden, tmpfs).
- **Primär rek: sops+age** (rotations-ergonomi stödjer TD-102).

**Minimi-baseline beta:** master-nyckel + DB-creds via (a)/(b), aldrig plaintext (B-1). Verifiera `appsettings.Local.json`/secrets-fil i `.gitignore`.
**Deferbart:** mejl-API-nyckel (TD-101, första utskick), BYOK (Fas 4).

**🔴 Blocker B-2 (§5.4):** Före Accepted-flip — verifiera on-disk att `appsettings.Local.json` + secrets-fil/age-nyckel är i `.gitignore` + att ingen master-nyckel/cred committats i git-historiken (gitleaks/historik-scan). Om läckt: **omedelbar rotation**. Billig gate; Blocker eftersom läckt master-nyckel = total PII-kompromiss.

---

## 4. PII-residens + data-transfer (backups, R2, AI)

**At-rest EU-residens: GRÖN.** Hetzner CX/CAX EU-only (web 2026-06-08). `FieldEncryptionOptionsValidator` no-op:ar AwsRegion-guard för Local (EU-residens = infra-egenskap, ej options) — korrekt.

**AI-transfer (ADR 0051): separat, redan grindat.** Anthropic Direct (US) + opt-in + 5 GDPR-villkor är Fas-4-blockerande, rör ej VPS-residens. Inget fynd utöver att grinden består.

**🔴 Backup-residens R2 — KÄRNFYND:**

**🟠 Major M-4 (Art. 32 + Kap. V):** `pg_dump` innehåller **icke-krypterad PII** — bara 4 kolumner fält-krypterade (ADR 0049); e-post/namn/kontodata/`waitlist_entries`/audit-IP/UA i klartext. Rå pg_dump → R2 transporterar oskyddad PII utanför fält-krypteringen. **Krav före beta:**
1. **Klient-side-kryptera dumpen före upload** (age/gpg, backup-nyckel som master-nyckeln) → R2-residensfrågan reduceras till ciphertext-at-rest.
2. **R2-jurisdiktion verifieras/dokumenteras.** Cloudflare = US-bolag → CLOUD Act även med EU-hint. Klient-krypterad → mildrar SCC. Okrypterad → tredjelandsöverföring (Art. 44-46) med SCC+TIA. **Alternativ: Hetzner-EU-storage** (Storage Box/volume-snapshot) → ingen tredjelands-fråga. **Multi-approach → CTO: R2-krypterat vs Hetzner-EU.**

**Crypto-erasure mot backups (Art. 17) — VERIFIERAD KORREKT med villkor:** DEK-kast → 4-kolumn-ciphertext olesbar i R2-dumpar. **MEN** icke-krypterad PII (e-post/namn/waitlist) raderas ej av DEK-kast — lever i varje gammal dump tills rotation. **Krav:** backup-retention/rotation definierad (t.ex. 14d, ADR 0024-paritet) så icke-krypterad PII har bortre gräns. EDPB CEF 2025: backup-overwrite med dokumenterad retention = accepterat. ADR 0024:s RDS 14d-rotation finns ej gratis på Hetzner — måste byggas.

---

## 5. TLS / cookie-säkerhet

**Bekräftat: deploy-gates är säkerhets-relevanta.**
- **`__Host-`-cookie + `secure:true`:** utan TLS på origin failar session-cookien tyst i prod (browsers sätter ej `__Host-`-secure över HTTP). `__Host-` kräver `Secure`+`Path=/`+ingen `Domain` — verifiera att Caddy/Cloudflare ej skriver om Set-Cookie. **Säkerhets-relevant deploy-gate.**
- **`Email:BaseUrl`:** reset-länkar måste `https://` absoluta (reset-token i URL över HTTP = exponering). Deploy-gate (TD-101).

**🟠 Major M-5 (härdning, gränsfall→Blocker):** TLS-topologi måste vara **Cloudflare "Full (strict)"** mot giltigt Caddy-origin-cert:
1. **Flexible = förbjudet** (HTTP CF→origin = klartext-PII).
2. **Origin-lockdown:** origin accepterar bara Cloudflare-IP:er på 443 (annars kringgås CF-WAF/TLS). Kopplas till origin-IP-döljning (Avsnitt 6).
3. **HSTS** av Caddy (max-age+includeSubDomains).
Major (konfig-härdning) men "Full strict"+origin-lockdown **icke-valfria** före real-PII; annars eskaleras M-5 → Blocker.

---

## 6. Blast-radius / single-box

Säkerhets-tillägg till ADR 0050:s erkända blast-radius: **master-nyckeln-i-minne delar samma feldomän** → box-kompromiss = total PII-kompromiss (alla DEK unwrap:bara). Inte explicit namngiven i ADR 0050 (skriven före LocalDataKeyProvider).

**Acceptabelt för beta** med proportionalitet (Art. 32, beta, opt-in, låg volym) OCH härdnings-baseline. Namngiven skala-trigger för isolering (managed PG/separat key-box/extern KV).

**🟠 Major M-6 (Art. 32 härdnings-baseline, beta-grind):**
- **SSH key-only** (lösenords-auth av, root-login av).
- **Brandvägg** (Hetzner Cloud Firewall + nftables): bara 443 (från CF-IP:er) + SSH (känd IP/VPN). Postgres/Redis/Seq **aldrig** publika — bind `127.0.0.1`/Docker-internt.
- **fail2ban** på SSH.
- **Auto-patch** (unattended-upgrades).
- **Cloudflare framför så origin-IP döljs** (M-5 p.2).
- **Minne-/dump-hygien för master-nyckel** (M-2): swap av/krypterad; core-dumps av (`LimitCORE=0`).
- **Docker:** non-root containers där möjligt; krypterad volym-disk om Hetzner stödjer.

---

## 7. Security-gate-lista: MÅSTE före real-PII vs deferbart

**Proportionalitets-tröskeln:** beta-opt-in har lägre Art. 32-tröskel än publik launch — men **inga MVP-undantag för GDPR-brott** (§12). Gräns: *exponeras faktisk real-PII oskyddat?* Allt som lämnar PII oskyddat (plaintext master-nyckel-på-disk, okrypterad backup till tredjeland, klartext-sista-ben) = **brott → Blocker**. Defense-in-depth utöver fungerande skyddslager (HSM vs env-nyckel, minne-exponering på härdad box) = **beta-proportionerlig accepterad risk + skala-trigger → Major**.

**MÅSTE grönt före första real-PII:**

| # | Gate | Fynd | GDPR-bindande? |
|---|---|---|---|
| 1 | Master-nyckel INTE plaintext-på-disk | **Blocker B-1** | Ja Art. 32 |
| 2 | Gitleaks/historik-scan; rotation om läckt | **Blocker B-2** | Ja Art. 32/5.4 |
| 3 | Körbar master-nyckel-rotation + kadens | **Major M-3** | Ja Art. 32 |
| 4 | pg_dump klient-side-krypterad | **Major M-4** | Ja Art. 32/44-46 |
| 5 | Backup-retention/rotation definierad | **Major M-4** | Ja Art. 17/CEF 2025 |
| 6 | R2-jurisdiktion verifierad ELLER Hetzner-EU-backup | **Major M-4** | Ja Kap. V |
| 7 | Cloudflare "Full strict" + origin-IP-lockdown + HSTS | **Major M-5** | Ja Art. 32 |
| 8 | VPS-härdnings-baseline | **Major M-6** | Ja Art. 32 |
| 9 | ADR 0049-amendment (prod-skyddsmodell + restrisk + skala-trigger) | **M-2+M-1** | Trail |
| 10 | ADR 0050 KMS-blocker-prosa amenderad → TD-102 | **Major M-1** | Trail |

**Deferbart (rätt-fas §9.6):** mejl-API-nyckel (TD-101), BYOK+5 AI-villkor (Fas 4), extern KV/HSM (skala-trigger M-2), managed PG/multi-box (skala), observability (TD-104), Migrate-VPS-port (TD-105 — deploy-blocker ej PII-exponering).

---

## Sammanfattning

**2 Blockers, 4 Majors.** Inga invändningar mot AWS-exit-strategin *som riktning* — Hetzner-EU GDPR-ren at-rest, krypto redan provider-agnostiskt migrerat (KMS-blocker→TD-102 **bekräftad kod-bevisad**). Blockers/Majors är **kontroller före första real-PII**, inte före Accepted-flip av strategin.

**Entydiga domar:** B-1, B-2, M-3, M-4-krypteringskravet, M-5, M-6 = GDPR-/säkerhets-golv.
**Multi-approach (CTO):** secrets sops+age vs systemd-credentials (rek sops+age); backup R2-krypterat vs Hetzner-EU; master-nyckel-skydd (a) vs (b); skala-trigger för (d).
**Eskaleras till Klas:** B-1 + B-2 (master-nyckel-skyddsmodell är enda kontroll mellan box-kompromiss och total PII-exponering — får ej defereras).
**Re-review krävs:** andra security-auditor-granskning av faktisk prod-config före beta-data (TD-102 p.3) = gaten, inte denna design-dom.

**Relevanta filer:** `docs/decisions/0050-*.md` (rad 100-117 KMS-prosa → M-1; Beslut 4 R2 → M-4), `docs/decisions/0049-*.md` (Not 2026-06-06; amendment-mål M-2/M-3/M-4-retention), `src/JobbPilot.Infrastructure/Security/LocalDataKeyProvider.cs`, `FieldEncryptionOptionsValidator.cs` (fail-closed grön), `FieldEncryptionOptions.cs`, `docs/tech-debt.md` (TD-102 rad 343-377).
