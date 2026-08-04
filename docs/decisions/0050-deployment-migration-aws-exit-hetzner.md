# ADR 0050 — Deployment-migration: full AWS-exit → Hetzner CAX31 + Cloudflare

**Status:** Accepted — **delvis superseded 2026-08-04 av ADR 0122** (Beslut 2 helt, Beslut 3:s värdreferens, Beslut 4:s Cloudflare-halva + backup-mål, gate M-5)
**Datum:** 2026-05-19 (Proposed); **Accepted 2026-06-08** (efter targeted amendment, se Livscykel-not + Amendment 2026-06-08)
**Kontext:** Post-Fas-3 + pre-migration-discovery-session (Block 2). Inför MVP-presentation 2026-05-25 + studentbudget-kostnadshygien. Accepted-flippen 2026-06-08 sker post-AWS-teardown (ADR 0066) som strategisk riktnings-bekräftelse — faktisk provisionering är fortsatt framtida Klas-gatat arbete (se Amendment 2026-06-08, Sekvensering).
**Beslutsfattare:** Klas Olsson (riktnings-GO 2026-05-19; Accepted-GO + sizing/sekvens-dom 2026-06-08); dotnet-architect (IaC-/sizing-/deploy-review §9.2, 2026-05-19 + 2026-06-08); senior-cto-advisor (§9.6 decision-maker, strategiskt fas-skifte, 2026-05-19 + 2026-06-08); security-auditor (secrets/master-nyckel/PII-residens §9.2, 2026-06-08)
**Relaterad:** ADR 0005 (kostnadsskydd — relevans-skifte post-migration, ej supersession); ADR 0019 (direct-push, granskningsspärrar); ADR 0065 (PR-flöde + automerge — denna amendment levereras via PR); ADR 0066 (AWS dev-stack-teardown — löser KMS-beroendet via `LocalDataKeyProvider`, se Amendment 2026-06-08); ADR 0049 (TD-13 envelope-encryption — KMS-beroendet LÖST via ADR 0066 `LocalDataKeyProvider`, kvarvarande Hetzner-härdning = TD-102); ADR 0051 (AI-provider — Bedrock utgår, möjliggör ren exit). Underlag: `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md`; `docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-{architect,security,cto}.md`. BUILD.md Bilaga B planerad `NNNN-aws-over-azure.md` — denna ADR fyller den slotten med motsatt slutsats (helt moln-exit, ej moln-byte).

> ### ⚠ Läs ADR 0122 först på allt som rör värd, sizing eller edge
>
> **Värdvalet i denna ADR gäller inte längre.** Klas-beslut 2026-08-04: **Netcup
> RS 1000 G12 (x86, 4 kärnor, 8 GB, Nürnberg)** är värden framöver; Hetzner är av
> bordet och den mellanliggande "svensk VPS"-riktningen är återkallad. **ADR 0122**
> (lokal ADR per ADR 0072 docs-privacy) bär beslutet, residensmätningen, Klas
> topologibeslut K1–K4 och de fyra kapacitetsvillkoren för en 8 GB-låda.
>
> **Superseded här:** Beslut 2 (helt) · Beslut 3 (enbart värdreferensen "på CAX31") ·
> Beslut 4 (enbart Cloudflare-halvan + backup-**målet**) · gate **M-5**.
>
> **INTE superseded — och den distinktionen är lastbärande:** Beslut 1 · **Beslut 4:s
> `Amendment 2026-07-18` (Option B, "route-all-through-Next", inkl. de sex
> lastbärande invarianterna)**, som är helt provider-oberoende och gäller oförändrad ·
> Beslut 3:s substans (Vercel-exit, FE som co-tenant-container, **build-in-CI-regeln**) ·
> Beslut 2:s topologi (en låda, Compose, co-tenant PG/Redis) · gates B-1/B-2/M-1/M-2/M-3/M-4/M-6 ·
> kravet på en **obligatorisk andra security-auditor-granskning** före första riktiga data.
>
> Texten nedan är **superseded, inte raderad** — den står kvar som protokoll.
> Se `Amendment 2026-08-04` sist i filen.

> **Livscykel-not:** Skriven 2026-05-19 av Claude Code på explicit Klas-begäran
> (medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen för
> denna session). Besluts-substansen är transkriberad från Block 2-beslut +
> dotnet-architect-/senior-cto-advisor-domar — inga nya beslut konstruerade.
>
> **Revision + Accepted-flip 2026-06-08 (Claude Code, §9.4-Klas-override-precedens
> `feedback_klas_can_override_adr_verbatim_source`):** ADR:n skrevs 2026-05-19 —
> FÖRE AWS-teardown (ADR 0066, 2026-05-26) och FÖRE `LocalDataKeyProvider`
> (2026-06-06). Tre delar var därmed föråldrade och amenderades före Accepted-flip:
> (1) "Öppen fråga — KMS-beroende" beskrev en migrations-blocker som ADR 0066
> sedan LÖST (krypto provider-agnostiskt migrerat; security-auditor 2026-06-08
> bekräftade kod-bevisat); (2) rollback-storyn ("behåll AWS-stacken körande")
> är ogiltig — AWS är rivet; (3) sizing (CX32) vägde aldrig ARM CAX-serien.
> Revisionen är grundad i dotnet-architect- + security-auditor- + senior-cto-
> advisor-domar 2026-06-08 (`docs/reviews/`) — inga nya beslut konstruerade
> utöver CTO-domarna. Klas godkände Accepted-flip + CAX31-sizing + Fas-4-före-
> Hetzner-sekvens 2026-06-08. Faktisk provisionering/migration utförs INTE denna
> session (Sekvensering: Hetzner sist, vid MVP före beta-testare).

---

## Kontext

JobbPilot driftas på AWS (eu-north-1) som en lean dev-stack som i praktiken
bär hela driften: ECS Fargate (API 0,5 vCPU/1 GB, Worker 0,25 vCPU/0,5 GB),
RDS PostgreSQL `db.t4g.micro` (1 GB RAM, 20 GB→autoscale 100), ElastiCache
Redis `cache.t4g.micro`, ALB, VPC, KMS, Secrets Manager, CloudTrail. Korpuset
är live ~45 000+ jobbannonser.

Month-to-date-kostnad 2026-05-19 ≈ $44,65, trajektoria ~$2,3/dygn (tidsbaserad
infra: Fargate/RDS/ALB/VPC). På en studentbudget är detta den dominerande
återkommande kostnaden, och den drivs av ren infrastruktur — inte av
AI-inferens (Fas 4 ej byggt) eller trafik.

Klas-beslut 2026-05-19: avveckla AWS helt efter MVP-presentationsveckan
(juni 2026). Block 1 (budget-höjning $50→$100) skippades medvetet — ingen
funktionell vinst på en stack som rivs (separat Klas-beslut, session-loggen).
ADR 0051 (Anthropic Direct, Bedrock utgår) tar bort det enda kvarvarande
motivet att behålla en AWS-tether → en **ren** exit blir möjlig (ej hybrid).

## Beslut

### Beslut 1 — Full AWS-exit, ej hybrid

All JobbPilot-drift lämnar AWS. Ingen kvarvarande AWS-tjänst (ingen
Bedrock-tether per ADR 0051, ingen kvar-RDS, ingen kvar-S3). Hybrid-scenariot
(behåll Bedrock/utvald AWS-tjänst) avvisas — det bevarar AWS-konto,
IAM/SDK-koppling och en kostnads-svans för marginell nytta. Ren exit ger
enklare ops-yta och eliminerar AWS-SDK-beroenden på driftboxen.

### Beslut 2 — Backend: Hetzner Cloud CAX31 (ARM), all-in-one Docker Compose

> **SUPERSEDED 2026-08-04 av ADR 0122** — värd och sizing är nu **Netcup RS 1000 G12
> (x86, 4 dedikerade kärnor, 8 GB, Nürnberg)**. **Grunden för att avvisa 8 GB nedan är
> mätt död:** `JobTechStreamClient`s `MaxResponseContentBufferSize = 500 MB` finns inte
> i `src/` (mätt 2026-08-04 mot HEAD `1b98d016`) — båda wire-vägarna strömmar, så en cap
> vore en no-op. ARM-resonemanget är moot (x86). Korpuset har däremot vuxit 46k →
> 106 071 annonser och `company_register` (1 066 938 rader) tillkommit, så domen är
> **marginell men körbar**, villkorad på fyra punkter i ADR 0122. Även
> `mem_limit`-doktrinen nedan ("generös/osatt cap på Postgres") är superseded: den vilade
> uttryckligen på att 16 GB upplöste nollsummespelet, och på 8 GB är det tillbaka.
> **Topologin — en låda, Compose, co-tenant PG/Redis, ingen managed DB — består.**

> **Amenderad 2026-06-08:** ursprungsvalet **CX32** (x86, 8 GB) uppgraderades till
> **CAX31** (ARM, 16 GB) efter dotnet-architect-/senior-cto-advisor-dom. Skälet:
> ADR 0050:s ursprungstext vägde bara CX22 vs CX32 (båda x86) — ARM CAX-serien
> övervägdes aldrig. Se motivering + avvisade alternativ nedan.

Hetzner Cloud **CAX31** (8 vCPU shared ARM Ampere Altra / 16 GB RAM / 160 GB
NVMe / 20 TB trafik, ~€15,99/mån, EU-datacenter Falkenstein/Nuremberg/Helsinki,
pris-verifierat 2026-06-08). En box kör hela stacken (backend + frontend) i Docker Compose:
.NET API + .NET Worker + PostgreSQL + Redis + Caddy (reverse proxy, auto-TLS) + Next.js (next start).
**Detta är den totala compute-kostnaden — Postgres är co-tenant i
container på boxen, ingen separat managed-DB-kostnad** (Ubicloud managed-PG
~$15/mån avvisad, se Alternativ).

FE-tillägget (`next start`, ~0,5 GB under last) ryms inom CAX31:s 16 GB headroom; den
dimensionerande vektorn förblir ingestion-OOM (Worker-minnesprofil), inte FE steady-state.
**Bindande regel: `next build` körs i CI — enbart den byggda imagen skickas till boxen;
build-peaken (~2–4 GB) belastar aldrig boxens RAM-feldomän.**

**Sizing-motivering (CAX31 över CX32/CAX21):** På en single-box samsas API +
Worker + Postgres + Redis om samma RAM-feldomän (AWS isolerade RDS/Redis
managed; en VPS gör det inte). Den dimensionerande risken är kod-bevisad:
`JobTechStreamClient` har `MaxResponseContentBufferSize = 500 MB`
(Platsbanken-ingestion, ADR 0032/TD-13-grunden) — en dokumenterad
minnes-blowout-vektor som konkurrerar med Postgres hot-index (46k+ annonser +
raw_payload-jsonb + STORED generated columns + FTS-GIN-index) på samma RAM.
På CX32:s 8 GB ligger PG:s working-set (~2–3 GB) + Worker-ingestion-spik +
Redis + .NET-heapar + OS farligt nära taket. CAX31:s **16 GB** ger headroom för
`mem_limit` per service (skydda Postgres mot Worker-OOM — se mem_limit-noten
under Amendment 2026-06-08) + korpus-tillväxt; **160 GB** disk rymmer PG + WAL +
Docker-images + pg_dump-staging. ARM-risken är låg: hela stacken är ARM64-ren
2026 (.NET 10 tier-1 `linux-arm64`, Npgsql/Hangfire/Postgres/Redis/Caddy
multiarch); enda historiska ARM-fällan (`System.Drawing`/libgdiplus) är
Fas-4-PDF-gated och ej aktuell vid cutover. ~€9/mån merkostnad mot CX32 köper
bort den största single-box-risken (Nygard *Release It!* — Bulkheads/Steady
State: medvetet SPOF-val för beta kompenseras med headroom i delad resurs).

### Beslut 3 — Frontend: co-tenant container på CAX31 (Vercel-exit)

> **DELVIS superseded 2026-08-04 av ADR 0122 — enbart värdreferensen.** Läs "CAX31"
> som "Netcup-lådan" överallt nedan, och sizing-re-valideringen (16 GB, ~8 GB headroom)
> som ersatt av ADR 0122:s `mem_limit`-tabell mot 8 GB. **Substansen består oförändrad:**
> Vercel-exiten, FE som `next start`-co-tenant-container bakom Caddy, och **den bindande
> build-in-CI-regeln** — som i ADR 0122 dessutom är kapacitetsvillkor 1 och därmed
> lastbärande, inte längre bara bekvämt.

> **Amenderad 2026-06-14 (Klas-direktiv):** ursprungsbeslutet **"Vercel behålls"**
> är supersederat — Next.js-frontend flyttar in som `next start`-container i samma
> Compose-stack bakom Caddy på CAX31.
>
> **Ursprungligt Beslut 3 (supersederat):** *"Next.js-frontend kvar på Vercel (EU). Ingen
> ändring — Vercel free/Pro-nivå bär frontend; ingen anledning att flytta in den på
> VPS-boxen och därmed öka dess RAM-/ops-börda."*
>
> **Varför supersederat:** Det ursprungliga argumentet (RAM-/ops-börda → behåll FE off
> the box) vägde aldrig jurisdiktions-/konsolideringsvinsten. FE:s verkliga fotavtryck
> (~0,5 GB under last) ryms komfortabelt inom CAX31:s headroom (se Beslut 2,
> FE-sizing-meningen). Vercel är ett US-bolag — att behålla applikationshosting hos
> en US-leverantör skapar en inkonsekvens med Beslut 4:s avvisning av Cloudflare R2
> (CLOUD Act / Schrems II / GDPR Kap. V). Den distinktionen görs nu fullt konsekvent:
> **Cloudflare behålls = edge-only** (TLS/DNS/proxy/DDoS — ingen applikationshosting,
> inget data-at-rest hos ett US-bolag; bara edge-transit passerar ett US-bolag);
> **Vercel lämnar = applikations-tier + data-at-rest blir EU-resident** (Hetzner-EU),
> enbart edge-transit kvarstår hos ett US-bolag. Detta är en strikt konsekvensstärkning
> av samma Schrems II / Kap. V-logik som motiverade R2-avvisningen — inte en
> komplikation.
>
> **Sizing re-validerad (dotnet-architect, 2026-06-14):** CAX31 (16 GB) håller med
> ~8 GB headroom även i worst-case-samvaro (ingestion-spik + SSR-last), **förutsatt**
> att `next build` körs i CI och enbart den byggda imagen skickas till boxen —
> build-peaken (~2–4 GB) får aldrig belasta boxens RAM-feldomän. **Build-in-CI är den
> bindande regeln.**

### Beslut 4 — Cloudflare-proxy + Hetzner-EU-backup-offload

> **DELVIS superseded 2026-08-04 av ADR 0122 — och gränsen går mitt i detta beslut.**
> Läs den innan du läser resten av sektionen:
>
> - **Cloudflare-halvan är DÖD** (Klas K3): ingen CDN, ingen "Full (strict)", **ingen
>   origin-IP-lockdown**, ingen DDoS-absorption. Caddy går direkt mot Let's Encrypt;
>   DNS ligger hos **Strato**. Därför står 80/443 öppna mot `any` i båda brandväggslagren
>   — **det krävs för ACME HTTP-01, och får inte "rättas" mot gate M-5:s text.**
> - **Backup-MÅLET är dött** (Hetzner-EU Storage Box). **Kraven består:** klient-side
>   age-kryptering före upload oavsett mål, EU-jurisdiktion, definierad rotation — nu
>   med ett tal, **K4: 30 dagar**. Målet är **inte valt och inte verifierat**; det ägs av
>   [#197](https://github.com/klasolsson81/jobbliggaren/issues/197).
> - **`Amendment 2026-07-18` nedan (Option B, "route-all-through-Next") är INTE
>   superseded.** Den är provider-oberoende — beslutad på applikationens form (11
>   Next-BFF-handlers under `/api/`, noll publika backend-konsumenter), inte på
>   Cloudflare. Den och dess **sex lastbärande invarianter gäller oförändrade**, och
>   att läsa "Beslut 4 är superseded" som att den föll vore att döda den tyst.
> - **EN rättelse i den, och den är kritisk att läsa FÖRE invariant 5 nedan:**
>   invariant 5 avslutas med parentesen *"(Med Cloudflare Full (strict) + origin-cert
>   är detta ändå moot.)"*. **Den parentesen är falsk under K3.** Utan CDN kör Caddy
>   ACME **HTTP-01 skarpt**, så invariant 5 går från moot till **lastbärande**: det
>   måste bevisas vid cutover att K2:s basic auth-grind inte skuggar
>   `/.well-known/acme-challenge/*`. Gör den det dör certifikatförnyelsen **tyst, cirka
>   60 dagar efter cutover**. Invariantens text i övrigt är oförändrad och gäller.

> **Amenderad 2026-06-08:** backup-målet **Cloudflare R2** ersattes med
> **Hetzner-EU Storage Box** efter security-auditor-/senior-cto-advisor-dom
> (M-4). Skälet: `pg_dump` bär icke-krypterad PII (bara 4 kolumner är
> fält-krypterade per ADR 0049; e-post/namn/`waitlist_entries`/audit-IP i
> klartext) och Cloudflare är ett US-bolag (CLOUD Act) → R2 vore en
> tredjelandsöverföring (GDPR Kap. V/Schrems II). Hetzner-EU håller hela
> data-livscykeln i samma jurisdiktion som boxen.

Cloudflare gratis-tier framför boxen (TLS-edge/DNS/CDN/DDoS) — **Cloudflare-proxy
"Full (strict)"** mot ett giltigt origin-cert på Caddy (aldrig "Flexible" =
klartext på sista benet) + origin-IP-lockdown (origin accepterar bara
Cloudflare-IP:er på 443) + HSTS. Caddy reverse-proxiar nu **både** API:et (`/api/*`)
och Next.js-servern (`localhost:3000`, övriga routes); origin-cert + origin-IP-lockdown
("Full (strict)") är oförändrade och täcker hela origin.

> **Amenderad 2026-07-18 (#756 — reverse-proxy-rutt-regeln korrigerad; denna
> amendment är SSOT för routingen):** rutt-regeln i stycket ovan (`/api/*` →
> ASP.NET) är **fel** och rättas här. Alla backend-**endpoints**
> (`MapGroup`-grupper) lever under `/api/v1/`; de enda backend-routes utanför
> `/api/v1/` är health-checkarna `/api/live` + `/api/ready` (direkt under `/api/`
> via `MapHealthChecks`), som under Option B förblir interna by construction
> (Compose-healthchecken träffar backend-containern på internnätet — default-path
> `/api/ready` — inte edge). **11 Next.js-BFF-route-handlers** lever direkt under
> `/api/`: prefixen
> `/api/jobb/*`, `/api/me/*`, `/api/cv/*`, `/api/foretag/lookup`,
> `/api/landing-stats`. En bred `/api/*` → ASP.NET vid edge skulle alltså slussa
> de 11 BFF-handlers till backend, som inte serverar dem → **404/401 i produktion**
> (typeahead, facett-räknare, CV-preview/import/ats-text, företagsuppslag,
> landing-stats, kriterie-/match-count-previews). Ingen Caddyfil finns ännu (noll
> nuvarande impact); detta rättar planen **före** TD-106 bygger den.
>
> **Korrigerad topologi (senior-cto-advisor-bind 2026-07-18,
> `docs/reviews/2026-07-18-756-caddy-topology-cto.md`) — Option B,
> "route-all-through-Next":** Caddy reverse-proxiar **all** trafik till
> Next.js-servern (`web:3000`); **ASP.NET-API:t exponeras aldrig vid edge** — det
> binds enbart till det interna Docker-nätverket (inte publicerad via `ports:`
> till host — `expose:` är informativt; host-onåbarheten kommer från frånvaron av
> host-publicering). Ingen `/api`-matcher finns vid edge överhuvudtaget. Caddyfilens
> app-del blir i praktiken en enda `reverse_proxy web:3000` (plus `encode`, se
> TD-106). Cloudflare "Full (strict)" + origin-cert + origin-IP-lockdown + HSTS
> är oförändrade.
>
> **Grund:** backend har **noll publika konsumenter** — det finns ingen
> `NEXT_PUBLIC`-prefixad backend-URL (browsern anropar aldrig backend direkt; all
> backend-trafik uppstår server-side i RSC/SSR/BFF-route-handlers över
> internnätet) och **ingen tredjeparts-inbound-callback** träffar backend
> (bekräftelse-länkar går till den publika Next-landningssidan som relayar
> server-side; ingen extern OAuth-IdP; annons-sync + e-post är outbound; ingen
> webhook). Att inte öppna en edge-yta som ingen konsumerar följer least
> privilege + YAGNI och — avgörande — **eliminerar defektklassen**: utan
> `/api`-matcher vid edge kan den breda-prefix-skuggningen inte återuppstå när en
> ny BFF-rutt eller backend-grupp tillkommer. **Option A** (exponera `/api/v1/*`
> vid edge) avvisad: öppnar `/api/v1/auth|admin|dev` mot internet för noll
> funktionell vinst och bär en stående matcher-ordnings-vaksamhet (Caddy `handle`
> = first-match; `/api/v1/*` måste matchas före ett bredare `/api/*`) som *är*
> defektklassen bakom #756.
>
> **Lastbärande invarianter (måste hålla — annars kollapsar Option B:s säkerhet
> eller korrekthet):**
> 1. **Browser-never-calls-backend:** ingen `NEXT_PUBLIC`-backend-URL får införas.
>    Ett framtida direkt browser→backend-anrop är en **topologiändring** som
>    kräver medveten ADR-amendment (återöppna en snävt-scopad, härdad
>    `/api/v1/*`-edge-rutt med egen auth/CORS/rate-limit) — får aldrig smygas in
>    tyst.
> 2. **Ingen tredjeparts-callback till backend:** en framtida webhook (t.ex.
>    betalprovider) kräver en egen medveten, snävt-scopad, härdad edge-rutt för
>    *just den pathen* — inte en blank `/api/v1`-öppning.
> 3. **`/api/v1/dev` + `/api/v1/admin/*` får aldrig vara edge-nåbara** —
>    högsta-risk-paths; Option B håller dem interna by construction.
> 4. **Health/readiness:** extern uptime-monitoring träffar den publika Next-ytan
>    (lägg en Next-health-route vid behov), aldrig backend; backend-liveness
>    kollas internt (Compose healthcheck + Caddy upstream-health på internnätet).
>    Ingen publik backend-health-route "för monitoring".
> 5. **ACME/TLS-challenge** (om Caddy någonsin kör HTTP-01) hanteras
>    `/.well-known/acme-challenge/*` av Caddy internt före all `reverse_proxy` —
>    varken backend- eller Next-rutt. (Med Cloudflare Full (strict) + origin-cert
>    är detta ändå moot.)
> 6. **`BACKEND_URL` resolvar internt (korrekthets-invariant, den som faktiskt får
>    Option B att fungera):** Next-serverns server-only `BACKEND_URL`
>    (`web/…/src/proxy.ts`, `lib/auth/session.ts`, `lib/security/security-headers.ts`
>    — dokumenterad "server-only env getter") måste peka på backend-containern på
>    det interna Docker-nätverket (service-DNS, t.ex. `http://api:8080`), **aldrig**
>    det publika origin:et. Under Option B är internvägen den **enda** vägen till
>    backend; pekas `BACKEND_URL` fel loopar varje SSR/BFF-anrop mot `/api/v1/*`
>    genom Caddy→Next→**404** (Next har inga `/api/v1/*`-handlers).
>
> **Överlämnat till TD-106 (build-tid; security-auditors veto beväpnas där, inte
> här — detta är en docs-only-ändring utan kod/secret/PII-touch):** (a)
> backend-bind-posture: inte publicerad via `ports:` till host (`expose:`
> informativt) — TD-106 verifierar host-onåbarhet **empiriskt** (curl mot
> host-IP:backend-port utifrån), inte via `expose:`-närvaro; (b) en
> cutover-curl-matris som bevisar att de 11 BFF-`/api/*`-handlers resolvar till
> Next; (c) `encode zstd gzip` (finding d2-compression); (d)
> **forwarded-headers/per-IP-rate-limit:** `AuthWritePolicy` rate-limitar per
> klient-IP → Caddy måste passa äkta klient-IP. Både `ForwardedHeadersConfig` +
> `appsettings.Production.json`:s `ForwardedHeaders.KnownNetworks` (den faktiska
> X-Forwarded-For-wiringen, i dag bunden till ALB:s VPC-CIDR) **och** `AlbOptions`
> (HTTPS/HSTS-gaten) är ALB-orienterade och måste re-homas för Caddy/Docker-nätets
> CIDR (jfr TD-106 punkt 3 `ForwardedHeadersConfig` + punkt 4 `AlbOptions →
> ReverseProxyOptions`), annars kollapsar limits till Caddys IP och
> rate-limitingen dör tyst.

Nattlig `pg_dump` → **Hetzner-EU Storage Box** (~€3,20/mån/1 TB,
samma EU-jurisdiktion som boxen) — backups ligger INTE på boxens 160 GB (håller
disk-budgeten hållbar mot korpus-tillväxt + WAL + Docker-images).
**Dumpen klient-side-krypteras (age) före upload oavsett mål** — fält-krypteringen
skyddar bara fyra kolumner *i* dumpen; resten kräver eget krypto-lager. Plus
definierad backup-retention/rotation (bortre gräns för icke-krypterad PII i
gamla dumpar; ADR 0024:s RDS-14d-rotation finns ej gratis på Hetzner — måste
byggas). Detaljerna = [#197](https://github.com/klasolsson81/jobbliggaren/issues/197).

> **Superseded 2026-08-04 av ADR 0122 — enbart MÅLET.** Hetzner-EU Storage Box faller
> med värdbytet, och boxens disk är 256 GB, inte 160. **Kraven i stycket ovan består
> ordagrant** (age-kryptering före upload oavsett mål, EU-jurisdiktion, definierad
> rotation) och får nu ett tal: **K4 = 30 dagars retention**. Ett nytt mål är **inte
> valt och inte verifierat** — #197 äger valet. **Cloudflare R2 är redan uttryckligen
> avvisat** i Amendment 2026-06-08 ovan och får inte återföreslås utan att
> security-auditor väger age-krypteringen mot Kap. V.

## Konsekvenser

### Positiva

- Återkommande kostnad ~€16/mån (CAX31, inkl. co-tenant-DB) + ~€3/mån EU-backup
  ≈ **~€19/mån totalt**, vs ~$45+/mån AWS-trajektoria — **~80% reduktion**, materiell
  på studentbudget. (Amenderat 2026-06-08: ursprungstexten angav ~€6,80 för CX32.)
  Vercel var på free/Pro-nivå och utgör ingen €-post i ADR:ns totalkostnad — FE
  flyttar in på den redan betalda CAX31, ingen ny återkommande compute-kostnad tillkommer.
- Ren ops-yta: en box, Docker Compose, inga moln-SDK-/IAM-tethers.
- Eliminerar AWS-SDK-beroenden i kodbasen (jfr ADR 0051 — `AWSSDK.BedrockRuntime`
  byggs aldrig).
- En färre extern subprocessor och US-bolags-beroende (Vercel-exit).
- ADR 0005:s kostnadsskydds-apparat (Budget Actions, Bedrock-deny,
  registrations_open-gating) blir **i stort sett moot post-migration** —
  relevans-skifte, ej supersession (ADR 0005-text orörd; flaggas i Block 4).

### Negativa

- **Singel-box blast-radius:** API/Worker/Postgres/Redis/Next.js (next start) delar OS, RAM och
  feldomän. En OOM eller box-incident tar hela produkten, inte en isolerad
  container (kontrast mot AWS managed-isolering). Next.js-tillägget är ett
  medvetet utökat SPOF-val, kompenserat av CAX31:s headroom och build-in-CI-regeln.
- Självhanterad Postgres + Redis + backups: ingen managed RDS-HA, ingen
  point-in-time-restore out-of-the-box, patch-/vacuum-/WAL-ansvar på Klas.
- Ops-börda flyttas från AWS-managed till Klas-manuell (Docker Compose-deploy,
  Caddy-config, restore-drill).

### KMS-beroende — LÖST 2026-05-26 via ADR 0066 (amenderat 2026-06-08)

> **Amenderad 2026-06-08:** denna sektion hette ursprungligen "Öppen fråga —
> KMS-beroende (migrations-blocker, EJ löst denna session)" och beskrev
> krypto-flytten som en oläst blocker. Den **prosan är föråldrad** — blockern
> löstes 2026-05-26 av ADR 0066 (`LocalDataKeyProvider`). security-auditor
> bekräftade 2026-06-08 kod-bevisat att omframingen är korrekt. En Accepted-ADR
> får inte bära en falsk blocker → sektionen omskriven.

**ADR 0049 (TD-13) implementerade PII-fält-kryptering för fyra user-ägda
kolumner via envelope-encryption (per-användar-DEK wrappad av master-nyckel,
lagrad i `user_data_keys`).** Den ursprungliga implementationen wrappade DEK via
AWS KMS (`GenerateDataKey`/`Decrypt`, CMK i HSM). En full AWS-exit (Beslut 1)
tar bort AWS KMS — men **det beroendet är redan löst**:

ADR 0066 (2026-05-26) införde `LocalDataKeyProvider` som ett andra
`IDataKeyProvider`-impl (config-switch `FieldEncryption:Provider` Kms/Local).
Local-grenen wrappar per-användar-DEK med en lokal AES-256-GCM master-nyckel
i stället för KMS. **Hela ADR 0049:s besluts-substans är oförändrad** —
envelope-strukturen (per-JobSeeker wrapped-DEK), owner-AAD-bindningen,
fail-closed-invarianten och `IFieldEncryptor`-primitiven (ren BCL `AesGcm`) är
identiska; bara DEK-wrap-mekanismen bytte. Verifierat e2e healthy 2026-06-07.
Crypto-erasure-semantiken (ADR 0049 Beslut 2) är bevarad.

**Kvarvarande Hetzner-arbete är därmed INTE "om-hemma krypto-mekanismen" (gjort)
utan att härda den självhanterade master-nyckelns prod-skyddsmodell + rotation**
— en känd, scopead, kod-bevisad TD: **TD-102** (Major, Hetzner-deploy;
ADR 0049-amendment-scope). Detaljerna (master-nyckel-skydd via
systemd-credentials/sops+age, körbar re-wrap-rotation, security-gates) listas
i "Pre-beta-data-gates" under Amendment 2026-06-08.

### Mitigering

- Nattlig klient-side-krypterad `pg_dump` → Hetzner-EU Storage Box +
  dokumenterad restore-drill innan produktions-cutover (DoD-grind) — **TD-107**.
- Caddy auto-Let's-Encrypt (DNS-01 via Cloudflare-plugin); health-checks +
  extern uptime-monitor (UptimeRobot/BetterStack free) ersätter
  ALB/CloudWatch-health.
  > **Rättad 2026-08-04 (ADR 0122 / K3):** "**DNS-01 via Cloudflare-plugin**" är död —
  > det finns ingen Cloudflare. Caddy kör **HTTP-01** över de portar som därför måste
  > stå öppna mot `any`. Health-checks + extern uptime-monitor består oförändrade,
  > men monitorn träffar den publika Next-ytan (Option B invariant 4), aldrig backend.
- KMS-beroendet är redan löst (ADR 0066 `LocalDataKeyProvider`); kvarvarande
  master-nyckel-prod-härdning + rotation = **TD-102** (egen security-auditor-
  granskning av faktisk prod-config före real-PII).
- Lasttest mot 46k-korpuset (NBomber, ADR 0045) före cutover för att
  validera CAX31-sizing empiriskt.

## Alternativ övervägda

- **CX22 (2 vCPU/4 GB/40 GB):** Avvisad. Under-provisionerad för co-tenant
  Postgres med 46k+ korpus + ingestion-minnesprofil; noll headroom för
  korpus-tillväxt; 40 GB disk snäv (PG + WAL + backups + raw_payload).
- **CX32 (x86, 4 vCPU/8 GB/80 GB, ~€6,80):** ursprungsvalet — **avvisat
  2026-06-08**. 8 GB är under-provisionerat för co-tenant Postgres + den
  kod-bevisade ingestion-OOM-vektorn (500 MB-buffer) + korpus-tillväxt på en
  delad feldomän. x86 ger noll fördel (ingen x86-only-dep i stacken). Prisdeltat
  (~€9/mån mot CAX31) är trivialt mot en helprodukts-OOM på en singel-box
  (samma resonemang som CX22→CX32-avvisningen, förlängt ett steg på kod-bevisad
  grund).
- **CAX21 (ARM, 4 vCPU/8 GB/80 GB):** Avvisad 2026-06-08. ARM-ekvivalent till
  CX32 men samma 8 GB-tak — ARM-byte utan RAM-vinst löser inte
  single-box-RAM-feldomän-risken.
- **Hybrid (behåll Bedrock/utvald AWS-tjänst på AWS):** Avvisad — bevarar
  AWS-konto/IAM/SDK-tether + kostnads-svans för marginell nytta. ADR 0051
  eliminerar Bedrock-motivet helt.
- **Stanna på AWS:** Avvisad — dominerande återkommande kostnad på
  studentbudget; ingen funktionell vinst som motiverar den mot Hetzner-paritet
  för en beta-skala.
- **Annan VPS/PaaS (DigitalOcean/OVH/Vultr/Coolify-managed):** Ej djup-jämförd
  — Klas pre-beslutade Hetzner (Block 2). Provider-jämförelsen blev därmed
  akademisk; sizing-frågan (CX22 vs CX32 vs CAX-serien) var den enda levande
  beslutsaxeln och är avgjord i Beslut 2 (CAX31).
- **Managed Postgres utanför boxen (Ubicloud på Hetzner ~$15/mån, eller annan):**
  Ej valt för beta-skala — **fördubblar nästan backend-budgeten** (~€16 + ~$15)
  + nätverkshop + extern beroende-yta. Co-tenant Postgres i container på CAX31
  är tillräckligt om sizing hålls (16 GB ger headroom). Kan omvärderas vid
  skala-signal (Trigger, §9.6) — ej TD.

## Implementationsstatus

> **Föråldrad 2026-08-04 (se ADR 0122).** Lådan **är** provisionerad och
> grundhärdad — en Netcup RS 1000 G12, inte en Hetzner CAX31 (PR #1196,
> `docs/runbooks/vps-base-hardening.md`, bevisad över två omstarter). Fortfarande
> sant: **ingenting är deployat och ingen applikationsdata finns på lådan.**
> Sekvenseringen nedan ("Hetzner-provisionering sist") är därmed passerad — nästa
> steg är deploy-stacken ([#196](https://github.com/klasolsson81/jobbliggaren/issues/196)),
> och samtliga Pre-beta-data-gates står kvar som grind före första riktiga data.

**Accepted 2026-06-08 (riktning bekräftad). Ingen migration/provisionering
utförd.** Accepted-flippen dokumenterar den bekräftade riktningen — den binder
ingen infra (en reversibel "two-way-door"; DNS-cutover är den enda irreversibla
flippen och utförs ej denna session). Faktisk Hetzner-provisionering är framtida
Klas-gatat arbete; per Sekvensering (Amendment 2026-06-08) sker den **sist**,
vid MVP före beta-testare, med samtliga Pre-beta-data-gates lösta först.

## Validering

Uppskjuten till migrations-utförandet: NBomber-lasttest mot 46k-korpus
(ADR 0045-budgetar), klient-side-krypterad `pg_dump`-restore-drill (TD-107),
end-to-end-rök på Hetzner-box före DNS-cutover.

**Rollback (amenderat 2026-06-08):** den ursprungliga rollback-storyn ("behåll
AWS-stacken körande tills Hetzner-paritet verifierad") är **ogiltig** — AWS är
rivet (ADR 0066, 2026-05-26). Den korrekta modellen: **lokal Docker-Compose-stack
på Klas laptop är paritets-baselinen** (samma image-byggväg som Hetzner-prod,
dev/prod-paritet). Rollback vid misslyckad cutover = återgå till lokal-dev +
ej-cutad DNS (Cloudflare). DNS-cutover är den reversibla flippen; tills den sker
påverkas ingen live-trafik (ingen live-miljö existerar idag).

## Amendment 2026-06-08 — sizing-uppgradering, backup-mål, KMS-omframing, security-gates, sekvensering

**Beslutsfattare:** Klas Olsson (Accepted-GO + CAX31-sizing + Fas-4-före-Hetzner-
sekvens). **Underlag:** dotnet-architect + security-auditor + senior-cto-advisor
(decision-maker) 2026-06-08 (`docs/reviews/2026-06-08-adr-0050-*`). **Kontext:**
ADR:n skrevs 2026-05-19, före AWS-teardown (ADR 0066) + `LocalDataKeyProvider` —
denna amendment re-validerar mot nuläget och flippar Proposed→Accepted.

### Sammanfattning av ändringar (inline ovan)

1. **Beslut 2 sizing:** CX32 (x86, 8 GB) → **CAX31** (ARM, 16 GB). Kod-bevisad
   ingestion-OOM-vektor + single-box-RAM-feldomän.
2. **Beslut 4 backup:** Cloudflare R2 → **Hetzner-EU Storage Box** + obligatorisk
   klient-side-kryptering (R2 = CLOUD Act-tredjelandsöverföring av icke-krypterad
   pg_dump-PII).
3. **KMS-beroende:** "oläst migrations-blocker"-prosan ersatt — beroendet löst
   av ADR 0066 (`LocalDataKeyProvider`), kvarvarande härdning = TD-102.
4. **Rollback-story:** "behåll AWS körande"-modellen ersatt (AWS rivet) med
   lokal-Compose-paritets-baseline.

### Amendment 2026-06-14 — Vercel-exit, FE co-tenant på CAX31

5. **Beslut 3 (Vercel-exit):** ursprungsbeslutet "Vercel behålls" supersederat — FE
   flyttar in som `next start`-container i Compose-stacken bakom Caddy. Titel uppdaterad
   (+ Vercel borttaget). CLOUD Act-konsekvensstärkning: Cloudflare kvarstår som edge-only
   (ingen applikationshosting, inget data-at-rest), Vercel lämnar (applikations-tier +
   data-at-rest EU-resident). Caddy reverse-proxiar nu hela origin (API + Next.js).
   Beslut 2-prosan generaliserad till "hela stacken (backend + frontend)";
   TD-106-scope utvidgad till FE-container + Caddy-FE-route + CI FE-build-steg.
   Beslutsfattare: Klas Olsson (direktiv 2026-06-14); sizing re-validerad av dotnet-architect.

### mem_limit-mekanik (konsekvens-not till Beslut 2)

Compose-stacken sätter **hybrid `mem_limit`**: hård cap på Worker + Redis (skydda
Postgres mot Worker-ingestion-OOM), generös/osatt cap på Postgres
(data-durabilitet — en hård PG-cap kan OOM-killa mitt i query). Bulkhead-principen
(Nygard *Release It!*): cappa angriparen (Worker-burst, Hangfire-Postgres-storage
→ dödad spik retryas durabelt), inte offret (PG). CAX31:s 16 GB upplöser det
nollsummespel detta vore på 8 GB. Mekanik-detaljer = TD-106.

**TD-106 scope-utvidgning (Beslut 3-amendment 2026-06-14):** TD-106:s Compose-stack-scope
vidgas till att inkludera FE-containern (`next start`, healthcheck, `mem_limit`),
Caddy-FE-routen (`localhost:3000`-proxying), samt ett CI FE-build-steg
(`next build` → image) som bindande pre-requisite för deployment till boxen.

### Pre-beta-data-gates (security-auditor 2026-06-08 — MÅSTE grönt före första real-PII)

Dessa är gates **före första real-PII (beta-testare)**, INTE före denna Accepted-
flip. Strategin *som riktning* har inga GDPR-blockers (Hetzner-EU at-rest
GDPR-ren; krypto provider-agnostiskt migrerat). Waitlist är tom idag. Gates bärs
operativt av TD-102 (master-nyckel), TD-106 (stack/härdning), TD-107 (backup).

| # | Gate | Källa | Hemvist |
|---|---|---|---|
| B-1 | Master-nyckel ALDRIG plaintext-på-disk på beta-VPS (systemd-credentials TPM-bunden el. sops+age→tmpfs; plaintext OK bara lokalt) | Blocker | TD-102 |
| B-2 | Gitleaks/historik-scan: ingen master-nyckel/cred committad; rotation om läckt | Blocker | **Verifierad GRÖN 2026-06-08** (`appsettings.Local.json` i .gitignore, aldrig committad; inget nyckel-värde i historik) |
| M-3 | Körbar idempotent master-nyckel-re-wrap-rotation + kadens (minst årlig + händelse-driven vid box-kompromiss/offboarding) | Major | TD-102 |
| M-4 | pg_dump klient-side-krypterad + backup-retention/rotation definierad + EU-jurisdiktion | Major | TD-107 |
| M-5 | ~~Cloudflare "Full (strict)" + origin-IP-lockdown (bara CF-IP på 443) + HSTS~~ | Major | **SUPERSEDED 2026-08-04 → M-5a + M-5b** (se `Amendment 2026-08-04`) |
| **M-5a** | **Origin-TLS är hela TLS-historien:** Caddy terminerar med publikt betrott LE-cert (HTTP-01), **HSTS emitteras faktiskt i Production**, ingen klartextsträcka. **Bevisas på svaret, inte på konfigen** | Major (ärvd från M-5) | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) |
| **M-5b** | **Kantexponeringen är omitigerad** (ingen CDN/WAF/DDoS-absorption, ingen origin-IP-allowlist): kompenserande kontroll är **admission + topologi**, aldrig filtrering — K2-grinden, Option B, per-IP-rate-limit, riktade `forward`-accepts | **ograderad — severity tillhör security-auditor** | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) |
| M-6 | VPS-härdnings-baseline (SSH-key-only, brandvägg, ~~fail2ban~~, auto-patch, PG/Redis ej publika, swap/core-dump-hygien mot master-nyckel-minnesläck) | Major | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) · **baseline i övrigt mätt grön** ([#1196](https://github.com/klasolsson81/jobbliggaren/pull/1196)) · **fail2ban-klausulen: avvikelse REGISTRERAD, ratificering väntar på Klas GO** (`Amendment 2026-08-04`) |
| M-1 | ADR 0050 KMS-blocker-prosa amenderad → TD-102-omframing | Major | **Åtgärdad denna amendment** |
| M-2 | ADR 0049-amendment: self-managed master-nyckels prod-skyddsmodell + accepterad minne-restrisk + namngiven skala-trigger för extern KV/HSM | Major | TD-102 (ADR 0049-amendment-scope) |

**Obligatorisk re-review:** en andra security-auditor-granskning av den faktiska
prod-konfigurationen (master-nyckel-injektion, backup-kryptering, TLS-topologi,
härdning) krävs **innan första beta-data laddas** (TD-102 punkt 3). Den
granskningen är gaten — inte denna design-dom.

### Sekvensering (Klas-beslut 2026-06-08)

Hetzner-provisionering är **inte** nästa steg. AWS är rivet (kostnad €0), all dev
kör lokalt, waitlist är tom — att deploya nu vore premature deployment för noll
användare (YAGNI; value over activity, Winters et al. *SWE at Google* 2020).
**Ordning:** (1) Fas 4 (AI Layer, ADR 0051) — alternativt TD-rensning — byggs/testas
lokalt; (2) **Hetzner-provisionering sist, vid MVP före beta-testare**, med
samtliga Pre-beta-data-gates lösta + andra security-granskning först. ADR 0050
Accepted dokumenterar riktningen så den är redo; exekvering väntar på produktbehov.

### AWS-kodhygien (separat Klas-GO, ej i denna ADR-PR)

Döda AWS-workflows (`deploy-dev.yml`, `rds-ca-bundle-check.yml`) bör rensas
in-block (CTO axel 6) men `.github/`-touch kräver egen Klas-GO + egen
`chore(infra)`-commit — **defereras till separat PR** (ingår ej i denna docs/ADR-
PR). `AWSSDK.KeyManagementService` BEHÅLLS (KMS referens-impl, ADR 0066-
reversibilitet). `AWSSDK.SecretsManager` rensas när Migrate re-homas (TD-105).

> **Truth-sync 2026-07-25 (#808):** `rds-ca-bundle-check.yml` är **raderad** (egen
> `chore(ci)`-PR, BUILD.md §15). Den var inte passiv historik utan ett aktivt
> månadsjobb (cron `0 3 1 * *`, `issues: write`) som kunde fila spöken i backloggen
> om en riven RDS-instans. `deploy-dev.yml` **består** — den är passiv historik
> (`workflow_dispatch`-only sedan 2026-06-28) och retireras i AWS-teardown-PR:en.
> Klas-GO-kravet ovan för `.github/`-touch föregår det autonoma flödet (CLAUDE.md
> §6, 2026-06-25) och gäller inte längre; kravet på **egen `chore(ci)`/
> `chore(infra)`-commit** består och är uppfyllt. `infra/certs/rds-global-bundle.pem`
> BEHÅLLS — tre Dockerfiles `COPY` den; ägare för borttagning är #196/TD-106.

> **Truth-sync 2026-07-12 (#802):** ovanstående "`AWSSDK.KeyManagementService`
> BEHÅLLS ... ADR 0066-reversibilitet" gäller **inte längre**. Klas bekräftade
> "no AWS, ever" (2026-07-12) → `AWSSDK.KeyManagementService` + `AWSSDK.Core` +
> `KmsDataKeyProvider` är borttagna; fält-krypteringen är **Local-only**
> (`LocalDataKeyProvider`), Provider-default `"Local"` med DI-fail-fast på ett
> explicit icke-Local-värde. Lösningen har nu **0 Amazon-paket**. Config-switchen
> `FieldEncryption:Provider "Kms"/"Local"` (nämnd tidigare i denna ADR) är
> reducerad till enbart `"Local"`. Prod-master-nyckelns skyddsmodell kvarstår
> **TD-102** — självständig från den borttagna KMS-providern.

## Amendment 2026-07-18 — reverse-proxy-rutt-regeln korrigerad (#756)

**Beslutsfattare:** senior-cto-advisor (decision-maker, §9.2 — entydigt verdikt,
exekverar utan extra Klas-GO; override-yta noterad).
**Underlag:** `docs/reviews/2026-07-18-756-caddy-topology-cto.md` + dotnet-architect
(obligatorisk IaC/deploy-scope, ADR 0036-precedens) + code-reviewer.
**Kontext:** perf-audit-epik #737, finding `d1-caddy-api-prefix-shadows-bff`
(P3/docs-only nu, men cutover-kritisk — regeln skulle brytas i produktion).

6. **Beslut 4 reverse-proxy-routing korrigerad → Option B ("route-all-through-
   Next"):** den inline-amenderade rutt-regeln (`/api/*` → ASP.NET) var **fel**.
   Alla backend-endpoints (`MapGroup`) lever under `/api/v1/` (enda undantag =
   health-routes `/api/live` + `/api/ready` direkt under `/api/`, interna under
   Option B); **11 Next.js-BFF-route-handlers** lever direkt under `/api/` (prefixen
   `/api/jobb/*`, `/api/me/*`, `/api/cv/*`, `/api/foretag/lookup`,
   `/api/landing-stats`). En bred `/api/*` → ASP.NET vid edge hade skuggat de 11 →
   404/401 i produktion. **Korrigering:** all trafik → Next (`web:3000`); ASP.NET-
   API:t exponeras **aldrig** vid edge (inte publicerad via `ports:` till host);
   ingen `/api`-matcher vid edge. Grund: backend har noll publika konsumenter
   (ingen `NEXT_PUBLIC`-backend-URL, ingen tredjeparts-callback) → least privilege +
   YAGNI, och topologin **eliminerar defektklassen** (utan `/api`-matcher kan
   bred-prefix-skuggningen inte återuppstå). **Supersederar** den tidigare "hela
   origin (API + Next.js)"-/`/api/*`-formuleringen i både Beslut 4:s brödtext och
   Amendment 2026-06-14 punkt 5, samt `localhost:3000`-upstreamen i TD-106-scope-
   noten (Amendment 2026-06-14 ovan) → `web:3000` (Compose-service-DNS). Detaljer,
   lastbärande invarianter och TD-106-överlämning: se den daterade inline-
   amendmenten under Beslut 4 (SSOT för routingen). Ingen live-miljö påverkas (ingen
   Caddyfil finns ännu). Rör ingen kod (docs-only); security-auditors veto beväpnas
   vid TD-106:s build-tid, inte här.

## Amendment 2026-08-04 — värdbytet Hetzner → Netcup, Cloudflare bort, grind-deltat

**Beslutsfattare:** Klas Olsson (värdvalet + topologibesluten K1–K4, 2026-08-04).
**Underlag:** senior-cto-advisor (§9.6 decision-maker, artefakt-uppdelning + M-5:s
efterträdare, 2026-08-04); dotnet-architect (obligatorisk, IaC-scope, ADR 0036-precedens);
security-auditor (kant-posture, residens). **Rationalen i sin helhet:** ADR 0122 (lokal
ADR per ADR 0072 docs-privacy).

> **Denna amendment är auktoritativ för grindarna och är skriven för att kunna läsas
> ensam.** ADR 0122 är gitignorerad och finns i en worktree bara om docs-synken kördes.
> Saknas den är detta avsnitt tillräckligt — du missar ingen grind, bara rationalen.

### 1. Värd och sizing (supersederar Beslut 2 helt)

**Netcup RS 1000 G12** — x86 (AMD EPYC 9645), 4 **dedikerade** kärnor, **8 GB** DDR5 ECC,
256 GB NVMe, Debian 13, **Nürnberg**. Ersätter Hetzner CAX31 (ARM, 16 GB).

**Residensen är mätt, inte antagen:** RIPE ger `netname DE-NETCUP-KVM`, `country DE`,
geolokalisering Nürnberg (2026-08-03) ⇒ EU-resident, **ingen Kap. V-överföring införs**.
Detta **fullgör inte Art. 28**: ett signerat biträdesavtal med **Netcup** måste finnas
före första riktiga användardata, det är **Klas att teckna, aldrig CC**, och den
publicerade policyn namnger fortfarande Hetzner — det ägs av
[#1199](https://github.com/klasolsson81/jobbliggaren/issues/1199) och grindar första datan.

**Beslut 2:s grund för att avvisa 8 GB är mätt död.** Den vilade på
`JobTechStreamClient`s `MaxResponseContentBufferSize = 500 MB`. Mätt 2026-08-04 mot HEAD
`1b98d016`: den raden finns inte i `src/` — `DependencyInjection.cs:312-323` säger
uttryckligen att ingen cap sätts, eftersom båda wire-vägarna strömmar
(`ResponseHeadersRead` + `ReadAsStreamAsync` + per-element-deserialisering) och en cap
därför vore en **no-op**. Enda kvarvarande cap i `src/` är 1 MB på HIBP-klienten (rad
1595), som inte rör ingestion. **x86 gör dessutom hela ARM-risk-avsnittet moot.**

Två andra ben blev däremot **sämre** (mätt 2026-08-02 mot dev-DB): korpus 46k →
**106 071 annonser / 2 493 MB**, och `company_register` **1 066 938 rader / 405 MB**
fanns inte alls i juni. Domen är därför **marginell men körbar**, villkorad på fyra
punkter (§2). Det osäkraste talet är **Postgres steady-state RSS — härlett ur
diskstorlek, inte mätt**; [#196](https://github.com/klasolsson81/jobbliggaren/issues/196)
mäter det på riktig låda.

**`mem_limit`-doktrinen ovan är superseded.** "Generös/osatt cap på Postgres" vilade
uttryckligen på att 16 GB upplöste nollsummespelet; på 8 GB är det tillbaka, så **varje
tjänst cappas, Postgres inklusive** (~2 560 MiB). Redis körs `noeviction`, **inte**
`allkeys-lru` — den hade vräkt sessioner och gett tysta utloggningar.

### 2. De fyra kapacitetsvillkoren — del av beslutet, inte råd

1. **`next build` i CI, aldrig på lådan** (= Beslut 3:s build-in-CI-regel, nu lastbärande).
2. **`DOTNET_gcServer=0`** för Api **och** Worker.
3. **Explicit tunad Postgres** — inte defaults, mot 8 GB.
4. **zram i stället för diskswap.**

> **Villkor 4 bär två krav samtidigt** — kapacitet **och gate B-1** (master-nyckeln
> aldrig plaintext på disk). Levererat i #1196. Därför: **en swapfil under minnestryck
> bryter B-1. Lägg till RAM i stället.**

### 3. Cloudflare utgår helt (supersederar Beslut 4:s Cloudflare-halva)

Klas-beslut **K3**. Beslut 4 köpte fyra saker i en mening ("TLS-edge/DNS/CDN/DDoS"); var
och en behöver eget svar: **TLS-edge → Caddy direkt mot Let's Encrypt** · **DNS → Strato**
(redan auktoritativ) · **CDN → ingen** · **DDoS-absorption → ingen**, kantfiltret hos
Netcup är allt som finns · **origin-IP-lockdown → ingen efterträdare, död**.

**80/443 står därför öppna mot `any` i båda brandväggslagren. Det krävs för ACME HTTP-01
och får inte "rättas" mot M-5:s ursprungstext** — då dör certifikatutfärdandet.

### 4. Klas topologibeslut K1–K4

**K1** live först på `dev.jobbliggaren.se` (apex parkerad hos Strato) · **K2** åtkomst
grindad med basic auth i Caddy · **K3** Let's Encrypt direkt, ingen CDN · **K4**
backup-/PITR-retention **30 dagar**. K4 besvarar STOPP-4 — fönstret två ADR:er
uttryckligen förbjuder CC att uppfinna — och ger #197:s restore-drill ett tal.

### 5. Gate M-5 pensioneras på plats → **M-5a + M-5b**

M-5 var inte en grind utan tre klausuler med tre öden: "Full (strict)" är **uppfylld by
construction** (utan CDN finns inget sista ben) · origin-IP-lockdown har **ingen
efterträdare** · HSTS **överlever**.

**M-5a (Major — ärvd från M-5, ej omgraderad).** Caddy terminerar med publikt betrott
LE-cert via HTTP-01; **HSTS emitteras faktiskt i Production**; ingen klartextsträcka.

> **Lastbärande och mätt trasigt i dag (2026-08-04, HEAD `1b98d016`):** det finns **ingen
> `Alb`-sektion i någon `appsettings*.json`** ⇒ `AlbOptions.HttpsEnabled` = `false` i
> Production ⇒ `Program.cs:333` registrerar **aldrig** `UseHsts()` och `:338` aldrig
> `UseHttpsRedirection()`. `appsettings.Production.json` har ett `Hsts`-block vars **egen
> kommentar** säger att det är "ren konfig utan effekt" så länge flaggan är false. Enda
> injektorn var Terraforms `Alb__HttpsEnabled` i ECS-task-def:en, riven med ADR 0066.
> **HSTS ser konfigurerat ut och är inert.** Rättas i `AlbOptions → ReverseProxyOptions`-
> re-homet (#196). **Bevis avläses på svaret** (`curl -sI` visar
> `Strict-Transport-Security`), aldrig på konfigen.
>
> Kontrast, samma mätning: `ForwardedHeaders:KnownNetworks: []` **är** fail-loud —
> `ForwardedHeadersConfig.EnsureSafeForEnvironment` kastar utanför Development/Test.
> **Rate-limit-halvan degraderar högljutt, HSTS-halvan tyst.**

**M-5b (NY rad — severity ograderad; den tillhör security-auditor vid den obligatoriska
andra granskningen, och sätts inte av denna session).** Kantexponeringen är omitigerad:
ingen CDN, ingen WAF, ingen DDoS-absorption, ingen origin-IP-allowlist. Kompenserande
kontroll är **admission och topologi, aldrig filtrering**, och grinden gäller att
kontrollerna är **levande och mätta**:

1. **K2-grinden** ger 401 på varje oautentiserad request, med ACME-pathen som enda
   undantag — och undantaget bevisat att inte läcka något annat.
2. **Option B håller empiriskt** — cutover-curl-matrisen (redan skyldig sedan
   Amendment 2026-07-18), utvidgad till att bevisa `/api/v1/dev` och `/api/v1/admin/*`
   onåbara utifrån.
3. **Per-IP-rate-limiting fungerar**: `ForwardedHeaders:KnownNetworks` re-homad från
   ALB:s VPC-CIDR till Caddy/Docker-nätets. Cloudflares bortfall **befordrar detta från
   korrekthetspost till stackens enda per-IP-kontroll.**
4. **`forward policy drop`** löses med riktade `iif`/`oif`-accepts för Docker-bryggan,
   **aldrig `policy accept`** — och kantens **IPv6-halva mäts före den växlingen**; den
   är i dag omätt.

**Restrisk, utskriven och ograderad av denna session:** en volymetrisk flod eller
TLS-handskakningsflod når en 8 GB-singelbox utan något framför, och basic auth prövas
**efter** TCP+TLS och försvarar därför inte tillgänglighet — Beslut 4:s egen negativa
punkt "singel-box blast-radius" har nu ingen uppströmsabsorbent, vilket är en **strikt
försämring av en risk ADR 0050 redan accepterade**. Ingen WAF eller bot-filtrering;
applagerabuse mot `/jobb`- och `/foretag`-sökytorna når Postgres på samma låda. Under
K1+K2 är exponeringen låg i dev-fasen; **kontrollmängden måste omverifieras när grinden
tas bort** — och ska grinden stå kvar för de första testanvändarna måste det skrivas
ner, inte antas.

### 6. Gate M-6 — fail2ban-klausulen

`fail2ban` är **inte installerad**. Den är ersatt av källrestriktion i två lager: en
kantregel som släpper port 22 bara från operatörens adress, och `from="…"` i
`authorized_keys` — det senare oberoende av Netcups kontrollplan. Med
`AuthenticationMethods publickey`, `AllowUsers jpadmin` och `PermitRootLogin no` skulle
fail2ban försvara en autentiseringsväg som inte finns, mot en population som inte når
porten, och samtidigt lägga till en root-körande loggparser som läser
angriparkontrollerad indata.

**Två gränser på den domen, och de gäller:** den täcker **bara SSH** (när 80/443 får en
riktig lyssnare är HTTP-abuse en egen fråga, se M-5b), och M-6 kräver en härdnings-
**baseline**, inte fail2ban som produkt — baselinen är mätt grön utan den (#1196).

> **STATUS: avvikelsen är REGISTRERAD, ratificeringen väntar på Klas GO.** Klas
> ratificerade 2026-08-04 **värdvalet**; ingenting i underlaget säger att han
> ratificerade fail2ban-utelämnandet, och CLAUDE.md §9.6 gör en säkerhetskoncession till
> **Klas att bevilja, aldrig sessionens att hävda**. Raden läses därför inte som
> "accepterad" förrän GO:t är registrerat här.

### 7. Backup: kraven består, målet är öppet

Beslut 4:s **mål** (Hetzner-EU Storage Box) faller med värdbytet. **Kraven består
ordagrant:** klient-side age-kryptering före upload **oavsett mål**, EU-jurisdiktion,
definierad rotation — nu med ett tal, **K4: 30 dagar** — och en **testad** restore-drill
före första riktiga data (M-4). **Målet är inte valt och inte verifierat**; det ägs av
[#197](https://github.com/klasolsson81/jobbliggaren/issues/197). **Cloudflare R2 är redan
uttryckligen avvisat** i Amendment 2026-06-08 på CLOUD Act-grund och får inte
återföreslås utan att security-auditor väger age-krypteringen mot Kap. V.

### 8. Icke-supersessions-staket — vad som står kvar oförändrat

Så att ingen läsare får två bilder. **Detta faller INTE:**

- **Beslut 1** (full AWS-exit) — orört.
- **Beslut 4:s `Amendment 2026-07-18`** (Option B, "route-all-through-Next") **i sin
  helhet, alla sex lastbärande invarianter.** Den är provider-oberoende: beslutad på
  applikationens form, inte på Cloudflare. **Enda rättelsen** är invariant 5:s parentes
  "*detta ändå moot*", som är falsk under K3 — ACME HTTP-01 körs skarpt och invarianten
  blir **kritisk** (se bannern vid Beslut 4).
- **Beslut 3:s substans** — Vercel-exiten, FE som co-tenant `next start`-container bakom
  Caddy, och **den bindande build-in-CI-regeln**. Endast värdreferensen "på CAX31" dör.
- **Beslut 2:s topologi** — en låda, Docker Compose, co-tenant Postgres och Redis, ingen
  separat managed DB (Ubicloud fortsatt avvisad).
- **Gates B-1, B-2, M-1, M-2, M-3, M-4** (målreferensen undantagen, §7) **och M-6** minus
  fail2ban-klausulen.
- **Kravet på en obligatorisk andra security-auditor-granskning av den faktiska
  prod-konfigurationen före första beta-data.** Ingen designdom, inklusive denna,
  ersätter den.
- **Rollback-modellen** — lokal Compose-stack som paritets-baseline; DNS-cutover som den
  reversibla flippen (TTL 300 s ⇒ fem minuter att göra, fem att ångra).

### 9. Kostnaden

ADR 0050:s "~€19/mån totalt" är **superseded utan ersättningssiffra**: ingen prisuppgift
för denna låda finns i något mätt underlag, och att uppfinna en vore ett tal ingen
domare satt.

### 10. Konsekvens som inte fanns i Beslut 4

**Utgående SMTP är blockerat hos leverantören.** Netcups `Mail block` är på som standard
och droppar 25/465/587. Det är ett andra, oberoende skäl att transaktionsmejl går över
leverantörens **HTTPS-API** (Resend i dag, SES planerat) och **aldrig SMTP** — och ett
skäl att aldrig be Netcup öppna 587.

**Netcup-snapshots är inte deploy-rollback:** copy-on-write, kräver 50 % ledig disk, och
bara *offline*-snapshots är konsistenta — och **en enda exportabel snapshot återstår**
(mätt 2026-08-03). Primär rollback är image-tag-rollback (sekunder); snapshotens rätta
roll är **före migreringar**, som en image-rollback inte kan ångra.

## Relaterade beslut

- **ADR 0005** — kostnadsskydd/launch-gating. Post-migration blir Budget
  Actions/Bedrock-deny/registrations_open i stort sett moot. **Relevans-skifte,
  ej supersession** — ADR 0005-text ändras inte; flaggas i Block 4.
- **ADR 0019** — direct-push/granskningsspärrar. Oförändrad; migration följer
  samma STOPP-disciplin.
- **ADR 0049** — TD-13 envelope-encryption. KMS-beroendet **LÖST** via ADR 0066
  `LocalDataKeyProvider` (ej längre migrations-blocker). Kvarvarande Hetzner-
  prod-härdning + rotation = **TD-102** (ADR 0049-amendment-scope, M-2/M-3).
- **ADR 0066** — AWS dev-stack-teardown. Löste KMS-beroendet (`LocalDataKeyProvider`)
  och gjorde rollback-storyn ("behåll AWS körande") ogiltig. Komplementär:
  ADR 0066 var temporär semester-pause, ADR 0050 är permanent provider-exit.
- **ADR 0051** — AI-provider Anthropic Direct/Bedrock utgår. Möjliggör ren
  exit (Beslut 1). AI-transfer (US, opt-in) är separat grindad, rör ej VPS-residens.
- **ADR 0065** — PR-flöde + automerge. Denna amendment levereras via PR.
- **VPS-fas-arbetet, alla gated på denna ADR** (TD-registret är pensionerat, ADR 0121 —
  framåtpekarna konverterade till sina issues per CLAUDE.md §1.6):
  [#183](https://github.com/klasolsson81/jobbliggaren/issues/183) (mejl-väg, f.d. TD-101) ·
  [#198](https://github.com/klasolsson81/jobbliggaren/issues/198) (master-nyckel + rotation, f.d. TD-102) ·
  [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175) (produktions-logg-sink; f.d. TD-104 var Seq-wiringen och är **levererad/stängd** — den kvarvarande sänkan är #1175) ·
  [#199](https://github.com/klasolsson81/jobbliggaren/issues/199) (Migrate-re-home, f.d. TD-105 — **levererad** i PR #236) ·
  [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) (Compose-stack + proxy + härdning, f.d. TD-106) ·
  [#197](https://github.com/klasolsson81/jobbliggaren/issues/197) (krypterad EU-backup, f.d. TD-107).
  Tillkommit sedan: [#1199](https://github.com/klasolsson81/jobbliggaren/issues/1199)
  (publicerad policy namnger fel personuppgiftsbiträde — **grindar första riktiga datan**).
- **ADR 0122** — värd, sizing, topologi och kapacitetsvillkor. Supersederar Beslut 2/3/4
  (delvis) + gate M-5; se banner överst och `Amendment 2026-08-04` nedan.
- **BUILD.md Bilaga B** — planerad `NNNN-aws-over-azure.md`. Denna ADR fyller
  slotten med motsatt slutsats (moln-exit, ej moln-byte). adr-keeper
  uppdaterar "Planerade ADRs"-listan.

## Referenser

- `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` — Block 2/3-discovery,
  web-verifierade priser/sizing (Hetzner CX-plans, pris-justering 2026-04-01)
- `docs/reviews/2026-06-08-adr-0050-aws-exit-hetzner-architect.md` (sizing/deploy/
  migrations-dom), `-security.md` (2 Blockers + 4 Majors, KMS-omframing-bekräftelse),
  `-cto.md` (decision-maker, 10 axlar)
- dotnet-architect IaC-/sizing-review 2026-05-19 + 2026-06-08
- senior-cto-advisor §9.6-triage 2026-05-19 + decision-maker-rond 2026-06-08
- security-auditor secrets/master-nyckel/PII-residens-dom 2026-06-08
- ADR 0005 / 0019 / 0049 / 0051 / 0065 / 0066 · CLAUDE.md §2.5 (perf), §9.2, §9.6
- Hetzner Cloud pricing (web-verifierat 2026-06-08): CX32 ~€6,80, CAX31 ~€15,99,
  CAX21; EU-DC Falkenstein/Nuremberg/Helsinki; ingen native managed-PG (Ubicloud
  tredjepart ~$15/mån)
- Microsoft Learn — ASP.NET Core forwarded-headers/proxy; .NET linux-arm64 tier-1
- Nygard *Release It!* (Bulkheads/Steady State); Ford/Parsons/Kua *Building
  Evolutionary Architectures* (two-way-door); Winters et al. *SWE at Google*
  (value over activity); GDPR Art. 32/17/44–46 + EDPB CEF 2025
