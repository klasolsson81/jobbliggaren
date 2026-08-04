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
> Se `Amendment 2026-08-04` längre ned (före *Relaterade beslut*).

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

> **DELVIS superseded 2026-08-04 — värdreferensen OCH Cloudflare-meningen.** Utöver
> värdnamnet faller Amendment 2026-06-14:s mening *"Cloudflare behålls = edge-only
> (TLS/DNS/proxy/DDoS …)"*: under K3 finns ingen Cloudflare alls. **Slutsatsen i det
> stycket stärks dock, den försvagas inte** — utan Cloudflare passerar ingen US-part
> kedjan över huvud taget, så Kap. V-berättelsen blir strikt renare än 2026-06-14.
> Läs i övrigt "CAX31"
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
> - **TVÅ rättelser i den** (samma räkning som §8 — se den för full text). **Den andra
>   är kritisk att läsa FÖRE invariant 5 nedan.**
>   1. **Slutmeningen i "Korrigerad topologi"** — *"Cloudflare 'Full (strict)' +
>      origin-cert + origin-IP-lockdown + HSTS är oförändrade"* — är **falsk under K3**:
>      "Full (strict)" är moot, origin-cert utgår, origin-IP-lockdown har **ingen**
>      efterträdare (M-5b), HSTS överlever men **byter emitter** (M-5a).
>   2. **Invariant 5:s avslutande parentes** — *"(Med Cloudflare Full (strict) +
>      origin-cert är detta ändå moot.)"* — **är falsk under K3.** Utan CDN kör Caddy
>   ACME **skarpt**, så invariant 5 går från moot till **lastbärande**: det måste bevisas
>   vid cutover att K2:s basic auth-grind inte skuggar `/.well-known/acme-challenge/*`.
>
>   **Felmodellen är dock inte "tyst död" utan något värre — tyst FALLBACK.** Caddy har
>   **både HTTP-01 och TLS-ALPN-01 aktiverade som standard** och väljer slumpvis, sedan
>   adaptivt efter vad som lyckats ([Caddy — Automatic HTTPS](https://caddyserver.com/docs/automatic-https),
>   läst 2026-08-04). Med både 80 och 443 öppna finns alltså **två** vägar: en skuggad
>   HTTP-01 dör inte, den **faller tillbaka tyst till TLS-ALPN-01**, och defekten göms
>   tills något stänger ALPN-vägen — varvid förnyelsen dör **utan koppling till den
>   ändring som orsakade det**. ACME-hanterarens företräde mot en site-nivå-`basic_auth`
>   är **omätt** — och det är just därför beviset måste vara en tvingad utfärdning och
>   inte en curl. *(En tidigare version citerade Caddys mening om att inskjutna rutter
>   hamnar "after your routes with a host matcher" som stöd. Den meningen handlar om
>   HTTP→HTTPS-**redirect**-rutterna, inte om challenge-hanteraren — sann om sitt
>   underlag, falsk om sitt ämne.)*
>
>   **Därför duger ingen curl mot ACME-pathen som bevis** — den säger ingenting om vilken
>   challenge Caddy faktiskt använde. Beviset vid cutover är en **tvingad utfärdning per
>   challenge-typ** (en enabled i taget, eller LE staging), och **#196 får inte
>   konfigurera bort TLS-ALPN-01 utan att först ha bevisat HTTP-01 skarpt.**
>   Invariantens text i övrigt är oförändrad och gäller.

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

> **⚠ Delvis superseded 2026-08-04.** Kostnadsposten nedan (~€16/mån CAX31 + ~€3/mån
> EU-backup ≈ **~€19/mån**) är superseded **utan ersättningssiffra** — ingen prisuppgift
> för Netcup-lådan finns i något mätt underlag (`Amendment 2026-08-04` §9). Varje
> "CAX31" nedan läses som Netcup-lådan. Och SPOF-punkten under *Negativa* säger att
> singel-box-valet är "kompenserat av CAX31:s headroom" — **båda kompensationerna är nu
> borta**: headroomet halverades (16 → 8 GB) och uppströmsabsorbenten föll med Cloudflare
> (M-5b). Kvar som kompensation står enbart build-in-CI-regeln.

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

> **⚠ Delvis superseded 2026-08-04.** Backup-**målet** i bullet 1 (Hetzner-EU Storage
> Box) är dött och ersättaren är **inte vald** (`Amendment 2026-08-04` §7 — kraven består
> och får två till). Bullet 2:s "DNS-01 via Cloudflare-plugin" är dött (K3 ⇒ HTTP-01).
> Bullet 4:s "validera CAX31-sizing" läses som Netcup-lådan, och lasttestet mäter nu mot
> 106 071 annonser, inte 46k.

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

> **⚠ SUPERSEDED 2026-08-04 av `Amendment 2026-08-04` — hela sektionen.** Den avvisar
> **8 GB-boxar** (CX32, CAX21), vilket är exakt den sizing som är köpt och i drift. Den
> avvisningens **grund är mätt död** (`MaxResponseContentBufferSize = 500 MB` finns inte
> i `src/`, mätt 2026-08-04 mot HEAD `1b98d016`), och leverantörsjämförelsen är
> överspelad: Hetzner är av bordet, Netcup är värden. Sektionen står kvar som protokoll
> över vad som vägdes 2026-06-08 — **den är inte en gällande dom över dagens låda.**
> Amendmentens §1 bär den aktuella domen ("marginell men körbar", fyra villkor), och
> Amendment §Alternativ i ADR 0122 bär de alternativ som faktiskt vägdes 2026-08-04.

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

> **⚠ Delvis superseded 2026-08-04.** "Hetzner-box" läses som Netcup-lådan; korpuset är
> 106 071 annonser, inte 46k. Rollback-**modellen** består oförändrad — men parentesen
> "ej-cutad DNS (**Cloudflare**)" är död: DNS ligger hos **Strato** (mätt 2026-08-02),
> och det är där cutovern görs och ångras (TTL 300 s).

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

> **⚠ SUPERSEDED 2026-08-04 — hela sektionen.** Doktrinen nedan (hård cap på Worker +
> Redis, **generös/osatt cap på Postgres**) vilar uttryckligen på att "CAX31:s 16 GB
> upplöser det nollsummespel detta vore på 8 GB". Lådan **har** 8 GB, så nollsummespelet
> är tillbaka och **allt cappas, Postgres inklusive**. Gällande allokering,
> cgroup-page-cache-priset och Redis `maxmemory`-fällan i
> `Amendment 2026-08-04` **§1**; `memswap_limit`-kravet i **§2** (villkor 4).

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
| B-1 | Master-nyckel ALDRIG plaintext-på-disk på beta-VPS (systemd-credentials TPM-bunden el. sops+age→tmpfs; plaintext OK bara lokalt) | Blocker | [#198](https://github.com/klasolsson81/jobbliggaren/issues/198) (f.d. TD-102) |
| B-2 | Gitleaks/historik-scan: ingen master-nyckel/cred committad; rotation om läckt | Blocker | **Verifierad GRÖN 2026-06-08** (`appsettings.Local.json` i .gitignore, aldrig committad; inget nyckel-värde i historik) |
| M-3 | Körbar idempotent master-nyckel-re-wrap-rotation + kadens (minst årlig + händelse-driven vid box-kompromiss/offboarding) | Major | [#198](https://github.com/klasolsson81/jobbliggaren/issues/198) (f.d. TD-102) |
| M-4 | pg_dump klient-side-krypterad + backup-retention/rotation definierad + EU-jurisdiktion (+ två krav 2026-08-04, se `Amendment 2026-08-04` §7) | Major | [#197](https://github.com/klasolsson81/jobbliggaren/issues/197) (f.d. TD-107) |
| M-5 | ~~Cloudflare "Full (strict)" + origin-IP-lockdown (bara CF-IP på 443) + HSTS~~ | Major | **SUPERSEDED 2026-08-04 → M-5a + M-5b** (se `Amendment 2026-08-04`) |
| **M-5a** | **Origin-TLS är hela TLS-historien:** Caddy terminerar med publikt betrott LE-cert (HTTP-01 **eller** TLS-ALPN-01 — se §5), **HSTS emitteras faktiskt i Production på BÅDA svarsvägarna** (Caddy och Next — de täcker olika svar), ingen klartextsträcka. **Bevisas på det OAUTENTISERADE 401-svaret, inte bara på ett autentiserat 200 — och aldrig på konfigen** (se §5) | Major (ärvd från M-5) | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) |
| **M-5b** | **Kantexponeringen är omitigerad** (ingen CDN/WAF/DDoS-absorption, ingen origin-IP-allowlist): kompenserande kontroll är **admission + topologi**, aldrig filtrering — K2-grinden, Option B, per-IP-rate-limit, riktade `forward`-accepts | **Major** (satt av security-auditor 2026-08-04 vid granskningen av PR #1200) — bär tre villkor, se `Amendment 2026-08-04` §5 | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) |
| M-6 | VPS-härdnings-baseline (SSH-key-only, brandvägg, ~~fail2ban~~, auto-patch, PG/Redis ej publika, swap/core-dump-hygien mot master-nyckel-minnesläck) | Major | [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) · **baseline i övrigt mätt grön** ([#1196](https://github.com/klasolsson81/jobbliggaren/pull/1196)) · **fail2ban-klausulen: avvikelse REGISTRERAD, ratificering väntar på Klas GO** (`Amendment 2026-08-04`) |
| **M-7** | **Detektionsförmåga** — grinden ställs på **skyldighet, inte mekanism**. Rättslig grund (satt av security-auditor, som äger fyndet — en tidigare version av denna rad skrev om grunden och försvagade den): **Art. 32(1)(b) + Art. 33 läst med Recital 87**, som uttryckligen kräver åtgärder för att *"establish immediately whether a personal data breach has taken place"* — detektionsplikten läses alltså in i anmälningsregimen, Art. 33 är inte bara följden. **Art. 5(2)** (accountability) bär kravet att förmågan ska vara **visbar**. *(Art. 32(1)(d) gäller återkommande testning och utvärdering av åtgärderna — pentest och kontrollutvärdering — och är inte grunden för detektionsförmågan.)* Utan den är ADR 0123:s scope-gräns overkställbar (lokal ADR; `Amendment 2026-08-04` §6b bär skälet i sin helhet) | **Major** (satt av security-auditor 2026-08-04) — **blir Blocker om ADR 0123 fortfarande är obeviljad eller omitigerad vid första riktiga data**: acceptansens utgångsvillkor vilar då på en detektionsförmåga som inte finns | [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201) — **värd-detektion + alerting ägs av [#196](https://github.com/klasolsson81/jobbliggaren/issues/196), nyckelåtkomst-detektion av [#198](https://github.com/klasolsson81/jobbliggaren/issues/198)** |
| M-1 | ADR 0050 KMS-blocker-prosa amenderad → TD-102-omframing | Major | **Åtgärdad denna amendment** |
| M-2 | ADR 0049-amendment: self-managed master-nyckels prod-skyddsmodell + accepterad minne-restrisk + namngiven skala-trigger för extern KV/HSM | Major | [#198](https://github.com/klasolsson81/jobbliggaren/issues/198) (f.d. TD-102, ADR 0049-amendment-scope) |

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

**Residensen är mätt så långt RIPE når:** `netname DE-NETCUP-KVM`, `country DE`,
geolokalisering Nürnberg (2026-08-03) ⇒ värdbenet är EU-resident, så **ingen
Kap. V-överföring införs** av värdbytet. *(Var precis om bevisstyrkan: `country:` och
`netname:` är registreringsattribut som LIR:en själv sätter och bevisar inte fysisk
DC-placering — slutsatsen bärs av Netcup som tyskt bolag plus vald lokation. Netcups
ägarstruktur och underbiträdeskedja är **omätt** och ägs av #1199:s DPA-arbete.)*
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
tjänst cappas, Postgres inklusive** (~2 560 MiB). ADR 0050:s farhåga (en hård PG-cap
OOM-dödar mitt i query) är verklig men åtgärdas av **tuning**, inte av **frånvaro av
cap**: PG:s OOM-exponering inne i en cgroup kommer från `work_mem` × samtidiga
sorts/hashes och `maintenance_work_mem` — båda PG-sidiga rattar.

> **Capen kostar mer än anon-RSS, och det ändrar tabellens slutrad.** I **cgroup v2**
> debiteras page cache den cgroup som first-faultar sidan. Postgres läser sina egna
> datafiler ⇒ cachen debiteras Postgres cgroup ⇒ `mem_limit: 2 560 MiB` cappar **även
> dess file cache**. Mot det mätta korpuset (`job_ads` 2 493 MB + `company_register`
> 405 MB) blir effektiv PG-cache ≈ `2 560 − anon-RSS`, alltså några hundra MiB —
> **inte** de ~5 GiB slutraden nedan antyder. De ~5 GiB ligger **utanför** Postgres
> cgroup och är för Postgres oåtkomliga. Det förklarar också varför "~1 900 MiB
> förväntat" känns högt: talet är härlett ur diskstorlek, vilket gör det till ett
> *cache*-mått och inte ett *RSS*-mått — den raden blandar två storheter.
>
> **Konsekvens för villkor 3:** capen är den storhet tuningen ska **deriveras UR**
> (`shared_buffers`, `work_mem` × `max_connections`, `maintenance_work_mem`), inte en
> siffra bredvid den. #196 mäter **både** anon-RSS och cache-hit-ratio på riktig låda.

**Redis: `maxmemory` OCH `maxmemory-policy` krävs — den ena utan den andra är värre än
ingen.** `noeviction` är en **Redis**-policy som bara träder i kraft när Redis egen
`maxmemory` är satt. I dag sätter compose-filen ingen (`redis:8.6-alpine`, ingen
`command:`, ingen `redis.conf`), så Redis växer förbi container-capen och **kärnan
OOM-dödar containern** — då försvinner *varje* session på en gång, vilket är strikt värre
än de tysta utloggningar `allkeys-lru` valdes bort för att undvika. Sätt **båda**
(`maxmemory 400mb` + `maxmemory-policy noeviction`). `volatile-lru` är ett tredje
alternativ och en #196-fråga: det kräver att varje nyckel i store:n bär TTL, och Redis
bär här session-store, cooldown-gates, rate-limiting och landing-stats-cache.

### 2. De fyra kapacitetsvillkoren — del av beslutet, inte råd

1. **`next build` i CI, aldrig på lådan** (= Beslut 3:s build-in-CI-regel, nu lastbärande).
2. **`DOTNET_gcServer=0`** för Api **och** Worker.
3. **Explicit tunad Postgres** — inte defaults, mot 8 GB.
4. **zram i stället för diskswap.**

> **Villkor 4 bär två krav samtidigt** — kapacitet **och gate B-1** (master-nyckeln
> aldrig plaintext på disk). Levererat i #1196. Därför: **en swapfil under minnestryck
> bryter B-1. Lägg till RAM i stället.**
>
> **`mem_limit` binder bara minnesaxeln.** Utan `memswap_limit` lämnas swap obunden, så
> en container som slår i sitt tak **swappar in i zram — samma 8 GiB fysiskt RAM, bara
> komprimerat**. Tabellens summa binder alltså inte den fysiska konsumtionen. #196 sätter
> därför `memswap_limit == mem_limit` (dvs `memory.swap.max=0`) på **api och worker** —
> de processer som kommer att bära masternyckeln. Det är samtidigt en **strikt starkare
> B-1-posture** än "ingen diskswap": nyckeln kan då inte nå zram heller, bara anonymt RAM.
>
> **Och zram är inte headroom.** Den byter CPU mot en ~2–3× multiplikator på swappade
> anonyma sidor, på en låda vars dimensionerande workload (nattlig ingestion) redan är
> CPU-hungrig. Den är den B-1-säkra swappen — inte extra kapacitet.

**Två noter om villkor 2, så att den som verifierar inte drar fel slutsats.**
`DOTNET_gcServer=0` är en **äkta beteendeändring för Api** (`Microsoft.NET.Sdk.Web` sätter
`ServerGarbageCollection=true`) men en **no-op för Worker** (`Microsoft.NET.Sdk.Worker`
gör det inte — workstation GC är redan default där). Villkoret är alltså rätt för Api och
defensiv dokumentation för Worker. **Motverkande effekt, utskriven:** Server→Workstation
kostar throughput på Api under samtidig last, vilket rör ADR 0045:s hot-path-budgetar —
NBomber mot dem hör till #196:s validering. **Gratis i motsatt riktning:** .NET läser
cgroup-gränsen och sätter `GCHeapHardLimit` till ~75 % av container-capen automatiskt
(Api på 1 024 MiB ⇒ ~768 MiB heap-tak, med `OutOfMemoryException` före kärnans
OOM-killer), vilket gör tabellen robustare än den ser ut.

### 3. Cloudflare utgår helt (supersederar Beslut 4:s Cloudflare-halva)

Klas-beslut **K3**. Beslut 4 köpte fyra saker i en mening ("TLS-edge/DNS/CDN/DDoS"); var
och en behöver eget svar: **TLS-edge → Caddy direkt mot Let's Encrypt** · **DNS → Strato**
(redan auktoritativ — mätt 2026-08-02 mot `8.8.8.8`/`1.1.1.1` + `.se`-registret: NS hos
Strato, **ingen CAA-post** så LE inte är spärrad, subdomäner fria utan wildcard, TTL 300 s) · **CDN → ingen** · **DDoS-absorption → ingen**, kantfiltret hos
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
> Production ⇒ grinden på `Program.cs:333` registrerar **aldrig** `UseHsts()` (anropet
> ligger på `:335`) och grinden på `:338` aldrig `UseHttpsRedirection()` (`:340`).
> `appsettings.Production.json` har ett `Hsts`-block vars **egen kommentar** säger att det
> är "ren konfig utan effekt" så länge flaggan är false. Enda **levande** injektorn var
> Terraforms `Alb__HttpsEnabled` i den **deployade** ECS-task-def:en, som ADR 0066 rev —
> men Terraform-**koden** är medvetet bevarad och bär injektionen kvar
> (`infra/terraform/environments/dev/main.tf:377`, CLAUDE.md §11). Ingen injektor finns
> alltså på Netcup-lådan; trädet är inte residual.
> **HSTS ser konfigurerat ut och är inert. Fixen ligger INTE i ASP.NET — se
> EMITTER-noten nedan.** **Bevis avläses på svaret** (`curl -sI` visar
> `Strict-Transport-Security`), aldrig på konfigen.
>
> Kontrast, samma mätning: `ForwardedHeaders:KnownNetworks: []` **är** fail-loud —
> `ForwardedHeadersConfig.EnsureSafeForEnvironment` kastar utanför Development/Test.
> **Rate-limit-halvan degraderar högljutt, HSTS-halvan tyst.**
>
> **EMITTERN ÄR CADDY, INTE ASP.NET — och det är inte samma fix som ovan.** Under
> Option B når API:ts svars-headers **aldrig** en browser: all browser-trafik
> terminerar i Caddy → Next, och API:t nås bara server-side över internnätet. En
> `Strict-Transport-Security` satt av `app.UseHsts()` hamnar i ett svar som en
> Next-route-handler konsumerar och kastar. Mätt 2026-08-04 mot HEAD `1b98d016`:
> `buildSecurityHeaders` (`web/…/lib/security/security-headers.ts`, monterad via
> `next.config.ts` `headers()` på `source: "/(.*)"`) emitterar CSP, `X-Frame-Options`,
> `X-Content-Type-Options`, `Referrer-Policy` och `Permissions-Policy` — **ingen HSTS**,
> noll träffar på `Strict-Transport-Security` i hela `web/`. Och **Caddy v2 lägger inte
> till HSTS automatiskt** (förslaget avvisat, `caddyserver/caddy#4751`, läst 2026-08-04).
>
> **Nettot: i dag finns ingen komponent i stacken som kan emittera browser-synlig
> HSTS.** Grinden ägs därför av **Caddyfilen i #196**
> (`header Strict-Transport-Security "max-age=31536000; includeSubDomains"`) och, **för
> Next-vägen**, av `buildSecurityHeaders` — den har redan ett kontraktstest som fryser
> mängden och ger grinden en regressionsspärr en Caddyfil saknar. Next-vägen är ett
> **komplement, aldrig ett substitut**: se 401-klausulen nedan. Valet av *hur* är #196:s;
> att **båda** svarsvägarna bär headern är grinden.
> **`AlbOptions → ReverseProxyOptions`-re-homet är fortsatt skyldigt — men för
> `ForwardedHeaders:KnownNetworks` (M-5b punkt 3), INTE för HSTS.**
>
> **A1-tillägg (levererat 2026-08-04, mätt mot HEAD `16aced64`): emittern är avgjord,
> ingen halva byggd, och det är avsiktligt.** Caddy-halvan **går inte att bygga i dag** —
> noll filer matchar `Caddyfile`/`caddy*` i repot, så det finns ingen fil att lägga
> `header Strict-Transport-Security` i; den uppstår först med #196:s compose-stack.
> Next-halvan **går** att bygga — `buildSecurityHeaders` emitterar fem headers och HSTS
> är inte en av dem, och dess kontraktstest fryser mängden med `toEqual` på den ordnade
> nyckellistan, så tillägget är en tvåfilsändring med regressionsspärr. Den gate:as på
> den `isDev`-flagga som redan trådas genom funktionen, annars HTTPS-låser `next dev`
> localhost i `MaxAgeDays` — exakt felet `HstsOptions` varnar för. Men det är en
> `web/`-ändring med **egen change-reason**, så den landade inte i A1:s Api-PR.
> **Grinden är oförändrad: båda svarsvägarna, bevisat på 401:an.** Att en halva är
> byggbar i dag flyttar inte grinden till den halvan.
>
> **Vad A1 däremot ändrade i ASP.NET-halvan:** `HttpsEnabled` läses inte längre som en
> TODO. Kommentarerna sa "aktiveras vid ADR 0026-trigger", vilket under Option B hade
> lett en läsare att flippa flaggan vid lansering och **bryta appen** — Next når API:t
> över internt HTTP, så `UseHttpsRedirection()` hade svarat 307 på varje internt anrop,
> och `UseHsts()` hade ändå inte nått en browser. Flaggan är nu dokumenterad som
> **korrekt `false` under Option B**, och `Hsts`-blocket som defence-in-depth för en
> topologi där API:t åter är kant-exponerat — inte som ett väntande lanseringssteg.
>
> **Namnet `AlbOptions` lever kvar i äldre text i denna ADR och ska inte städas bort.**
> `Amendment 2026-07-18` (TD-106-överlämningen) och mätningen ovan i denna amendment
> skrevs båda när typen hette så; de är protokoll över vad som gällde då, inte
> instruktioner. Typen heter sedan 2026-08-04 `ReverseProxyOptions` och
> konfigsektionen `ReverseProxy` — den gamla `Alb`-nyckeln har **ingen**
> övergångsbindning, vilket `ReverseProxyOptionsTests` pinnar som körbart faktum.
>
> **Beviset läses på det OAUTENTISERADE 401-svaret**, inte bara på ett autentiserat 200.
> Caddy svarar 401 utan att nå Next, så en HSTS enbart i `buildSecurityHeaders` finns
> **inte** på det svaret — och på en basic auth-grindad dev-låda är 401 det första och
> ofta enda svar en browser möter, vilket är precis det ögonblick HSTS finns för. Vilken
> komponent som emitterar är **#196:s val**; att **båda** svarsvägarna bär headern är
> grinden. *(Husprecedens: `Program.cs` ~344 stämplar via `OnStarting` uttryckligen "on
> EVERY response — the 200, the 404, the 401 auth challenge, and a 405 — not only the
> happy path".)*
>
> **CAA + Strato (klausul under M-5a).** Utan CDN är "origin-TLS är hela TLS-historien"
> bokstavligt sann: den som tar Strato-kontot får giltiga certifikat och total MITM.
> `jobbliggaren.se` saknar CAA-post (mätt 2026-08-02 mot `8.8.8.8`/`1.1.1.1` + `.se`-registret)
> — vilket möjliggör Let's Encrypt, men också innebär att **vilken CA som helst** kan
> utfärda. Billig stängning: CAA låst till `letsencrypt.org` + verifierad 2FA på
> Strato-kontot. Inte orsakad av värdbytet; blir lastbärande i och med att CDN:et faller.

**M-5b (NY rad — severity `Major`, satt av security-auditor 2026-08-04 vid hennes
granskning av PR #1200; denna session graderade den inte. `docs/reviews/` är gitignorerad,
så granskarens namn + datum ÄR domarregistret här).** Tre villkor hör till graden: (i) den är Major **som
grind före första riktiga data** och **inget skäl att hålla tillbaka en tom, basic
auth-grindad dev-låda**; (ii) den ska **omgraderas, inte ärvas**, vid den obligatoriska
andra granskningen — "ingen DDoS-absorption" blir en Art. 32(1)(b)-tillgänglighetsfråga
så snart riktiga användare finns; (iii) kontrollmängden är rätt i **form** (admission och
topologi, aldrig filtrering — rätt svar när det inte finns något uppströms att filtrera
med) men var ofullständig i **innehåll** tills punkt 5–7 nedan lades till.

Kantexponeringen är omitigerad:
ingen CDN, ingen WAF, ingen DDoS-absorption, ingen origin-IP-allowlist. Kompenserande
kontroll är **admission och topologi, aldrig filtrering**, och grinden gäller att
kontrollerna är **levande och mätta**:

1. **K2-grinden** ger 401 på varje oautentiserad request, med ACME-pathen som enda
   undantag — och undantaget bevisat att inte läcka något annat.
2. **Option B håller empiriskt** — cutover-curl-matrisen (redan skyldig sedan
   Amendment 2026-07-18), utvidgad till att bevisa `/api/v1/dev` och `/api/v1/admin/*`
   onåbara utifrån.
3. **Per-IP-rate-limiting fungerar.** Cloudflares bortfall **befordrar detta från
   korrekthetspost till stackens enda per-IP-kontroll.** Grinden ställs på att
   kontrollen **mäts levande**, aldrig på att en konfignyckel fått ett värde.

   **Re-homet av `ForwardedHeaders:KnownNetworks` är NÖDVÄNDIGT MEN INTE TILLRÄCKLIGT —
   den tidigare formuleringen, som lade hela villkoret på re-homet, är falsifierad.**
   Mätt 2026-08-04 mot HEAD `16aced64`: under Option B når **ingen** `X-Forwarded-For`
   fram till API:t. Next är ingen transparent proxy (noll `rewrites()` i
   `next.config.ts`), `src/proxy.ts` grindar bara, och varje backend-anrop är ett nytt
   `fetch()` från `src/lib/api/*` vars `authHeaders()` returnerar exakt `Authorization`
   + `Content-Type` — noll träffar på `x-forwarded` i hela `web/`.
   `UseForwardedHeaders` skriver om `Connection.RemoteIpAddress` **bara när headern
   finns**, så utan header är den no-op och **inget CIDR-värde kan laga det**. Nyckeln
   styr vilka proxies som får ha satt headern; den kan inte frambringa en header ingen
   skickar. `AuthWritePolicy` partitionerar därmed på Next-containerns IP — alla
   användare i en hink — och `AuthAuditLogger` + `RequestContextProvider` registrerar
   samma container-IP, vilket gör `docs/runbooks/failed-access-anomaly.md` verkningslös.

   Ägs av **[#1202](https://github.com/klasolsson81/jobbliggaren/issues/1202)** (`mvp`),
   som bär hela mätningen. **Grinden stängs på att en request från en känd klient-IP
   syns med den IP:n i rate-limit-partitionen och i auth-revisionsspåret** — aldrig på
   att `KnownNetworks` är ifylld. Re-homet i sig levererades av A1
   (`AlbOptions` → `ReverseProxyOptions`), som **medvetet lämnade värdet tomt**: ingen
   compose-fil i repot deklarerar ett nätverk, så det finns ingen bridge-CIDR att mäta,
   och en gissning hade avväpnat den fail-loud-grind som stoppar en felkonfigurerad
   deploy.
4. **`forward policy drop`** löses med riktade `iif`/`oif`-accepts för Docker-bryggan,
   **aldrig `policy accept`** — och kantens **IPv6-halva mäts före den växlingen**; den
   är i dag omätt.
5. **K2:s basic auth-credential ligger UTANFÖR repot** (env eller secret-fil), och
   Caddyfilen i git innehåller **ingen bcrypt-hash**. Repot är **publikt**; en committad
   hash är offline-knäckbar, och K2 är hela admission-kontrollen — då är den en tidsfråga.
   (CLAUDE.md §5 Security: inga hemligheter i committad konfig.)
6. **Ingen container publicerar till `0.0.0.0`**, verifierat **utifrån** med `curl` mot
   varje containerport — aldrig genom att läsa `expose:`. Detta är inte hypotetiskt:
   dev-compose binder **fem av sex portar till `0.0.0.0`, inklusive en oautentiserad
   Seq** ([#1198](https://github.com/klasolsson81/jobbliggaren/issues/1198)), och det är
   den filen #196 utgår från. Seq bär loggar — den mest sannolika vägen till att lådan ägs.
7. **Request-size- och timeout-tak i Caddy.** Slowloris och stora bodies mot en 8 GB-låda
   är den enda tillgänglighetskontroll som återstår när CDN:et faller. Billig; ta den.

**Restrisk, utskriven av denna session och graderad av security-auditor (raden är
`Major`, se grindtabellen):** en volymetrisk flod eller
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

**M-6 bär en ANDRA avvikelse, och den är större än fail2ban.** `NOPASSWD`-sudo för
`jpadmin` kombinerat med en **passfras-lös** operatörsnyckel betyder att **nyckelstöld =
root**, och root på en körande låda betyder fält-krypteringens masternyckel ur
processminnet så snart en sådan finns. Den är **inte** en fail2ban-fråga och får inte
läsas in i raden ovan.

- **Rationalen och hela hotmodellen: ADR 0123** (lokal ADR, `Proposed`) — den hålls lokal
  därför att den är en levande hotmodell för en produktionsvärd, samma disciplin som
  håller operatörens adress utanför den publika runbooken.
- **Status: `Proposed`. Ratificering väntar på Klas GO**, precis som fail2ban-klausulen.
- **Kompenserande kontroll i dag:** källrestriktion i två lager. Dess två mätta gränser:
  bakom NAT — och särskilt bakom **CGNAT**, där tusentals abonnenter delar adress —
  uppfyller varje enhet på nätet den; och på konsumentlina är adressen dynamisk, så en
  släppt lease kan tilldelas någon annan.
- **Detta står här därför att den auktoritativa grindtexten måste kunna läsas ensam.**
  En granskare utan lokala docs läser denna fil, får veta att hon inte missar någon
  grind — och skulle annars aldrig få veta att lådans enskilt största levande exponering
  finns. Den publika runbooken §11 säger redan ordagrant *"key theft equals root"*, så
  raden röjer ingenting nytt.

### 6b. Ny gate M-7 — detektion, för att §6:s scope-gräns ska betyda något

Grindtabellen är i dag **helt preventiv**. Det finns ingen `auditd`, ingen
file-integrity monitoring och ingen alerting; `LogLevel VERBOSE` på sshd är den enda
detektiva kontrollen. GDPR Art. 33:s 72-timmarsfrist löper från att man blir **medveten**
— och på en värd där ingenting gör någon medveten är den fristen inte svår att hålla,
den är **omätbar**.

**Skälet detta är en grind och inte en anteckning:** ADR 0123:s acceptans är
scope-begränsad till "medan lådan saknar riktig användardata". Den gränsen är bara
meningsfull om man **vid gränsen kan avgöra att ingen kompromettering redan skett** — och
0123 säger själv att man inte kan det. **En acceptans vars utgångsvillkor inte går att
verkställa mot en redan inträffad kompromettering är inte tidsbegränsad; den är permanent
med ett datum på.** Detektionsgrinden är alltså inte en angränsande fråga utan
förutsättningen för §6:s och ADR 0123:s egna gränser.

Grinden ställs på **skyldighetsnivå, inte mekanismnivå**. Rättslig grund står i grindraden
och är security-auditors att sätta — den restateras medvetet inte här, eftersom en andra
formulering är en andra plats att driva isär. **Severity restateras däremot**, eftersom
grindraden pekar hit och en pekare måste leda till svaret: en grad plus ett villkor är
billig att hålla synkad, ett stycke juridik är det inte. Skyldigheten ägs av
[#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201) — **värd-detektion och
alerting hos [#196](https://github.com/klasolsson81/jobbliggaren/issues/196),
nyckelåtkomst-detektion hos [#198](https://github.com/klasolsson81/jobbliggaren/issues/198)**;
en grind med två ägare har ingen, därav den egna issuen.
**Severity är satt:** `Major` (security-auditor 2026-08-04), med **eskalering till Blocker
om ADR 0123 fortfarande är obeviljad eller omitigerad vid första riktiga data** — samma
text som grindraden, eftersom raden pekar hit.

### 7. Backup: kraven består, målet är öppet

Beslut 4:s **mål** (Hetzner-EU Storage Box) faller med värdbytet. **Kraven består
ordagrant:** klient-side age-kryptering före upload **oavsett mål**, EU-jurisdiktion,
definierad rotation — nu med ett tal, **K4: 30 dagar** — och en **testad** restore-drill
före första riktiga data (M-4). **Två krav tillkommer** (security-auditor 2026-08-04):

- **(a) Målet ska vara en felmängd oberoende av BÅDE lådan och operatörens
  arbetsstation.** ADR 0123 placerar uttryckligen arbetsstationen **innanför**
  produktionens trust boundary — en backup som ligger i samma blast radius som det den
  skyddar är en kopia, inte en backup, och uppfyller inte Art. 32(1)(c).
- **(b) Age-PRIVATNYCKELN får aldrig ligga tillsammans med ciphertexten.** Ligger den med
  dumparna är krypteringen dekoration mot stöld av samma enhet.

**Vad det betyder för "lokalt på Klas dator":** mot den tidigare kravmängden såg den
**godkänd** ut — Sverige är EU, age-kryptering går att göra, restore-drillen blir enklare,
och utan biträde behövs inget DPA. Security-auditors dom 2026-08-04: **acceptabelt som
interimsmål medan lådan saknar riktig data; inte acceptabelt som mål vid första riktiga
data** — på (a), på att K4:s 30 dagar måste vara **demonstrerbar** (Art. 5(2)
accountability; manuella kopior har ingen verkställbar rotation och inget spår av att en
dump äldre än 30 dagar faktiskt förstörts), och på (b). **Skälet är inte kostnad** — ett
gratisalternativ kan vara helt i sin ordning så länge (a) och (b) håller.

**Målet är inte valt och inte verifierat**; det ägs av
[#197](https://github.com/klasolsson81/jobbliggaren/issues/197). **Cloudflare R2 är redan
uttryckligen avvisat** i Amendment 2026-06-08 på CLOUD Act-grund och får inte
återföreslås utan att security-auditor väger age-krypteringen mot Kap. V.

### 8. Icke-supersessions-staket — vad som står kvar oförändrat

Så att ingen läsare får två bilder. **Detta faller INTE:**

- **Beslut 1** (full AWS-exit) — orört.
- **Beslut 4:s `Amendment 2026-07-18`** (Option B, "route-all-through-Next") **i sin
  helhet, alla sex lastbärande invarianter.** Den är provider-oberoende: beslutad på
  applikationens form (11 Next-BFF-handlers under `/api/`, noll publika
  backend-konsumenter — **antalet** ommätt 2026-08-04: fortfarande exakt 11. **Uppräkningen
  är nu också ommätt** (dotnet-architect, 2026-08-04): **exakt ett** av fem prefix har
  glidit — regionen namnger `/api/foretag/lookup`, katalogen innehåller `sok`; övriga fyra
  stämmer. **Driften rör inte invarianten:** Option B säger "ingen `/api`-matcher vid edge
  över huvud taget", så ett omdöpt prefix ändrar ingenting i beslutet. Antal ≠ uppräkning —
  samma lärdom ett snäpp ned), inte på Cloudflare.

  **TVÅ rättelser i den, inte en.** *(En tidigare version av detta stycke sa "enda
  rättelsen" och var mätt falsk — regionens text är orörd, men en orörd mening kan bli
  falsk, och ett uttömmandehets-påstående får läsaren att sluta leta.)*
  1. **Slutmeningen i "Korrigerad topologi"** — *"Cloudflare 'Full (strict)' +
     origin-cert + origin-IP-lockdown + HSTS är oförändrade"* — är **FALSK under K3**.
     "Full (strict)" är moot (utan CDN finns inget sista ben), origin-cert utgår,
     **origin-IP-lockdown har ingen efterträdare** (M-5b), och HSTS överlever men
     **byter emitter** (M-5a). Samma sak gäller motsvarande mening i Beslut 4:s brödtext
     ovanför amendmenten.
  2. **Invariant 5:s parentes** *"(detta ändå moot)"* — se bannern vid Beslut 4 och
     dess ACME-not.

  Regionens text ändras inte (husmönstret är daterade noter ovanpå, aldrig omskrivning
  av beslutad text) — men båda meningarna läses som superseded.
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
och droppar 25/465/587 (mätt i Netcups SCP-panel 2026-08-03 under grundhärdningen,
#1196; leverantörsinställning, inte en repo-konfiguration — omverifiera i panelen före
cutover). Det är ett andra, oberoende skäl att transaktionsmejl går över
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
  (delvis) + gate M-5; se banner överst och `Amendment 2026-08-04` ovanför.
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
