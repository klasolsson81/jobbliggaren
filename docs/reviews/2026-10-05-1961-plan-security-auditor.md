# 2026-10-05 — #1961 planning — security-auditor

Round 1 (placement, before Klas's decision) and round 2 (under Klas's override, 2026-10-05). The placement Blocker in round 1 stands as recorded; its remedy was withdrawn by Klas (ADR 0154 §5). In the text, "ADR 0153" is the number planned at the time (filed as ADR 0154), and "p2-sec" is round 1.

## Round 1
## Security-audit: #1961, placering och PR 1-design (planläge, före beslut)
**Status:** BLOCKED för samma värd · Approved med villkor för separat produktionsvärd · ✓ Approved för PR 1-designen, med testkrav
**Auktoritet:** GDPR Art. 5(1)(f), 13(1)(e), 25, 28, 30(1)(d), 32(1)(b)/(c), 33 med skäl 87, kap. V · ADR 0050 Amendment 2026-08-04 §5 (M-5a/M-5b), §6b (M-7), §7 (M-4) · ADR 0143 · ADR 0148 · ADR 0149 R2/R9 · `docs/threat-model.md:112-146` · CLAUDE.md §9.6 · Klas A2 (kommentar 5982420396 på #1238)

**Egna mätningar** (read-only, 2026-10-05, `ssh -o BatchMode=yes jp-vps`):
- `sudo -n -l` → jpadmin `(ALL) NOPASSWD: ALL`.
- `daemon.json` har nycklarna `default-address-pools, live-restore, log-driver, log-opts`. Ingen userns-remap.
- rclone-credential `/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64` **saknas**. backup-, backup-fresh- och host-secrets-present-timrarna är `disabled`. logship är `enabled` men hoppar över varje timme ("unmet condition").
- Postgres-loggen vid 02:03:00–02:03:10Z innehöll 1 429 ERROR, 1 429 STATEMENT och 0 DETAIL. Alla STATEMENT bär `$n`-platshållare och har 0 citerade literaler.
- Postgres äldsta kvarvarande loggrad är `2026-10-05T02:02:39Z`.

### Del 1: Placering

**Blocker**

1. **Samma värd + A2 = riktiga användares PII bakom en agentnåbar root utan lösenord** — Fil: `docs/threat-model.md:138-140`, `docs/runbooks/vps-base-hardening.md:198`, `docs/runbooks/release-checklist.md:1757-1800`
   Nuvarande:
   - A2 kräver icke-interaktiv root för dev (checkout-advance under reconcile-låset, `systemctl start`).
   - Den nyckel agentsessionerna använder ger NOPASSWD:ALL på hela värden (mätt).
   - På en delad värd blir den nyckeln root över prods Postgres, master key (`LocalDataKeyProvider.cs:88-118`), DP-nyckelringen, som ligger oskyddad på disk (`DependencyInjection.cs:1623-1624, 1648-1650`), samt Seq och backups.

   Krävs: produktion på egen värd. Samma sak gäller RAM-uppgradering eller en andra IPv4 på samma låda: varianterna byter inte root.

   Motivering:
   - (a) Agentens läsningar av prod-data lämnar ut riktiga användares PII till en mottagare som varken finns i `gdpr-processing-register.md:108ff` (huvudkopian, mätt) eller i `content-legal.json`, som inte nämner någon modelleverantör (mätt). Det träffar Art. 13(1)(e), 28, 30(1)(d) och kap. V.
   - (b) M-7 är redan Blocker vid första riktiga data, och dess krav (1), att stöld av nyckeln inte i sig ger root, kan inte uppfyllas på en värd där A2 behöver just en sådan nyckel (`release-checklist.md:1783-1800`). Det här tillämpar min befintliga gradering och omgraderar den inte.
   - (c) Grunden för Klas direktiv 2026-08-20 ("read-only-mätning kräver inget GO") var att det inte finns några användare utom Klas och CC. Den grunden upphör på en prod-värd.

   Delegera till: driving session (ADR 0153 via adr-keeper). Placeringsbeslutet är Klas.

**Major-kluster, bara om samma värd ändå väljs (de faller bort med separat värd)**

2. **Delad kärna, daemon, tagglager, disk och edge** — Fil: `deploy/docker-compose.yml:33,239,265-285,305,625,698`; `deploy/systemd/jobbliggaren-reconcile.sh:349-352,501`; `deploy/caddy/Caddyfile:220-222,250`
   - (i) Dev kör kod som mergats inom ungefär 2 h, utan granskning vid promotion, på samma kärna. Det finns ingen userns-remap och ingen `cap_drop`/`pids_limit`, och bara caddy har `cpus`. Hot: illasinnad PR-bidragsgivare, `threat-model.md:17-18`.
   - (ii) Dev-reconcilen flyttar `:applied` och drar upstream-taggar per daemon. Nästa `up` i prod tar då dev-valda images, vilket bryter #1961:s "no dev timer … can advance production".
   - (iii) Gemensam edge:
     - K2 får fel lösenord att betala full bcrypt-kostnad varje gång (`compose:265-275`), och den kostnaden delar `cpus: 1.0`/128m med prods publika edge. Det ger oautentiserad DoS mot prod.
     - Båda projekten heter `web`. Dubbel alias kan skicka prod-trafik med `__Host-`-cookie och formulär-PII till dev, vilket är Blocker-klass om det inte utesluts strukturellt.
   - (iv) Ett prod-projekt med namnet `jobbliggaren-prod` ärver devs volymer (mätt: `jobbliggaren-prod_dataprotection_keys`), alltså devs identiteter, nyckelring och DB. Det bryter #1961:s "do not copy dev identities".
   - (v) Devs image-churn fyller den disk där prods WAL ligger (se F1).

   Delegera till: dotnet-architect.

**Villkor för separat produktionsvärd (det jag kan signera)**

- **C1** (Blocker om det bryts): ingen agentsession (CC, Codex, subagent) får en credential till prod-värden, prods backups, Seq eller DB, om inte modelleverantören först blir dokumenterat biträde. Det kräver DPA, registerrad, mottagare i policyn och en kap. V-grund. ADR 0153 ska skriva att A2 och direktivet från 2026-08-20 är dev-scopade. Bevis: `ssh -o BatchMode=yes <prod>` från arbetsstationen ska misslyckas.
- **C2:** prod körs på en **nyinstallerad** värd, inte på den nuvarande lådan. Den har haft agentnåbar root utan lösenord sedan 2026-08 och kan inte attesteras om utan ominstallation. M-7 urladdas inte av placeringen. Vid första riktiga data krävs egenskapen mätt på prod-värden, ett **nytt** Klas-beviljande som täcker riktiga data och båda M-7-benen verifierade.
- **C3:** M-4 (Major, #197) är kvar oförändrat. Därtill:
  - egen bucket **och** egen credential för prod, så att devs credential inte kan läsa, skriva eller radera prod. Prefix-separation räcker inte, eftersom överskrivningsrisken för `deks/verified` (`jobbliggaren-backup.sh:67-81`) kvarstår med en delad credential.
  - samma separation för logships `hostlogs`
  - en testad restore och ett mål som är oberoende av lådan och arbetsstationen (ADR 0050:1236-1243)
- **C4:** färskt nyckelmaterial för prod, aldrig kopierat från dev: master key, DP-nyckelring, PG-/Redis-ACL-lösenord per ADR 0143-identitet, Scaleway-nyckel och OAuth-hemligheter. OAuth-klientbeslutet ägs av #1768/ADR 0142. Ingen dev-dump eller dev-volym återställs i prod. Referensdata kommer från källartefakten. #1768 äger replay-testerna.
- **C5** (Major om det är fail-open): prod-consumern vägrar köra utan pin och accepterar bara `sha256:`. `read_selection` faller i dag tillbaka på kanalen när pin saknas (`reconcile.sh:107-111`). En saknad pin-fil skulle alltså låta `dev` driva prod.
- **C6** (Major om det är fail-open):
  - Caddyfilen bakas in i den release-image som ska promotas (`deploy/caddy/Dockerfile:67,72`) och har ovillkorlig `basic_auth` (`Caddyfile:220-222`). En K2-växel måste vara fail-closed: på som default, och dev-applyn vägrar K2-av, eftersom `Caddyfile:185-192` bär en GDPR-slutsats. `compose:254,258` kräver dessutom `BASIC_AUTH_*` med `:?`.
  - M-5a-beviset i prod läses på svar som Caddy självt ger (ACME-404, 503, www-redirect), eftersom prod inte har något 401.
  - Innan apex sänder `includeSubDomains` (`Caddyfile:159`) mäts Strato-zonen efter subdomäner som bara svarar över http (#1768).
- **C7** (Major om det är fail-open): prod-applyn vägrar en `.env` med `DEV_TOOLS_RESET_ENABLED=true` (`release-checklist.md` §2.7; Klas äger borttagningen), och registreringsknappen rörs inte (#734).
- **C8:** image-prune (F1) finns innan någon prod-värd ärver reconcilen.
- **M-5b:** omgraderingen sker vid den obligatoriska andra granskningen mot faktisk prod-konfig. Den här bedömningen föregriper den inte. Indata till den: prod har ingen K2, så per-IP-rate-limiting blir den enda per-IP-kontrollen.

**Minor**

3. **Namnhygien och defense-in-depth** — Fil: `docker-compose.yml:33`; `DependencyInjection.cs:1638`; `LocalDataKeyProvider.cs:124`
   - Byt namn på devs projekt `jobbliggaren-prod`, med `volumes.<k>.name` för att behålla datan. Under A2 ("rör aldrig produktion") ska "prod" inte vara namnet på dev.
   - Ge prod ett eget `LocalMasterKeyId`, så att en återställning över miljögränsen syns.
   - Ett miljöspecifikt DP-applikationsnamn gör kopierade nyckelringar oanvändbara.
   - På prod: `cap_drop: [ALL]` och `pids_limit` på web/api/worker. Om något GHCR-paket är privat får prod bara en `read:packages`-token.

   Delegera till: dotnet-architect.

### Del 2: PR 1-designen (web-env.md §7, critic.md §E): Approved, inga Blocker eller Major

4. **Minor: validering och felmeddelanden** — Fil: `src/lib/site-url.ts:10-11`, `src/app/layout.tsx:15-17`
   Krävs:
   - Vägra även path ≠ `/`.
   - Vägra all whitespace och alla kontrolltecken **före** parsning. WHATWG tar tyst bort `\t\n\r`; jämför `same-origin.ts:6`.
   - Meddelanden namnger `SITE_URL`, aldrig värdet (paritet med `ExternalLoginRedirectOptionsValidator.cs:10`; userinfo kan bära ett lösenord).
   - Startvakten validerar med **samma** validator, inte `-n`. Healthchecken rör inte SITE_URL (critic A.6).

   Motivering:
   - http mot loopback i produktionsbyggen är godtagbart **bara** för att SITE_URL är presentationsdata (metadataBase/robots/sitemap). Skriv in det som invariant.
   - `same-origin.ts:23-45` ska fortsätta använda Host+Origin. Det är redan pinnat mot `SITE_URL` (`same-origin.test.ts:52-67`; försvaga inte den testen).
   - Server Actions-nyckeln är redan publik, eftersom imagen kan dras anonymt (ADR 0148:59-63). Att samma nyckel används i dev och prod exponerar alltså inget nytt, och en nyckel per miljö går inte ihop med build-once.
   - Mätt: 0 inbäddade `"use server"`-closures i `src`. ADR 0153 skriver att bundna argument aldrig betros.

   **Tester som måste finnas i PR 1:**
   - T1. Oinställd, tom eller bara whitespace i produktion ger `SiteUrlNotConfiguredError`. Meddelandet innehåller `SITE_URL`; med `https://u:secret@x.test` ska `secret` inte förekomma.
   - T2. Vägras:
     - userinfo, query, fragment och path
     - `javascript:`, `file:` och `data:`
     - `http://localhost.evil.test` och `http://127.0.0.1.nip.io`
     - CR/LF/tab
   - T3. Accepteras: https-origin, `http://localhost:<p>`, `127.0.0.1` och `[::1]`. Returvärdet är origin utan avslutande `/`.
   - T4. Lazy: stubba a, sedan b; robots, sitemap och metadataBase följer med.
   - T5. Post-build: ingen `robots.txt.body`/`sitemap.xml.body`, inga `process.env.NEXT_PUBLIC_`-läsningar i `src` (i dag bara `site-url.ts:11` och `layout.tsx:16`), och inget värde i `.next/static`.
   - T6. Lokal proof run (e2e är `continue-on-error`):
     - Samma image-ID med två värden.
     - Fientlig `Host`/`X-Forwarded-Host` ändrar inte utdata.
     - Oinställd, tom, `http://evil.example` och `https://u:p@x` ger exit ≠ 0, och loggen nämner variabeln men inte värdet.
     - `-e NODE_ENV=development` utan SITE_URL vägras ändå.
     - CMD-override utan SITE_URL ger 500 med det namngivna felet.

   Delegera till: nextjs-ui-engineer.

### Del 3: Fynd i mätningarna

5. **Major: diskutmattning omkring 2026-10-21** — Fil: ingen prune finns (grep i critic A.1; `box.md` §1)
   Nuvarande: 41 GB ledigt, cirka 2,6 GB/dag, 1 267 otaggade images.
   Krävs: en prune som bevarar rollback-setet och aldrig tar bort images som ett kvitto eller en pin namnger. Den ska vara på plats före 2026-10-21 och innan någon prod-värd finns.
   Motivering: full rot ger PANIC i Postgres vid WAL-skrivning, att Redis AOF-skrivningar misslyckas och att auditd stannar. Det är en defekt i levererad kod, separat ändringsorsak.
   Delegera till: dotnet-architect.

6. **Minor: nattburst i Postgres-loggen raderar DB-sidans spår** — Fil: `UpsertExternalJobAdCommandHandler.cs:74-88`; `docker-compose.yml:661-669`; `jobbliggaren-logship.sh:357-365`
   Nuvarande:
   - Ingen PII i loggen (mätt). `terse` undertrycker DETAIL, och STATEMENT bär bara platshållare.
   - Felen kommer av INSERT-först-upserten i ADR 0032 §5 och är avsiktliga.
   - Bursten roterar ändå ut all tidigare Postgres-logghistorik varje natt (äldsta rad 02:02:39Z), och Postgres-loggen skeppas inte.

   Krävs: en upsert som inte loggar ERROR (`ON CONFLICT`), med racesäkerheten bevarad.
   Delegera till: dotnet-architect.

7. **Ingen ny gradering:** backups och logship har aldrig kört (credential saknas, mätt). Det är befintliga M-4 och M-7, förvillkor för prod (C2, C3).

### Praise
- `log_error_verbosity=terse` håller: den nattliga ERROR-bursten bär mätbart ingen PII ✓
- `read_selection` vägrar en felformad pin och faller aldrig tillbaka på den. Det är rätt grund för prods läge med obligatorisk pin ✓

### Sammanfattning
För placeringen: 1 Blocker (samma värd), 1 Major-kluster som bara gäller samma värd, villkoren C1–C8 och 1 Minor. PR 1-designen har 0 Blocker och 0 Major, men 1 Minor och testerna T1–T6. Mätningarna gav 1 Major (disk) och 1 Minor (Postgres-loggen). Omgranskning efter fix: samma agent, report-only, scopad till fix-deltat (CLAUDE.md §9.6).

**Eskalering till Klas:** ja.
1. Placering: jag kan bara signera produktion på en egen, nyinstallerad värd. Samma värd, också med mer RAM eller en andra IPv4, är Blocker så länge dev behåller A2:s agentnåbara root utan lösenord. Kostnaden för en andra värd är ditt beslut. Alternativet är att dra in A2 för dev, och då bedömer jag om; M-7 gäller fortfarande.
2. Fastställ i ADR 0153 att A2 och ditt direktiv från 2026-08-20 om read-only-mätning inte omfattar produktion, och att ingen agentsession får credentials till prod-värden, dess backups eller Seq. Undantaget är om du först gör modelleverantören till dokumenterat biträde med DPA, registerrad, mottagare i policyn och en grund för kap. V.
3. M-7 i prod kräver vid första riktiga data ett **nytt** uttryckligt beviljande från dig. Beviljandet från 2026-08-16 täcker bara tillståndet utan riktiga data.

Filer: `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/docs/threat-model.md`, `.../deploy/docker-compose.yml`, `.../deploy/caddy/Caddyfile`, `.../deploy/caddy/Dockerfile`, `.../deploy/systemd/jobbliggaren-reconcile.sh`, `.../deploy/systemd/jobbliggaren-backup.sh`, `.../web/jobbliggaren-web/src/lib/site-url.ts`, `.../web/jobbliggaren-web/src/lib/security/same-origin.ts`, `.../docs/runbooks/release-checklist.md`, `C:/DOTNET-UTB/JobbPilot/docs/runbooks/gdpr-processing-register.md`
## Round 2
## Security-audit: #1961/#1768 under K1/K2, en server på jobbliggaren.se (planläge, ingen PR)
**Status:** ✓ Approved för K1-planen. Den har 1 ny Major (läsregeln för agenter), som är en grind före #734. Placerings-Blockern står kvar i protokollet men är inte signerad. PR 1-godkännandet står kvar.
**Auktoritet:** GDPR Art. 5(1)(a)/(f), 5(2), 24(1), 28, 30(1)(g), 32(1)(b)/(c), 33, 44 · CLAUDE.md §9.2, §9.6 · ADR 0050 grindtabell (`:585-590`), §5 (`:887-1154`), §7 (`:1229-1250`) · ADR 0133 (`:100-127`) · `release-checklist.md` §2.6 p. 3.5 (`:1757-1992`), §2.7 (`:2379-2455`)

Egna läsningar 2026-10-05, alla read-only:
- **DNS (DoH/Resolve-DnsName):**
  - apex A är 217.160.0.188 och AAAA är 2001:8d8:100f:f000::200. Båda pekar på Strato.
  - `www` är en CNAME till apex. `dev` har A 159.195.203.88 och ingen AAAA.
  - Ingen CAA-post finns. `autoconfig` är en CNAME till autoconfigure.strato.de. mail, webmail, autodiscover, ftp, smtp, imap, pop3, staging och api ger inget svar.
- **apex https över IPv4:** TLS-handskakningen misslyckas, så Strato visar inget giltigt cert.
- **Anthropic-villkor:**
  - code.claude.com/docs/en/data-usage: Max lyder under Consumer Terms. Lagring är 30 dagar, eller 5 år om träningsinställningen är på. Lokala transkript ligger i klartext under `~/.claude/projects/` i 30 dagar.
  - anthropic.com/legal/data-processing-addendum (gäller från 2025-02-24): DPA:n gäller bara Commercial Terms eller avtal som hänvisar till den.

### Del 1: Vad överkörningen täcker (för protokollet)

- **Vad som överkörs:** min Blocker från 2026-10-05 (p2-sec, del 1 punkt 1): samma värd plus A2 innebär att riktiga användares PII ligger bakom en root som agenter når utan lösenord. `jpadmin (ALL) NOPASSWD: ALL` är mätt 2026-10-05. Klas överkörde den med K1 och K2 samma dag.
- **Vad som dras tillbaka är botemedlet**, alltså en separat, nyinstallerad värd, och allt som byggde på den:
  - C1 (inga agent-credentials till prod), C2, C3:s separation från dev, C4 (färskt nyckelmaterial och ingen dev-data), C5, C6:s K2-växel, C7:s vägran i prod-consumern och C8:s ordningsvillkor.
  - Major-klustret för samma värd (punkt 2). Med en enda stack försvinner (ii)–(v). (i), att mergad kod går live inom cirka 2 h, är K1:s uttryckliga val.
- **Vad som står kvar:** fyndet, graden Blocker och grunderna Art. 5(1)(f)/32(1)(b), plus den villkorade armen 13(1)(e)/28/30(1)(d)/kap. V. Den armen blir ett brott först när en agent faktiskt läser PII, och därför finns Del 2 punkt 1.
  - Det är **ingen §9.6 (3)-acceptans**. Bindningen faller vid riktiga användare, och en Blocker har ingen väg dit. Jag signerar inte.
  - Det är ett beslut av den personuppgiftsansvarige enligt Art. 24(1), av samma form som `release-checklist.md:1894-1929` och `:1989-1992`.
  - Hemmet är ADR 0153: K1/K2 ordagrant, adjudikator Klas, 2026-10-05, och "fyndet står, osignerat". Aldrig en PR-body.
- **Följder för mina tidigare eskaleringar:**
  - Klas A2-mening från 2026-10-04, *"Produktion ska köra en uttryckligen godkänd release"*, ersätts av K1. Från och med nu **är mergen godkännandet**, så granskningen per diff är den enda säkerhetsgrinden före produktion.
  - Mina eskaleringar 1–2 i p2-sec är besvarade av K1/K2.
  - **Eskalering 3 (ett nytt M-7-beviljande) drar jag tillbaka.** Den håller inte, eftersom Klas redan svarade 2026-09-06 med *"Ja, ingen blocker"* (`release-checklist.md:1989-1992`).
- **Det jag fortsätter att gradera per diff, område 1–8, Blocker inkluderat:**
  - Allt som får en agent att läsa, skriva ut eller skicka vidare riktiga användares PII. Överkörningen gäller **åtkomst, inte röjande**.
  - Nya credentials eller ytor som placeras där agenter läser, till exempel Seq-admin, brevlåda, app-adminsession eller backup-nyckel.
  - Diffen som tar bort basic_auth, där omgraderingen av M-5b och N-1 sker.
  - Backup-PR:erna, rivningen enligt §2.7, cutovern och PR 1.
  - M-7 och M1 förblir mina graderingar.

### Del 2: Minsta poster före #734-flippen, förenliga med K1/K2

**Major**

1. **Det finns ingen skriven regel om vad en agent får läsa på lådan** (ny, Major). Det är en #734-grind, men den gäller redan i dag för rekryterarkontakterna i `job_ads`.
   - Fil: CLAUDE.md §1.5/§6.5 saknar regeln. Bara tre avläsningar har "Counts, never printouts" (`docs/runbooks/vps-deploy-stack.md:753,840,915`).
   - Nuvarande: direktivet från 2026-08-20 (mät utan GO) vilar på grunden "inga användare utom Klas och CC". Den grunden upphör vid första användare som inte är Klas.
   - Krävs, i en enda regel som sessionen skriver som spec-PR med §9.2-agenterna (inget Klas-beslut behövs, och A2-åtkomsten rörs inte):
     - (a) Läsningar på lådan är bara metadata: antal, storlekar, schema, hälsa, nyckel**namn** och hashar.
     - (b) Aldrig radinnehåll ur användartabeller: identity.*, job_seekers, CV, ansökningar, anteckningar, sökningar, bevakningar, audit_log, sessioner, inloggningsutmaningar och kontaktfälten i job_ads. Aldrig Redis-värden, Seq-händelser, råa api/worker/web/caddy-loggrader utöver antal, värden i hemlighetsfiler eller `.env`, eller backup-artefakter.
     - (c) Ingen användar-PII i chatt, filer, memory, commits eller issue- och PR-kommentarer. Repot är publikt.
     - (d) Ingen agent i inloggade vyer eller `/admin/*` på prod.
     - (e) Om PII råkar läsas: sluta, upprepa den inte, säg till Klas. Han recordar enligt Art. 33(5).
     - (f) Om en agent någonsin ska behandla användardata krävs först kommersiella villkor med DPA, en rad i registret, mottagare i policyn och en grund enligt kap. V. Det är inte aktuellt nu.
   - Motivering: Max lyder under Consumer Terms utan DPA, med 30 dagars till 5 års lagring hos Anthropic (USA) och ett transkript i klartext på arbetsstationen.
     - En läsning blir därför ett röjande utan Art. 28-avtal och med en kap. V-överföring. Det är Blocker-klass vid händelsen.
     - Den gör dessutom policyns publicerade *"Ingen spårning och ingen AI"* (`messages/sv/content-legal.json:5`) falsk (Art. 5(1)(a)).
     - **Så länge regeln håller behövs ingen ändring i dokumentationen**: ingen registerrad, ingen policyrad, ingen DPA och ingen DPIA. Root-förmåga är inte ett röjande.
   - Delegera till: driving session (dotnet-architect + code-reviewer).

2. **M-4 backup** (Major, min gradering 2026-08-04, oförändrad). Fil: ADR 0050 `:585`, `:1229-1250`.
   - Nuvarande (mätt 2026-10-05): rclone-credential saknas. backup- och backup-fresh-timrarna är disabled och har aldrig kört.
   - **K1-följd:** varje mergad migration går automatiskt mot den enda kopian av användarnas data inom cirka 2 h.
   - Krävs, minsta skiva:
     - mål valt i #197. Ett befintligt biträde, som Scaleway med DPA på plats, undviker ett nytt DPA. Målet ska vara oberoende av både lådan och arbetsstationen, och age-nyckeln får inte ligga hos chiffertexten (`:1236-1241`).
     - credential, båda timrarna påslagna och en restore-drill (rad 31, `vps-deploy-stack.md:1332`).
     - En dump före varje migration är valfri och kan vara ett senare issue.
   - Routing: en #734-grind enligt ADR 0050:s tabell för pre-real-data. Klas skopade #197 ur mvp 2026-09-04, så frågan går till honom som Q1.
   - Delegera till: dotnet-architect via #197.

**Befintliga grindar, ingen omgradering**

3. **§2.7 dev-verktyg** (HÅRD GRIND, `release-checklist.md:2379-2395`). En #734-grind, senast före första konto som inte är Klas. Den körs på Klas ord från 2026-09-15: *"När vi går live kommer knappen tas bort helt."* Steg 1 slår av flaggan i `.env`, steg 2 river koden. `accounts` och `login-code` mappas bara i Development (`src/Jobbliggaren.Api/Program.cs:481-482`) och kan inte nås på lådan.

4. **M-7** (Blocker sedan 2026-08-17, `release-checklist.md:1757-1758`). Det är ingen grind och inget som ska frågas igen: Klas beslutade 2026-09-06 (`:1989-1992`). K2 vilar på samma fakta som M-7:s krav (1) (`:1835`). Graderingen står och är min. Detektionskedjan (expecter-paging) ska fortsätta köra.

5. **M-5a** (Major, ärvd). Hör till #1768.
   - Båda halvorna är byggda (`deploy/caddy/Caddyfile:159`, `web/jobbliggaren-web/src/lib/security/security-headers.ts:101,121-127`).
   - Utan K2 finns ingen 401, så beviset på apex läses på Caddys egna svar och på ett 200 från Next. Caddys egna svar är 404 på ACME-prefixet (`Caddyfile:180-182`), 503 och omdirigeringen från http till https.
   - CAA saknas (mätt). Lägg CAA på `letsencrypt.org` och verifierad 2FA på Strato (ADR 0050 `:978-983`).
   - includeSubDomains: läs hela Strato-zonen i panelen. Min sondering hittade bara `autoconfig`, som går till Strato och används av e-postklienter, inte webbläsare. Inget hinder hittat.

6. **M-5b + N-1 (#1796): omgradering schemalagd, inte nu.** Den görs vid granskningen av diffen som tar bort `basic_auth` (`Caddyfile:220-222`), mot levande mätningar:
   - Option B-curl-matrisen inklusive `/api/v1/dev/*` och `/api/v1/admin/*`.
   - #1202 (stängd) mätt levande: känd klient-IP syns i rate-limit-partitionen och i auth-revisionsspåret.
   - Ingen port på 0.0.0.0, kontrollerat utifrån.
   - Caddys timeouts och body-tak (`Caddyfile:25-30,161-163`).
   - IPv6-halvan.

   K1 tar bort delrisken med delad kant. Det är en mätsession, inget bygge.

**Minor**

7. **Värdnamn vid cutover.** Hör till #1768.
   - A och AAAA flyttas tillsammans. Den AAAA som pekar på Strato annars delar trafiken.
   - `www` behöver ett eget site-block med redirect. Caddy serverar bara `{$SITE_HOST}` (`Caddyfile:129`), och HSTS gör att webbläsaren inte faller tillbaka.
   - `dev` får en 301 vid kanten utan proxy, eller så tas posten bort. Annars finns en alternativ ingång.
   - Ta bort `dev.jobbliggaren.se` ur e2e-allowlistan (`web/jobbliggaren-web/tests/e2e/helpers/auth.ts:28-34`). Annars följer e2e en 301 in i produktion.

8. **Publiceringen är en merge.** K1 deployar automatiskt. PR:en som tar bort `basic_auth`, dess kommentar (`Caddyfile:185-219`) och `BASIC_AUTH_*:?` (`deploy/docker-compose.yml:254,258`) är den akt som gör sajten publik. Armera automerge först på Klas GO för cutovern (§9.2). Hör till #1768.

**Record, ingen gradering**

9. **ADR 0133 förfaller vid cutovern.**
   - Trigger (c), att texten blir publikt läsbar (`0133:102-106`), fyrar även med registreringen stängd.
   - Då återgår benen (b) och (c) och Art. 7.4 (`:123-127`).
   - När bindningen har fallit finns ingen §9.6 (3)-väg. Det blir Klas beslut enligt Art. 24(1), och det är Q2.

10. **M1, Art. 14-notisen (positivt).**
    - Cutovern gör `/kontaktperson-i-annons` publik. Sidan ligger utanför `PROTECTED_PREFIXES` (`src/lib/auth/protected-routes.ts:19-32`).
    - Det är just det rutt (c) väntade på (`release-checklist.md:1967-1973`).
    - Mät ett anonymt 200 på apex. Jag graderar M1 arm (i) då.

**Ingen åtgärd krävs**

- **OAuth:** Klas beslut 2026-10-03 gäller: Google (a) med delad lokal klient, GitHub (d) med apex först. m-8 står kvar. Cutovern byter redirect_uri via `SITE_HOST` och `Email__BaseUrl` (`docker-compose.yml:180`) till apex-callbacken, som redan är registrerad. #734 rad 12 mäts på apex. Att ta bort de döda dev-callbackarna är valfritt.
- **Cookies:** alla är `__Host-` (`src/lib/auth/cookie-names.ts:9-21`), alltså host-only. De följer inte med vid värdbytet, och Klas loggar in igen. Inget behöver roteras.
- **Seq och loggar:**
  - Seq har 30 dagars retention sedan 2026-08-23 (`docs/runbooks/gdpr-processing-register.md:1119`).
  - Caddy-filtret raderar token, email, uid, q, code och state (`Caddyfile:61-126`), och Caddy loggar bara 5xx.
  - Postgres kör terse, och nattbursten bar 0 literaler (mätt 2026-10-05).
  - Det enda nya är att läsningar av Seq och loggar faller under punkt 1.
- **Data från dev-tiden:**
  - Båda kontona är Klas. Ett `+`-alias räknas som hans genom egenskapen (`release-checklist.md:894-898`).
  - Rekryterarkorpuset är redan produktionsbehandling.
  - C4 dras tillbaka med botemedlet.
  - `ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL` (`docker-compose.yml:394`) gör inget så länge rollen har en innehavare (#2001). Att tömma den är valfritt.

**Valfritt eller senare (Minor, issue vid behov):**
- Dependabot `cooldown` saknas (`.github/dependabot.yml`), och med K1 blir varje auto-mergat beroende en prod-deploy.
- En dump före migration.
- En mening om tekniska och organisatoriska åtgärder i registret, Art. 30(1)(g). Lagen kräver den bara där det är möjligt.
- Klas egen Claude-inställning för att förbättra modellen: stängs den av blir lagringen 30 dagar i stället för 5 år.
- Nyckelrotation krävs inte.

| #734-grind | #1768-cutover | Senare |
|---|---|---|
| 1, 2, 3 och #734 rad 6–12, oförändrade | 5, 6, 7, 8, 9, 10 och record av K1/K2 i ADR 0153 | Minor-listan ovan |

### Del 3: PR 1 (runtime SITE_URL, T1–T6)

Godkännandet står: 0 Blocker och 0 Major. Minor 4 och T1–T6 gäller oförändrade. Under K1 ändras följande:
- **Skälet blir ett annat:** PR 1 gör att cutovern är ett enda runtime-värde, utan ombyggnad och utan att ändra repovariabeln. T6:s bevis med två värden visar just det.
- **Ett avsnitt i ADR 0148-noten utgår:** stryk "Server Actions-nyckeln delas mellan miljöer", eftersom det bara finns en miljö. Behåll att nyckeln saknar konfidentialitetsroll eftersom imagen är publik, att det finns 0 closures och att bundna argument aldrig betros.
- **Compose-pinnet blir lastbärande:** testet ska visa att `SITE_URL` och `Email__BaseUrl` båda kommer från `https://${SITE_HOST:?}`. En enda ändring av `SITE_HOST` ska flytta Caddys site (`:247`), inloggningslänkar och redirect_uri (`:180`) och webbens `SITE_URL` samtidigt.
- **Dev-avanceringen** täcks nu av K2.

### Praise
- Att alla cookies är `__Host-` gör att värdbytet inte läcker något och att cookie-tossing från subdomäner är stängt ✓
- Caddy-loggfiltret och Postgres terse bär mätbart ingen PII ✓
- GitHub-appens apex-först (d) passar redan K1 ✓

### Sammanfattning
1 ny Major (läsregeln), 1 befintlig Major som är grind (M-4), 4 befintliga grindar utan omgradering, 2 Minor, 2 poster att recorda, och en Minor-lista som är valfri. Placerings-Blockern står recordad och osignerad. PR 1 är oförändrat godkänd.

**Eskalering till Klas:** ja. Q1 är en ny fråga. Q2 föregriper en trigger som fyrar vid cutovern.
1. **Q1 (M-4):** "Ska nattlig krypterad backup till ett mål utanför lådan, plus en återläsningsövning, vara på plats innan första användare som inte är du? Rekommendation: ja. Med K1 går varje mergad migration direkt mot den enda kopian av användarnas data. Din skopning 09-04 vägde risken att bli anmäld, inte dataförlust. Väljer du nej recordas det som ditt beslut som personuppgiftsansvarig, och M-4 står kvar som Major."
2. **Q2 (ADR 0133):** "När basic auth tas bort vid cutovern fyrar ADR 0133:s trigger (c). Ska Scaleway-benen (b) EU-behandling utan kontraktsrang, (c) omätt TEM-retention och invändningsrätten i Art. 7.4 stå kvar som ditt beslut som personuppgiftsansvarig efter cutovern? Rekommendation: ja, i samma form som 09-06. Min signatur är inte tillgänglig efter att bindningen fallit."

Källor: [code.claude.com/docs/en/data-usage](https://code.claude.com/docs/en/data-usage) · [anthropic.com/legal/data-processing-addendum](https://www.anthropic.com/legal/data-processing-addendum) · [compound.law Anthropic GDPR](https://compound.law/en-DE/compliance/anthropic-gdpr-compliance/) (sekundär, bara korroboration)

Filer:
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/deploy/caddy/Caddyfile`
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/deploy/docker-compose.yml`
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/docs/runbooks/release-checklist.md`
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/docs/decisions/0050-deployment-migration-aws-exit-hetzner.md`
- `C:/DOTNET-UTB/JobbPilot/docs/decisions/0133-the-letter-to-scaleway-is-struck-two-legs-of-the-mail-gate-stand-on-an-accepted-risk-bounded-by-the-controller-being-the-only-data-subject.md`
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/web/jobbliggaren-web/messages/sv/content-legal.json`
- `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobb-card-density-1828-79e055/web/jobbliggaren-web/tests/e2e/helpers/auth.ts`