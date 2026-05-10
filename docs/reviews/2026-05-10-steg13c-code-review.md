# Code-review: STEG 13c — Route53 + ACM + DNS + TLS (pre-apply)

**Status:** APPROVE-with-fixes (1 Major, 4 Minor, 2 Nit)
**Granskat:** 2026-05-10
**Reviewer:** code-reviewer agent
**Auktoritet:** CLAUDE.md §2.1 (Clean Arch / module-isolation), §5.4 (säkerhet),
egen kontext: BUILD.md §15, ADR 0026 (ALB HTTP-only Fas 0), TD-30 (domän-trigger)
**Scope:** Terraform — `modules/route53/`, `modules/acm/`,
`environments/prod/{main,variables,outputs}.tf`,
`environments/dev/{main,variables,outputs}.tf`
**Filer granskade:** 9 filer (2 nya moduler, 6 edited environment-filer,
1 cross-reference till `modules/alb/main.tf`)

---

## Sammanfattning per fil

| Fil | Critical | Major | Minor | Nit |
|---|---|---|---|---|
| `modules/route53/main.tf` | 0 | 0 | 0 | 0 |
| `modules/route53/variables.tf` | 0 | 0 | 0 | 0 |
| `modules/route53/outputs.tf` | 0 | 0 | 0 | 0 |
| `modules/acm/main.tf` | 0 | 0 | 1 | 1 |
| `modules/acm/variables.tf` | 0 | 0 | 0 | 0 |
| `modules/acm/outputs.tf` | 0 | 0 | 0 | 0 |
| `environments/prod/main.tf` | 0 | 0 | 0 | 0 |
| `environments/prod/variables.tf` | 0 | 0 | 0 | 0 |
| `environments/prod/outputs.tf` | 0 | 0 | 0 | 0 |
| `environments/dev/main.tf` | 0 | 1 | 2 | 1 |
| `environments/dev/variables.tf` | 0 | 0 | 1 | 0 |
| `environments/dev/outputs.tf` | 0 | 0 | 0 | 0 |
| **Totalt** | **0** | **1** | **4** | **2** |

---

## Critical (BLOCK)

Inga.

---

## Major (måste fixas innan apply, men inget av dem är design-fel —
endast operativa edge-cases)

### M-1: Apply-flödet hänger om registrar-NS inte propagerat innan första apply

**Fil:** `environments/dev/main.tf:300-326` (data-source + acm_dev + alias-record)
**Severity:** Major (operationell trasig-sekvens-risk, inte kod-defekt)

**Beskrivning:**
Modulkoden är korrekt, men apply-sekvensen som dokumenteras i header-kommentaren
(rad 290-298) är teoretiskt rätt och praktiskt skör:

1. `terraform apply` (denna stack) skapar `aws_acm_certificate.this` +
   `aws_route53_record.validation` + `aws_acm_certificate_validation.this` i
   en och samma plan
2. `aws_acm_certificate_validation.this` blockerar tills ACM observerar
   validation-CNAME — vilket kräver att registrar redan delegerat NS till AWS
3. Om Klas kör `terraform apply` *innan* registrar-NS propagerat globalt
   (typiskt 30 min – 48 h hos svenska registrarer som Loopia/Inleed):
   `aws_acm_certificate_validation` timeout efter ~45 min default → apply
   abortar i delvis-applierat state (cert + records skapade men inte validerade)

Header-kommentaren rad 291-292 nämner kravet men inte hur Klas verifierar det.

**Krävs (välj en):**

- **Alternativ A (rekommenderad):** lägg till `timeouts { create = "60m" }`
  på `aws_acm_certificate_validation.this` i `modules/acm/main.tf` med
  kommentar att default 45 min är knappt för svenska registrarer +
  uppdatera header-kommentaren med pre-apply-checklista:
  ```
  PRE-APPLY-CHECKLISTA:
    1. dig NS jobbpilot.se +short — ska returnera 4 AWS-NS från
       prod/baseline-output route53_name_servers
    2. Om dig returnerar registrar's egna NS → vänta 1-24 h, retry
    3. terraform apply
  ```

- **Alternativ B:** dela upp 13c-applien i två stacks (acm separat från
  alias-record) med explicit Klas-GO mellan. Mer overhead, men ger fail-fast
  om validation-CNAME inte landar.

**Delegera till:** terraform-implementation-agent eller Klas direkt
(trivial fix). Header-kommentaren är ändå designad för operatör-läsning, så
kompletteringen passar in i existerande pattern.

---

## Minor (bör fixas — ingen blockerar apply)

### m-1: ACM-modulens `for_each`-key kraschar vid duplikatfri validation_options

**Fil:** `modules/acm/main.tf:36-43`

**Beskrivning:**
Pattern är AWS-officiellt rekommenderat och fungerar för CN + unika SANs.
Kommentaren rad 31-33 säger att `allow_overwrite` skyddar mot kollision —
det är inte helt sant. `for_each` använder `dvo.domain_name` som key, så
om CN och en SAN är *samma domain* får du faktiskt en plan-tids-fel
("two objects have same key"), inte en runtime-overwrite. För nuvarande
användning (`domain_name = "dev.jobbpilot.se"`, `subject_alternative_names = []`)
är det icke-issue. Men om någon senare lägger till `["dev.jobbpilot.se"]`
i SANs-listan av misstag → cryptic plan-error.

**Föreslås:** uppdatera kommentaren rad 31-33 till att förklara vad
`allow_overwrite` faktiskt skyddar mot (samma `resource_record_name` från
två olika `domain_name`-keys, t.ex. när apex + www validerar via samma
CNAME-name) och att duplikat i SANs-listan måste förhindras av anroparen.

**Delegera till:** dokumentations-fix, kan göras direkt.

### m-2: `aws_route53_record.dev_alb_alias` saknar lifecycle-precondition

**Fil:** `environments/dev/main.tf:316-326`

**Beskrivning:**
Resursen skapar A-ALIAS *direkt* mot `module.alb.alb_dns_name`. Om någon
flyttar ALB:n eller refaktorerar `module.alb` så `alb_dns_name` blir `null`
under en intermediate plan → record skapas trasig. Inte ett akut problem
(STEG 13b är stabilt), men inkonsekvent med `modules/alb/main.tf:133-138`
som har precondition på `acm_certificate_arn != null` för exakt samma typ
av fail-fast-skydd.

**Föreslås:** lägg till
```hcl
lifecycle {
  precondition {
    condition     = module.alb.alb_dns_name != null && module.alb.alb_zone_id != null
    error_message = "ALB måste vara skapad innan dev_alb_alias kan peka på den."
  }
}
```

**Eller acceptera:** dokumentera explicit i ADR 0027 (supersession av 0026)
att alias-record:en inte har precondition pga implicit dependency via
module-reference. Båda är giltiga val.

### m-3: TTL=60 i validation-CNAMEs är ovanligt lågt för auto-renewal-records

**Fil:** `modules/acm/main.tf:48`

**Beskrivning:**
TTL=60 sek används traditionellt vid migrering eller test. För
ACM-validation-records som ska ligga kvar under hela cert-livstiden (renewal
sker år efter år) är TTL=300 (5 min) eller TTL=3600 (1 h) standard. TTL=60
ökar Route53-query-kostnaden marginellt (~$0.40 per miljon queries) och
sätter mer last på AWS:s egna validation-poller.

**Föreslås:** byt till `ttl = 300`. Inte affärskritiskt, men följer AWS-egna
referens-exempel i [acm_certificate-docs](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/acm_certificate).

**Delegera till:** trivial fix, kan göras direkt.

### m-4: `output "acm_dev_certificate_arn"` borde dokumentera referens till
`aws_acm_certificate_validation` (inte direkt cert-resource)

**Fil:** `environments/dev/outputs.tf:147-150`

**Beskrivning:**
Värdet är korrekt eftersom `module.acm_dev.certificate_arn` redan är
`aws_acm_certificate_validation.this.certificate_arn` (se
`modules/acm/outputs.tf:1-4`). Men i description-texten står "Validerad
ACM-cert-ARN" utan att förklara *varför* det är säkert att flippa
ALB-listenern direkt mot detta värde. För framtida läsare som copy-pastar
till annan kontext är det icke-uppenbart att "validerad" betyder "blocked
on validation_complete".

**Föreslås:** utöka description: `"Validerad ACM-cert-ARN (refererar
aws_acm_certificate_validation, dvs blockerad tills DNS-validering klar —
säkert att skicka direkt till ALB-listener). Kopiera till terraform.tfvars
som alb_acm_certificate_arn samtidigt som alb_https_enabled flippas till true."`

**Delegera till:** dokumentations-fix, kan göras direkt.

---

## Nit (kosmetiska, ingen åtgärd krävs)

### n-1: Stavning i `modules/acm/main.tf:30`

`# `for_each` över domain_validation_options...` — backticks saknar
markdown-rendering i `.tf`-kommentar (Terraform parser bryr sig inte). Inte
ett fel, men inkonsekvent — andra kommentarer i samma fil använder rak text
utan backtick-formatering.

### n-2: Kommentar rad 290 i `environments/dev/main.tf` säger "ALIAS-record dev.jobbpilot.se → ALB" men resursen heter `dev_alb_alias` (engelska)

Inkonsekvens svenska-i-kommentar / engelska-i-kod är CLAUDE.md §10.1
faktiskt korrekt — kod på engelska, kommentar på svenska. Inte ett fel,
bara värt att notera att stilen är konsekvent med projektet.

---

## Bedömningskriterie-genomgång

### 1. Följer Terraform-modul-konventioner i repo (3-fil-struktur, namngivning, descriptions)

**OK.** Båda nya modulerna följer `main.tf` / `variables.tf` / `outputs.tf`
3-fil-konvention som `modules/network/`, `modules/rds/`, `modules/redis/`,
`modules/alb/`. Alla `variable`-block har `description`. Alla `output`-block
har `description`. Resurs-namngivning (`aws_route53_zone.this`,
`aws_acm_certificate.this`) följer single-resource-modul-pattern (`.this`)
likt `modules/alb/main.tf` (`aws_lb.this`).

### 2. Resource-naming + tagging-konsekvens med STEG 13a/13b

**OK.** `tags = merge(var.tags, { Name = ... })` matchar
`modules/alb/main.tf:107`, `modules/rds/main.tf`, etc. `var.tags`-default `{}`
används, common_tags injiceras från environment-stack via `tags = var.common_tags`
i `environments/prod/main.tf:82` + `environments/dev/main.tf:311`.

### 3. lifecycle-blocks, dependencies, validation-precondition-pattern

**Mestadels OK med en lucka.**
- `aws_acm_certificate.this` har `create_before_destroy = true` med korrekt
  motivering (rad 17-21). Bra.
- `aws_acm_certificate_validation.this` blockar nedströms-konsumenter
  korrekt via `output certificate_arn` referens. Bra.
- Saknad precondition: se m-2 (alias-record).
- Saknad timeout: se M-1 (ACM-validation default-timeout).

### 4. Outputs sensitive-flaggade där det behövs

**OK.** Cert-ARN, zone-ID, name-servers, FQDN är inte hemliga (publika DNS-data
+ AWS-resource-IDs som ändå syns i Console). Konsekvent med
`environments/dev/outputs.tf:50-54` där bara faktiska secrets
(`master_user_secret_arn`, `auth_token_secret_arn`) är
`sensitive = true`.

### 5. Cirkulära dependencies

**OK.** Dependency-graph: `modules/route53` (prod-stack) → registrar
(out-of-band) → `data.aws_route53_zone.apex` (dev-stack) → `modules/acm` (dev) →
`module.alb` consumer av cert-ARN via `var.alb_acm_certificate_arn` (manuell
2-stegs-flip per `terraform.tfvars`). Ingen cykel.

ALB→ACM-cert-flippet är korrekt designat som **manuell** uppdatering av
`terraform.tfvars` (variables.tf:140-144 default `null`) snarare än
automatisk module-reference. Det är medveten design för säkerhets-isolation:
operatören måste explicit konfirmera "ja, certet är validerat och redo att
gå live". Bra mönster.

### 6. State-stack-separation: zone i prod/baseline, dev konsumerar via data-lookup

**OK och korrekt motiverat.** Hosted zone är global delad resurs (apex är
samma över dev/staging/prod-miljöer) och ska bo i stack med längst lifetime.
prod/baseline.tfstate har redan KMS-master-key + CloudTrail + Bedrock-IAM —
zonen passar in i samma kategori. Dev-stack använder `data "aws_route53_zone"`
namn-baserad lookup (rad 300-303) likt
`environments/dev/main.tf:8-10`'s `data "aws_kms_alias"`-pattern. Konsekvent.

---

## Bra gjort (reinforce-feedback)

- **`aws_acm_certificate_validation` blocking-resource korrekt använd** —
  output exponerar dess ARN, inte direkt `aws_acm_certificate.this.arn`.
  Det är AWS-best-practice som många får fel.
- **`create_before_destroy = true` på ACM-cert** med uttrycklig motivering
  i kommentar (förhindrar listener-downtime vid CN-byte).
- **`for_each` over `domain_validation_options`** är AWS-officiellt
  rekommenderat pattern och hanterar CN + SANs korrekt.
- **State-separation prod/baseline ↔ dev/main** är arkitekturellt korrekt
  och dokumenterad i header-kommentar.
- **Apply-flöde dokumenterat** i header-kommentar rad 290-298 — sekvensen
  (apply 1 → flip vars → apply 2 → smoke → ADR 0027) är glasklar.
- **Manuell ALB-flip via `alb_acm_certificate_arn` + `alb_https_enabled`**
  istället för automatisk module-reference — medveten safety-gate, inte
  kod-lathet.
- **Konsistent svenska-kommentar / engelska-kod** per CLAUDE.md §10.1.
- **Inga AWS-region-hårdkodningar** — modulen är region-agnostisk genom
  provider-arv (`environments/prod/main.tf:1-7`).
- **`alias { evaluate_target_health = true }`** på A-ALIAS — automatisk
  health-aware routing utan manuell konfig.

---

## Veto-status

**APPROVE-with-fixes**

**Innan `terraform apply` körs:**

1. **M-1 (Major):** lägg till `timeouts { create = "60m" }` på
   `aws_acm_certificate_validation.this` *eller* utöka header-kommentaren
   i `environments/dev/main.tf:283-298` med pre-apply-checklista som
   verifierar att `dig NS jobbpilot.se +short` returnerar AWS-NS innan
   andra-apply körs. Båda ihop är optimalt.
2. **m-1 till m-4:** kan applieras nu eller skjutas till uppföljande commit.
   Inget av dem blockerar.

**Apply-sekvens som code-reviewer godkänt:**

```
Steg A: Klas registrerar jobbpilot.se hos svensk registrar (out-of-band)
Steg B: cd environments/prod
        terraform apply  # skapar route53-zonen
Steg C: terraform output route53_name_servers
Steg D: Kopiera 4 NS till registrar's NS-records
Steg E: dig NS jobbpilot.se +short  # verifiera AWS-NS, vänta tills propagerat
Steg F: cd ../dev
        terraform apply  # skapar ACM-cert + validation + alias-record
Steg G: terraform output acm_dev_certificate_arn
Steg H: Edit terraform.tfvars: alb_https_enabled=true,
        alb_acm_certificate_arn="<output>"
Steg I: terraform apply  # flippar ALB HTTP→HTTPS
Steg J: curl -v https://dev.jobbpilot.se/api/ready
Steg K: Skapa ADR 0027 supersession av ADR 0026
```

**Re-review:** ej krävd för m-fixes. Vid M-1-fix räcker grep-bekräftelse av
`timeouts`-block + uppdaterad header-kommentar i STOPP-rapport.

---

## Delegationer

| Finding | Action | Owner |
|---|---|---|
| M-1 | Lägg till timeout + pre-apply-checklista | terraform-impl eller Klas |
| m-1 | Förtydliga `allow_overwrite`-kommentar | trivial fix |
| m-2 | Lägg till precondition på alias-record (eller dokumentera medvetet val i ADR 0027) | terraform-impl |
| m-3 | TTL 60 → 300 | trivial fix |
| m-4 | Utöka output-description | trivial fix |
| n-1, n-2 | Ignorera | — |

---

**Slut på review.**
