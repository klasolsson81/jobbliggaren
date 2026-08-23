# Jobbliggaren — Arkitekturspecifikation

> Arkitektur-inriktad del av byggspecifikationen för Jobbliggaren, en svensk
> jobbansökningshanterare byggd som civic utility. Täcker teknikstack,
> systemarkitektur, domänmodell, API, datamodell, deterministiska CV-/matchnings-
> motorer, säkerhet och infrastruktur. Systemdokument:
> [`AGENTS.md`](./AGENTS.md), [`CLAUDE.md`](./CLAUDE.md), [`DESIGN.md`](./DESIGN.md)

---

## 3. Teknikstack

### 3.1 Exakta versioner

| Komponent | Val | Version | Notis |
|-----------|-----|---------|-------|
| Backend runtime | .NET | 10 (LTS till 2028-11) | GA sedan 2025-11-11 |
| Språk (backend) | C# | 14 | Extension members, `field` keyword GA, null-conditional assignment |
| Backend framework | ASP.NET Core | 10 | Minimal API |
| ORM | EF Core | 10 | Npgsql-provider 10.x |
| Auth | ASP.NET Core Identity | 10 | Egen Identity-DB |
| Mediator | Mediator (martinothamar) | 3.x | Source-generated CQRS, MIT, Native AOT-kompatibelt (ersätter MediatR) |
| Validering | FluentValidation | 12.x | Via Mediator-pipeline |
| Mapping | — (manuell) | — | Ingen mapping-bibliotek; explicit DTO-mappning per CLAUDE.md §5 (AutoMapper/Mapster avvisade över domängränsen) |
| Background jobs | Hangfire | 1.8.x | Postgres-storage |
| Smart enum | Ardalis.SmartEnum | 8.x | State machines i domänen |
| Logging | Microsoft.Extensions.Logging | 10.x | `Microsoft.Extensions.Logging.Console` → stdout + persistent strukturerad sink via Seq (dev levererad under TD-104/STEG 6; prod-sinken **levererad i repot** — [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175), ADR 0128; installationsläget på en given låda mäts i `docs/runbooks/log-sink.md` §4) |
| Log sink | Seq.Extensions.Logging | 9.0.0 | MEL-provider → Seq (datalust); config-gated på `Seq:ServerUrl`; net9-asset .NET 10-kompatibel (MEL `>= 9` unifieras uppåt); dev lokal Seq. **Prod-topologin är namngiven sedan ADR 0128 och var tidigare bara "self-hosted EU":** `datalust/seq:2026.1` som compose-tjänst på produktionslådan (EU, Netcup), **utan publicerad port** — appen postar mot ingest-lyssnaren `5341`, aldrig UI/query-porten `80`. **Operatörsåtkomst är INTE en SSH-tunnel** — lådan kör `AllowTcpForwarding no` (mätt 2026-08-11), så drift sker headless via lådans egen `curl` mot container-IP:n; `docs/runbooks/log-sink.md` §3 äger proceduren och §4 dess verifieringsrader |
| Observability | OpenTelemetry | 1.15+ | Traces + metrics. **Beroende-kandidat, obyggd** (ingen dom fälld — till skillnad från Catalyst-raden) — ingen `PackageReference` i något `.csproj`, ingen användning i `src/`; exporter/backend definieras med observability-sinken (§14.2, [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175)). `Directory.Packages.props` innehåller `OpenTelemetry.Api` + `.Exporter.OpenTelemetryProtocol` som **transitiva CVE-pins för WireMock.Net** (posternas egen kommentar), inte som en observability-implementation |
| PDF parsing | PdfPig | 0.1.14+ | Text extraction |
| DOCX parsing | DocumentFormat.OpenXml | 3.x | Microsoft-underhåll |
| PDF generation | QuestPDF | 2026.7.2 | Community v3.0 (source-available, non-copyleft). Eligible on the **revenue** ground — categories (1)/(6), USD 1M threshold — never on category (5) open-source, which requires an OSI-approved licence and ours is PolyForm Noncommercial (ADR 0072). Public-sector entities are ineligible regardless of revenue. *(Non-copyleft is assessed against the server-side, non-distributed model ADR 0050 locks — the repo idiom this row used as "ADR 0050-safe", now written out. **ADR 0071** carries the dependency-licence table and its 2026-06-14 License correction, and designates this section as the authoritative source; licence facts land here.)*; `QuestPDF.Settings.License = LicenseType.Community` i startup |
| DOCX generation | DocumentFormat.OpenXml | 3.x | Template-baserad |
| NLP (svenska) | Catalyst (+ Catalyst.Models.Swedish) | 26.x (CalVer) | MIT; lokal svensk NLP — tokenisering, lemmatisering, POS, NER (deterministisk CV-/matchnings-motor, ADR 0071 Beslut 6); svensk modell = separat MIT-datapaket. **Beroende-kandidat, utdömd för v1** — `Jobbliggaren.Infrastructure.csproj` säger "Catalyst medvetet UTE (OQ1)"; `Directory.Packages.props` bär CTO-domen (Snowball/libstemmer-only; YAGNI + Worker-minnesbudget ADR 0045) **och återinträdes-triggern: läggs till reaktivt först om ett mätt F4-9/10-kriterium bevisar POS/lemma-behov** |
| Stemmer (svenska) | libstemmer.net | 2.2.x | MIT-wrapper; Snowball-kärna BSD-3-Clause; svensk Snowball-stemmer |
| Stavning | WeCantSpell.Hunspell | 7.x | Hunspell-port — tri-licens **MPL 1.1 / GPL 2.0 / LGPL 2.1**; licensval MPL 1.1 (LGPL 2.1 fallback), aldrig GPL; server-side + oförändrad binär → ingen copyleft på produkten (se §3.1-notis) |
| Svensk ordlista | sv_SE Hunspell-ordlista (DSSO) | datafil | **LGPL-3.0** — oförändrad separat datafil, ej statiskt länkad/inbäddad/modifierad → copyleft smittar ej produkten (se §3.1-notis) |
| HTTP | HttpClientFactory + Refit | 10.x | JobTech-klient |
| Transaktionell e-post | **Inget paket** — handrullad `HttpClient` + `System.Text.Json` (Scaleway Transactional Email, `fr-par`) | — | Scaleway publicerar officiella SDK:er för Python, Go och JavaScript och **ingen för .NET** (mätt 2026-08-15), och behövs ingen: sändningen är en POST med JSON-kropp. `AWSSDK.SimpleEmailV2` togs bort i #183 utan ersättare, och `NoAmazonReferenceTests` är därefter ett **totalförbud** mot Amazon-paket och Amazon-importer, inte en allow-list. Infrastructure-confined (wire-payloaden är nästlad i avsändaren och korsar aldrig IEmailSender-porten, paritet Refit/QuestPDF); **all** utgående e-post: notiser (ADR 0080 Vag 4 PR-4) + kontolivscykel (§13.4); **HTTPS-API, aldrig SMTP** (Netcup blockerar 25/465/587, ADR 0050 §10); transport-retry stängs av att armen **inte registrerar någon resilience-handler** — se `ScalewayClientRegistration`, som också säger varför ingen får läggas till (sändnings-endpointen bär ingen idempotensparameter); **prod-utskick aktiverat 2026-08-16 utan att §2.5-grinden passerades** (raden sa "grindat" till dess) — se §13.4 + release-checklist.md §2.5, som bär grindens oförändrade KVAR-status. [#183](https://github.com/klasolsson81/jobbliggaren/issues/183) |
| Database | PostgreSQL | 18.3 | lokal Docker Compose nu; co-tenant container på Hetzner CAX31 (ADR 0050, ingen separat managed-DB) |
| Cache | Redis | 8.6 | lokal Docker Compose nu; co-tenant container på Hetzner CAX31 (ADR 0050) |
| Test-assertions | Shouldly | 4.3.x | MIT, ersätter commercial FluentAssertions |
| Test-mocks | NSubstitute | 6.x | Mock-ramverk för Application-tester; 6.0.0 annoterar hela publika API:t nullable — migrationsformen bor i `Directory.Packages.props` |
| Arch-tests | NetArchTest.Rules | 1.x | V1-val; abandoned sedan 2022 — överväg ArchUnitNET vid v2 |
| Load-test | NBomber | 6.x | MIT; .NET-native, xUnit/MTP-koherent; k6 avvisat (ADR 0045 Beslut 4) |
| Load-test HTTP | NBomber.Http | 6.x | HTTP-scenario-helpers för API-latens-mätning |
| Frontend framework | Next.js | 16.2 (App Router) | SSR + ISR |
| Frontend bundler | Turbopack | bundlad med Next 16.2 | Next 16-default; `--webpack`-opt-outen (commit `63ea6683`) borttagen i #1046 — den kringgick Vercels edge-routing, och FE byggs inte längre på Vercel (ADR 0050 Beslut 3, amenderad 2026-06-14; §15.3 "ingen Vercel-build") |
| Språk (frontend) | TypeScript | 6.0 | Strict mode |
| UI-komponenter | shadcn/ui | senaste (CLI v4) | Tung customisering, se DESIGN.md |
| Styling | Tailwind CSS | 4.x | **CSS-first-config:** `@import "tailwindcss"` + `@theme {}` i `globals.css`. **Det finns ingen `tailwind.config.ts`** — ADR 0015 Beslut 1 avvisade hybrid-läget (Alt C), och dess §Kontext, öppen fråga 1, pekar ut just den här raden som källan till missförståndet. Truth-sync #1154 |
| Data fetching | Server Actions + RSC | – | **TanStack Query finns inte i `package.json` och har aldrig installerats** (ingen `QueryClientProvider`-infra); ADR 0042 Beslut C (impl-notat 2026-05-17) avvisade den för typeahead-ytan. Mönstren — inklusive BFF-undantaget för binär uppladdning och poll-vägen — bor i §10.2. Truth-sync #1154 |
| Tabeller | Handrullad semantisk `<table>` | – | **TanStack Table finns inte i `package.json` och har aldrig installerats**; det finns ingen `src/components/ui/table.tsx`. Ingen formell avvisning — mönstret är oanvänt, inte omprövat. Truth-sync #1154 |
| Form | React Hook Form + Zod | RHF 7.x, Zod 4.x | Schema-baserad validering |
| Auth-klient | Egen cookie-baserad klient (ADR 0017) | – | NextAuth.js/Auth.js AVVISADES och finns inte i `package.json`; backend utfärdar ingen JWT — bäraren är ett opakt session-id (§11.2). Truth-sync #569/#827 |
| Datum/tal | `@/lib/i18n/format` + `@/lib/i18n/relative-time` | – | **date-fns finns inte i `package.json` och har aldrig installerats.** Aktiv locale resolvas per request ur `NEXT_LOCALE`-cookien, vilket kräver next-intls formaterare — rena funktioner som tar `useFormatter()`/`await getFormatter()` som parameter (CTO 2026-06-25, Variant B). Truth-sync #1154 |
| Ikoner | Lucide React | 1.x | Minimalistiskt, civic-vänligt |
| Typografi | Source Sans 3 | Google Fonts | Primär (next/font/google, byte från Hanken Grotesk — ADR 0091 / #549 WS4); systemfont-fallback. JetBrains Mono för kod |

> **AI SDK borttaget (ADR 0071):** `Anthropic`-NuGet och `AWSSDK.BedrockRuntime`
> ingår inte — produkten har ingen AI/LLM. PdfPig / DocumentFormat.OpenXml /
> QuestPDF (ovan) täcker PDF/DOCX/render-tiern.
>
> **Lokal NLP-tier (Fas 4, ADR 0071 Beslut 6) — INLÅST 2026-06-14.** Catalyst
> (+ Catalyst.Models.Swedish), libstemmer.net och WeCantSpell.Hunspell driver den
> deterministiska CV-/matchnings-motorns lokala NLP (tokenisering, svensk
> stemming/lemmatisering, POS-taggning, stavning — ~26 % av kunskapsbankens
> kriterier per ADR 0071). Ingen AI/LLM; all NLP körs lokalt på VPS:en.
> Stemming (libstemmer/Snowball) och lemmatisering (Catalyst) är komplementära;
> den slutliga stemming-vägen för title/keyword-overlap avgörs vid Fas 4-design
> (ADR 0071 Open question 1) — båda inlåsta som beroende-kandidater, ej bindande
> att båda används i v1.
>
> **AOT-/VPS-notis (Fas 4-design).** Catalyst laddar svenska modeller via
> `Register()` + `DiskStorage` + MessagePack binär-deserialisering (runtime
> typupplösning) → NLP-tiern är **inte verifierat Native-AOT/trimming-säker**; kör
> JIT i container (ADR 0050, default). Mediator-AOT-kompatibiliteten (ovan) gäller
> CQRS-pipelinen, inte NLP-tiern. Modellerna laddas dessutom residenta i
> Worker-processen → cold-start-latens + statiskt minnesfotavtryck på CAX31
> (16 GB co-tenant, ADR 0050); mät mot ADR 0045 Worker-minnesbudget vid Fas 4 och
> överväg lazy-init (ladda vid första CV-operation, ej vid boot).
>
> **Copyleft-separation (security-auditor-sign-off 2026-06-14).** Jobbliggaren
> distribueras **inte** som binär — produkten kör server-side på VPS:en (ADR 0050)
> och konsumenten interagerar enbart över HTTP. MPL 1.1-, LGPL 2.1- och
> LGPL-3.0-copyleft utlöses av *distribution* ("Distribute"/"convey"); ingen av
> licenserna är AGPL (ingen network-use-klausul). Eftersom ingen binär lämnar
> VPS:en utlöses ingen copyleft-förpliktelse på produktkoden. Som extra marginal
> konsumeras de två copyleft-artefakterna ändå som **oförändrade, separerbara**
> enheter:
> 1. **WeCantSpell.Hunspell** ärver Hunspells **tri-licens MPL 1.1 / GPL 2.0 /
>    LGPL 2.1** (ADR 0071 Beslut 6:s "MIT" var ett faktafel — korrigerat här efter
>    licensverifiering 2026-06-14). Vi väljer **MPL 1.1** (file-level weak
>    copyleft: förpliktelser fäster bara på de licensierade *källfilerna*, aldrig
>    på vår egen kod i våra egna filer); LGPL 2.1 som fallback. Vi väljer **aldrig
>    GPL 2.0**. Villkor: den publicerade NuGet-binären får ej modifieras och ingen
>    produktkod får läggas in i eller härledas från de licensierade filerna.
> 2. **sv_SE Hunspell-ordlista (DSSO)** är **LGPL-3.0**. Den konsumeras som en
>    **oförändrad, separat datafil** (ej kompilerad in, ej inbäddad som resurs, ej
>    modifierad) → LGPL-copyleft sträcker sig inte till applikationen.
>
> **Notice-förpliktelse vid distribution (Fas 4 build-time):** MIT (Catalyst,
> libstemmer.net-wrapper), BSD-3-Clause (Snowball-kärnan), MPL 1.1 och LGPL-texten
> kräver att respektive licens-/copyright-notis medföljer deploy-artefakten —
> samla i `THIRD-PARTY-NOTICES`. Permissiva licenser är inte notis-fria.

### 3.2 Infrastruktur

> **Statusbanner (2026-06-08):** AWS-dev-stacken är avvecklad (ADR 0066) och
> AWS lämnas permanent (Klas-direktiv 2026-06-06). All utveckling kör nu lokalt
> på laptop (Docker Compose: postgres + redis). Permanent deploy-mål — **Hetzner
> Cloud CAX31 (ARM, 16 GB) all-in-one Docker Compose **BE + FE** + Cloudflare
> DNS/CDN/proxy** — är beslutat i **ADR 0050 (Accepted 2026-06-08)**. Tabellen
> nedan beskriver **nuläge (lokalt)** + **beslutat permanent mål**.
> **VÄRDVALET ÄR AVGJORT 2026-08-04** (Klas-direktiv, ersätter 2026-08-02-läget): Hetzner ut
> **och "svensk VPS" återkallat på pris/prestanda** — värden är en **Netcup RS 1000 G12
> (x86, 4 kärnor, 8 GB, Nürnberg) utan CDN**. Lådan är köpt, provisionerad och grundhärdad
> ([#1196](https://github.com/klasolsson81/jobbliggaren/issues/1196)); **ingenting är
> deployat.** Beslutet bärs av **ADR 0050 `Amendment 2026-08-04`** (Beslut 2/3/4 delvis
> superseded, gate M-5 → M-5a/M-5b). Topologin står; **värd- och edge-leverantörsnamnen i
> tabellerna nedan är fortfarande inte omskrivna.** Skärlinjen är **substitution mot beslut**
> (senior-cto-advisor 2026-08-09): #1199 bytte 2026-08-09 varje mening vars ersättare **redan är
> ratificerad** — den publicerade `/integritet`-copyn, §13.4:s underbiträdeslista, §13.2/§13.3,
> §15.1:s värd/kant/backup, ROPA-posterna och §2.6:s grind — och lät varje mening som påstår en
> **kapacitet, sizing eller feldomän** stå, eftersom en omskrivning där kräver tal ADR 0122 äger.
> Tabellerna nedan är den senare klassen och ägs av
> [#1264](https://github.com/klasolsson81/jobbliggaren/issues/1264). **#1199 står kvar öppen på
> biträdesavtalet**, som fortsatt grindar första riktiga datan (Klas, aldrig CC). E-postraden är också
> upphävd, men av ett annat direktiv och med **vald** ersättare — sedan 2026-08-15
> **Scaleway Transactional Email i `fr-par`** (ADR 0131; AWS SES var ersättaren
> 2026-08-08 till 2026-08-15 och föll när AWS permanent vägrade häva sandbox-läget),
> ägd av #183 — se §13.4. Faktisk
> provisionering är fortsatt framtida Klas-gatat arbete (ADR 0050 Sekvensering:
> Hetzner sist, vid MVP före beta-testare). AWS-kolumnerna i ADR/sessions/
> research bevaras som historik.

| Tjänst | Nuläge (lokal dev) | Permanent mål (ADR 0050, Accepted) |
|--------|--------------------|---------------|
| Compute (backend/worker) | `dotnet run` lokalt | Hetzner CAX31 (ARM, 16 GB), Docker Compose: API + Worker co-tenant |
| Database | PostgreSQL 18.3 (Docker Compose) | PostgreSQL co-tenant container på CAX31 (ingen managed-DB) |
| Cache | Redis 8.6 (Docker Compose) | Redis co-tenant container på CAX31 |
| Object storage | lokal disk / ej aktiverat | TBD — roll/behov ej fastställt |
| AI inferens | Ingen — produkten har ingen AI/LLM (ADR 0071) | Ingen (deterministiska motorer på BE/VPS) |
| Email | `ConsoleEmailSender` (dev/test) / `NullEmailSender` (default annars) | Scaleway Transactional Email `fr-par` — **aktiverad 2026-08-16 utan att §2.5-grinden passerades**, armen skickar skarpt (§13.4, [#183](https://github.com/klasolsson81/jobbliggaren/issues/183)) |
| Secrets | `appsettings.Local.json` (gitignored) | Self-managed på VPS (systemd-credentials / sops+age, [#196](https://github.com/klasolsson81/jobbliggaren/issues/196)) |
| Encryption keys | `LocalDataKeyProvider` AES-256-GCM (ADR 0066) | Self-managed master-nyckelmodell + rotation ([#198](https://github.com/klasolsson81/jobbliggaren/issues/198)) |
| Frontend | `pnpm dev` (localhost:3000) | Next.js `next start` co-tenant container på CAX31 (bakom Caddy) |
| DNS / CDN / proxy | — | Cloudflare gratis-tier "Full (strict)" framför Caddy-origin på CAX31 |
| Backup | — | Nattlig klient-side-krypterad `pg_dump` → **mål inte valt, ägs av [#197](https://github.com/klasolsson81/jobbliggaren/issues/197)** (kraven i §13.4) |
| Logging / monitoring | console (MEL) + Seq (`Seq.Extensions.Logging`) | **Två mekanismer, inte en** (ADR 0128): Seq self-hosted på produktionslådan för sökbarhet (retention **avsedd** — en policy som sätts för hand inne i Seq, det finns ingen miljövariabel för den; **varaktigheten står i `docs/runbooks/log-sink.md` §3 steg 7, och om policyn är satt på en given låda mäts i samma fils §4** — ingetdera påstås här), plus `jobbliggaren-logship` — timrad, `age`-krypterad off-box-arkivering av journal, auditd och app-loggar till OVH `hostlogs/`. Åldersgränsen för `json-file`-lagret är fortfarande öppen — [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) |
| Errors | — | Sentry (EU) planerat |
| CI | GitHub Actions (build + test + coverage, inga moln-anrop) | oförändrat |
| IaC | `infra/terraform/` bevarad som reversibilitets-mekanik (ADR 0066 Beslut 1) | retireras via egen ADR vid Hetzner-cutover |

### 3.3 Miljöer

> **Status (2026-06-08):** dev/staging/production-AWS-miljöerna är avvecklade
> (ADR 0066). `local` är enda aktiva miljön. **Värdvalet är avgjort 2026-08-04**
> (Klas-direktiv: Netcup, 8 GB, ingen CDN — ADR 0050 `Amendment 2026-08-04`; ersätter
> det obeslutade 2026-08-02-läget) — se §3.2:s statusbanner; raderna nedan beskriver den
> beslutade FORMEN och namnger ännu fel leverantör. De är sizing-bärande och ägs därför av
> [#1264](https://github.com/klasolsson81/jobbliggaren/issues/1264), inte av #1199, som
> 2026-08-09 svepte substitutionerna och därefter bara bär biträdesavtalet.
> Permanent deploy-mål var **beslutat**
> (Hetzner CAX31 + Cloudflare, ADR 0050 Accepted 2026-06-08) men ännu
> ej provisionerat (ADR 0050 Sekvensering: Hetzner sist, vid MVP före
> beta-testare). Tag-baserad AWS-deploy (`v*-dev`/`v*-rc*`/`v*`) refererar
> **avvecklad infra** — den AWS-baserade stacken är **riven** (ADR 0066,
> 2026-05-26), inte pausad; deploy-workflowsen (`deploy-dev.yml` m.fl.) är
> bevarade som reversibilitets-/historik-mekanik (ADR 0066 Beslut 1 + ADR
> 0069 D3) och `deploy-dev.yml`:s auto-trigger är borttagen 2026-06-28. Ny
> Hetzner-pipeline byggs vid cutover.

| Miljö | Syfte | Deployment | Status |
|-------|-------|-----------|--------|
| local | Utveckling | Docker Compose | **Aktiv** |
| production (planerad) | Live | ~~Hetzner CAX31 + Cloudflare~~ → **Netcup RS 1000 G12 (8 GB), ingen CDN** (ADR 0050 `Amendment 2026-08-04`) | Värdvalet avgjort 2026-08-04 (se §3.2); lådan provisionerad + grundhärdad (#1196), inget deployat |
| dev / staging (AWS) | f.d. integration / pre-prod | — | Avvecklad (ADR 0066) |

PR-flöde mot `main` per ADR 0065 (CI-gate). Permanent deploy-strategi och
miljö-topologi är fastställd i ADR 0050; pipelinen byggs vid Hetzner-cutover.

---

## 4. Systemarkitektur

### 4.1 Lager (Clean Architecture)

```
┌─────────────────────────────────────────────────────┐
│  Presentation / Interfaces                          │
│  ├─ Jobbliggaren.Api          (REST endpoints)         │
│  ├─ Jobbliggaren.Worker       (Hangfire host)          │
│  └─ Jobbliggaren.Web          (Next.js, extern)        │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Infrastructure                           │
│  ├─ Persistence (EF Core, migrations)               │
│  ├─ Identity                                        │
│  ├─ JobSources.Platsbanken                          │
│  ├─ CvEngines (parsing, lokal NLP, render — Fas 4)  │
│  ├─ Email (Console/Null/Scaleway, ADR 0080/#183)    │
│  ├─ Security (Local/Kms DEK-provider, ADR 0066)     │
│  ├─ CalendarIntegration.Google                      │
│  ├─ GmailSync                                       │
│  ├─ Salary.Scb                                      │
│  └─ BackgroundJobs (Hangfire setup)                 │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Application                              │
│  ├─ Common (interfaces, behaviors, exceptions)      │
│  ├─ JobSeekers                                      │
│  ├─ Resumes                                         │
│  ├─ JobAds                                          │
│  ├─ Applications                                    │
│  ├─ CoverLetters                                    │
│  ├─ SavedSearches                                   │
│  ├─ Companies                                       │
│  ├─ Contacts                                        │
│  ├─ Matching                                        │
│  ├─ CvAssist (deterministic review/improve)         │
│  └─ Admin                                           │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Domain  (PURE, no external deps)         │
│  ├─ Common (AggregateRoot, Entity, ValueObject)     │
│  ├─ JobSeekers                                      │
│  ├─ Resumes                                         │
│  ├─ JobAds                                          │
│  ├─ Applications                                    │
│  ├─ CoverLetters                                    │
│  ├─ SavedSearches                                   │
│  ├─ Companies                                       │
│  ├─ Contacts                                        │
│  ├─ Matching                                        │
│  └─ Shared (Money, Location, EmailAddress, ...)     │
└─────────────────────────────────────────────────────┘
```

### 4.2 Dependency direction

Domain beror på ingenting (inte ens Mediator.SourceGenerator).
Application beror på Domain.
Infrastructure beror på Application (implementerar interfaces) och Domain (läser entities).
Api och Worker beror på Infrastructure och Application.

Verifieras via ArchUnit.NET eller NetArchTest-regler i Domain.ArchitectureTests-projektet.

### 4.3 Solution-struktur

```
/Jobbliggaren.sln
/src
  /Jobbliggaren.Domain
  /Jobbliggaren.Application
  /Jobbliggaren.Infrastructure
  /Jobbliggaren.Api
  /Jobbliggaren.Worker
  /Jobbliggaren.Migrate         (DDL-init console-app, ADR 0033)
/web
  /jobbliggaren-web             (Next.js)
/tests
  /Jobbliggaren.Domain.UnitTests
  /Jobbliggaren.Application.UnitTests
  /Jobbliggaren.Api.IntegrationTests    (Testcontainers + WebApplicationFactory)
  /Jobbliggaren.Architecture.Tests
  /jobbliggaren-web-tests       (Playwright e2e)
/infra
  /terraform
    /modules
    /environments
/docs
  /decisions                  (Architecture Decision Records — index: decisions/README.md)
  /api
  /runbooks
```

### 4.4 Cross-cutting concerns

**Alla genom Mediator.SourceGenerator-pipeline i Application-lagret:**

1. `LoggingBehavior` — loggar request-start, duration, success/fail
2. `ValidationBehavior` — kör FluentValidation, returnerar `Result<T>.Failure` vid fel
3. `AuthorizationBehavior` — kontrollerar att current user har rätt att köra handler
4. `CachingBehavior` — caches `ICacheable`-queries till Redis
5. `UnitOfWorkBehavior` — wrappar commands i DB-transaction, triggar domain event dispatch efter SaveChanges

---

## 5. Domänmodell

### 5.1 Aggregate roots (översikt)

| Aggregate | Typ | Äger | Refererar till |
|-----------|-----|------|---------------|
| `JobSeeker` | AR | `Preferences` (VO) | — |
| `Resume` | AR | `ResumeVersion` (entity) | `JobSeekerId` |
| `SavedSearch` | AR | `SearchCriteria` (VO) | `JobSeekerId` |
| `JobAd` | AR | — | `CompanyId` |
| `Company` | owned VO (se not) | — | — |
| `Contact` | AR | — | `CompanyId` (opt.) |
| `Application` | AR | `FollowUp` (entity), `ApplicationNote` (entity) | `JobSeekerId`, `JobAdId`, `ResumeVersionId`, `CoverLetterId`, `ContactId` |
| `CoverLetter` | AR | — | `JobSeekerId`, `JobAdId`, `ApplicationId` |

> **`Company` är inte ett persisterat aggregat i v1 (ADR 0087 D2, Accepterat
> 2026-06-30).** I domänen är `Company` en namn-only *owned VO* på `JobAd`
> (`src/Jobbliggaren.Domain/JobAds/Company.cs`), inte en Aggregate Root.
> Företagsidentitet för företagsbevakning löses via en read-model-projektion
> över `job_ads` (org.nr som naturlig nyckel + `company_name`); det byggda
> aggregatet för bevakning är `CompanyWatch` (med notisspåret
> `FollowedCompanyAdHit`), inte `Company`.
>
> Motsvarande obyggda rekryterar-/CRM-yta står kvar på flera ställen i specen och
> behålls som framtida scope, inte v1-verklighet: `Company` som surrogat-nycklat
> AR med `website`, `CompanyId`-referenserna (`JobAd` och `Contact`, §5.1;
> `CompanyId`-VO:t, §5.2), `companies`/`contacts`-tabellerna i datamodellen (§7.1)
> med deras `/api/v1/companies`- och `/contacts`-endpoints (§6.2), och
> `Company.website` i Gmail-synk-matchningen (§9.2). Allt detta tillhör en **separat, ännu obyggd
> framtida rekryterar-/CRM- + Gmail-synk-feature** (§9.2 är själv uppskjuten ur
> MVP per #321).

### 5.2 Value Objects

- `JobSeekerId`, `ResumeId`, `ResumeVersionId`, `JobAdId`, `CompanyId`, `ContactId`, `ApplicationId`, `CoverLetterId`, `SavedSearchId`, `FollowUpId` — strongly-typed IDs (record struct med Guid wrapped)
- `Money` (amount: decimal, currency: Currency)
- `SalaryRange` (min: Money, max: Money, type: SalaryType)
- `Location` (city, region, postalCode, countryCode, coordinates?)
- `SsykCode` (code: string) — svensk yrkeskod
- `SsykTaxonomyPath` — hierarkisk yrkeshiearki
- `EmploymentType` enum: Permanent, FixedTerm, Substitute, Hourly, Internship
- `WorkMode` enum: OnSite, Remote, Hybrid
- `EmailAddress`, `PhoneNumber`, `Url` — validerade wrappers
- `MatchScore` (value: int 0-100, breakdown: MatchBreakdown)
- `MatchBreakdown` (ssykOverlap: 0-100, titleSimilarity: 0-100, skillMatch: 0-100, requirementCoverage: 0-100, locationFit: 0-100, employmentTypeFit: 0-100, matchedKeywords: IReadOnlyList<string>, missingKeywords: IReadOnlyList<string>) — deterministisk "Fast mode", förklarbar by design (ADR 0071)
- `SourceReference` (source: string, externalId: string, originalUrl: string)
- `FollowUpChannel` enum: Email, LinkedIn, Phone, Meeting, Other
- `ResumeContent` — strukturerad data: PersonalInfo, List<Experience>, List<Education>, List<Skill>, etc.
- `DateTimeRange` — inclusive range för intervjuer

### 5.3 Application-aggregatet (central hub)

Se arkitekturdiagram i det inledande samtalet. Application är det viktigaste aggregatet.

```csharp
public sealed class Application : AggregateRoot<ApplicationId>
{
    private readonly List<FollowUp> _followUps = new();
    private readonly List<ApplicationNote> _notes = new();

    public JobSeekerId JobSeekerId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public ResumeVersionId? ResumeVersionId { get; private set; }
    public CoverLetterId? CoverLetterId { get; private set; }
    public ContactId? RecruiterContactId { get; private set; }

    public ApplicationStatus Status { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset? LastStatusChangeAt { get; private set; }

    public IReadOnlyList<FollowUp> FollowUps => _followUps.AsReadOnly();
    public IReadOnlyList<ApplicationNote> Notes => _notes.AsReadOnly();

    public DateTimeOffset LastContactAt =>
        _followUps.Count == 0 ? AppliedAt : _followUps.Max(f => f.OccurredAt);

    private Application() { } // EF Core

    public static Application Submit(
        ApplicationId id,
        JobSeekerId jobSeekerId,
        JobAdId jobAdId,
        ResumeVersionId? resumeVersionId,
        CoverLetterId? coverLetterId,
        DateTimeOffset appliedAt)
    {
        var app = new Application
        {
            Id = id,
            JobSeekerId = jobSeekerId,
            JobAdId = jobAdId,
            ResumeVersionId = resumeVersionId,
            CoverLetterId = coverLetterId,
            Status = ApplicationStatus.Submitted,
            AppliedAt = appliedAt,
            LastStatusChangeAt = appliedAt,
        };
        app.RaiseDomainEvent(new ApplicationSubmittedEvent(id, jobSeekerId, jobAdId, appliedAt));
        return app;
    }

    public void LogFollowUp(FollowUp followUp)
    {
        if (Status.IsTerminal)
            throw new DomainException($"Cannot log follow-up on a {Status.Name} application.");
        _followUps.Add(followUp);
        RaiseDomainEvent(new FollowUpLoggedEvent(Id, followUp.Id, followUp.Channel, followUp.OccurredAt));
    }

    public void AddNote(ApplicationNote note)
    {
        _notes.Add(note);
        RaiseDomainEvent(new ApplicationNoteAddedEvent(Id, note.Id, note.CreatedAt));
    }

    // ADR 0092 D3: transitions are FREE — any status is a valid target. The old
    // CanTransitionTo grind is replaced by an undo-toast + audit + the append-only
    // StatusChange timeline. Only two hard guards remain (soft-delete, self no-op).
    public Result TransitionTo(ApplicationStatus next, IDateTimeProvider clock)
    {
        if (DeletedAt is not null) return Result.Failure(/* borttagen ansökan */);
        if (next == Status) return Result.Success();                 // self-transition no-op
        var previous = Status;
        Status = next;
        LastStatusChangeAt = clock.UtcNow;
        _statusChanges.Add(StatusChange.Create(previous, next, clock.UtcNow)); // timeline
        RaiseDomainEvent(new ApplicationStatusTransitionedDomainEvent(Id, JobSeekerId, previous, next, clock.UtcNow));
        return Result.Success();
    }

    public void AttachTailoredResume(ResumeVersionId versionId)
    {
        if (Status != ApplicationStatus.Draft)
            throw new DomainException("Can only attach a resume version while in draft.");
        ResumeVersionId = versionId;
    }

    public void AttachCoverLetter(CoverLetterId coverLetterId)
    {
        if (Status != ApplicationStatus.Draft)
            throw new DomainException("Can only attach a cover letter while in draft.");
        CoverLetterId = coverLetterId;
    }

    public void MarkGhostedIfStale(DateTimeOffset now, TimeSpan threshold)
    {
        if (Status != ApplicationStatus.Submitted && Status != ApplicationStatus.Acknowledged)
            return;
        if (now - LastContactAt < threshold)
            return;
        TransitionTo(ApplicationStatus.Ghosted, now);
    }
}
```

### 5.4 ApplicationStatus (statusar + rekommenderade övergångar)

Tio statusar. **ADR 0092 D3: övergångar är fria** — `Application.TransitionTo` tillåter
vilken status som helst som mål (framåt, bakåt eller Ghosted). Den tidigare
tillståndsmaskin-grinden (`CanTransitionTo` som kastade) är ersatt av ångra-toast +
full audit + den append-only `StatusChange`-tidslinjen, så varje byte är spårbart och
ångringsbart. Grafen nedan är nu **enbart rådgivande** (`RecommendedNextStatuses`) — den
driver UI-hinten ("Flytta till {nästa steg}" + avsluta/parkera-alternativen), aldrig en
grind. Detta löser #566 (saknade övergångar + drift blir irrelevant när alla byten tillåts).

```csharp
public sealed class ApplicationStatus : SmartEnum<ApplicationStatus>
{
    public static readonly ApplicationStatus Draft = new("Draft", 1);
    public static readonly ApplicationStatus Submitted = new("Submitted", 2);
    // ... Acknowledged(3), InterviewScheduled(4), Interviewing(5), OfferReceived(6),
    //     Accepted(7), Rejected(8), Withdrawn(9), Ghosted(10)

    private readonly HashSet<ApplicationStatus> _recommendedNext = [];

    // ADVISORY ONLY (ADR 0092 D3) — a UI hint, NOT a guard. TransitionTo permits
    // any target regardless of what is listed here. Do not reintroduce enforcement.
    public IReadOnlySet<ApplicationStatus> RecommendedNextStatuses => _recommendedNext;

    // static ctor seeds the conventional forward step + usual closing moves per status
    // (e.g. Submitted → {Acknowledged, Rejected, Withdrawn}); see ApplicationStatus.cs.
}
```

### 5.5 Domain events

Alla domain events implementerar `IDomainEvent` och dispatchas av `SaveChangesInterceptor` efter `SaveChangesAsync`. Events hanteras av Mediator.SourceGenerator `INotificationHandler<>` i Application-lagret.

**Händelser som ska finnas:**

- `JobSeekerRegisteredEvent`
- `ResumeCreatedEvent`, `ResumeVersionCreatedEvent`, `ResumeImprovedEvent` (deterministisk förbättring, ADR 0071)
- `SavedSearchCreatedEvent`, `SavedSearchTriggeredEvent`
- `JobAdIngestedEvent`, `JobAdDismissedEvent`
- `ApplicationSubmittedEvent`, `ApplicationStatusChangedEvent`, `ApplicationNoteAddedEvent`
- `FollowUpLoggedEvent`
- `CoverLetterCreatedEvent` (användarens egen text — ingen AI-generering, ADR 0071)
- `CvReviewedEvent`, `MatchScoreComputedEvent`
- `UserImpersonationStartedEvent`, `UserImpersonationEndedEvent`

Alla events loggas till `AuditLog`-tabellen via en gemensam `AuditLogHandler`.

### 5.6 Konsistensregler (invariants)

- Application.Status är en av de tio statusarna; övergångar är fria (ADR 0092 D3 — `RecommendedNextStatuses` är rådgivande, ej en grind). En borttagen ansökan kan inte byta status, och ett självbyte är en no-op.
- Varje statusbyte registreras append-only på `StatusChange`-tidslinjen, atomiskt med `Status` (samma UnitOfWork)
- FollowUp kan inte loggas på en Application i terminal state
- ResumeVersion kan inte raderas om den är refererad av en icke-terminal Application
- Resume måste ha exakt en `Master`-version
- SavedSearch.Criteria måste ha minst ett kriterium (ej tomt sökning)
- MatchScore.Value är alltid 0–100

---

## 6. API-design

### 6.1 Konventioner

- REST, JSON body
- `/api/v1/...` prefix
- Kebab-case i URL, camelCase i JSON
- `Authorization: Bearer <sessionId>` för alla skyddade endpoints (opakt session-id, INTE en JWT — se §11.2; truth-sync #569/#827)
- Pagination: `?page=1&pageSize=20`, response wrappar med `{ items, page, pageSize, totalCount }`
- Error response: Problem Details (RFC 7807), alltid `application/problem+json`
- ETag + If-Match för optimistic concurrency på aggregate-updates
- `X-Correlation-Id` header propageras genom alla lager

### 6.2 Endpoints (grupperade per kontext)

**Auth**
- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/forgot-password`
- `POST /api/v1/auth/reset-password`
- `POST /api/v1/auth/verify-email`
- `POST /api/v1/auth/oauth/google`
- `POST /api/v1/auth/oauth/microsoft`

**Me / profil**
- `GET /api/v1/me`
- `PATCH /api/v1/me`
- `GET /api/v1/me/preferences`
- `PATCH /api/v1/me/preferences`
- `DELETE /api/v1/me` (GDPR-radering, soft delete + 30-dagars restore)

**Resumes**
- `GET /api/v1/resumes` (paginerad lista)
- `GET /api/v1/resumes/{id}` (inkl. versions)
- `POST /api/v1/resumes` (ny master eller upload)
- `POST /api/v1/resumes/{id}/upload` (PDF/DOCX, triggar parsing)
- `POST /api/v1/resumes/{id}/versions` (ny version manuellt)
- `POST /api/v1/resumes/{id}/review` (deterministisk granskning → per-kriterium PASS/WARN/FAIL med citerad evidens)
- `POST /api/v1/resumes/{id}/improve` (deterministiska förbättringsförslag som propose-and-approve-diffar, valfritt `jobAdId` för krav/keyword-täckning; ingen prosasyntes)
- `GET /api/v1/resumes/{id}/versions/{versionId}/export?format=pdf|docx`
- `DELETE /api/v1/resumes/{id}`
- `DELETE /api/v1/resumes/{id}/versions/{versionId}`

**Job ads**
- `GET /api/v1/job-ads` (sök, filter, paginerad)
- `GET /api/v1/job-ads/{id}`
- `POST /api/v1/job-ads/{id}/dismiss` (dölj från sök)
- `POST /api/v1/job-ads/{id}/save` (bookmark)
- `POST /api/v1/job-ads/{id}/compute-match` (beräkna deterministisk "Fast mode"-match)
- `GET /api/v1/job-ads/{id}/salary-stats` (SCB-data för SSYK)

**Saved searches**
- `GET /api/v1/saved-searches`
- `POST /api/v1/saved-searches`
- `GET /api/v1/saved-searches/{id}`
- `PATCH /api/v1/saved-searches/{id}`
- `DELETE /api/v1/saved-searches/{id}`
- `POST /api/v1/saved-searches/{id}/run`

**Applications**
- `GET /api/v1/applications` (filter status, datum, company)
- `POST /api/v1/applications` (draft)
- `GET /api/v1/applications/{id}`
- `PATCH /api/v1/applications/{id}` (notes, attach resume version, cover letter)
- `POST /api/v1/applications/{id}/submit`
- `POST /api/v1/applications/{id}/transition` (body: `{ status, occurredAt }`)
- `DELETE /api/v1/applications/{id}` (användar-initierad **hard delete**, #782/ADR 0104 — se §7.3)
- `POST /api/v1/applications/{id}/follow-ups`
- `GET /api/v1/applications/{id}/follow-ups`
- `POST /api/v1/applications/{id}/notes`
- `GET /api/v1/applications/pipeline` (board-style aggregation)
- `GET /api/v1/applications/stats` (avslags-analys, pipeline-konvertering)

**Cover letters**
- `POST /api/v1/cover-letters` (skapa, body: `{ applicationId, tone }` — användarens egen text, ingen AI)
- `GET /api/v1/cover-letters/{id}`
- `PATCH /api/v1/cover-letters/{id}`
- `POST /api/v1/cover-letters/{id}/detect-cliches` (deterministisk klysch-flaggning mot kurerad lista)
- `GET /api/v1/cover-letters/{id}/export?format=pdf|docx`

**Contacts / companies**
- `GET /api/v1/companies`
- `GET /api/v1/companies/{id}`
- `GET /api/v1/contacts`
- `POST /api/v1/contacts`
- `PATCH /api/v1/contacts/{id}`

**Integrations**
- `POST /api/v1/integrations/gmail/connect` (OAuth-start)
- `POST /api/v1/integrations/gmail/callback` (OAuth-callback)
- `GET /api/v1/integrations/gmail/status`
- `DELETE /api/v1/integrations/gmail` (disconnect)
- `POST /api/v1/integrations/gmail/sync-now`
- Samma mönster för `google-calendar`

> **AI settings / BYOK-endpoints utgår (ADR 0071):** `me/ai-settings`,
> `me/ai-keys`, `me/ai-usage`, `me/credits` byggs aldrig — ingen AI/LLM, ingen
> BYOK, inga credits.

**Admin (role = Admin eller SuperAdmin)**
- `GET /api/v1/admin/users`
- `GET /api/v1/admin/users/{id}`
- `POST /api/v1/admin/users/{id}/suspend`
- `POST /api/v1/admin/users/{id}/unsuspend`
- `POST /api/v1/admin/users/{id}/reset-password`
- `POST /api/v1/admin/users/{id}/impersonate` — **OBYGGD.** Endpointen finns inte i `Endpoints/`, och "returnerar temporär JWT" beskriver en mekanism som inte längre existerar (§11.3). Truth-sync #569/#827
- `GET /api/v1/admin/audit-log?from&to&userId&action&aggregateType`
- `GET /api/v1/admin/job-sources/status`
- `POST /api/v1/admin/job-sources/{source}/resync`

**Health & meta**
- `GET /api/health`
- `GET /api/ready`
- `GET /api/meta/version`

---

## 7. Datamodell

### 7.1 Primära tabeller (PostgreSQL, snake_case)

```sql
-- Identity (ASP.NET Core Identity-defaults utökas)
users                           -- IdentityUser
user_roles, roles, role_claims, user_claims, user_logins, user_tokens

-- Core domain
job_seekers
  id (uuid PK)
  user_id (uuid FK users)
  display_name (text)
  preferences (jsonb)            -- flexibel VO
  created_at, updated_at, deleted_at (soft delete)

resumes
  id (uuid PK)
  job_seeker_id (uuid FK)
  name (text)
  created_at, updated_at, deleted_at

resume_versions
  id (uuid PK)
  resume_id (uuid FK)
  kind (text: 'master'|'improved')   -- 'improved' = deterministisk förbättring (ADR 0071); ingen LLM-skräddarsöm
  tailored_for_job_ad_id (uuid FK null)  -- mål-annons för keyword/krav-täckning (deterministisk)
  content (jsonb)                -- ResumeContent VO
  created_at, updated_at

saved_searches
  id (uuid PK)
  job_seeker_id (uuid FK)
  name (text)
  criteria (jsonb)
  notification_enabled (boolean)
  last_run_at (timestamptz null)
  created_at, updated_at, deleted_at

companies
  id (uuid PK)
  name (text)
  org_number (text null)         -- svenskt organisationsnummer
  website (text null)
  industry (text null)
  size_bucket (text null)        -- '1-10','11-50','51-200',etc.
  research_brief (jsonb null)
  research_brief_updated_at (timestamptz null)
  created_at, updated_at

contacts
  id (uuid PK)
  company_id (uuid FK null)
  full_name (text)
  title (text null)
  email (text null)
  linkedin_url (text null)
  phone (text null)
  added_by_job_seeker_id (uuid FK)
  created_at, updated_at, deleted_at

job_ads
  id (uuid PK)                   -- vår egen id
  source (text)                  -- 'platsbanken', 'eures', ...
  external_id (text)
  source_url (text)
  company_id (uuid FK null)
  title (text)
  description (text)
  description_html (text)
  ssyk_code (text null)
  employment_type (text)
  work_mode (text)
  location (jsonb)
  salary (jsonb null)
  deadline_at (timestamptz null)
  published_at (timestamptz)
  ingested_at (timestamptz)
  raw_payload (jsonb)            -- komplett JobTech-JSON
  UNIQUE(source, external_id)

applications
  id (uuid PK)
  job_seeker_id (uuid FK)
  job_ad_id (uuid FK)
  resume_version_id (uuid FK null)
  cover_letter_id (uuid FK null)
  recruiter_contact_id (uuid FK null)
  status (text)                  -- enum name
  applied_at (timestamptz)
  last_status_change_at (timestamptz)
  notes_summary (text null)
  ghosted_threshold_days (int default 21)
  created_at, updated_at, deleted_at

follow_ups
  id (uuid PK)
  application_id (uuid FK, ON DELETE CASCADE)
  channel (text)
  occurred_at (timestamptz)
  note (text null)
  outcome (text null)
  created_at

application_notes
  id (uuid PK)
  application_id (uuid FK, ON DELETE CASCADE)
  content (text)
  created_at

cover_letters
  id (uuid PK)
  job_seeker_id (uuid FK)
  application_id (uuid FK null)
  content (text)                 -- användarens egen text (ingen AI-generering, ADR 0071)
  tone (text)
  created_at, updated_at

-- Matching (read model) — deterministisk "Fast mode" (ADR 0071); ingen Deep mode
match_scores
  id (uuid PK)
  job_seeker_id (uuid FK)
  job_ad_id (uuid FK)
  resume_version_id (uuid FK null)
  score (int)
  breakdown (jsonb)              -- matchade/saknade nyckelord m.m. (förklarbar by design)
  computed_at (timestamptz)
  UNIQUE(job_seeker_id, job_ad_id, resume_version_id)

-- Ingen ai_operations- eller byok_credentials-tabell (ADR 0071): ingen AI/LLM,
-- ingen BYOK, inga token-/credit-räknare.

-- Audit
audit_log
  id (uuid PK)
  occurred_at (timestamptz)
  correlation_id (uuid)
  user_id (uuid FK null)
  impersonated_by (uuid FK null)
  event_type (text)
  aggregate_type (text null)
  aggregate_id (uuid null)
  payload (jsonb)
  ip_address (inet null)
  user_agent (text null)
  -- retention: 90 dagar (hantera via partitionering per dag)

-- Notifications
notifications
  id (uuid PK)
  job_seeker_id (uuid FK)
  type (text)
  title (text)
  body (text)
  read_at (timestamptz null)
  action_url (text null)
  created_at

email_log
  id (uuid PK)
  to_address (text)
  subject (text)
  template (text)
  sent_at (timestamptz)
  provider_message_id (text null)   -- provider-neutralt (transaktionell väg = Scaleway Transactional Email, #183/§13.4; providerns message-id skrivs medvetet INTE av avsändaren — beslutet är ADR 0124:s, och sedan #183 finns det dessutom inget att skriva: avsändaren läser aldrig svarskroppen, HttpCompletionOption.ResponseHeadersRead)
  status (text)

-- Integrations
oauth_connections
  id (uuid PK)
  job_seeker_id (uuid FK)
  provider (text)                -- 'gmail', 'google-calendar'
  encrypted_access_token (bytea)
  encrypted_refresh_token (bytea)
  scopes (text[])
  expires_at (timestamptz)
  created_at, updated_at, disconnected_at
  UNIQUE(job_seeker_id, provider)

-- Reference data
ssyk_salary_stats
  ssyk_code (text PK)
  median_sek (int)
  p10_sek (int)
  p90_sek (int)
  source (text)                  -- 'SCB'
  updated_at (timestamptz)
```

### 7.2 Indexeringsstrategi

Alla FK-kolumner har index. Utöver det:
- `job_ads (source, external_id)` UNIQUE
- `job_ads (published_at DESC)` — senaste först
- `job_ads (ssyk_code)` för filtrering
- `job_ads USING gin(to_tsvector('swedish', title || ' ' || description))` — full-text search
- `applications (job_seeker_id, status, last_status_change_at DESC)` — för pipeline-vy
- `match_scores (job_seeker_id, score DESC)` — för "topp-matchningar"
- `audit_log (occurred_at DESC)` + partitionering per dag

### 7.3 Soft delete-strategi

- Alla user-ägda aggregates har `deleted_at` (timestamptz null)
- Global EF Core query filter på alla soft-deletable entities
- Hard delete efter 30 dagar via schedulerad Hangfire-job
- `DELETE /me` sätter `deleted_at` på alla aggregat tillhörande användaren
- Restore-endpoint (`POST /api/v1/admin/users/{id}/restore`) återställer inom 30 dagar
- **Undantag — användar-initierad per-ansökan-radering** (#782/ADR 0104): `DELETE
  /api/v1/applications/{id}` ("Radera ansökan") är en **hard delete** — raden + barnen
  (follow_ups/application_notes/application_status_changes via FK-cascade) tas bort
  omedelbart, audit-raden (`Application.Deleted`) överlever (Art. 5(2)). INTE soft:
  copyn lovar "kan inte ångras", ingen per-ansökan-sweeper finns, och GDPR-minimering
  (Art. 5(1)(c)/(e)) gynnas av omedelbar utplåning. Konto-nivåns `SoftDelete`-cascade
  (ADR 0024) och aggregatets `Application.SoftDelete` är orörda.

### 7.4 Migrations

- EF Core migrations i `Jobbliggaren.Infrastructure/Persistence/Migrations/`
- Namn: `20260418_InitialSchema`, `20260420_AddImpersonationClaim`, etc.
- Aldrig redigera applied migration — skapa ny
- Migration körs automatiskt i Api-startup i dev/staging, manuellt i prod
- Seed-data för reference (SSYK) körs via separat `Seed`-kommando

---

## 8. Deterministiska CV- och matchnings-motorer

> **Princip (ADR 0071, ersätter ADR 0051).** Produkten innehåller **ingen
> AI/LLM** och ingen BYOK. `IAiProvider`/`IAiProviderResolver`, `CvTailor`,
> credit/BYOK-systemet och `AiProviderKind` byggs **aldrig**. Jobbliggaren är
> gratis utan abonnemang; kostnaden för även ett magert LLM-lager (API-spend,
> DPIA/SCC/TIA-compliance, opt-in-UX, credits) är oförenlig med
> gratis-produkt-taket. Inget **AI-relaterat** tredjelands-PII-transfer kvarstår (e-postvägen är en
> separat behandling och **aktiverad 2026-08-16 utan att §2.5-grinden passerades** — §13.4 säger vad
> som körs, `release-checklist.md` §2.5 om det fick köras. ⚠ **Kalla den aldrig en
> tredjelandsöverföring:** avtalsparten är fransk och Kap. V-bedömningen är *ej tillämplig* — ett
> utkast, inte en dom, och led (b) är dess hem. Raden sa "grindad överföring" till 2026-08-16 och
> var då fel på båda orden) — ingen
> CV-PII skickas till någon AI-provider, så ADR 0051:s fem GDPR-villkor
> upplöses. Allt nedan är **deterministiskt**: regex, list-lookup,
> datum-aritmetik, taxonomi-lookup och lokal NLP på VPS:en. En intern kriterie-analys
> visar att ~70 % av
> rubric-kriterierna är ren determinism, ~26 % determinism + lokal NLP, och
> bara ~4 % (mening-för-mening-prosa, ton, profilsyntes, annons-skräddarsöm)
> är genuint LLM-gatade — dessa är **uteslutna ur scope**.

### 8.1 CV-granskningsmotor — per-kriterium PASS/WARN/FAIL

En regelbaserad motor producerar en verdict per kriterium (PASS / WARN / FAIL)
**med citerad textevidens** ur det uppladdade CV:t, mappad mot kunskapsbankens
rubric (den versionerade kunskapsbanken). Scoringen är **kategori-primär**
(viktade kategorisummor — Innehåll, Struktur, Språk, ATS-parsbarhet, Visuell
kvalitet) med separata profiler för ATS-optimerad respektive visuell rendering
där innehållskriterierna delas. Rubriken är **versionerad** (`rubric@major.minor.patch`);
`rubric_version` lagras med varje bedömning.

- **Kritiska auto-fails lyfts separat**, oavsett totalpoäng: personnummer (B4),
  stavfel/grammatik (C1), fel filformat (D1), inga mätbara resultat (A1).
- **Kategori-score är primär UX**, totalscore sekundär — motverkar
  Goodhart-effekten.
- **Personnummer-guard** (regex, GDPR + civic-utility) är **högst prioriterat**:
  ett CV som innehåller svenskt personnummer (helt eller fyra sista) flaggas
  och användaren uppmanas stryka det före submit. Motorn uppmanar **aldrig**
  användaren att lägga in personnummer eller andra känsliga uppgifter (IMY).
- **Reducerad precision dokumenteras, ej missrapporteras:** kriterier som utan
  LLM blir svårbedömda (t.ex. karriärprogression A5, genuin grammatik C1) märks
  "ej bedömt v1" i output i stället för att rapportera fel verdict.

### 8.2 Matchningsmotor — "Fast mode" (taxonomi + lexikalt)

Matchningsscoren byggs som en deterministisk **"Fast mode"** och beräknas
gratis för alla synliga annonser:

- **SSYK nivå-4-overlap** (annonsens taxonomi mot CV-härledd SSYK)
- **Titellikhet** (stammad strängjämförelse)
- **Keyword/skill-overlap** (stammad, mot JobTech-taxonomin)
- **Kravtäckning** (parsade annonskrav mot CV-innehåll)
- **Region- och anställningsform-match**

"Deep mode" (LLM-baserad semantisk matchning) är **inställd**. Den
deterministiska scoren är **förklarbar by design**: matchade och saknade
nyckelord visas för användaren, vilket är arkitektoniskt överlägset en
LLM-black-box för en civic-utility-produkt — inte bara billigare.

### 8.3 CV-bygg/förbättringsmotor — diagnostik och struktur

Bygg/förbättra-motorn täcker:

- **Mall-rendering**: ATS-plain och visuell från **samma JSON-källdata**
  (QuestPDF) så att innehållskriterierna är identiska och bara rendering skiljer
- **Custom färgpalett** (WCAG-validerad, kontrast ≥ 4,5:1)
- **Svensk och engelsk** output
- **ATS-sanering** (strip av icke-standardtecken, tabellstrukturer, textrutor)
- **Klysch-flaggning** mot en kurerad svensk lista
- **Action-verb-förslag** ur en kurerad lista
- **Strukturell/format-normalisering** (sektionsordning, datumstandardisering)
- **Personnummer- / foto- / GPA-strip** (deterministiskt, GDPR; foto-default = av
  för SE-marknad)

Alla operationer producerar **propose-and-approve-diffar** — inget appliceras
utan explicit användarbekräftelse. Det uppfyller no-hallucination-kravet by
construction: en regelmotor kan inte hitta på kvalifikationer användaren saknar.

**Uteslutet (LLM-gatat):** mening-för-mening-prosaomskrivning,
tonjustering, profiltext-syntes, annons-skräddarsöm (`CvTailor`). Gränsen:
*determinism kan DIAGNOSTISERA och STRUKTURERA prosa, men inte SYNTETISERA den.*

### 8.4 SSYK-härledning via taxonomi-lookup (ADR 0040 re-scopad)

Smart CV-baserat sparat-sök-filter (ADR 0040, Proposed) härleder SSYK
**deterministiskt**: yrkestitel → SSYK nivå 4 via JobTech-taxonomin, med
**obligatorisk användarbekräftelse** innan en `SavedSearch` skapas. ADR 0040:s
transparens- och bekräftelsekrav är fullt bevarade; bara härlednings­mekanismen
ändras (LLM-inferens → taxonomi-lookup). Titlar som saknas i taxonomin
auto-mappas inte (fallback: manuellt SSYK-val, samma UX som bekräftelsesteget).

### 8.5 Interfaces (illustrativa — namngivning fastställs i Fas 4-design)

Motorerna lever i Application/Infrastructure per Clean Architecture; de exakta
signaturerna binds av dotnet-architect vid Fas 4-design (Last Responsible
Moment). Illustrativ form:

```csharp
namespace Jobbliggaren.Application.Common.Interfaces;

// Granskar CV mot den versionerade rubriken; returnerar per-kriterium-verdict
// med citerad evidens. Inga externa anrop — ren determinism + lokal NLP.
public interface ICvReviewEngine
{
    Task<CvReviewResult> ReviewAsync(
        ParsedResume resume,
        RenderProfile profile,          // Ats | Visual
        CancellationToken ct);
}

// Deterministisk "Fast mode"-matchning; förklarbar (matchade/saknade nyckelord).
public interface IMatchScorer
{
    Task<MatchScore> ScoreAsync(JobAdId jobAdId, ResumeId resumeId, CancellationToken ct);
}

// Föreslår förbättringar som propose-and-approve-diffar; applicerar aldrig själv.
public interface ICvImprovementEngine
{
    Task<IReadOnlyList<ProposedChange>> SuggestAsync(ParsedResume resume, CancellationToken ct);
}

public enum RenderProfile { Ats, Visual }
public enum CriterionVerdict { Pass, Warn, Fail, NotAssessed }
```

### 8.6 Kurerade datakällor och lokal NLP

- **Rubric, klysch-lista och action-verb-lista** är **versionerad data/config**
  (versionerad kunskapsbank), aldrig hårdkodade C#-strängar.
- **Lokal NLP-tier** (~26 % av kriterierna) körs på VPS:en utan externa anrop:
  tokenisering, svensk stemming, POS-taggning. Biblioteken (Catalyst,
  libstemmer.net, WeCantSpell.Hunspell + sv_SE-ordlista) är **flaggade i ADR
  0071 Beslut 6 men ej inlåsta i §3.1** förrän dotnet-architect/CTO-GO + Klas
  spec-edit-approve (se §3.1-notis). PdfPig / DocumentFormat.OpenXml / QuestPDF
  är redan godkända och täcker PDF/DOCX/render-tiern.

---

## 9. Extern integration

### 9.1 JobTech (Arbetsförmedlingen)

**Huvud-APIer i v1:**
- `JobSearch` — sök annonser med filter. https://jobsearch.api.jobtechdev.se/
- `JobStream` — streaming av nya/uppdaterade annonser. https://jobstream.api.jobtechdev.se/
- `Taxonomy` — SSYK-kod-referens, kompetensbegrepp. https://taxonomy.api.jobtechdev.se/
- `JobAd Enrichments` — kompetens-extraktion (används för Fast match). https://jobad-enrichments-api.jobtechdev.se/

**Implementation:**
- `IJobTechClient` interface, implementation via Refit
- `PlatsbankenJobSource : IJobSource`
- Sync-strategi: JobStream-prenumeration för realtid + JobSearch för backfill
- Retry via `Microsoft.Extensions.Http.Resilience` (Polly v8 under huven): 3 försök med exponential backoff
- Circuit breaker efter 5 consecutive failures, cooldown 5 min
- Hangfire-job `SyncPlatsbankenJob` kör var 10:e minut (JobStream-subscription) + nattlig full backfill

**Dataflöde:**
1. JobStream pushar/polls nya annonser
2. Varje annons parsas → `JobAdSnapshot`
3. Upsert i `job_ads` (unique `source+external_id`)
4. Kompetensextraktionsanrop till Enrichments API, cache 7 dagar
5. `JobAdIngestedEvent` raisas → matchning mot alla aktiva SavedSearches triggas

### 9.2 Gmail-sync

- Google Workspace OAuth 2.0 flow
- Scopes: `gmail.readonly` (minimal)
- User-consent-skärm visar exakt vad vi gör: "Jobbliggaren läser inkomna mejl från adresser du märkt som rekryterare för att automatiskt logga uppföljningar"
- Implementation: `IGmailSyncService`
- Sync-strategi: Pub/Sub via Gmail API history (`users.history.list`), fallback till polling var 15:e min
- Hantering av tokens: refresh token lagras envelope-krypterat i `oauth_connections`
- Användaren kan disconnecta när som helst → raderar token + stoppar sync

**Matchningslogik:**
1. Hämta inkomna mejl sedan sista sync
2. För varje mejl: kolla om `from`-adressen matchar en `Contact.email` eller innehåller domän som matchar en `Company.website`
3. Om match: försök hitta öppen `Application` där `recruiter_contact_id` eller `company_id` matchar
4. Skapa `FollowUp` med channel=Email, occurred_at=mejlets datum, note=subject (första 200 tecken)
5. Notifiera användaren i app

### 9.3 Google Calendar

- OAuth 2.0
- Scopes: `calendar.events` (läsa + skriva egna events)
- När användaren sätter status till `InterviewScheduled`, skapar appen ett calendar event
- iCal-export via egen endpoint som genererar `.ics`-fil (inget OAuth krävs)

> **Gmail-sync (§9.2) + Google Calendar (§9.3) skjuts upp — utgår ur MVP (#321,
> Klas-beslut 2026-07-10).** GDPR-/drift-kostnaden är hög mot single-box- (~$16
> CAX31, ADR 0050) och gratis-constraintsen — känslig `gmail.readonly`-OAuth-
> appverifiering, per-användare-envelopekryptering av refresh-token och en
> synk-last var 15:e minut per användare — och inget annat blockeras av att
> skjuta upp den. Det användarnära behovet (aldrig missa en intervju/deadline)
> täcks i stället av påminnelse-notiser i appen på översikten (t.ex. "Du har en
> intervju bokad om 3 dagar"), spårat som **#726**. §9.2/§9.3-specarna ovan
> bevaras som framtida referens om en extern-integrations-fas återupptas efter
> beta.

### 9.4 SCB (Statistiska centralbyrån)

- Användar Pxweb-API för lönestatistik: https://api.scb.se/OV0104/v1/doris/sv/ssd
- Tabellen `AM/AM0110/AM0110A/LonArbsSNI2025` eller motsvarande löne-per-SSYK
- Månatlig import via Hangfire → `ssyk_salary_stats`-tabellen

> **AI-provider-integration utgår (ADR 0071).** Tidigare §9.5/§9.6 specade
> Anthropic Direct API för BYOK respektive systemnyckel. Produkten innehåller
> ingen AI/LLM — ingen `Anthropic`-NuGet, ingen `api.anthropic.com`-klient,
> ingen Bedrock-adapter, inget tredjelands-inferensanrop. CV- och
> matchnings-funktionerna är deterministiska (§8).

---

## 10. Frontend-arkitektur

### 10.1 Next.js 16 App Router-struktur

```
/web/jobbliggaren-web
  /app
    /(marketing)               -- publika sidor
      /page.tsx               -- landing
      /om
      /priser
      /integritet
    /(auth)
      /logga-in
      /registrera
      /glomt-losenord
    /(app)                     -- autentiserat
      /layout.tsx             -- app shell, navigation
      /instrumentpanel        -- dashboard
      /jobb                   -- discovery
        /page.tsx             -- lista + filter
        /[id]/page.tsx        -- detaljvy
      /sokningar              -- saved searches
      /ansokningar            -- pipeline
        /page.tsx             -- tabell
        /[id]/page.tsx        -- detalj
        /pipeline/page.tsx    -- status-grupperad vy
      /statistik              -- avslags-analys + pipeline-konvertering (#313).
        /page.tsx             -- Top-level, INTE /ansokningar-nästlad: en sub-route
                              -- fångas av @modal/(.)ansokningar/[id]-intercepten på
                              -- soft-nav (samma skäl som /aktivitetsrapport — #316/#332).
      /cv
        /page.tsx
        /[id]/page.tsx
      /brev                   -- cover letters
      /foretag                -- companies + contacts
      /kalender               -- upcoming events
      /installningar
        /profil
        /integrationer        -- Gmail, Calendar
        /aviseringar
    /(admin)                   -- role=Admin+
      /anvandare
      /audit
      /jobbkallor
  /components
    /ui                        -- shadcn komponenter (customiserade)
    /layout
    /job-ad
    /application
    /resume
    /admin
  /lib
    /api                       -- API-klient (auto-genererad från OpenAPI)
    /auth                      -- session, JWT
    /hooks
    /utils
  /styles
    /globals.css               -- Tailwind + custom tokens
  /public
    /fonts                     -- Source Sans 3
    /logo.svg
```

**Deployment:** Next.js-frontend körs som en `next start`-container i samma Docker Compose-stack på Hetzner CAX31 som backend (ADR 0050 Beslut 3, amenderad 2026-06-14). `next build` körs i CI; endast den färdiga imagen shippas till boxen.

### 10.2 Data fetching-mönster

- Server components för initial rendering (paginerade listor, detaljvyer)
- Server Actions för klient-mutationer (statusändringar, notes m.m.) — `useTransition` för pending-tillstånd, `useOptimistic` där optimistisk rendering behövs (§3.1: TanStack Query är inte installerad)
- **Undantaget: binär uppladdning går via BFF-route** (`app/api/cv/import/route.ts`), eftersom en Server Action inte kan strömma `multipart/form-data` (`duplex: "half"`). Regeln ovan är alltså inte universell; detta är den enda **mutations**-vägen utanför Server Actions (flera andra klient-`fetch`ar är POST-formade *läsningar*)
- **Kortlivad klient-read** — keystroke-driven suggest, popover-counts, utkasts-preview-counts, on-demand dokument-/blob-hämtning: `AbortController`, i `useEffect`, aldrig en mutations-väg (ADR 0042 Beslut C = prejudikatet). Formen är en self-contained hook **eller** en komponentlokal `useEffect` — `lib/hooks/use-facet-counts.ts` är den förra, `components/resumes/cv-preview.tsx` den senare. Debounce där inmatning driver den; en engångshämtning vid `enabled`-flip behöver ingen, och inte heller en mount-hämtning av en komponentlokal artefakt som inte kan renderas server-side (`resumes/template-builder.tsx`, blob-preview i iframe)
- **Periodisk uppdatering** = visibility-aware `setInterval` + `fetch` i en dedikerad klient-komponent (`shell/header-stats.tsx`, 10 min)
- CLAUDE.md §5:s `useEffect`-fetch-förbud gäller **sidans initial-data**, som hör hemma i en Server Component — det når inte de två punkterna ovan
- Form state: React Hook Form + Zod schema
- Optimistic updates för statustransitions
- Skeleton/progressiv rendering för CV-granskning och mall-rendering (deterministiskt, inget LLM-streaming)

### 10.3 State management

- Ingen global store — server state via RSC + Server Actions (revalidering på servern), local UI state via useState/useReducer
- Auth state via egen cookie-baserad klient (ADR 0017) — Auth.js finns inte i `package.json` (§3.1)
- Command palette (⌘K) med custom hook, knappas via shadcn-kommandokomponent

### 10.4 Sökupplevelse (jobb)

- URL-driven state: alla filter i query params så URL:en är delbar
- Debounced text search (300 ms)
- Facet counts visade inline ("Stockholm (142), Göteborg (87)")
- Server-paginerad tabell
- Inline match-score med färgkodning (muted: grå/gul/grön)
- "Räkna om match"-knapp per rad (deterministisk "Fast mode", gratis)

### 10.5 Tillgänglighet

- WCAG 2.1 AA som golv
- Keyboard-first: alla flöden navigerbara utan mus
- `role`, `aria-*` korrekt satta
- Fokusring synlig, svensk ledsagartext
- Hög kontrast (lägsta ratio 4.5:1 för body, 3:1 för stora rubriker)
- Testat mot NVDA + VoiceOver

### 10.6 Språk

- UI på svenska
- Admin-UI på svenska
- Inga hårdkodade strängar — alla via `messages/sv/` (next-intl)
- Engelska som fallback för teknikorienterade fel ("Internal server error") men primärt "Ett fel uppstod, försök igen"

---

## 11. Auth & Authorization

### 11.1 Roller

- `User` — default, får hantera egen data
- `Admin` — admin-funktioner utom impersonation
- `SuperAdmin` — Admin + impersonation + feature flags

Roles lagras i `user_roles` (Identity).

### 11.2 Session-flöde (opaka sessioner — JWT-designen skeppades aldrig)

> **Truth-sync #569/#827 (2026-07-25).** Det som stod här beskrev en RS256-JWT-design med
> refresh-token-rotation. **Den byggdes aldrig.** ADR 0017/0018 landade i stället opaka,
> server-lagrade sessioner, och i #827 raderades den kvarvarande JWT-koden (sex typer +
> DI-registreringar) sedan den stått `[Obsolete]` med fyra suppression-regioner vars
> motivering — "bevaras för `RefreshCommandHandler`" — pekade på en handler som inte finns.
> ADR 0014-noten som stod här förfinade en mekanik som aldrig existerade.

- **Bärartoken är ett session-id, inte en JWT.** `Authorization: Bearer <sessionId>`; sessionen
  är opak och slås upp server-side. Ingen signatur, inga claims i token.
- **Session-state ligger i storen** (Redis bakom en resiliens-decorator, #511/#728), inte i
  token. Revokation = ta bort sessionen; ingen separat revokations-lista behövs.
- **Förnyelse:** `POST /api/v1/auth/refresh` → `RefreshSessionCommand`, som *slidar* sessionen
  och roterar id:t när det är dags (#481 persistent-login). Ingen refresh-token-rotation.
- **Livslängder** (`SessionStoreOptions`, sanningskälla — inte dupliceras utan läsas där):
  *Session* (vanlig inloggning) 24 h sliding / 24 h absolut, ingen rotation. *Persistent*
  ("Håll mig inloggad") 30 d sliding / 180 d absolut / id-rotation var 24:e timme. Den gamla
  §11.2:s "15 min / 14 dagar" gällde den JWT-design som aldrig byggdes.
- **Backend sätter inga cookies** (ADR 0018). Next.js-proxyn äger `__Host-`-cookien och
  ersätter dess värde när svaret bär `{ rotated: true }`.

### 11.3 Impersonation-flöde

> **OBYGGD, och mekaniken nedan gäller INTE (truth-sync #569/#827).** Steg 3 utfärdar en JWT —
> det finns ingen JWT-utfärdare kvar i kodbasen efter #827, och §11.2 två rader upp säger att
> JWT-designen aldrig skeppades. Flödet måste omspecas mot session-modellen innan det byggs;
> tills dess är det här en skiss, inte en specifikation. Lämnas medvetet omskrivet i #827 —
> att designa om impersonation är en egen change-reason, inte en följd av en radering.

1. SuperAdmin klickar "Logga in som [user]" i admin-UI
2. Backend verifierar SuperAdmin-roll
3. Backend utfärdar ny JWT med `sub=targetUser.Id`, `impersonating_by=adminUser.Id`, TTL 30 min
4. `UserImpersonationStartedEvent` raisas → audit log
5. UI:t visar banner "Du ser appen som [användarnamn]. Avsluta impersonation."
6. Alla handlingar i impersonation-sessionen har båda user-IDs i audit
7. Banner-knapp "Avsluta" → `POST /api/v1/auth/end-impersonation` → återgår till admin-session

### 11.4 Authorization-policies

- `[Authorize]` på alla endpoints utom `/auth/*`, `/health`
- `[Authorize(Roles = "Admin,SuperAdmin")]` för admin-endpoints
- Resource-based authorization: user kan bara läsa/skriva egna resumes, applications, etc.
- Implementerat via `IAuthorizationRequirement`-handlers som injiceras i Mediator.SourceGenerator-pipelinen

---

## 12. Design system

Se [`DESIGN.md`](./DESIGN.md) för komplett specifikation: färgtokens, typografi, komponenter, copy-riktlinjer.

**Viktigaste principer att komma ihåg under utveckling:**
- Civic-utility-estetik: tabeller före kort, hierarki före dekoration
- Grön accent `#15603F` som enda interaktionsfärg (`--jp-accent-*`-ramp, ADR 0068 — ersätter tidigare myndighetsblå)
- Inga emojis i UI, inga exklamationstecken, inga gradients (enda undantag: hero-plattans scopade gröna gradient, ADR 0068)
- Rak svensk copy: kvantifierad information först
- `border-radius` 6px-golv för rader/kort/knappar, 12px endast hero (ADR 0052), pills/badges undantagna
- Exakta tokens (färg/typografi/spacing/radius) ägs av DESIGN.md + design-skills

---

## 13. Säkerhet & GDPR

### 13.1 Dataklassificering

| Klass | Exempel | Hantering |
|-------|---------|-----------|
| Känsligt | CV-innehåll, cover letters, OAuth-tokens | Kryptera at rest, logga aldrig |
| Personligt | Namn, email, ansökningar | Standard GDPR, logga ej i klartext |
| Operationellt | JobAd-data | Offentligt, cacha fritt |

### 13.2 Encryption

**At rest:**
- Databas: co-tenant PostgreSQL på netcup-lådan (ADR 0050 `Amendment 2026-08-04`/0122); disk-/volym-kryptering på VPS-nivå
- Backup: nattlig `pg_dump` klient-side-krypterad (age) → **mål inte valt, ägs av [#197](https://github.com/klasolsson81/jobbliggaren/issues/197)**; kraven står i §13.4:s backup-post, som är det enda hemmet för dem
- PII-fält (`cover_letter`, `resume_versions.content` m.fl.) och OAuth-tokens:
  per-användar-DEK envelope encryption via `IDataKeyProvider`
  (Local AES-256-GCM eller KMS, config-switchat per ADR 0066/0049) — extra lager
  utöver databas-kryptering

**In transit:**
- TLS 1.3 överallt
- HSTS + preload
- Certificate pinning i mobilklient (framtida)

**Secrets-hantering per miljö:**
- `local`: `appsettings.Local.json` (gitignored) + `.env` för frontend; committade defaults i `appsettings.Development.json`
- permanent miljö (netcup): self-managed på VPS (systemd-credentials / sops+age, ADR 0050 + [#196](https://github.com/klasolsson81/jobbliggaren/issues/196)); master-nyckel aldrig plaintext-på-disk ([#198](https://github.com/klasolsson81/jobbliggaren/issues/198))
- `IConfiguration`-abstraktionen gör att koden är identisk oavsett källa; endast DI-registreringen skiljer

### 13.3 GDPR-flöden

**Registerutdrag (Art. 15):**
- `GET /api/v1/me/export` genererar ZIP med alla användardata som JSON + originalfiler
- Delivered via signerad nedladdnings-URL, giltig 24 h (lagring på netcup-lådan / EU-storage, ADR 0050 `Amendment 2026-08-04`/0122)
- Loggas som `DataExportRequestedEvent`

**Rätt till radering (Art. 17):**
- `DELETE /me` sätter `deleted_at` på alla aggregat
- 30-dagars restore-fönster
- Hard delete-job rensar efter 30 dagar
- Härledda CV-artefakter (parsad `ResumeContent`, granskningsresultat, match_scores) raderas samtidigt
- Audit log behålls i 90 dagar (rättslig grund)

**Dataportabilitet (Art. 20):**
- Export i strukturerad JSON + DOCX för CVs

**Samtyckeslog:**
- Alla samtycken (TOS, privacy) sparas i `user_consents` med version + timestamp

### 13.4 Subprocessors

Upprätthålls i publik lista på `/integritet#subprocessors` (publiceras när
permanent infra aktiveras; listan nedan speglar **beslutad** uppsättning, ADR 0050):
- Infrastruktur (hosting/databas): **netcup GmbH** (Emmy-Noether-Straße 10, 76131 Karlsruhe,
  Tyskland — HRB 705547 Amtsgericht Mannheim), server i **Nürnberg, Tyskland** — inom EU/EES
  (ADR 0050 `Amendment 2026-08-04` / ADR 0122; ersätter Hetzner Cloud, Klas-beslut 2026-08-04).
  **Ingen Kap. V-överföring** införs av värdbenet: avtalsparten är tysk och behandlingen sker i
  Tyskland, så den krok som fällde e-postposten i AWS-eran — EU-avtalspart under **amerikansk**
  koncernmoder — saknas här (`security-auditor` 2026-08-09). *(Kontrasten är historisk sedan
  2026-08-15: e-postposten saknar numera samma krok, av samma strukturella skäl — ADR 0131. Det
  som frikänner värdbenet är oförändrat; det är jämförelseobjektet som bytts ut.)* **Underbiträdeskedjan var OMÄTT till 2026-08-16 och är det inte längre.**
  netcup publicerar ingen lista (mätt 2026-08-09 mot DPA-sidan, AVV-sidan, Impressum och
  DC-sidan) — den bor i AVV-bilagan och blev läsbar när avtalet tecknades. **ANNEX 2 namnger tre
  underbiträden, samtliga inom EU** (två Klagenfurt AT, ett Karlsruhe DE), så kedjan **får** nu
  påstås ligga inom EU/EES. ⚠ **Med en namngiven gräns:** kollokations-datacentren är
  **onamngivna** i avtalet, på netcups eget ställningstagande att de inte är biträden — det är en
  gräns för mätningen, inte ett mätresultat, och skriv aldrig om detta till "kedjan är fullständigt
  kartlagd". Tystnad om kedjan vore laglig ändå (Art. 13(1)(e) kräver mottagare eller kategorier,
  inte biträdets egen lista); ett **falskt** påstående om den vore det inte.
  ✅ **AVV:t är tecknat 2026-08-03.** Mekaniken står kvar som beskrivning och är fortfarande sann
  om **hur** avtalet sluts: netcups AVV gäller **inte** automatiskt utan sluts i Customer Control
  Panel, och den mätningen får aldrig generaliseras från e-postleverantörernas DPA:er — **både
  AWS-erans och Scaleways gäller automatiskt**, så netcup var undantaget bland biträdena och inte
  regeln. Grind: `release-checklist.md` §2.6 punkt 3, som är **flervillkorad** — signaturen är ett
  led av flera, och bockstatus läses där.
- Backup: **mekanismen är byggd och målet är valt och mätt (2026-08-09); Art. 28-avtalet är INTE tecknat** — ägs av
  [#197](https://github.com/klasolsson81/jobbliggaren/issues/197) (Hetzner-EU Storage Box föll med
  värdbytet). **Det här är kravens enda hem.** Kraven består oförändrade: klient-side
  age-kryptering före upload oavsett mål · EU-jurisdiktion · retention/rotation **30 dagar**
  (Klas-beslut K4) · testad restore-drill · ett mål vars **feldomän är oberoende av både lådan och
  operatörens arbetsstation** · och age-**privatnyckeln** aldrig lagrad tillsammans med
  ciphertexten (de två sista: `security-auditor` 2026-08-09, ur ADR 0050 `Amendment 2026-08-04`
  §7). **ADR 0125 (2026-08-09) urladdar dem** med en nattlig splittad dump — huvudartefakt utan
  `user_data_keys`-innehåll plus en separat DEK-artefakt — och binder ett **kravprofil-mål**
  (S3-kompatibelt, server-side lifecycle, credential utan `DELETE`, EU, **annan leverantör OCH
  annat konto än Netcup**). **MÅLET ÄR VALT OCH MÄTT 2026-08-09:** OVHcloud Object Storage,
  container `jobbliggaren-backups`, region `eu-west-par` (Paris), med **provider-verkställd**
  30-dagarsregel scopad till `main/` (en tidsregel över `deks/` hade kunnat radera nycklarna före
  ciphertexten). Versionering och Object Lock **av** — Klas-beslut, enklare och strikt starkare
  för en-generationsegenskapen; priset är att Object Lock är permanent stängt på containern.
  ⚠ **Två krav i profilen är INTE uppfyllda, båda Klas:** **Art. 28-avtalet är inte tecknat**
  (konto och credits är inget biträdesavtal), och **credentialen KAN radera** (mätt). Det senare
  är **reparerbart** — en OVH **user policy** med explicit `Deny` på `s3:DeleteObject`; explicit
  deny hedras även för en ägare, det är bara *implicit* deny som inte gör det. Skriptet utfärdar
  inget delete-verb alls, så inget går sönder. **Ingen av dem är applicerad.**
  **Klas accepterade 2026-08-09 restexponeringen där en återställning från en artefakt äldre än
  en raderingsbegäran återuppväcker användaren** — daterat, och registrerat här därför att båda
  dess andra hemvister är gitignorerade och ett accepterande som bara finns i osynliga filer är
  inget accepterande.
  `docs/runbooks/backup-restore.md`. **Tre grindar kvar och alla är Klas:** **Art. 28-avtalet** med
  OVHcloud (målet är valt och mätt, avtalet är inte tecknat), **credentialens `DELETE`** — mätt att
  den finns, reparerbar med en OVH user policy med explicit `Deny` (rad 27d) — och **escrow av
  age-privatnyckeln**: en backup vars nyckel inte är escrowad är ingen backup (samma grind som
  masternyckelns, `vps-deploy-stack.md` §5 rad 26 respektive 32).
- DNS / CDN / proxy: **utgår helt** (Klas-beslut K3 2026-08-04, ADR 0050 `Amendment 2026-08-04` §3).
  Ingen CDN, ingen edge-proxy; Caddy terminerar TLS direkt mot Let's Encrypt och DNS ligger hos
  Strato. **Strato är inte ett personuppgiftsbiträde:** en auktoritativ DNS-operatör publicerar vår
  zon och tar inte emot registrerades uppgifter för vår räkning (Art. 4(8)). Cloudflare hade blivit
  biträde i egenskap av **proxy/CDN som terminerar användartrafik** — den funktionen har ingen
  efterträdare, så posten stryks utan ersättare i stället för att pekas om.
- Transaktionell e-post: **Scaleway S.A.S. (Paris, Frankrike — R.C.S. Paris 433 115 904)** via
  **Scaleway Transactional Email** i **`fr-par` (Paris)** — beslutad (Klas-val 2026-08-14/15;
  ADR 0131, #183 — ersätter Amazon SES, som föll när AWS permanent vägrade häva sandbox-läget,
  vilket i sin tur ersatte Resend, Inc. (USA); båda är helt ute), och **AKTIVERAD 2026-08-16 UTAN
  ATT §2.5-GRINDEN PASSERADES**: `Email:Provider` sattes till `Scaleway` på lådan medan led (a),
  (b), (c) och (e) alla bar KVAR. **Armen skickar skarpt** — mätt leverantörssidigt samma dag,
  `Processed 4 / Delivered 4`. Personuppgifter HAR alltså nått biträdet.
  *(Posten sa "planerad, ännu inte aktiverad … så ingen e-post lämnar systemet" till 2026-08-16 —
  `Email:Provider` defaultar fortfarande till `Console`, som i non-dev löser till `NullEmailSender`,
  men lådans `.env` sätter den och defaulten beskriver därför inte längre driften. Läs aldrig
  defaulten som ett driftläge.)* **Statusen på grinden själv står i `release-checklist.md` §2.5
  punkt 1 och är oförändrat KVAR** — den här raden säger vad som körs, aldrig om det fick köras.
  Gäller **all** utgående e-post, inte bara
  notiser: `EmailTemplates` har åtta sorter varav sex är kontolivscykel (bekräfta e-post,
  byta e-post, ändrad-e-post-avisering, konto-finns-redan, lösenordsåterställning,
  ändrat-lösenord-avisering). **Ingen tredjelandsöverföring — och det är en OMPRÖVAD fråga,
  inte en ärvd:** avtalsparten är fransk, behandlingen sker i Frankrike, och den *krok* som
  gjorde SES-posten till en Kap. V-fråga — en EU-avtalspart under en **amerikansk** koncernmoder
  som kan nå uppgifterna (Schrems II / EDPB Rec. 01/2020) — saknas i en kedja som är fransk hela
  vägen upp till Niel-familjens grupp (iliad Holdings årsredovisning 2024 §5.1–5.3).
  **Kap. V är därmed EJ TILLÄMPLIG i stället för uppfylld:** ingen SCC-grund, ingen adekvans,
  ingen DPF — inte för att de är avklarade, utan för att det inte finns någon överföring att
  grunda. TEM har dessutom **inga underbiträden** (leverantörens egen TEM-FAQ), och `fr-par` är
  produktens enda region; residensåtagandet vilar på **DPA Art. 11**, aldrig på DNS.
  ⚠ **Två förbehåll som hör till bedömningen:** `Scaleway US Corporation` (Chicago) finns
  **nedströms** i koncernen utan TEM-roll — påstå aldrig "ingen US-enhet i koncernen" — och
  **var leverantörens support-/driftpersonal har åtkomst ifrån saknar AVTALSRANG**. Leverantören
  påstår i sin TEM-FAQ att *"all data is hosted and processed entirely within the European
  Union"* (`scaleway.com/en/docs/transactional-email/faq/`, läst 2026-08-15), och under Art. 4(2)
  omfattar *processing* åtkomst, så meningen träffar frågan — men den
  är dokumentation, inte avtalstext, och binder inte som DPA Art. 11 gör. **Det var den meningen som
  skulle bekräftas skriftligt**, inte en lucka som ska fyllas från noll. ⚠ **Frågan är INTE längre
  schemalagd: brevet är struket och risken accepterad 2026-08-16 (ADR 0133).** Rangbristen står
  oförändrad — det som försvann är åtgärden. En artefakt **med avtalsrang** som bär samma mening
  stänger posten på egna meriter och kräver inget brev.
  **Läsningen är `security-auditor`s skärpning 2026-08-15/16 och statusens hem är
  `release-checklist.md` §2.5 punkt 1 led (b)** — den här posten refererar den, den avgör den inte. Överfört innehåll är
  mottagar-adressen och meddelandets innehåll (för notiserna
  **avslöjar** leveransen opt-in-faktumet, och `EmailTemplates` skriver det dessutom i klartext
  i själva kroppen — själva *flaggan* i vår DB överförs aldrig, men faktumet gör det).
  **Avtalsparten är HÄRLEDD ur avtalsvillkoren 2026-08-15** (GTS Art. 23 bestämmer entiteten ur
  faktureringsadressen; svensk adress → Scaleway S.A.S.), **inte avläst ur vårt eget konto** —
  en svagare mätform än SES-erans två API-svar, och skillnaden står utskriven i
  `release-checklist.md` §2.5 punkt 1 led (a), som är det ledets hem.
  Kräver före flippen
  flera led — **antalet och uppräkningen bor på ett ställe, inte här**:
  `docs/runbooks/release-checklist.md` §2.5 punkt 1 (avtalsledet = Klas, aldrig CC).
  **Karakteriseringen ovan är `security-auditor`s med Klas** — den ratificeras i led (b)/(e),
  och statusen läses där.
- **Ingen AI-subprocessor** (ADR 0071): produkten har ingen AI/LLM, så ingen CV-PII och
  ingen matchningsdata lämnar systemet till någon **AI-leverantör**, och det finns inget
  AI-relaterat tredjelands-transfer. CV-innehåll lämnar aldrig systemet alls. CV- och
  matchnings-motorerna är deterministiska och körs på egen infra. Notis-kropparna
  (jobbtitel, företagsnamn, grad-label) går till e-postleverantören per posten ovan — det är
  inte en AI-överföring, men det ÄR matchningsdata, så ledet får inte skopas på dataklass.
- Google (Gmail/Calendar, frivilligt, global)
- Sentry (errors, EU) — planerat
- PostHog self-hosted (analytics, EU — inte subprocessor)

> AWS (infrastruktur + SES) var avvecklat (ADR 0066) och utgick ur subprocessor-kedjan.
> **SES-halvan av det påståendet är upphävd (Klas-direktiv 2026-08-02, verkställt i ADR 0124
> / #1237): AWS-*infrastrukturen* förblir avvecklad, men SES är tillbaka som e-postleverantör.**
> Att Resend var SES:s ersättare gällde mellan 2026-06-24 och 2026-08-08 och gäller inte längre;
> Resend är helt borttaget ur lösningen.
>
> **E-POSTPOSTEN ÄR OMSKRIVEN 2026-08-09 (#1169) och tredjelandsfrågan är AVGJORD, inte öppen** —
> `security-auditor` 2026-08-08: överföringen redovisas, grunden är SCC Art. 46(2)(c), adekvans
> och DPF är strukna. Den publika copyn på `/integritet` är omskriven i samma ändring, och
> ROPA-posten är ombunden till behandlingen *"Utgående transaktionell e-post"* (samtliga
> e-postmallar). **Kvar hos `security-auditor` + Klas:** sign-off på prod-e-post-konfigen och
> bekräftelsen av avtalsledet — se §2.5 punkt 1, som är uppräkningens hem.
> Release-checklistan §2.5 punkt 5 tvingar fortfarande denna sektion vid **e-postflippen**;
> denna ändring var en motpartskorrigering, inte flippen.
>
> **VÄRDPOSTEN ÄR OMSKRIVEN 2026-08-09 (#1199) och Kap. V-frågan för värdbenet är AVGJORD** —
> `security-auditor` 2026-08-09: **ingen** tredjelandsöverföring införs, och copyn ska därför vara
> **tyst** om Kap. V för värden, till skillnad från e-postposten. Skillnaden är strukturell och inte
> en gradskillnad: AWS-domen vilade på **vem som kan nå uppgifterna** (EU-avtalspart under
> amerikansk koncernmoder ⇒ Schrems II / EDPB Rec. 01/2020), och den kroken saknas när avtalsparten
> är tysk och behandlingen sker i Tyskland. **Cloudflare-posten är struken utan efterträdare**
> (Klas-beslut K3) — listan tappar en leverantör i stället för att byta en. Den publika copyn på
> `/integritet` skrevs om i samma ändring, ROPA-posterna följde med, och `content-legal-parity.test.ts`
> pinnar sedan dess att `netcup GmbH` är namngiven i båda språken **och** att raden inte bär
> status-markören.
>
> **E-POSTLEVERANTÖREN ÄR BYTT IGEN 2026-08-15 (#183, ADR 0131), OCH DEN HÄR GÅNGEN ÄNDRAS
> KAP. V-SVARET, INTE BARA PARTEN.** AWS vägrade 2026-08-14 permanent att häva sandbox-läget —
> 200 mejl per dygn och enbart till verifierade mottagaridentiteter, vilket gör riktiga
> testanvändare omöjliga — så SES-spåret avslutades och Klas valde **Scaleway Transactional
> Email i `fr-par`**. Konsekvensen för lagren ovan: **SES-halvan av 2026-08-02-upphävandet är i
> sin tur upphävd** (AWS-*infrastrukturen* förblir avvecklad, och nu är även AWS-e-posten det),
> och **SCC-domen från 2026-08-08 är historik över en aldrig aktiverad överföring** — armen var
> mörk hela sin livstid och ingen personuppgift nådde någonsin SES. ⚠ **Läs därför
> värdpostens kontrast ovan som daterad:** *"till skillnad från e-postposten"* var sant
> 2026-08-09 och är det inte längre — e-postposten saknar sedan ADR 0131 samma krok som värden
> saknar, så de två är numera parallella i stället för motsatta. Den strukturella regel stycket
> uttrycker — att Kap. V utlöses av **vem som kan nå uppgifterna**, inte av bytenas plats — är
> oförändrad — och den friar värden, medan e-postposten står på ett **oratificerat utkast** mot
> samma regel (led (b); rekvisitets andra led är öppet). §13.4:s e-postpost, `/integritet`-copyn sv+en
> och ROPA-posten är omskrivna i samma ändring; `content-legal-parity.test.ts` är omriktad från
> AWS-formen till `Scaleway SAS`. **Kvar hos `security-auditor` + Klas:** ratificeringen av att
> Kap. V är **ej tillämplig** (inte "uppfylld") samt sign-off — se §2.5 punkt 1 led (b)/(e).
> Release-checklistans §2.5 punkt 5 tvingar fortfarande denna sektion vid **e-postflippen**;
> inte heller denna ändring var flippen.
>
> **Biträdesavtalet är tecknat 2026-08-03** (uppgiften bor i ROPA:ns värdpost). #1199 står kvar
> öppen på sina **övriga** led — netcups AVV gäller **inte** automatiskt (mätt förstahands
> 2026-08-09), vilket var skälet att den inte gällde av sig själv. Första riktiga datan grindas
> fortfarande av `release-checklist.md` §2.6 punkt 3, som är flervillkorad. Se §15:s not.

### 13.5 Säkerhetshygien

- `dotnet-outdated` + `npm audit` körs i CI, bryter build vid kritiska CVEs
- Secrets aldrig i kod — allt via managed secrets-store eller miljövariabler (lokalt: `appsettings.Local.json`, gitignored)
- `dotnet format` + ESLint/Prettier i pre-commit (Husky)
- Rate limiting per IP + per user på alla endpoints (AspNetCoreRateLimit eller custom middleware)
- CORS restriktivt: bara `jobbliggaren.se`-domäner
- CSP: strict, script-src 'self'
- Weekly dependency update via Dependabot

---

## 14. Observability

### 14.1 Logging

- `Microsoft.Extensions.Logging` — strukturerad loggning; console (stdout) + Seq-sink
- Sinks: console (stdout) + persistent strukturerad **Seq**-sink via `Seq.Extensions.Logging` (MEL-provider, config-gated på `Seq:ServerUrl`); dev lokal Seq (`localhost:5341`), dev-sinken levererad under TD-104
- **Prod är TVÅ mekanismer med olika ändamål, och de får inte läsas som en** (ADR 0128):
  - **Sökbarhet** — Seq som compose-tjänst på produktionslådan, `mem_limit: 512m` (den **mätta**
    konfigurationen — Seq dimensionerar sin cache mot cgroup-gränsen, så ett högre tak är en annan,
    omätt konfiguration och inte marginal. Talen och instrumentet som återskapar dem har **ett**
    hem: `docs/runbooks/log-sink.md` §4).
    Ingen publicerad port; `Seq:ServerUrl` pekar på ingest-lyssnaren `5341`, vilket håller
    query-API:t utanför appens **konfiguration**. **Det är inte nätverksisolering** — mätt
    2026-08-11 når en syskoncontainer `seq:80` (200), eftersom containrar på samma user-defined
    bridge når varandra som default. Det som faktiskt håller är att query-API:t på 80 svarar
    **401** utan autentisering och att 5341 bär **404** på query-vägen: försvaret är autentisering,
    inte topologi. Retention: en policy, satt **för hand** inne i Seq (`log-sink.md` §3 steg 7),
    aldrig i konfiguration — Seq har ingen yta för den. **Varaktigheten står avsiktligt inte här:**
    ett tal som bor på två ställen förfaller på det ena. Den bor i `log-sink.md` §3 steg 7, i
    curl-anropet som sätter policyn; om policyn *är* satt på en given låda är en annan fråga och
    mäts i samma fils §4.
  - **Varaktighet** — `jobbliggaren-logship`, timrad off-box-arkivering krypterad med `age` till en
    mottagare lådan inte kan dekryptera. Bär journal + auditd + app-loggar. Detta, och inte Seq, är
    kopian som är avsedd att överleva en root-angripare — **och den egenskapen är inte i kraft**
    förrän verifikationsrad 27d:s `Deny s3:DeleteObject` är applicerad.
- **Tre lager är TÄNKTA att hålla app-events, och de två åldersgränserna är mekanismer — inte
  tillstånd:** Seq (en handsatt policy), off-box-arkivet (en lifecycle-regel), och Dockers
  `json-file` som är **volymbunden och åldersobunden** och därför aldrig får en åldersgräns av sig
  själv. Vilka av de tre som faktiskt bär något på en given låda, och vilka gränser som är i kraft
  där, är en mätning med ett hem: `docs/runbooks/log-sink.md` §4. En tidigare formulering här
  räknade Seq och arkivet som *åldersbundna* rakt av; det var ett påstående om driftläge som den
  här filen inte kan bära — [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170)
  stängs inte av detta
- Log levels:
  - `Trace`/`Debug`: dev only
  - `Information`: normala request-flows (start/slut av handlers)
  - `Warning`: validation failures, rate limits, degraded dependencies
  - `Error`: exceptions, failed external calls (JobTech, Gmail, SCB)
  - `Critical`: crashing errors
- Alla logs har `CorrelationId`, `UserId`, `OperationType`
- Känslig data (CV-innehåll, parsad CV-text) loggas **aldrig** i klartext

### 14.2 Traces

- OpenTelemetry (exporter/backend definieras med observability-sinken, [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175))
- Trace från frontend genom backend till DB/external (JobTech, Gmail, SCB)
- Sampling: 100% i dev, 10% i prod

### 14.3 Metrics

- `http.request.duration`, `.count`, `.error_rate`
- `cv.review.duration`, `match.compute.duration` (deterministiska motorer)
- `jobtech.sync.duration`, `.new_ads`, `.errors`
- `application.status_change.count` per transition
- Exposeras på `/metrics` för Prometheus-format (om vi behöver senare)

### 14.4 Alerting

Alarms (plattform med observability-sinken, [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175); larmen själva parkerade i [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172)).

⚠ **Den externa uptime-monitorn är INTE UptimeRobot eller BetterStack.** Den här raden namngav
båda fram till 2026-08-11, och **ADR 0126 avvisade båda på jurisdiktion** — de är US-registrerade,
och att välja någondera vore en supersession av ADR 0122:s "US-part ur kedjan", inte ett
leverantörsval. Det som faktiskt kör är **Healthchecks.io** (SIA Monkey See Monkey Do, Lettland;
Hetzner, Tyskland), som dead-man plus `/fail`-verb, installerad på lådan 2026-08-10
([#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201)). **"Installerad" är lådsidan,
och expecter-sidan är ett eget led:** `host-detection.md` §7:s rader för expectern bär ännu inga
datum. Att pingen når fram, att `/fail` sidar och att larmet självläker mättes vid expectern
2026-08-11 under en verklig incident och är protokollfört på #1201 — dead-man-armen (D5) och
nyttolastraden är fortfarande omätta. Larmen nedan är fortfarande parkerade och ingenting nedan är
byggt:
- Backend 5xx rate > 1% över 5 min → email
- JobTech sync misslyckas 3 gånger i rad → email
- Databas CPU > 80% i 10 min → email

### 14.5 Product analytics (PostHog)

- Self-hosted PostHog i EU (placering på/bredvid Hetzner-infra, ADR 0050)
- Auto-capture off, explicit event-tracking
- Events: `job_searched`, `application_submitted`, `cv_reviewed`, `cv_improved`, `cliche_detected`, `match_computed`, etc.
- Session recording av för integritet (kan slås på per användare via admin-flag)
- Feature flags via PostHog

---

## 15. Infrastruktur & deployment

> **Status (2026-06-08):** Den AWS-baserade deploy-arkitekturen är **avvecklad**
> (ADR 0066) och AWS lämnas permanent. Permanent deploy-mål — **Hetzner Cloud
> CAX31 (ARM, 16 GB) all-in-one Docker Compose (**BE + FE**) + Cloudflare
> (DNS/CDN/proxy)** — är **beslutat i ADR 0050 (Accepted 2026-06-08)** och
> beskrivs nedan. Faktisk provisionering är framtida Klas-gatat arbete (ADR 0050
> Sekvensering: Hetzner sist, vid MVP före beta-testare, med samtliga
> Pre-beta-data-gates lösta + andra security-granskning först).
>
> `infra/terraform/` (den tidigare AWS-stacken) + `deploy-dev.yml` refererar **avvecklad infra** —
> den AWS-baserade dev-stacken (ECS/ECR/RDS/Redis) är **riven** 2026-05-26
> (ADR 0066, commit `a1d9abd`), inte pausad; bara prod-baseline (~$2/mån: Route 53,
> KMS, CloudTrail, IAM) kvarstår. Filerna är **bevarade som reversibilitets-/
> historik-mekanik** (ADR 0066 Beslut 1 + ADR 0069 D3) och retireras via egen
> teardown-ADR/PR vid Hetzner-cutover, inte i en städ-PR. `deploy-dev.yml`:s
> auto-trigger (`push: tags v*-dev`) är **borttagen** 2026-06-28 så den inte kan
> köra mot riven infra; endast manuell `workflow_dispatch` kvarstår.
>
> **#808 (2026-07-25):** `rds-ca-bundle-check.yml` är **raderad**. Till skillnad från
> `deploy-dev.yml` var den inte passiv historik utan ett **aktivt månadsjobb** (cron
> `0 3 1 * *`, `issues: write`) som hämtade en AWS-upstream-URL och **öppnade ett
> GitHub-issue** vid drift — mot en CA-bundle för en RDS-instans som revs 2026-05-26.
> Den kunde alltså fila spöken i backloggen. `infra/certs/rds-global-bundle.pem`
> BEHÅLLS: alla tre Dockerfiles `COPY` den fortfarande, och `.dockerignore` bär
> avsiktliga negationer för att släppa igenom den. Dess borttagning har egen ägare
> (#196, Hetzner-image). `infra/terraform/` är **orörd** — den retireras via
> egen teardown-ADR/PR enligt stycket ovan, inte här.
>
> **Not om issue-länkarna (2026-08-02, PR #1173).** `TD-NNN`-markörer som pekade in i
> det retirerade TD-registret är utbytta mot de issues som äger arbetet — i **§3, §7,
> §13, §14 och §15**, inte bara här ([#196](https://github.com/klasolsson81/jobbliggaren/issues/196),
> [#197](https://github.com/klasolsson81/jobbliggaren/issues/197),
> [#198](https://github.com/klasolsson81/jobbliggaren/issues/198),
> [#183](https://github.com/klasolsson81/jobbliggaren/issues/183),
> [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175)).
>
> **En länk säger var arbetet ägs — inte att premissen omkring den är aktuell.** Två
> premisser är **upphävda** av Klas-direktiv 2026-08-02 — i detta kapitel **och i §3.2**
> (e-postpremissen i §3.2:s **Email**-rad; värdpremissen i statusbannern och i raderna
> för Compute, Database, Cache, Frontend, DNS och Backup, varav flera är celler denna
> PR redigerade — **radnummer står medvetet inte här: den förra versionen av denna
> mening bar ett, och det bröts av samma commit som skrev det**). Uppräkningen är
> **inte uttömmande** — den namnger var premisserna är mest lästa, inte varje
> förekomst. Inte bara under
> omprövning: **värdvalet** (Hetzner ut; **och "svensk VPS" i sin tur återkallat
> 2026-08-04 på pris/prestanda — ersättaren är VALD: Netcup RS 1000 G12, 8 GB, ingen CDN**,
> bärs av ADR 0050 `Amendment 2026-08-04`) och **e-postleverantören** (Resend ut; **och AWS SES i
> sin tur ute 2026-08-15 sedan AWS permanent vägrat häva sandbox-läget — ersättaren är VALD:
> Scaleway Transactional Email i `fr-par`**, ADR 0131, som supersederar ADR 0124).
>
> **E-POSTHALVAN ÄR SEDAN 2026-08-09 (#1169) INTE LÄNGRE EN ÖPPEN FRÅGA** — stycket som beskrev
> den som öppen är struket ur meningen ovan. Frågan var: faller en US-**ägd** leverantör i EU-region under
> samma standard som §15.1 tillämpar när den avvisar Cloudflare R2 *"pga CLOUD
> Act-tredjelandsöverföring **av icke-krypterad pg_dump-PII**"*? `security-auditor` avgjorde
> den 2026-08-08 för e-postens del: **ja** — överföringen redovisas trots `eu-north-1`,
> eftersom en standard som tillämpas selektivt inte är en standard (Art. 5(2)), och grunden är
> **SCC Art. 46(2)(c)**, inte adekvans och inte DPF. Detaljerna bor i §13.4:s e-postpost, som är
> omskriven; den publika `/integritet`-copyn och ROPA-posten ändrades i samma ändring.
> **Domen är skopad till e-posten** och avgör ingenting om värdvalet eller om R2.
>
> **VÄRDHALVAN ÄR SEDAN 2026-08-09 (#1199) OCKSÅ AVGJORD, och svaret blev det motsatta mot
> e-postens.** `security-auditor` 2026-08-09: värdbenet införer **ingen** tredjelandsöverföring, så
> den frågan §15 ställde — faller en US-**ägd** leverantör i EU-region under samma standard? — når
> inte netcup alls. Avtalsparten är **tysk** (`netcup GmbH`, HRB 705547 Amtsgericht Mannheim) och
> behandlingen sker i **Nürnberg**; ingen amerikansk part är i kedjan, och därmed finns ingen
> selektivitet att pröva. **R2-avvisandet står orört och ska inte städas bort** — det är den
> tillämpade standard som gör AWS-redovisningen icke-selektiv (Art. 5(2)), och den behövs lika
> mycket nu som före värdbytet. Värdraderna i §13.4:s subprocessor-lista, §13.2, §13.3 och §15.1:s
> värd/kant/backup är omskrivna i samma ändring; **kapacitetsprosan är medvetet orörd** och ägs av
> [#1264](https://github.com/klasolsson81/jobbliggaren/issues/1264). Skärlinjen är **substitution
> mot beslut**: ett leverantörsnamn vars ersättare redan är ratificerad byts, ett tal som 8 GB mot
> 16 GB ändrar gör det inte — det senare kräver siffror ADR 0122 äger.
>
> **E-POSTHALVANS SVAR ÄR I SIN TUR ÖVERSPELAT 2026-08-15 (#183, ADR 0131) — och det är
> STANDARDENS UTFALL, inte ett undantag från den.** AWS SES föll när production access nekades
> permanent; ersättaren är **Scaleway S.A.S.** via Transactional Email i `fr-par`. Frågan §15
> ställde — faller en US-**ägd** leverantör i EU-region under R2-standarden? — når därmed inte
> längre e-posten heller, av exakt samma skäl som den aldrig nådde netcup: avtalsparten är
> **fransk**, behandlingen sker i **Paris**, och ägarkedjan är fransk hela vägen upp till
> Niel-familjens grupp. **Rekvisitet är tvåledat — se §15.1 — och dess FÖRSTA led är negativt här:
> ingen tredjelandsenhet kan rättsligt förmås att producera uppgifterna. Det ANDRA ledet, om någon
> faktiskt kan nå dem, är OAVGJORT för Scaleway** (`security-auditor` delratificerade 2026-08-15/16;
> `release-checklist.md` §2.5 punkt 1 led (b) är statusens hem) (⚠ `Scaleway US Corporation` finns
> **nedströms** i koncernen utan TEM-roll. Det utlöser
> inte rekvisitet: kontroll flödar nedåt, så en fransk moder kan inte föreläggas via ett
> amerikanskt dotterbolag. Men påståendet "ingen US-enhet i koncernen" är mätt **falskt** och får
> inte skrivas). **SCC-domen från 2026-08-08 står orörd som dom över sin
> egen era och sin egen part** — den var korrekt, den redovisade en överföring som aldrig
> aktiverades, och den ska inte städas bort. Vad som ändras är att e-postposten sedan 2026-08-15
> är **tyst** om Kap. V i copyn, precis som värdposten och backup-posten är det. **En leverantör
> prövad och friad, två tysta på oavgjord grund, och en historisk post som rekvisitet nådde
> redovisade — det är vad icke-selektivitet SER UT SOM**, inte ett tecken på att standarden
> slutat gälla. ⚠ **Tystnad i copyn är inte detsamma som en avgjord dom:** **netcup** är prövad och
> friad (`security-auditor` 2026-08-09), medan **OVHcloud och Scaleway båda står på oavgjord grund** —
> OVH:s **andra led är OPRÖVAT** — hon avstod uttryckligen från koncernstruktur, underbiträdeskedja
> och supportåtkomst och lämnade slutsatsen **obelagd, inte falsk**. *(Diskriminatorn är avståendet,
> inte kedjan: netcups kedja var också omätt när detta skrevs och stod ändå i den friade kolumnen,
> **uppskjuten till AVV-bilagan med en namngiven omprövningsutlösare** — ett icke-EU-underbiträde
> hade tvingat omprövning **före** korpusladdningen. ⚠ **Bilagan är läst sedan 2026-08-16 och
> utlösaren fyrade inte:** ANNEX 2 namnger tre underbiträden, samtliga inom EU, så netcups kedja är
> inte längre omätt (§13.4 bär uppgiften med sin gräns). OVH:s andra led är fortfarande oprövat —
> hon avstod från hela ledet.
> Vad hon förklarade oväsentligt för netcup-slutsatsen var **ägandet**, inte kedjan.)* Och Scaleway står på ett utkast
> `security-auditor` **delratificerat**: strukturen avgjord, slutsatsen inte, eftersom
> den FAQ-mening som besvarar frågan om leverantörens support-geografi saknar avtalsrang
> (`release-checklist.md` §2.5 punkt 1 led (b) är
> statusens hem). ⚠ **Raden sa "i väntan på svar" till 2026-08-16, och det svaret kommer inte:**
> brevet som skulle begära det är struket och risken accepterad (ADR 0133). **Posten är därmed
> oavgjord utan en åtgärd som väntar** — vilket gör den här paragrafens varning skarpare, inte
> mildare: en oavgjord post utan pågående utredning är exakt den sortens rad som med tiden läses
> som frikänd.
> Blanda aldrig ihop de tre lägena — §15.1 förbjuder uttryckligen att en oavgjord post skrivs som
> frikänd.
> Se §15.1, där rekvisitet är utskrivet och R2-meningens formulering omankrad i samma ändring.
>
> **Vad #1199 bar: biträdesavtalet med netcup — tecknat 2026-08-03.** Det var issuens femte
> acceptanskriterium, det blockerande och Klas-ägda, och DPA:t har ingen egen issue. #1199 är
> **bredare** än avtalet (policy-copy, ROPA, `BUILD.md`, paritetstestet) och står kvar öppen på
> dem. Ingen supersessions-ADR blev skyldig — ADR 0050 `Amendment 2026-08-04`
> hade redan landat och bär värdbeslutet.

### 15.1 Deploy-layout (ADR 0050, Accepted)

**Backend — en netcup RS 1000 G12** (x86 AMD EPYC 9645, 4 dedikerade kärnor / 8 GB DDR5 ECC
/ 256 GB NVMe, Debian 13, Nürnberg). Hela backend-stacken kör i **Docker Compose** på boxen:
.NET API + .NET Worker + PostgreSQL (co-tenant container, ingen managed-DB) + Redis + **Caddy**
(reverse proxy, auto-TLS via Let's Encrypt **direkt**, HTTP-01/TLS-ALPN-01 — ingen DNS-01 och
ingen CDN, Klas-beslut K3). **`mem_limit` sätts på varje tjänst, Postgres inklusive** — den
tidigare hybrid-doktrinen ("generös/osatt på Postgres") vilade uttryckligen på att 16 GB löste
nollsummespelet och är superseded av ADR 0050 `Amendment 2026-08-04` §1; kapacitetstabellen och
de fyra villkoren bor i ADR 0122, inte här ([#196](https://github.com/klasolsson81/jobbliggaren/issues/196)).

**Frontend — Next.js co-tenant container på netcup-lådan.** FE körs som en `next start`-container i samma Compose-stack bakom Caddy (ADR 0050 Beslut 3, amenderad 2026-06-14). `next build` körs i CI; endast den färdiga imagen shippas till boxen (build-toppen belastar aldrig RAM-feldomänen) — det är sedan ADR 0122 **kapacitetsvillkor 1** och därmed lastbärande, inte bara bekvämt. FE-footprint (~0,5 GB under last) ryms i CAX31:s headroom.

**Edge — ingen.** Cloudflare utgår helt (Klas-beslut K3, ADR 0050 `Amendment 2026-08-04` §3): ingen
CDN, ingen TLS-edge, ingen DDoS-absorption och **ingen efterträdare till origin-IP-lockdown** —
kantfiltret hos netcup är allt som finns. DNS ligger hos Strato. Caddy terminerar TLS direkt och
reverse-proxiar två upstreams (API på port 5000 + `next start`-FE på localhost:3000 för
icke-`/api`-vägar). **80/443 står öppna mot `any` i båda brandväggslagren — det krävs för ACME och
får inte "rättas" mot gate M-5:s ursprungstext.** M-5 är pensionerad på plats → **M-5a** (HSTS
faktiskt emitterad i Production, bevisad på det **oautentiserade 401-svaret**) + **M-5b**; se
ADR 0050 `Amendment 2026-08-04` §5.

**Backup — mekanismen levererad (ADR 0125), målet valt och mätt 2026-08-09; ägs av
[#197](https://github.com/klasolsson81/jobbliggaren/issues/197).** Hetzner-EU Storage Box föll med
värdbytet; ersättaren är bunden som **kravprofil**, inte som leverantör. **Kraven räknas inte upp
här — de har ett enda hem, §13.4:s backup-post**, och en andra uppräkning hade blivit ett andra hem
som ruttnar isär från det första. Backups ligger INTE på lådans disk. **Cloudflare R2 är fortsatt
avvisat pga CLOUD Act-tredjelandsöverföring av icke-krypterad `pg_dump`-PII** — den meningen står
kvar med avsikt: den är **den tillämpade standard som avgör NÄR en Kap. V-redovisning krävs**, och
en standard som tillämpas selektivt är ingen standard (Art. 5(2)).
**Omankrad 2026-08-15 (#183, ADR 0131), och skälet är att dess tidigare formulering pekade på en
redovisning som inte längre finns:** meningen sa att standarden gjorde *§13.4:s AWS-SCC-redovisning*
icke-selektiv, men e-postposten bär sedan providerbytet ingen SCC-redovisning alls — biträdet är
franskt och ingen överföring uppstår. **Standarden är oförändrad; det är dess utfall som varierar,
och det är precis vad som gör den till en standard.**
**REKVISITET, utskrivet en gång så att nästa läsare kan tillämpa det i stället för att jämföra mot
prejudikat** (`security-auditor` Major 3, 2026-08-15): **finns det en enhet i tredjeland som
rättsligt kan förmås att producera uppgifterna, eller som faktiskt kan nå dem?** Ägarriktningen är
ett **symptom** av det, inte regeln — det som gjorde AWS-moderns existens rättsligt relevant var att
**kontroll flödar nedåt** (en amerikansk moder kan föreläggas producera vad dess EU-dotterbolag
håller i *"possession, custody or control"*; ett amerikanskt dotterbolag kan inte föreläggas
producera sin franska moders uppgifter). **Räckvidden är rekvisitet.** Utan den satsen är nästa
koncernstruktur — en US-enhet **sidledes**, med delade drifttjänster — oavgörbar mot texten.
Meritlistan, tillämpningar åt båda hållen: den **fällde** R2 (US-ägt mål för okrypterad PII) och
e-postposten i AWS-eran (EU-part under amerikansk moder — redovisad med SCC 2026-08-08 till
2026-08-15, se §13.4:s historiklager); den **friar** netcup (`security-auditor` 2026-08-09); och
**två fall är oavgjorda och skrivs aldrig som frikända**: **OVHcloud** — `security-auditor`
2026-08-09 avgjorde **första** ledet positivt (fransk avtalspart, ingen tredjelandsmoder,
`eu-west-par` mätt) och **avstod uttryckligen** från koncernstruktur, underbiträdeskedja och
supportåtkomst, alltså rekvisitets andra led; hennes egen rapport bär dessutom motbevisning
(en kanadensisk koncernenhet i OVH:s underbiträdeslista — Kanada bär adekvansbeslut, så den enheten
är inte i sig en krok; det är kedjans oprövade skick som är det — och OVH:s DPA beskriver sig som
**dataexportör med SCC:er** mot dotterbolag utan adekvansbeslut). *Klientsidig kryptering
frikänner inte — den är en **kompletterande åtgärd** i EDPB Rek. 01/2020:s mening och
förutsätter att en överföring finns.* Och
**Scaleway**, som står på ett **utkast som ännu inte är ratificerat** — `security-auditor`
delratificerade 2026-08-15/16 den strukturella halvan men **inte** slutsatsen, som hänger på om
leverantörens support-/driftpersonal har åtkomst från tredjeland (`release-checklist.md` §2.5 punkt 1
led (b) är statusens hem). **Skriv det aldrig som ett frikännande innan ledet är stängt** — utan
ordinal, med flit: ett ordningstal här måste räknas om vid varje ändring i meritlistan och är
därmed sin egen driftgenerator.
En standard är icke-selektiv när den prövas varje gång dess rekvisit kan föreligga — **inte när
utfallet alltid blir detsamma**.

**Single-box blast-radius** (API/Worker/Postgres/Redis delar OS + RAM + feldomän)
är ett medvetet beta-skala-val (ADR 0050 Negativa konsekvenser); CAX31:s 16 GB +
per-service `mem_limit` ger headroom. NBomber-lasttest mot 46k-korpuset (ADR 0045)
körs före cutover för att validera sizing empiriskt.

> ⚠ **Stycket ovan sizear mot en låda vi inte har, och det är MEDVETET orört.** 16 GB, 46k-korpuset
> och FE-footprintens headroom är **tal**, inte leverantörsnamn — att skriva om dem kräver siffror
> ADR 0122 äger (korpuset är mätt till 106 071 annonser / 2 493 MB, och `mem_limit`-doktrinen är
> superseded). #1199 svepte **substitutioner**, aldrig beslut.
> Ägs av [#1264](https://github.com/klasolsson81/jobbliggaren/issues/1264).

Den tidigare AWS-layouten (VPC/ECS/RDS/ElastiCache/S3/Bedrock/Route 53) finns
dokumenterad i ADR 0066 + sessions som historik.

### 15.2 IaC (ADR 0050)

Befintlig AWS-Terraform under `infra/terraform/` bevarad som reversibilitets-
mekanik (ADR 0066 Beslut 1), retireras via egen ADR/PR vid Hetzner-cutover.
Hetzner-provisioneringen är compose-centrerad (en box, Docker Compose + Caddy);
VPS-härdnings-baseline (SSH-key-only, brandvägg, fail2ban, auto-patch, PG/Redis
ej publika, swap/core-dump-hygien) = gate M-6, hemvist [#196](https://github.com/klasolsson81/jobbliggaren/issues/196).

### 15.3 CI/CD

**Aktivt nu (PR-flöde per ADR 0065):**

`build.yml` (`ci`-aggregat):
- Trigger: PR mot `main`, push till `main`
- Jobs: backend build + test, frontend lint/typecheck/test, coverage-gate (ADR 0044)
- Inga moln-anrop, inga deploys

Observe-only-jobb (lighthouse / loadtest / audit per ADR 0045) blockerar ej merge.

**Historiskt (deploy — refererar avvecklad AWS-infra):**
Tag-baserad AWS-deploy (`deploy-dev.yml` m.fl.) refererar den **rivna** AWS-dev-stacken
(ADR 0066, 2026-05-26) — avvecklad, inte pausad; auto-triggern är borttagen 2026-06-28.
Ny deploy-pipeline mot **Hetzner** byggs vid cutover (ADR 0050: Compose-push till CAX31 — **FE-image byggs i CI (`next build`) och shippas som container**, ingen Vercel-build).

### 15.4 Deployment-strategi (ADR 0050)

Topologin är en **Netcup RS 1000 G12** (x86, 4 kärnor, 8 GB) som kör hela stacken
som en Compose-projekt-katalog, **utan CDN** — Hetzner och Cloudflare är av bordet
sedan Klas-beslut 2026-08-04 (ADR 0050 `Amendment 2026-08-04`, kapacitetstabellen i
ADR 0122). Kanten är Caddy med Let's Encrypt via HTTP-01 direkt; **Option B** gäller
oförändrat, alltså går all trafik genom Next och API:t är aldrig edge-exponerat.

Deploy-mekaniken i sin helhet — first-boot-ordning, migrationsgrind via
`Jobbliggaren.Migrate`, hälsokontroller, nftables-`forward`-deltat och
cutover-bevisen — står i [`docs/runbooks/vps-deploy-stack.md`](docs/runbooks/vps-deploy-stack.md);
den återupprepas inte här. Images byggs i CI och **aldrig på lådan** (kapacitetsvillkor
1); lådan hämtar dem ur GHCR.

**Rollback-modell:** image-tagg. Pinna föregående `sha-<short>` i boxens `.env` och kör
reconcile — sekunder. En Netcup-snapshot är **inte** deploy-rollback (copy-on-write,
kräver 50 % ledigt disk, endast offline-snapshots är konsistenta); dess roll är **före en
migration**, när riktig användardata väl finns. Health-check-kravet `/api/ready` → 200
inom 30 s består oavsett plattform.

---

## 16. Background jobs

### 16.1 Hangfire-setup

- Postgres-storage (`Hangfire.PostgreSql`)
- Dashboard på `/hangfire` skyddad med Admin-roll
- Dedicerad worker-process (separat från Api — egen container i Compose-stacken på Netcup-lådan, ADR 0050 + dess `Amendment 2026-08-04`)

### 16.2 Schedulerade jobb

> Speglar `src/Jobbliggaren.Worker/Hosting/RecurringJobRegistrar.cs` (verifierat
> 2026-06-28). Cron-tider är UTC. 30-min-padding mellan natt-jobben ger tydliga
> recovery-fönster (se klassens XML-doc för rationalen).

| Jobb (Hangfire-id) | Schema (UTC) | Beskrivning |
|------|--------|-------------|
| `sync-platsbanken-stream` | `*/10 * * * *` | Pull JobStream (overlap-fönster 15 min), upsert/arkivera annonser |
| `sync-platsbanken-snapshot` | 02:00 daglig | Full snapshot-backfill mot stream-drift |
| `audit-log-retention` | 03:00 daglig | Atomisk partition-DDL, rullar 90-dagars audit-retention (ADR 0024) |
| `retain-platsbanken-job-ads` | 03:15 daglig | Snapshot-miss-retention (ADR 0032-amend) |
| `background-matching` | 03:20 daglig | Per-user matchnings-scan: JobAds → `UserJobAdMatch` (ADR 0080 Våg 4) |
| `expire-job-ads` | 03:45 daglig | `ExpiresAt`-cron, defense-in-depth (ADR 0032-amend) |
| `hard-delete-accounts` | 04:00 daglig | Permanent radera soft-deleted efter 30 dagar (GDPR Art. 17) |
| `purge-stale-raw-payloads` | 04:30 daglig | Rensa mognad `raw_payload`-jsonb (TD-73 p2) |
| `reap-stranded-matches` | 04:45 daglig | `UserJobAdMatch` fast i Queued → terminal Failed (TD-114) |
| `backfill-field-encryption` | 05:00 daglig | DEK-backfill av PII-fält (ADR 0049) |
| `parsed-resume-retention` | 05:15 daglig | GDPR-svep av mognad ParsedResume-staging (TD-111, ADR 0074) |
| `digest-dispatch-daily` | 06:00 daglig | Strong-match-digest, daglig kadens (ADR 0080 Våg 4 PR-4b) |
| `digest-dispatch-weekly` | måndag 06:00 | Strong-match-digest, veckovis kadens (civic-default) |
| `refresh-landing-stats` | `*/5 * * * *` | Publik landing-stats pre-compute (ADR 0064) |

**Planerat / ej registrerat** (speccat men inte byggt — finns inte i
`RecurringJobRegistrar`):

| Jobb | Avsedd funktion | Status |
|------|--------|-------------|
| `RunSavedSearchesJob` | Kör sparade sökningar → notiser | Ej byggt (#312) |
| `SendFollowUpRemindersJob` | Uppföljnings-påminnelser | Ej byggt (Fas 5) |
| `SyncGmailJob` | Per-user Gmail-sync | Ej byggt (Fas 5, #321) |
| `ImportScbSalaryStatsJob` | Månatlig SCB-lönestatistik | Ej byggt (#320, backlog) |

### 16.3 Fire-and-forget jobb

Triggas av handlers för:
- Skicka välkomst-email
- Generera exportfil efter export-request
- Skicka invite-email
- Uppdatera SCB-data när SSYK-kod ändras

---

## 17. Testing

### 17.1 Test-pyramiden

**Domain unit tests** (Jobbliggaren.Domain.UnitTests, ~70% av antalet tester)
- Aggregate-invariants, state machines, value objects
- Ingen databas, ingen I/O
- Använder xUnit + Shouldly (ersätter FluentAssertions efter dess kommersialisering 2025)
- Target coverage på Domain: **>90%**

**Application unit tests** (Jobbliggaren.Application.UnitTests, ~20%)
- Handlers mot in-memory fakes/mocks (NSubstitute)
- xUnit + Shouldly + NSubstitute
- Testar use case-logik utan Infrastructure

**Integration tests** (Jobbliggaren.Api.IntegrationTests, ~10%)
- Testcontainers för Postgres + Redis (ephemeral per test-klass)
- WebApplicationFactory
- Shouldly för assertions
- Happy-path + nyckel-felscenarion per endpoint
- Kör i CI som del av hela sviten, utan trait-filtrering. Invocation-formen har
  ett hem — `.github/workflows/build.yml` — och anropsreglerna ett: CLAUDE.md §7

**Architecture tests** (Jobbliggaren.Architecture.Tests)
- NetArchTest-regler:
  - Domain beror inte på Infrastructure/Application/Api
  - Application beror inte på Infrastructure
  - Alla endpoints har auth-attribute (eller explicit `[AllowAnonymous]`)
  - Alla aggregates ärver `AggregateRoot<>`

**E2E tests** (jobbliggaren-web-tests, Playwright)
- Kritiska användarflöden: registrera → skapa sökning → söka jobb → submit ansökan → logga follow-up
- Kör lokalt / i CI mot lokal stack (staging-miljön är avvecklad, ADR 0066)
- Max 15-20 tester (dyra att underhålla, håll tight)

### 17.2 Testdata

- `TestDataBuilder`-klasser per aggregate (fluent builder-pattern)
- Inga `.sql` seed-filer i tests — bygg data via builders för tydlighet
- Test-premisser styrs av CLAUDE.md §5 `Tests:` — en regel, ett hem

### 17.3 Motor-tester (deterministiska — ADR 0071)

Motorerna är deterministiska → testbara som vanlig kod (inga mockade
AI-provider-anrop, inga icke-deterministiska evals):

- **Granskningsmotor:** golden-set av svenska CV med förväntad per-kriterium-verdict
  (PASS/WARN/FAIL); samma input → samma output, assertas exakt
- **Personnummer-guard:** regex-tester (helt personnummer + fyra sista, positiva/negativa)
- **Matchningsmotor ("Fast mode"):** kända "bra fit"/"dålig fit"-par med förväntad
  score + att matchade/saknade nyckelord surfas korrekt (förklarbarhet)
- **Mall-rendering:** ATS-plain + visuell från samma JSON → snapshot/struktur-assertion
- **Rubric-versionering:** `rubric_version` lagras med varje bedömning; N-1-kompatibilitet
- Körs i CI (deterministiska → inga flakes, ingen separat manuell eval-körning)

---

## Bilaga A — Viktiga externa referenser

- JobTech Dev: https://jobtechdev.se / https://data.arbetsformedlingen.se
- JobSearch API docs: https://jobsearch.api.jobtechdev.se
- Taxonomy API: https://taxonomy.api.jobtechdev.se
- SCB Pxweb API: https://api.scb.se/OV0104/v1/doris/sv/ssd
- GOV.UK Design System: https://design-system.service.gov.uk
- Digg (svensk digital förvaltning): https://www.digg.se
- WCAG 2.1 AA: https://www.w3.org/TR/WCAG21/
- EU AI Act: https://artificialintelligenceact.eu

---

## Bilaga B — Arkitekturbeslut (ADRs)

ADR:er lagras i `docs/decisions/` och namnges `NNNN-slug.md`. Den **auktoritativa
listan (SSOT)** över alla registrerade och planerade ADRs — med status, datum och
korsreferenser — underhålls i **[`docs/decisions/README.md`](./docs/decisions/README.md)**
(av `docs-keeper`-agenten). Denna bilaga duplicerar inte indexet; se README:n för
aktuell uppsättning. Nya ADRs skapas via `/new-adr` (adr-keeper); nästa lediga
nummer hämtas ur indexet.

---

**Slut på BUILD.md.** Nästa läsning: [`AGENTS.md`](./AGENTS.md) för kodningsstandarder, [`CLAUDE.md`](./CLAUDE.md) för Claude-Code-flödet och [`DESIGN.md`](./DESIGN.md) för design-specifikation.
