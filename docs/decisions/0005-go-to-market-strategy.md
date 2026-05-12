# ADR 0005 — Go-to-market-strategi och kostnadskontroll

**Datum:** 2026-04-18
**Status:** ACCEPTED 2026-05-12 (med amendment 2026-05-12: invitations + waitlist)

## Kontext

Klas kör JobbPilot på AWS med startup-credits ($100 mottagen, ytterligare $100
möjlig). Aktuell infrastruktur (ECS Fargate + RDS + ElastiCache + Bedrock) har
uppskattade månadskostnader:

| Fas | Scenario | Uppskattad kostnad/mån |
|-----|----------|------------------------|
| Fas 0 | Utveckling | $5–10 |
| Fas 1 | MVP + klasskamrater testar | $30–50 |
| Fas 2 | Beta med 50+ användare | $50–150 |
| — | Virala scenarier (1 000+ users) | $200–500 |

Klas är student på studiemedel med ung son, driver ingen näringsverksamhet, och
vill inte råka ut för överraskningsfakturor. AWS-credits räcker uppskattningsvis
2–3 månader av aktiv fas 1–2-användning.

Ursprungligen diskuterades plattformsbyte (Supabase/Railway) för att sänka
kostnader till $5–15/mån. **Beslut: behåll AWS-infrastruktur** eftersom
(a) den är redan uppe och tillämpad via Terraform,
(b) credits räcker för planerad testperiod,
(c) AWS-kompetens har CV-värde för LIA-sökning.

## Öppet beslut: go-to-market-strategi

Tre alternativ att väga innan Fas 2 public-exponering:

### Alternativ A — Stängd klassapp

Endast `@nbi.se`-emails + invite-koder. JobbPilot förblir internt verktyg
för klasskamrater och framtida NBI-studenter.

| Aspekt | Utfall |
|--------|--------|
| Kostnad | Stadigt $20–30/mån |
| Intäktsmodell | Ingen |
| CV-värde | Medel ("internt verktyg") |
| Kräver | Email-domän-kontroll i registreringsflöde |
| Skala-risk | Ingen |

### Alternativ B — Public freemium

Gratis-tier (5 jobbansökningar/mån) + Premium 49–99 SEK/mån.

| Aspekt | Utfall |
|--------|--------|
| Kostnad | $50–200+/mån beroende på trafik |
| Intäktsmodell | Stripe-integration, moms-hantering |
| CV-värde | Högt ("public SaaS med betalande kunder") |
| Kräver | Stripe, VAT/moms-flöde, ToS, GDPR DPA, ev. enskild firma |
| Skala-risk | Medium — break-even ~20–50 premium-users |

### Alternativ C — Invite-only public beta med hård kapp

Waitlist-baserad. Nya registreringar stängs av vid kostnads- eller
antals-tak. Befintliga users fortsätter.

| Aspekt | Utfall |
|--------|--------|
| Kostnad | Kontrollerbar via hård cap |
| Intäktsmodell | Ingen i v1, möjligt att lägga till senare |
| CV-värde | Medel–högt ("curated beta") |
| Kräver | Feature-flag-system + waitlist-sida |
| Skala-risk | Låg |

## Beslut (2026-05-12)

**Alternativ C — Invite-only public beta med hård cap** är vald väg framåt.
A avvisad (skadar reverse-pivot-möjligheter + domain-restriction-kod som ej
återanvänds). B avvisad bestämt (YAGNI mot oprövad intäkt, bryter
civic-utility-identiteten, regulatorisk friktion inkompatibel med
studentstatus).

CV-värde är **inte** beslutsaxel — Klas planerar private repo efter MVP.
Public under dev är endast för att Claude Code ska kunna jobba mot repot.

**Nyckel-mekanik:**

- **JobAd-listning/sökning är auth-gated** i Fas 2-start. Anonym publik
  yta kan låsas upp senare via separat ADR efter mätning av JobTech-
  proxy-kostnad och bot-trafik.
- **`registrations_open`-flagga** bor i `appsettings.json` + AWS Secrets
  Manager override per BUILD.md §13.2. Application-lager-port
  (`IFeatureFlags`) + Infrastructure-impl mot `IOptionsMonitor<T>`.
  Default `false` — emergency kill-switch som blockerar både
  invitation-redemption och waitlist-signup (semantik utvidgad per
  amendment nedan).
- **Rate-limit-policies** ovanpå befintliga tre (AccountDeletion,
  AuthWrite, AuthLoose) — konkret uppsättning definierad i amendment.
- **Budget Actions $50/mån** auto-disablar `JobbPilotBedrockInvoke`-
  IAM-policy + stoppar ECS-services. Bedrock-disable behövs nu trots
  att Fas 4 är långt borta — credentialed bot-trafik mot framtida AI-yta
  är enda realistiska blowout-vektorn.

**Reversibilitet bevarad i båda riktningar:**

- C → stängd klassapp: `registrations_open=false` permanent + waitlist-
  copy uppdateras (<1 dag)
- C → public freemium: öppna flaggan, ta bort cap, lägg till Stripe i
  separat ADR (~2-3 veckors arbete)
- C → anonym publik JobAd-katalog: ta bort `.RequireAuthorization()`,
  lägg till IP-baserad rate-limit, mät bot-trafik (~1 vecka)

**Fas 4-koppling (TD-26 AI-kostnadstak):**

Kostnadsskyddsmönstret från Fas 2 ärvs direkt:
- Ny `AiInvocationPolicy` (per UserId) läggs till på samma sätt som
  JobAd-policy idag
- `IAiOperation`-pipeline-behavior för token-tracking + per-user
  spend-cap (TD-26-design)
- Budget Actions-tröskeln höjs vid Fas 4 (per revision-historik
  2026-05-09)
- Bedrock-IAM-auto-disable-mekaniken är redan på plats

Detta är arkitekturell symmetri, inte överlapp.

**Decision-maker:** senior-cto-advisor 2026-05-12.
**Godkänd av:** Klas Olsson 2026-05-12.
**Granskningstrail:** `docs/reviews/2026-05-12-fas2-cto-adr0005.md`.

---

## Amendment 2026-05-12 — Invitations + waitlist

Klas-input efter huvudbeslutet: registreringsflödet ska gå via två
parallella vägar:

1. **Magic-link-invitation** — Klas skickar direktlänk till valda personer
2. **Waitlist + manuell approval** — anonyma besökare skriver upp sig på
   `/vantelista`, Klas väljer från listan, godkända får email med
   fortsatt-registrering-länk (samma magic-link-mekanik)

CTO-beslut: accepterat scope-tillägg i Fas 2-prereqs med snitt mot Fas 6.

### Scope-snitt

| Del | Fas |
|---|---|
| Backend: Invitation-domän + Waitlist-domän + endpoints + email | **Fas 2 (nu)** |
| Frontend: `/vantelista`-publik signup-sida | **Fas 2 (nu)** |
| Admin-UI för waitlist + invitations | **Fas 6 (admin-panel-fasen)** |

Klas använder Postman/curl/Bruno mot admin-endpoints under Fas 2–5.
YAGNI för 20 användare på 20 veckor (~1 invite/vecka). Admin-UI byggs
ovanpå samma endpoints i Fas 6 — inget bortkastat arbete.

### Domänmodell — två separata aggregates

**`Invitation`** (aggregate root): `Email`, `Origin` (DirectInvite |
WaitlistApproved), `TokenHash` (HMAC-SHA256, plaintext bara i email),
`ExpiresAt` (7 dagar default), `Status` (Pending | Redeemed | Expired |
Revoked), audit-fält. Events: Issued, Redeemed, Expired, Revoked.

**`WaitlistEntry`** (separat aggregate): `Email`, `RequestedAt`, `Status`
(Pending | Approved | Rejected), `ApprovedAt`, `ApprovedByAdminId`,
`ResultingInvitationId` (sätts vid Approved). Events: Requested, Approved,
Rejected.

Båda vägarna konvergerar på samma `POST /api/v1/auth/redeem-invitation`-
endpoint. `WaitlistEntry.Approve()` skapar `Invitation` idempotent.

Motivering: Evans 2003 kap. 6 + Vernon 2013 kap. 10 — olika livscykler,
olika invarianter, olika consistency boundaries kräver separata aggregates.

### Token-mekanism — dedikerad opaque token

32 bytes URL-safe base64 plaintext (skickas bara i email) +
HMAC-SHA256-hash lagrad i DB. Single-use via optimistic concurrency
(RowVersion). 7 dagars expiry, konfigurerbar.

**Avvisat:** JWT (deprecated per ADR 0017/0018) + återanvänd session-token
(annan livscykel, SRP-brott). Referens: OWASP ASVS V3.7.

### Email-infrastruktur

`IEmailSender` i Application + `SesEmailSender` i Infrastructure.
AWS SES (eu-north-1) redan dokumenterad subprocessor (BUILD.md §13.4) —
ingen ny ADR. Svenska email-templates per design-skills.

### Rate-limit-policies — tre nya

| Endpoint | Policy | Limit | Nyckel |
|---|---|---|---|
| `POST /api/v1/auth/redeem-invitation` | `InvitationRedeemPolicy` | 5/h | per IP + per token |
| `POST /api/v1/waitlist` | `WaitlistSignupPolicy` | 3/24h | per IP |
| `POST /api/v1/admin/invitations` | `AuthWritePolicy` (befintlig) | 20/min | per IP |

### Admin-endpoints (Postman/curl-baserade under Fas 2–5)

- `POST /api/v1/admin/invitations` — utfärdar Invitation, skickar email
- `GET /api/v1/admin/invitations?status=Pending`
- `POST /api/v1/admin/invitations/{id}/revoke`
- `GET /api/v1/admin/waitlist?status=Pending`
- `POST /api/v1/admin/waitlist/{id}/approve` — skapar Invitation, skickar email
- `POST /api/v1/admin/waitlist/{id}/reject`

Skyddade av `[Authorize(Roles = "SuperAdmin")]` (BUILD.md §11.4).

### Impl-plan — F2-P0 läggs in före F2-P1

| Batch | Innehåll |
|---|---|
| F2-P0a | Invitation + WaitlistEntry aggregates + tests (>80% coverage) |
| F2-P0b | EF mappings + migration (`invitations`, `waitlist_entries`) |
| F2-P0c | 5 commands (Issue/Redeem/RequestWaitlist/Approve/Reject) + validators + tests |
| F2-P0d | `IEmailSender` + `SesEmailSender` + svenska email-templates |
| F2-P0e | API-endpoints (admin + public) + rate-limit-policies + kill-switch-gate |
| F2-P0f | `/vantelista`-publik sida (Next.js RSC + form action) |

Total F2-P0-tillkomst: ~15-20h CC-tid över 6 sub-batches. Inom Fas 2:s
2-veckors-budget. Befintliga F2-P1 till F2-P6 (feature-flag, budget actions,
runbook, readiness-probe) levereras efter P0.

## Obligatoriska kostnadsskydd innan Fas 2 oavsett val

Följande måste vara implementerade innan JobbPilot exponeras utanför Klas
direkta kontroll — även för Alternativ A (stängd klassapp):

### 1. AWS Budget Actions vid $50/mån

- Auto-disable IAM-policy `JobbPilotBedrockInvoke`
- Stoppa eventuella ECS-services
- Dokumenterat återställningsflöde i `docs/runbooks/aws-cost-recovery.md`

### 2. Feature flag: `registrations_open`

- `registrations_open: boolean` — togglebar utan kod-deploy
- Stänger nya registreringar omedelbart vid behov
- Implementeras som konfigurationsvärde i `appsettings.json` +
  AWS Secrets Manager override

### 3. Rate limiting per user på AI-anrop

- Max N Claude-anrop per user per dag (N definieras per tier i BUILD.md §7)
- Skyddar mot enskild-user-kostnadsdrift
- Skyddar mot skadliga eller buggiga AI-loopar

### 4. Runbook: `docs/runbooks/aws-cost-recovery.md`

- Vad göra vid budget-alert
- Hur återställa efter hård Budget Action-spärr
- Kontaktpunkter vid eskalering

## Konsekvenser

**Positiva:**
- Klas fattar go-to-market-beslut med fakta, inte med antaganden
- Inga överraskningsfakturor oavsett vilket alternativ som väljs
- Kan alltid re-pivota (t.ex. börja med A, öppna till C senare)

**Negativa:**
- Beslutsförskjutning kräver disciplin — lätt att glömma att återkomma
- Kostnadsskydden (feature flags, rate limiting, runbook) är extra v1-arbete
  som inte är domänlogik

**Risker som adresseras:**
- Viral oförutsedd tillväxt orsakar skenande AWS-faktura
- Klas fastnar med oväntade skulder som student
- Projektet stängs ner av ekonomiska skäl snarare än tekniska

## Revision-historik

- **2026-05-12 (Fas 2-kickoff):** ADR flippad PROPOSED → ACCEPTED. Alternativ C
  vald (invite-only public beta med hård cap). Amendment 2026-05-12 lägger till
  invitations + waitlist-flöde (två aggregates, opaque tokens, SES, admin-CLI
  under Fas 2–5). F2-P0 (sub-batches a–f) införs som Fas 2-prereq före F2-P1.
  Decision-maker: senior-cto-advisor. Klas-GO: 2026-05-12.
  Granskningstrail: `docs/reviews/2026-05-12-fas2-cto-adr0005.md`.
- **2026-05-09 (STEG 13a):** Tröskel sänkt $80 → $50 efter cost-policy-skärpning.
  Lean-dev-defaults (RDS t4g.micro Single-AZ, Redis t4g.micro × 1, Interface
  VPC Endpoints av) gjorde dev-baseline ~$53/mån — $80-tröskeln gav för tunn
  margin. $50 är enda lager-tröskeln (ingen $80-fallback kvar); månads-slut-
  trigger accepteras som disciplin-mekanism. Höjs vid Fas 4 (AI-features
  driver Bedrock-cost) eller MVP-närmande, i samma round som ADR 0005:s
  slutgiltiga go-to-market-beslut (Alt A/B/C). ADR-status oförändrat PROPOSED.
