# ADR 0066 — AWS dev-stack teardown 2026-05-26 — semester-pause (Fas B)

**Datum:** 2026-05-26
**Status:** Accepted
**Kontext:** Klas-direktiv 2026-05-26 efter MVP-demo + semester-pause inför 0-6 mån-horisont mot VPS-uppstart; senior-cto-advisor decision-maker-rond `a547e7ddc12dd6a81` 2026-05-26
**Beslutsfattare:** Klas Olsson (strategic GO + Beslut 3 "stoppa allt"-direktiv); senior-cto-advisor (Beslut 1–6 verbatim-dom)
**Relaterad:** ADR 0005 (kostnadsskydd — relevans-skifte ej supersession), ADR 0024 (RDS backup-retention 14d), ADR 0036 (prod-stack deferred — bekräftar dev som enda live-miljö), ADR 0044 (CI-aggregat), ADR 0049 (TD-13 KMS-envelope — field-key bevarad i prod-baseline), ADR 0050 (Hetzner-exit, Proposed — komplementär, EJ supersedad), ADR 0064 (Worker-precomputed Redis-cache), ADR 0065 (PR-flöde — denna ADR levereras via PR)

> **Livscykel-not:** Skriven 2026-05-26 av Claude Code via adr-keeper-agent på
> Klas explicit-begäran (medveten override av CLAUDE.md §9.4
> webb-Claude-verbatim-konventionen för denna session). Besluts-substansen är
> transkriberad från senior-cto-advisor-rond `a547e7ddc12dd6a81` 2026-05-26 +
> Klas-direktiv samma datum — inga nya beslut konstruerade.

---

## Kontext

JobbPilot driftas idag på AWS (eu-north-1) som lean dev-stack som i praktiken
bär hela driften: ECS Fargate (API + Worker), RDS PostgreSQL 18.3
`db.t4g.micro` (1 GB RAM), ElastiCache Valkey 8 `cache.t4g.micro`, ALB, VPC,
ECR, Route 53, ACM, CloudWatch, KMS field-key (per ADR 0049), Secrets Manager.
MVP-demo genomförd 2026-05-26.

**Kostnadsläge 2026-05-26 (Cost Explorer-screenshot verifierad samma dag):**

- Month-to-date: $84.84
- Forecast denna månad: $115.88/mån

**Användar-/data-läge:**

- Inga betalande användare (waitlist tom — Klas-bekräftat samma datum)
- Korpus ~46 000 JobTech-annonser (publikt API, gratis re-import vid återstart)
- Ingen produktions-PII (waitlist tom; demo-data utan reell användarinformation)

**Strategisk situation:**

Klas tar semester. VPS-uppstart planerad inom 0-6 mån-horisont (sannolikt
ADR 0050:s Hetzner-cutover eller likvärdig). Under semester-pausen finns:

- Ingen aktiv utveckling som kräver dev-stacken
- Ingen användartrafik att betjäna
- Ingen lärar-/demo-press som motiverar uppe-tid

På studentbudget är ~$115/mån för en pausad miljö en disciplinmiss. ADR 0005
(kostnadsskydd) sätter Budget-Actions-tröskel vid $50/mån — men ADR 0005:s
mekanism är reaktiv mot budget-breach, inte proaktiv mot förutsedd pausad
period. En proaktiv teardown är därför rätt verktyg, inte budget-actions-flip.

ADR 0050 (Hetzner-exit, Proposed) är en separat strategi: **permanent
provider-exit** efter KMS-rehoming-design. Denna ADR (0066) är temporär
**pause-and-resume** mot samma AWS-yta — olika triggers, olika trade-offs.
Beslut 6 (nedan) bevarar reversibilitet medvetet för att inte föregripa
ADR 0050:s slutgiltiga utvärdering.

## Beslut

### Beslut 1 — Fas B: destroy `environments/dev/`, bevara prod-baseline + bootstrap

Kör `terraform destroy` på dev-stacken (`infra/terraform/environments/dev/`).

**Bevaras:**

- `infra/terraform/bootstrap/` (state-bucket + DynamoDB-locks) — alltid bevarad,
  ej i scope för någon teardown-fas
- `infra/terraform/environments/prod/` (baseline-only):
  - Route 53 hosted zone (jobbpilot.se)
  - KMS-master-key + `jobbpilot-td13-field-key` (per ADR 0049)
  - CloudTrail (multi-region-trail)
  - IAM Identity Center
  - Bedrock-policy-fragment (per ADR 0005)
  - AWS Budgets

**Förväntad kvarvarande kostnad:** ~$1-2/mån (Route 53 $0.52/zone + KMS $1 per
key/mån × 2 prod-keys + CloudTrail $0 för multi-region-trail + tax). Detta är
~98 % reduktion från $115.88/mån forecast.

**Motiverat mot källor:**

- **Ford/Parsons/Kua 2017 (*Building Evolutionary Architectures* kap. 4):**
  reversibilitet är en central evolutionary property. Bevarad prod-baseline
  möjliggör enkel återstart — re-apply dev-stack mot samma Route 53-zone och
  KMS-aliases utan att rebuild identity- eller DNS-grund.
- **Beck/Fowler 2004 (*XP Explained* kap. 17):** YAGNI mot Fas D (close
  AWS-konto). "Kanske räcker" är inte tillräcklig grund för irreversibel
  åtgärd.
- **Saltzer/Schroeder 1975 (IEEE-paper, fail-safe defaults):** bevara
  optionalitet är default; irreversibla åtgärder kräver explicit override.
- **Hunt/Thomas 1999 (*Pragmatic Programmer* kap. 7):** KISS — 95+ %
  kostnadsreduktion är tillräckligt för studentbudget under semester; Fas C/D
  förstör mer värde (re-bootstrap-kostnad, AWS-konto-stängningsprocess) än de
  sparar.

### Beslut 2 — Skip pg_dump (data-erasure utan backup)

Ingen `pg_dump` körs före destroy. Waitlist är tom (Klas-bekräftat
2026-05-26 — implicit `SELECT COUNT(*) FROM waitlist_entries = 0`). Ingen
real-användar-PII bearbetas, ingen affärs-data går förlorad.

Korpus av ~46k JobTech-annonser är **publikt API-data** — gratis att re-importa
vid återstart (par timmar Hangfire-tid via ADR 0032:s ingestion-pipeline).
Demo-data utan reell användarinformation.

**Motiverat mot källor:**

- **GDPR Art. 5(1)(c) data-minimisation:** tillämpas naturligt — det finns
  inget att rädda som är värt risken/komplexiteten av en mellanlagrad
  pg_dump-artefakt.
- **GDPR Art. 17 right-to-erasure:** automatisk täckning när data raderas i
  samband med destroy; ingen backup som måste hanteras separat.

### Beslut 3 — Skip RDS final snapshot + ECR `force_delete = true` (clean destroy)

**Klas-direktiv 2026-05-26 (verbatim):** "Vi kan stoppa allt på jobbpilot ju
nu, dvs behöver inget bakgrundsjobb, ingen snapshot osv."

**Konsekvens i Terraform-modul-konfiguration vid destroy:**

- `skip_final_snapshot = true` i RDS-modul (eliminerar permanent
  snapshot-kostnad ~$1/mån + storage-tax)
- `deletion_protection = false` på RDS innan destroy
- ECR `force_delete = true` (raderar images i samband med repo-destroy —
  images re-pushas från GitHub Actions deploy-workflow vid återstart)

**Motiverat mot källor:**

- **AWS RDS-docs (`skip_final_snapshot`):** explicit dokumenterat val för
  scenario där rådata-bevaring ej krävs.
- **YAGNI (Hunt/Thomas 1999):** final-snapshot-artefakter som vi vet att vi
  inte vill restore:a är dead-weight på kostnad och ops-yta.

### Beslut 4 — Skip KMS-key-deletion-schedule (reversibilitet > crypto-erasure)

KMS field-key (`jobbpilot-td13-field-key` per ADR 0049) bor i prod-baseline
och **schedule:as INTE för deletion** i samband med dev-stack-teardown.

**Mekanik:**

Vid destroy av dev-stacken raderas data-på-RDS (Beslut 3). Field-key bevaras
i prod-baseline. RDS automated backups (per ADR 0024 `backup_retention_period
= 14d`) går ut automatiskt efter 14 dygn — ingen aktiv backup-radering krävs.

Reversibilitet vinner över crypto-erasure för semester-pause-horisonten
(0-6 mån): om/när dev-stacken återstartas mot samma AWS-yta är field-key
omedelbart tillgänglig utan re-create.

**Motiverat mot källor:**

- **EDPB Guidelines 4/2019 (Art. 25 by design/by default)** och **EDPB CEF
  2025-rapporten:** godkänner automatic overwrite cycles + live-radering som
  Art. 17-täckning för backups. Crypto-erasure är inte krav när data faktiskt
  raderas i samband med destroy.
- **GDPR Art. 17 + Art. 32:** uppfyllt via destroy + 14d-backup-rotation;
  ingen aktiv key-deletion behövs för compliance-täckning.
- **AWS KMS-docs (`schedule-key-deletion`):** explicit 7-30 dagars-fönster är
  defensiv mekanism mot oavsiktlig key-radering; att avstå är medveten
  reversibilitets-prioritering, inte ouppmärksamhet.

**Trigger för framtida key-deletion:** vid faktisk VPS-uppstart (post-
teardown), om field-key INTE behöver återanvändas (t.ex. ADR 0050:s
Hetzner-rehoming väljs och KMS-blockern löses utanför AWS) → då schedule:a
30d-deletion via `aws kms schedule-key-deletion`. Ej i scope för denna ADR.

### Beslut 5 — Vercel pause (Klas-uppgift, dokumenteras)

Frontend på Vercel **pausas** via Vercel-dashboard (Klas-uppgift — CC har
ingen Vercel-access). Pause > delete på samma reversibilitets-grund som
Beslut 1: one-knapps-toggle vs full re-setup.

**Konsekvens:**

- www.jobbpilot.se visar Vercel-fallback efter pause
- dev.jobbpilot.se 404 från destroy-tid till återstart (parallellt med
  ALB-radering)
- Vercel free-tier = $0 oavsett, men pause förhindrar oavsiktlig deploy under
  semester (t.ex. om CC kör `gh workflow run` av misstag)

**Holding-page:** out-of-scope för denna ADR — separat civic-utility-ton-fråga
om/när det blir aktuellt.

### Beslut 6 — AWS-konto kvar öppet (Fas D avvisad)

**Avvisat alternativ:** close AWS account efter teardown.

"Kanske räcker" från Klas är inte tillräcklig grund för irreversibel åtgärd
(AWS-konto-stängning är 90-dagars-process med svår-reverserbar effekt på
billing-historik, IAM Identity Center-länkning, Route 53-zone-ägarskap).

**Bevarat konto =** optionalitet för enkel återstart + ingen 90-dagars-konto-
stängningsperiod att navigera vid VPS-pivot-beslut.

**Motiverat mot källor:**

- **Saltzer/Schroeder 1975 (fail-safe defaults):** irreversibla åtgärder
  kräver explicit positiv override, inte "kanske".
- **Ford/Parsons/Kua 2017 (evolutionary architecture):** optionalitet är
  egenvärde på en evolutionary horizon — Fas D är en separat strategisk
  decision som hör hemma i ADR 0050:s exekvering, inte i en semester-pause-
  teardown.

## Konsekvenser

### Positiva

- **~98 % kostnadsreduktion** ($115.88 → ~$2/mån). Materiell vinst på
  studentbudget under semester-pausen.
- **Reversibilitet bevarad** — re-apply dev-stack mot samma prod-baseline-
  resources (Route 53-zone, KMS-aliases) utan re-bootstrap.
- **Eliminerar drift-risk under semester** — ingen aktiv ops-yta att övervaka,
  inga säkerhetspatches att hålla efter, ingen CloudWatch-alarm-respons-yta
  som ackumulerar tickets utan respondent.
- **Tre TDs obsoleta under semester-pause** (stängs som sido-effekt — Klas/CTO-
  triage avgör om de återöppnas vid återstart):
  - TD-91 (RDS-param-group-drift)
  - TD-94 (`ListJobAds` COUNT-perf)
  - TD-95 ("Senaste sökning" tom)

### Negativa

- **Re-import av ~46k JobTech-annonser vid återstart** — gratis publikt API,
  par timmar Hangfire-tid via ADR 0032:s ingestion-pipeline. Acceptabelt pris.
- **~$2/mån prod-baseline-kostnad** kvarstår under budget-action-tröskel
  ($50/mån per ADR 0005). Inom Klas-budgetacceptans för optionalitet.
- **dev.jobbpilot.se 404** från destroy-tid till återstart (Vercel-frontend
  pausad parallellt — symmetri-täckning).
- **DEMO-data + waitlist (tom) försvinner permanent.** Godkänt per Beslut 2 —
  waitlist är tom; demo-data är trivial att återskapa.

### Mitigering

- **CC-/Klas-runbook för återstart:** dokumenteras vid faktisk återstart (ej i
  scope nu) — Terraform-apply-ordning, GitHub Actions deploy-trigger,
  Hangfire-jobb för korpus-re-import, Vercel-resume-toggle.
- **Budget-monitoring fortsätter** — AWS Budgets bevarade i prod-baseline,
  Klas får alert om kvarvarande kostnad oväntat skenar (t.ex. CloudTrail-
  storage-tillväxt eller Route 53-query-volume).
- **ADR 0050:s KMS-rehoming-design oberoende** — denna ADR förutsätter inte
  ADR 0050-utfall; field-key bevarad så ADR 0050:s rehoming kan ske separat
  vid faktisk Hetzner-pivot.

## Alternativ övervägda

- **Fas A — pausa ECS-services + stoppa RDS utan teardown.** Avvisad —
  RDS-stop är 7-dygns-fönster (AWS auto-startar efter), Fargate-pausning
  sparar inte Fargate-tids-kostnad signifikant, ALB-/NAT-Gateway-kostnaden
  kvarstår. Ej en signifikant kostnadsreduktion för semester-horisont.
- **Fas B — destroy dev-stack, bevara prod-baseline + bootstrap (valt).** Se
  Beslut 1.
- **Fas C — destroy även prod-baseline (bevara endast bootstrap).** Avvisad —
  förlorar Route 53-zone (DNS-history + waitlist-domän-kontinuitet), KMS-
  master-key (Bedrock-policy-fragment per ADR 0005), CloudTrail-audit-historik.
  Re-bootstrap är ~30-60 min ops, men förlust av Route 53-zone-ägarskap kan
  introducera DNS-glitch vid återstart. Inte värt $1-2/mån-besparingen.
- **Fas D — close AWS account.** Avvisad per Beslut 6 — irreversibel, "kanske
  räcker" inte.
- **Behåll dev-stacken körande genom semester.** Avvisad — $115/mån för pausad
  miljö är disciplinmiss på studentbudget; AWS-billing-disciplin är en del av
  Mastercard-nivå-kvalitetsstandard (CLAUDE.md §1).
- **Kör `pg_dump` + S3-upload före destroy (försäkring).** Avvisad per Beslut
  2 — ingen real-data att försäkra; pg_dump-artefakt blir dead-weight
  underhållsdrift.
- **Schedule:a KMS-field-key för 30d-deletion direkt vid teardown.** Avvisad
  per Beslut 4 — reversibilitet vinner över crypto-erasure för pause-horisont;
  EDPB godkänner backup-overwrite-cycle som Art. 17-täckning.

## Implementationsstatus

**Accepted 2026-05-26.** Faktisk teardown utförs **inom samma session/dag**
efter ADR 0066-mergen via PR (per ADR 0065).

**Teardown-sekvens (orienteringspunkt, ej detaljerad runbook):**

1. Pre-flight: `terraform plan -destroy` i `environments/dev/` för verifiering
   att inga prod-baseline-resurser dras med
2. Terraform-modul-overrides för clean destroy: `skip_final_snapshot = true`,
   `deletion_protection = false`, ECR `force_delete = true`
3. `terraform destroy -auto-approve` (efter Klas-GO)
4. Verifiera AWS Cost Explorer dagliga forecast inom 24-48h att kostnad
   sjunker mot ~$2/mån-targetet
5. Vercel-dashboard: pausa frontend-deployment (Klas-uppgift)
6. Uppdatera `docs/current-work.md` + session-log + stäng TD-91/94/95 i
   `docs/tech-debt.md`

**Trigger för återstart:** vid faktisk VPS-/dev-pivot-beslut — separat
session, separat runbook-design, ej i scope för ADR 0066.

## Trigger för omvärdering

Detta beslut omvärderas (ny ADR som superseder, alternativt amendment) vid
något av följande:

1. **ADR 0050 (Hetzner-exit) Accepted + utförd före återstart** → prod-
   baseline-resources kan eventuellt rehoma:s eller raderas separat; denna
   ADR blir då retrospektivt en transition-period-ADR.
2. **VPS-uppstart sker mot samma AWS-yta inom 0-6 mån** → re-apply dev-stack;
   denna ADR förblir historisk teardown-dokumentation.
3. **Kostnad förblir > $5/mån i prod-baseline** under 2 månader i följd (utan
   förklaring) → ny ADR för Fas C-bedömning eller resurs-specifik teardown.
4. **Säkerhetsincident i CloudTrail/IAM** under pausen → omvärdera om bevarade
   resurser introducerar attack-yta som överstiger optionalitets-värdet.

Vid trigger: ny ADR skapas. ADR 0066 superseras eller amenderas explicit.

## Relation till andra beslut

- **ADR 0005 (kostnadsskydd):** Denna teardown realiserar ADR 0005-andan
  (proaktiv kostnadshygien) men för annan grund (semester-pause, ej
  budget-breach-trigger). **Relevans-skifte under pausen, ej supersession** —
  ADR 0005:s mekanism (Budget Actions, Bedrock-deny, registrations_open-
  gating) återaktualiseras vid återstart eller pivot.
- **ADR 0024 (RDS backup-policy 14d):** Backup-retention bevaras tills
  auto-expire efter destroy (14d-fönster). Inom ADR 0024:s normaldrifts-
  policy — ingen amendment behövs.
- **ADR 0036 (prod-stack deferred):** Bekräftar att dev är vår enda live-
  miljö. Teardown är konsekvens av den positionen — ingen amendment.
- **ADR 0044 (CI-aggregat):** PR-flöde för denna teardown följer ADR 0044
  (CI måste passera) + ADR 0065 (PR-gate).
- **ADR 0049 (TD-13 KMS-envelope):** KMS field-key bevarad i prod-baseline
  per Beslut 4. ADR 0049:s envelope-encryption-mekanik orörd; data raderas
  via RDS-destroy. Ingen amendment. _(Not 2026-07-12 #802: den KOD-sidiga
  AWS-KMS DEK-providern är nu borttagen — fält-krypteringen är Local-only. Detta
  rör inte 0066:s AWS-resident-nyckel-/teardown-resonemang; se ADR 0049/0050-
  amendments 2026-07-12.)_
- **ADR 0050 (Hetzner-exit, Proposed):** **EJ supersedad av ADR 0066.** ADR
  0050 är full permanent provider-exit med KMS-rehoming-blocker; ADR 0066 är
  temporär semester-pause med reversibel återstart mot samma AWS-yta. Olika
  strategier, olika triggers, olika trade-offs. **Komplementär:** båda
  hanterar AWS-kostnadshygien, men 0050 är permanent provider-exit medan
  0066 är pause-and-resume. ADR 0050 förblir Proposed; dess KMS-rehoming-
  design är oberoende av denna teardown.
- **ADR 0064 (Worker-precomputed Redis-cache):** Worker-Redis-yta försvinner
  med dev-stacken; återställs naturligt vid återstart (Worker Hangfire-cron
  + Redis-write-port re-konfigureras automatiskt). Ingen amendment.
- **ADR 0065 (PR-flöde):** Denna ADR levereras via PR per ADR 0065.

## Referenser

- senior-cto-advisor decision-maker-rond `a547e7ddc12dd6a81` 2026-05-26
  (Beslut 1–6 verbatim-dom)
- Klas-direktiv 2026-05-26 (Beslut 3 "stoppa allt"-citat + Beslut 5/6 strategic
  GO)
- AWS Cost Explorer-screenshot 2026-05-26 (MTD $84.84, forecast $115.88)
- Ford, N., Parsons, R., Kua, P. — *Building Evolutionary Architectures*
  (O'Reilly, 2017), kap. 4 (reversibility, evolutionary properties)
- Beck, K., Fowler, M. — *Extreme Programming Explained* 2nd ed. (Addison-
  Wesley, 2004), kap. 17 (YAGNI)
- Hunt, A., Thomas, D. — *The Pragmatic Programmer* (Addison-Wesley, 1999),
  kap. 7 (KISS, YAGNI)
- Saltzer, J. H., Schroeder, M. D. — *The Protection of Information in
  Computer Systems* (IEEE, 1975), princip 5 (fail-safe defaults)
- EDPB Guidelines 4/2019 (Art. 25 Data Protection by Design and by Default)
- EDPB Coordinated Enforcement Framework 2025-rapport (right-to-erasure i
  backups)
- GDPR Art. 5(1)(c) (data minimisation), Art. 17 (right to erasure), Art. 32
  (security of processing)
- AWS RDS-docs: `skip_final_snapshot`, `backup_retention_period`,
  `deletion_protection`
- AWS KMS-docs: `schedule-key-deletion` (7-30d-fönster)
- CLAUDE.md §1 (Mastercard-disciplin), §1.5 (session-protokoll), §9.4
  (discovery-rapporter + livscykel-not), §9.6 (in-scope-fix vs TD-skapande)
- ADR 0005, 0024, 0036, 0044, 0049, 0050, 0064, 0065
