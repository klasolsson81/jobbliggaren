# CTO-beslut — TD-13 ADR 0049 designval (5 punkter), FAS 3.5 STOPP D

**Datum:** 2026-05-18
**Roll:** senior-cto-advisor (decision-maker, §9.6) — agentId `ab0fea3417811c5f7`
**On-disk HEAD:** `8474c06` (read-only)
**Uppdrag:** avgör 5 interna designval för TD-13 så ADR 0049 kan utkastas.
Fas-sekvensering (FAS 3.5 före FAS 4) redan Klas-GO:dd; detta omprövar den ej.

> CC-not: senior-cto-advisor-agentens egen filskrivning landade ej på disk
> (§9.4 paste-verifiering, samma mönster som IDOR-auditen 2026-05-18). Denna
> fil är CC:s trogna verbatim-transkription av agentens dom till review-
> trailen — ingen fabrikation, ingen omtolkning. Agenten var read-only
> (git status verifierad: inga on-disk-ändringar av agenten).

---

## Beslut 1 — DEK-granularitet: per-användare för de 4 user-ägda; per-rad för raw_payload (om/när krypteras)

Hybrid efter äganderätt, inte uniform.

- `cover_letter`, `application_notes.content`, `follow_ups.note`,
  `resume_versions.content` → **per-användare-DEK** (en DEK per `JobSeeker`,
  wrappad av CMK, lagrad i `user_data_keys`-tabell eller på JobSeeker-aggregatet).
- `job_ads.raw_payload` → **per-rad-DEK** om Beslut 3 leder till kryptering
  (gör det inte — se Beslut 3). JobAd är ej user-ägd; per-användare semantiskt
  omöjlig.

**Motivering:** DDD aggregate-ägande (Evans 2003; Vernon 2013 kap. 10) — DEK-
livscykel följer aggregatets, inte fysiska raden. De 4 lever/dör med JobSeeker
(Art. 17). En DEK per JobSeeker → Beslut 2 (crypto-erasure) möjlig och billig.
SRP (Martin 2017 kap. 7): per-användare-DEK = en change-reason (kontoradering),
inte N×M nyckelpunkter. KISS/key-rotation: O(användare) re-wrap vid CMK-rotation.

**Avvisat:** uniform per-rad (bryter billig-Art.17 + SRP); uniform per-aggregat
(döljer att applications/resume_versions är olika aggregat under samma owner);
uniform per-användare inkl. raw_payload (ingen ägande-användare → primitive-
obsession på nyckelnivå, bryter bounded context).

---

## Beslut 2 — Crypto-erasure för Art. 17/backups: JA, som dokumenterad förstärkning ovanpå backup-overwrite (ej ersättning)

ADR 0049 antar crypto-erasure för de 4 user-ägda kolumnerna (möjliggjort av
per-användare-DEK). Kontoradering kastar DEK → backup-resident ciphertext
omedelbart olesbar. ADR 0024:s backup-overwrite-story (RDS automated 7–35d)
**kvarstår** som primär Art. 17-motivering; crypto-erasure stänger fönstret
*under* overwrite-perioden.

**Motivering:** EDPB CEF 2025 (rapport 2026-02): backup-exklusion utan
motivering = fynd; "raderas vid overwrite" + dokumenterad motivering = OK;
crypto-erasure = medel, ej ursäkt. ADR 0024 delbeslut 1/7 täcker
`audit_log`+CloudWatch men **inte** RDS automated-backup-PII — crypto-erasure
stänger exakt det gapet. Defense-in-depth (OWASP; Microsoft Learn): kollapsar
7–35d klartext-fönster till "tiden att kasta en nyckel". YAGNI-kontroll:
per-användare-DEK byggs ändå (Beslut 1) → crypto-erasure = litet tillägg, ej
separat system.

**Avvisat:** NEJ/enbart-overwrite (Mastercard-test — 90%-kontroll som stannar);
JA-som-ersättning (bryter EDPB-norm: crypto-erasure får ej åberopas som ursäkt
för avsaknad retention-story — båda måste samexistera i ADR-texten).

**Trade-off:** restore av backup med sedan-raderad användare ger olesbar
ciphertext (önskat). ADR 0049 dokumenterar: restore återupplivar ej raderat
innehåll; key-rotation bevarar icke-raderade användares wrapped DEK:er.
**Ingen ADR 0024-amendment** — ADR 0024 (live+applog) ↔ ADR 0049 (backup-PII-
lager) är komplementära; **cross-ref ska skrivas, ej amendment**.

---

## Beslut 3 — raw_payload: (a) EXKLUDERA ur envelope-scope. (b) AVVISAS. Ingen Klas-STOPP-eskalering.

raw_payload krypteras **inte** av TD-13-envelopet. ADR 0049 dokumenterar
exklusionen med tre-lagers befintlig motivering.

**Motivering:** YAGNI+KISS (Hunt/Thomas 1999; Martin 2017 kap. 22) — raw_payload
redan saniterad (JobTechPayloadSanitizer allowlist, ADR 0032 §8-amendment),
self-purgande (30d), Art.17-null-out (RecruiterPiiPurger). Envelope ovanpå tre
befintliga kontroller på redan-saniterad icke-user-PII = noll GDPR-vinst till
priset av att bryta tre Postgres-side-mekanismer. Component cohesion/CRP
(Martin 2017 kap. 13): raw_payload load-bearing för generated columns →
taxonomi-sök-SPOT (ADR 0039) + JsonContains-Art.17 — funktionellt kohesivt med
giltig JSONB. SRP: TD-13 change-reason = "skydda user-ägd Känsligt-PII vid
backup-läckage"; raw_payload change-reason = "JobTech-ingest-artefakt med egen
sanitering/retention" — olika domäner (ADR 0032/0039, ej TD-13). Risk/värde
(Fowler 2002 PoEAA): (b) kostar schema-omstrukturering + JsonContains-ersättning
+ SPOT-omskrivning + jsonb→text + migration/test; GDPR-vinst = noll additionell.
Negativ ROI.

**Avvisat (b):** scope-creep förklädd till grundlighet — skulle eskalera Klas
för noll GDPR-värde. CTO-jobb = avvisa scope-creep explicit.

**Trade-off:** raw_payload förblir klartext-JSONB at-app-rest (skyddad av RDS
KMS + sanitizer + 30d-purge + Art.17-null-out). Medveten dokumenterad exklusion
(EDPB CEF 2025: exklusion *med* motivering = accepterat). ADR 0049 skriver
motiveringen explicit + future-watch: om någon av de 4 kolumnerna får
WHERE/LIKE-konsument bryts kryptering rakt-av och frågan återöppnas
(searchable-encryption utanför scope, YAGNI idag).

**Konsekvens:** (a) valt entydigt → ingen (b)-scope-vidgning → **ingen
Klas-STOPP-eskalering utlöses** (uppdragets eskalerings-trigger inträffar inte).
CC-direkt-implementerbart (dokumentations-/scope-avgränsning, ingen raw_payload-
kodändring).

---

## Beslut 4 — Migrerings-strategi: Hybrid — lazy encrypt-on-write (primär) + bounded idempotent backfill-job (completion-garanti)

Lazy converter krypterar vid write, dekrypterar vid read; read-path tål både
klartext-legacy och ciphertext via versions-/sentinel-prefix (t.ex. `v1:` +
base64, bär DEK-version för key-rotation). Idempotent batchat cancellation-bart
backfill-job (Hangfire-chassi, samma mönster som PurgeStaleRawPayloadsJob /
HardDeleteAccountsJob) driver deterministiskt till 100% ciphertext.

**Motivering:** TD-13-spec mandaterar icke-destruktiv (rad 98-100). Ren lazy =
obegränsad klartext-svans (besegrar FAS 3.5-syftet). Ren backfill = downtime.
Ford/Parsons/Kua 2017: migration utan deterministiskt slut = permanent
dual-state; backfill = fitness-funktion (`COUNT(*) WHERE ej-ciphertext = 0`).
Cryptographic agility (OWASP): sentinel behövs ändå för key-rotation → ej
additiv komplexitet. CCP (Martin 2017 kap. 13): återanvänd Hangfire-kohesion.

**Avvisat:** ren lazy (svans, ej bounded); ren backfill big-bang (downtime,
onödigt då converter ändå byggs).

---

## Beslut 5 — jsonb→text-skifte: additiv ny kolumn + backfill + cutover + drop. Aldrig in-place ALTER TYPE.

Gäller `resume_versions.content` (raw_payload berörs ej — Beslut 3a). Fyra steg:

1. **Additiv:** `content_enc text NULL` (noll-risk, ingen lock).
2. **Backfill:** Beslut 4-jobbet populerar `content_enc` lazy+batch; read-path
   prioriterar `content_enc`, fallback `content`.
3. **Cutover:** vid 100% (`COUNT(*) WHERE content_enc IS NULL = 0`) flippa
   EF-mappning till `content_enc`; `content` read-only legacy.
4. **Drop:** separat senare migration (egen commit, efter prod-verifiering)
   droppar gamla `content` JSONB.

**Motivering:** expand/contract/parallel-change (Fowler; Ford/Parsons/Kua 2017)
— typ-skifte med befintlig data aldrig in-place destruktivt; varje steg
reverterbart med egen down(). DDD: befintlig JSON-VC+ValueComparer
(ResumeVersionConfiguration.cs:41-59) bevaras — krypto komponeras runt
(`ResumeContent→JSON→ciphertext→content_enc`), comparer fortsatt på klartext-
JSON. Idempotent (`IF [NOT] EXISTS`, ADR 0024-precedens).

**Avvisat:** in-place `ALTER COLUMN TYPE text USING` (destruktiv, ingen down,
table-lock); ciphertext-i-jsonb (typ-lögn, bryter schema-som-domänsanning
Evans 2003 + onödig JSONB-overhead på opak data).

---

## Klas-GO-matris (§9.6 p.5)

| Beslutspunkt | Klas-GO? | Skäl |
|---|---|---|
| Fas-sekvensering FAS 3.5<FAS 4 | Redan inhämtad | Prejudikat-dom 2026-05-18 |
| Beslut 1–5 (var för sig) | Nej — CC-direkt efter ADR-Accepted | Entydiga mot principer (§9.6 p.5) |
| raw_payload (a) | Nej — ingen (b)-eskalering | (a) valt → trigger inträffar ej |
| **ADR 0049 Proposed→Accepted** | **JA — enda kvarvarande grind** | Arkitekturbeslut (§8 p.9); Klas läser+godkänner före implementation |

**Sammanfattning:** inget enskilt designbeslut kräver egen Klas-GO. Enda
kvarvarande Klas-grind = godkänn ADR 0049 Proposed→Accepted. Efter det är hela
TD-13 CC-direkt-implementerbart enligt dessa 5 beslut i split-batch-struktur
(prejudikat-domen §2: 1.5–2.5 v scope-realism).

---

## Referenser

Martin *Clean Architecture* (2017) kap. 7/13/22 · Evans *DDD* (2003) + Vernon
*IDDD* (2013) kap. 10 · Fowler *Refactoring* 2e (2018) + *PoEAA* (2002) +
ParallelChange · Hunt/Thomas *Pragmatic Programmer* (1999) kap. 7 ·
Ford/Parsons/Kua *Building Evolutionary Architectures* (2017) · Microsoft Learn
Encryption-at-rest/key-hierarchy · OWASP defense-in-depth/crypto-agility · EDPB
CEF 2025 right-to-erasure (2026-02) + blockchain-guidelines 2025 · ADR
0009/0024/0032/0039/0042 · `2026-05-18-td13-pii-encryption-discovery.md` ·
`docs/tech-debt.md:77-108` · Nästa fria ADR = **0049** (senaste = 0048)
