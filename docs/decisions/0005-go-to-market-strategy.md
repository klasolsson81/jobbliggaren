# ADR 0005 — Go-to-market-strategi och kostnadskontroll

**Datum:** 2026-04-18
**Status:** PROPOSED (beslut skjuts till innan Fas 2 public launch)

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

## Beslut (att fattas)

Beslutet fattas baserat på:

1. Hur väl MVP fungerar efter 2–3 veckors klass-testning (Fas 7)
2. Klas appetit för Stripe/moms-komplexitet
3. Hur mycket tid Klas har kvar för JobbPilot efter LIA-sökning
4. Om credits eller annan finansiering finns tillgänglig för Fas 2+

**Målsättning:** Beslut fattas och dokumenteras här senast när Fas 1 MVP är
funktionell och minst 10 klasskamrater har testat i intern beta (BUILD.md §18
Fas 7-milstolpe).

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

- **2026-05-09 (STEG 13a):** Tröskel sänkt $80 → $50 efter cost-policy-skärpning.
  Lean-dev-defaults (RDS t4g.micro Single-AZ, Redis t4g.micro × 1, Interface
  VPC Endpoints av) gjorde dev-baseline ~$53/mån — $80-tröskeln gav för tunn
  margin. $50 är enda lager-tröskeln (ingen $80-fallback kvar); månads-slut-
  trigger accepteras som disciplin-mekanism. Höjs vid Fas 4 (AI-features
  driver Bedrock-cost) eller MVP-närmande, i samma round som ADR 0005:s
  slutgiltiga go-to-market-beslut (Alt A/B/C). ADR-status oförändrat PROPOSED.
