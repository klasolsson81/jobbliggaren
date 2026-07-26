# Release-checklist (generisk, återkommande)

> Repeterbar release-procedur för JobbPilot. Gäller **varje** tag-driven
> release, oavsett fas. Skild från `v0.2-prod-launch-checklist.md` — den är
> en engångs-checklist för *första* prod-deployen; detta är den löpande
> rutinen som används om och om igen.
>
> **Skapad:** 2026-05-17 (roster-gap-CTO 2026-05-17 §1.5 — "runbook, inte
> release-manager-agent"; ADR 0045-bunt steg 6). Deploy-beslut är strategiska
> och kräver Klas-godkännande (CLAUDE.md §9.2) — denna runbook ersätter inte
> det, den strukturerar det.

---

## 1. Tag-semantik (ADR 0019)

| Tag-mönster | Miljö | Approval | Exempel |
|---|---|---|---|
| `v*-dev` | dev | Automatisk (deploy-dev.yml) | `v0.3.1-dev` |
| `v*-rc*` | staging | Automatisk till staging | `v0.3.0-rc1` |
| `v*` (ren) | prod | **Manuell approval (Klas)** | `v0.3.0` |

`main` är enda branch (ADR 0019, direct-push). Staging är *miljö*, inte
branch. Deploy sker via tag-push på `main`, aldrig via branch-merge.

---

## 2. Före tag (pre-flight)

- [ ] **main-CI grön** — `gh run list --workflow build --limit 1` → `success`
      (backend + frontend + coverage + ci alla gröna). Coverage-gaten
      (ADR 0044) får inte vara röd.
- [ ] **Observe-only-signaler granskade** (ADR 0045) — `lighthouse` /
      `loadtest` / `audit`-jobben är observe-only och blockerar inte, men
      deras `::warning::`/summary ska läsas inför release: ny CWV-regression,
      p95-budget-överskridande eller High/Critical-CVE noteras och bedöms
      (åtgärda eller medvetet acceptera + motivera).
- [ ] **Inga öppna Klas-STOPP-flaggor** i `docs/current-work.md`.
- [ ] **Aktiva Major-TD mot release-scope** genomgångna (`docs/tech-debt.md`)
      — launch-blocker-TD löst eller medvetet deferrad med motiv.
- [ ] **Migrations** — om EF Core-migration ingår: verifiera schema-mode-
      dispatch (ADR 0033) och DB-roll-separation (ADR 0034); Identity-schema-
      ändring → manuell procedur (TD-72).
- [ ] **Kollations-version — ENDAST vid Postgres-image-bump eller major-uppgradering**
      (#884, ADR 0109). Ett btree-index på text är byggt **med** en kollation. Ändras
      kollationens *definition* under det — en ny ICU-version i basimagen, en ny glibc,
      en major-uppgradering — sorterar indexet efter en ordning som inte längre gäller.
      Postgres **kraschar inte** på det: frågorna blir bara tyst fel (rader hittas inte,
      `ORDER BY` ljuger). Detta gäller `en_US.utf8` **redan idag** (collversion 2.41);
      #884 skapade inte exponeringen, det är första gången repot **namnger** den.
      **Efter varje Postgres-image- eller major-bump, före tag:**
      ```sql
      -- 1. Har någon kollation drivit? (tom output = inget att göra)
      SELECT collname, collversion, pg_collation_actual_version(oid) AS faktisk
      FROM pg_collation
      WHERE collversion IS NOT NULL
        AND collversion IS DISTINCT FROM pg_collation_actual_version(oid);

      -- 2. Om någon rad kom tillbaka: bygg om berörda index och kvittera versionen.
      REINDEX DATABASE CONCURRENTLY jobbliggaren;   -- eller de berörda indexen
      ALTER COLLATION public.swedish REFRESH VERSION;
      ALTER DATABASE jobbliggaren REFRESH COLLATION VERSION;  -- för DB-defaulten
      ```
      **Kvittera INTE versionen (steg 2b) utan att först ha byggt om (steg 2a)** — det
      tystar varningen utan att laga indexen, vilket är strikt värre än att inte ha
      kollat alls.
- [ ] **Om en migration faller på `lock_timeout` — kör om den, det är säkert.** Migrationen
      som sätter kollationen (#884) tar ACCESS EXCLUSIVE och binder sin väntan till 3 s.
      Krockar den med en långkörande transaktion får du
      `canceling statement due to lock timeout` och **hela migrationen rullas tillbaka
      atomärt** (verifierat mot riktig Postgres med en konkurrerande AccessShareLock:
      avbrott efter 3001 ms, databasen orörd). Inget delvis applicerat tillstånd kan
      uppstå. Vänta ut den blockerande transaktionen — typiskt nattsynken — och kör om.
      Det är felläget guarden **finns** för: ett högljutt deploy-fel i stället för ett
      tyst läs-avbrott.
- [ ] **GDPR-konsekvens** för nytt scope bedömd (CLAUDE.md §8 punkt 8) — ny
      PII? loggning? retention? Audit-wire intakt (ADR 0035)?
- [ ] **Secrets-hygien** — inga nya secrets i klartext; gitignored
      `appsettings.Local.json` lokalt / managed secrets-store i ops + DEK-envelope
      (`IDataKeyProvider`, ADR 0066/0049) för allt känsligt (CLAUDE.md §5; AWS
      Secrets Manager + KMS rivet, ADR 0066).
- [ ] **Lokal diff-granskning** (CLAUDE.md §6.3 mekanism 4) — Klas läser
      `git log` + `git diff` för release-spannet.

---

## 2.5 HÅRD GRIND: Resend e-post-prod-flip (ADR 0080)

> Gäller ENDAST en release som aktiverar `Email:Provider=Resend` i non-dev
> (bakgrundsmatchnings-notiser). Tills dess kör `NullEmailSender` — ingen
> e-post skickas, och denna grind är inte relevant. Resend är en **US-processor**
> → mottagar-adress + opt-in-faktum är en tredjelandsöverföring. **Alla fyra
> punkter MÅSTE vara gröna innan `Email:Provider` flippas** (ADR 0080
> prod-flip-checklista). CC får ALDRIG flippa providern eller signera DPA:t.

- [ ] **1. Tredjelands-grund** — signerad **DPA** med Resend på fil +
      dokumenterad **SCC/adekvans**-grund + Resend-posten i
      `docs/runbooks/gdpr-processing-register.md` (ROPA, lokal) +
      **security-auditor-sign-off** på prod-e-post-konfigen. (DPA-signering =
      Klas; ROPA + sign-off = #183.)
- [ ] **2. TD-115** — legacy opt-OUT-default sanerad (#185 / PR #211 — **KLAR**).
- [ ] **3. TD-116** — consent-/disclosure-copy avslöjar e-postleverans för
      användaren (#186).
- [ ] **4. TD-114** — stranded-Queued-reaper (#184 / PR #212 — **KLAR**) +
      **Resend `Idempotency-Key`** på real-send-vägen (#187 / PR #230 — **KLAR**;
      VO `MatchNotificationIdempotencyKey`, ad-scoped Direct + content-hash Digest).

Källa: ADR 0080 §"Prod-Resend-flip pre-condition checklist"; ROPA-behandlingen
"Bakgrundsmatchnings-notiser via e-post (Resend)".

---

## 2.6 GRIND (mänsklig, interim): integritetspolicyns "planerat"-formuleringar (#852)

> **Detta är en MÄNSKLIG grind, inte en mekanisk.** Ingenting hindrar
> `git tag v1.0.0 && git push --tags` från att gå igenom med policyn oflippad —
> en människa måste läsa den här sektionen före taggen. Rubriken säger därför
> inte "HÅRD": ordet hade hävdat en egenskap instrumentet inte har, och husets
> egen lärdom (#861, samma epik-uppsättning: en CI-defekt besvaras inte med en
> mänsklig regel; *fail loud over fail silent*) gäller lika här.
>
> **En mekanisk grind är skyldig, och skyldigheten är placerad:** epik #1034
> (`make the flow's gates mechanically enforced, not remembered`). Den byggs
> tillsammans med prod-pipelinen (Hetzner-cutover, ADR 0050) — det finns idag
> **inget tagg-triggat workflow alls** att hänga en grind på (`deploy-dev.yml`:s
> `push: tags`-trigger är borttagen). Därför är checklistan det rätta
> *interim*-instrumentet, inte sluttillståndet.
>
> **Den mekaniska grinden ska levereras före eller med den första `v*`-taggen.**
> Den mänskliga grinden får inte vara det enda instrumentet i det ögonblick den
> först bär verklig risk. Att dokumentera ett gap skapar en skyldighet att stänga
> det: ett känt gap som överlever sin egen relevans är sämre än ett odokumenterat,
> eftersom det bevisar kännedom (Art. 5(2)/24(1)). Exponeringsfönstret är tomt i
> dag — grinden kan inte behövas före en prod-deploy, och #1034:s mekanism rider
> samma prod-pipeline — men den sammanfallande tidplanen är en tillfällighet tills
> den skrivs ut, vilket den härmed är.
>
> **Grinden bär redan sitt eget maskinläsbara predikat:** punkt 2:s
> inventeringsgrepp ÄR assertionen. Bygg dock INTE den naiva formen "fäll taggen
> om någon `planerat` återstår" — planerat-påståenden får legitimt kvarstå för
> icke-aktiverade behandlingar, så den kontrollen skulle tvinga fram förtidiga
> flippar, dvs. exakt den skada sektionen finns för att förhindra. Två
> aktiveringstillstånds-OBEROENDE invarianter kan byggas nu (observe-only per
> CLAUDE.md §2.5 till en Klas-ratchet): **(a) sv/en-paritet** på planerat-
> radmängden (fångar mekaniskt det mest sannolika felet — att flippa ett språk;
> mängderna är idag radidentiska), och **(b) `privacy.updated`-datumparitet**
> mellan språken. Full form: ett trackat aktiveringstillstånds-manifest per
> behandling + en CI-assertion på `v*`-ref:en att manifestet matchar policyns
> planerat-mängd — det inverterar kontrollen rätt (kräver inte en flip, kräver
> att publicerad copy matchar ett deklarerat tillstånd).
>
> Gäller **den första `v*`-taggen till prod** och varje senare release som
> aktiverar en behandling policyn ännu beskriver som planerad. Detta är en
> **aktiverings**-händelse, inte en copy-händelse — därför bor den här och inte i
> en PR.
>
> **Läget idag är korrekt, inte trasigt.** Policyn beskriver ansökningshistorik/
> företagsöversikt, SCB-uppslag, Hetzner och Cloudflare som planerade. Koden är
> skeppad till dev, men det finns ingen prod-deploy och inga registrerade som når
> policysidorna — policyn styr den *driftsatta* tjänsten. **Flippa aldrig i
> förväg**, och för SCB är det inte ens ett val mellan två oriktigheter: prod-
> providern är `NullCompanyRegistry` och den riktiga adaptern finns inte, så ett
> presens-påstående skulle hävda en överföring till en myndighet som **bevisligen
> inte sker**. I samma sekund en release aktiverar en behandling blir dess
> planerat-mening falsk, och en behandling som körs under en policy som förnekar
> att den körs är enligt ADR 0090 D3 *"unlawful-by-transparency-defect until the
> policy is honest"* (Art. 12/13). Konsekvensen är juridisk, inte kosmetisk.
>
> **CC får ALDRIG utföra flippen på eget mandat och aldrig signera ett
> biträdesavtal** (samma reservation som §2.5). Att publicera ett
> transparens-påstående är en juridisk handling — CC förbereder diffen, Klas
> beslutar och släpper.

- [ ] **1. Inventera hela ytan** — men gör **punkt 2:s triage FÖRST**: aktiverar
      releasen ingen av behandlingarna är rätt utfall att bocka hela sektionen och
      sluta, utan att röra en rad. Inventeringen finns för att punkt 2 sa att det
      finns något att göra. (Inte bara den avslutande meningen:)
      ```bash
      grep -n "planerat\|planerad\|planeras" web/jobbliggaren-web/messages/sv/content-legal.json
      grep -n "planned"                      web/jobbliggaren-web/messages/en/content-legal.json
      ```
      Vid 2026-07-26: **8 + 8** (sv rad 37, 49, 71, 75, 76, 95, 96, 131 — alla äkta
      statuspåståenden, ingen falsk träff med detta mönster). **Grepa INTE bara på
      `"planerat och ännu inte i drift"`** — det ger 6 och missar de TVÅ
      retentionsposterna på rad 95 och 96, som bär `(planerat)` utan
      avslutningsmeningen. Rad 95 (annonskorpusens lagringsrad, #880) nämner
      ansökningshistoriken som ett ÄNDAMÅL med att arbetsgivarens identitet
      sparas; rad 96 är ansökningshistorikens egen post. Lagringstiden är en egen obligatorisk
      uppgift (Art. 13(2)(a)) och ADR 0090 D3 räknar uttryckligen upp
      retentionsraden som del av samma leverans. Flippar du 6 och lämnar 1 säger
      kategorilistan drift medan retentionsavsnittet säger planerat.
- [ ] **2. Avgör vad releasen faktiskt aktiverar** — tre olika klasser, blanda dem
      inte:
      - **Kod-aktiverad:** ansökningshistorik/företagsöversikt (rad 37, 95, 96, 131).
        Handlers + endpoints + FE är skeppade utan feature-flagga → aktiveras av
        att tjänsten alls går i drift.
      - **Deploy-aktiverad:** Hetzner, Cloudflare (rad 75, 76) → aktiveras av att
        stacken körs hos dem. Se punkt 3 — dessa får inte flippas på egen hand.
      - **Konfigurations-grindad:** SCB (rad 49, 71). **Aktiveras INTE av en
        `v*`-tagg.** Två skilda mekanismer, båda mörka i prod: per-sökningens
        `ICompanyRegistry` (ADR 0088) får `NullCompanyRegistry` — valet styrs av
        `CompanyRegistry:Provider`, den riktiga adaptern siktar på SCB:s nya
        API (~sept 2026) och dess **första verkliga överföring är hårt grindad på
        DPIA #456 + SCB terms review** (ADR 0088 D3); bulk-populeringen
        `IScbCompanyRegisterSource` (ADR 0091) är Worker-only och grindad på
        `ScbRegister:Enabled=true` + klientcert, och skickar aldrig ett
        användarskrivet org.nr. **Flippa rad 49/71 först när respektive grind är
        passerad** — inte när koden deployas.
      Kvarstående planerat-meningar för behandlingar som fortfarande inte är i
      drift ska stå kvar. Släpper releasen ingen av dem är rätt utfall att **inte
      ändra något**.
- [ ] **3. Art. 28 + Kap. V innan Hetzner/Cloudflare flippas** (speglar §2.5
      punkt 1 — utan detta blir två redan presens-formulerade meningar falska i
      samma ögonblick):
      - signerat **personuppgiftsbiträdesavtal** med **Hetzner** och med
        **Cloudflare** på fil (rad 69 påstår redan *"Med dem har vi
        personuppgiftsbiträdesavtal"* — idag finns inga aktiva biträden alls);
      - dokumenterad **Kap. V-grund** för Cloudflare (US-domicilierat bolag; även
        en EU-only-konfiguration kräver grunden dokumenterad) — rad 82 är ett
        **absolut** påstående: *"I dagsläget sker inga överföringar av dina
        personuppgifter till länder utanför EU/EES"*, och det måste omprövas som
        del av samma flip;
      - ROPA-posterna uppdaterade + **security-auditor-sign-off**.
      DPA-signering = **Klas**, aldrig CC.
- [ ] **4. Paritet sv + en** — båda språken i samma ändring. Formuleringen bärs av
      **fem** element i `privacy.sections`: kategorilistan (rad 37), ändamåls-/
      SCB-avsnittet (49), mottagare + tredjeland (71/75/76), retentionslistan (96)
      och "Inga automatiserade beslut" (131). Missa inte retentionsposten.
- [ ] **5. Bumpa `privacy.updated`** ("Senast uppdaterad: YYYY-MM-DD"), båda
      språken. Skopa till **`privacy.updated`** — filen har fem `updated`-nycklar
      (privacy/terms/cookies/accessibility/recruiterNotice).
- [ ] **6. Tidsordning — två olika fall, blanda dem inte:**
      - **(a) Första prod-taggen:** flippen deployas **samtidigt** med
        aktiveringen. Inga registrerade finns före, så ingen förhandsinformation
        är möjlig eller krävd.
      - **(b) Senare release med befintliga registrerade:** informationen
        publiceras **FÖRE** aktiveringen. Ansökningshistoriken är enligt ADR 0090
        D3 *"a new purpose section under 6(1)(b)"*, dvs. vidarebehandling för ett
        nytt ändamål av redan insamlade uppgifter → **Art. 13(3) kräver
        information "prior to that further processing"**, och policyns eget löfte
        (rad 150) säger *"Vid mer betydande ändringar informerar vi dig på lämpligt
        sätt"*. Formulera som förhandsbesked (*"från och med &lt;datum&gt; behandlar vi
        även …"*), aldrig som påstående om pågående drift.
      Aldrig **efter** aktiveringen i något av fallen.
- [ ] **7. Konsistenskontroll efter flippen** (per behandling, båda språken). För
      varje behandling ska **alla** dess omnämnanden ha samma status.
      Ansökningshistoriken nämns på fyra ställen (kategorilistan, retentionslistan,
      "Inga automatiserade beslut" och Art. 30-registret); SCB på tre
      (ändamålslistan, mottagarstycket, "Överföring till tredje land"). **En
      mottagare får aldrig stå som planerad medan behandlingen som skickar till
      den står som i drift, och omvänt.** Kör inventeringsgreppet igen efter
      flippen: antalet träffar ska minska med **exakt** antalet poster releasen
      aktiverar, aldrig med fler.
      **Rad 131 kräver särskild kontroll — den är den enda rad greppet inte
      självskyddar.** Dess inledning (`planerar` / `plans`) matchas INTE av
      inventeringsmönstret (verifierat: 0 träffar), så raden syns bara via sin
      avslutande mening. Tas bara den bort faller raden ur greppet helt, räkne-
      testet ovan säger "minskade med exakt 1 — korrekt", och policyn påstår
      fortfarande *"Jobbliggaren planerar en översikt av din egen
      ansökningshistorik"* — mitt i avsnittet **"Inga automatiserade beslut"**,
      dvs. i Art. 22-negationen. Läs rad 131 i sin helhet: hela stycket skrivs om
      till presens, aldrig trunkeras. (De övriga sex raderna bär `(planerat)`/
      `planeras` i själva sakpåståendet och lämnar därför kvar en grepp-träff om
      flippen är ofullständig.)
- [ ] **8. Art. 30-registret speglar flippen** —
      `docs/runbooks/gdpr-processing-register.md`, Art. 30(1)(d)/(f). OBS: den
      filen är **gitignorerad**, alltså osynlig för CI och för en PR-granskare.
      Den är en accountability-spegel, **inte** grinden — den normativa texten bor
      i den här filen, som är trackad.
- [ ] **9. security-auditor + design-reviewer** på copy-diffen (Art. 12/13 + civil
      ton, CLAUDE.md §10) — det är en renderad juridisk sida.

Varför grinden bor här: plikten var tidigare spårad **enbart** i
`docs/decisions/0090-*.md` och en `docs/reviews/`-rapport — **båda gitignorerade**,
alltså osynliga för CI, för en PR-granskare och för en parallell CC-session
(#852:s acceptanskriterium 4). Den här filen är trackad; det är hela poängen.

Källa: #852 · ADR 0090 D3 · ADR 0088 D3/D4 (SCB per-sökning, hård grind) ·
ADR 0091 (SCB bulk-populering) · #824 PR 4 (som kvalificerade golv-semantiken i
samma stycken men medvetet inte flippade dem).

> **OBS om ADR-referenserna ovan:** ADR 0074+ är **gitignorerade** (CLAUDE.md
> §6.5) och finns bara i huvudkopian — alltså osynliga för CI, för en
> PR-granskare och för en parallell CC-session, precis som ROPA-filen i punkt 8.
> Därför är de lastbärande citaten **inlinade ordagrant** i punkterna ovan
> ("unlawful-by-transparency-defect until the policy is honest", "a new purpose
> section under 6(1)(b)", "prior to that further processing"): sektionen ska stå
> självständigt utan sina källor. Citaten finns kvar för Klas' egen
> revisionskedja, inte som något en granskare kan följa.

---

## 3. Tagga + deploy

```bash
# Verifiera HEAD är exakt det som ska släppas
git log --oneline -1
git rev-parse HEAD

# dev/staging — automatisk efter push
git tag v<X.Y.Z>-dev <HEAD> && git push origin v<X.Y.Z>-dev      # → dev
git tag v<X.Y.Z>-rc1 <HEAD> && git push origin v<X.Y.Z>-rc1      # → staging

# prod — KRÄVER Klas-GO innan tag-push (CLAUDE.md §9.2)
git tag v<X.Y.Z> <HEAD> && git push origin v<X.Y.Z>             # → prod (manuell approval i pipeline)
```

CC får **inte** push:a en prod-tag (ren `v*`) utan explicit Klas-GO i
sessionen. dev/rc-tags är CC-tillåtna efter grön CI.

---

## 4. Efter deploy (verifiering)

> Hetzner-modell (ADR 0050/0066): hela stacken (API + Worker + Postgres + Redis +
> Caddy + Next.js) kör i Docker Compose på CAX31-boxen bakom Caddy. Konkreta
> service-namn/kommandon finalize:ras med **#196 / TD-106** (Compose-stack + proxy
> + härdning) — stegen nedan är på modell-altitud tills dess.

- [ ] **Compose-tjänster startar** (api + worker) — `docker compose ps` på boxen
      visar dem `healthy` (konkret service-namn/compose-fil: #196/TD-106).
- [ ] **`/api/ready` → 200** mot målmiljöns domän (strict readiness: DB +
      Redis dependency-checks, TD-29).
- [ ] **`/api/health` → 200** (liveness).
- [ ] **Hangfire-jobben** kör enligt schema om release rör Worker
      (`*/10`-cron etc.) — verifiera i Hangfire-dashboard/loggar.
- [ ] **Audit-wire** — om release rör audit-genererande flöden: bevisa
      INSERT i `audit_log` via den strukturerade logg-sinken (MEL → Seq; full
      prod-sink = TD-104) + direkt `audit_log`-query (ADR 0035).
- [ ] **Ops-signaler granskade** — health-checks + extern uptime-monitor
      (UptimeRobot/BetterStack, ADR 0050 — ersätter ALB/CloudWatch-health);
      jobtech-sync-/auditor-write-/log-pipeline-health läses via logg-sinken.
      Konkret alerting-konfig: #196/TD-106 + TD-104.
- [ ] **Frontend** (om i scope) — Lighthouse observe-signal mot
      ADR 0045-budgetar; manuell rök-test av kritiska flöden.
- [ ] **Rollback känd** — återställ föregående byggda image-tag via Compose
      (se §5); konkret procedur #196/TD-106.

---

## 5. Rollback

Vid fel efter prod-deploy (Hetzner-modell, ADR 0050 "Rollback" amenderat
2026-06-08 — AWS-stacken är riven, ADR 0066):

```bash
# På CAX31-boxen: pinna image-taggen tillbaka till föregående release och
# re-deploya Compose-stacken. Samma image-byggväg som prod (next build / dotnet
# publish körs i CI → enbart den byggda imagen skickas till boxen), så den lokala
# Docker-Compose-stacken är dev/prod-paritets-baselinen vid en misslyckad cutover.
IMAGE_TAG=<föregående-release> docker compose up -d
# Konkret tag-mekanism + service-namn finalize:ras med #196/TD-106 (ADR 0050).
```

Notera incidenten i `docs/sessions/` + relevant runbook. Skapa ADR om
rollback avslöjar ett arkitekturellt problem (CLAUDE.md §8 punkt 9).

---

## 6. Efter release (docs-synk)

- [ ] `docs/current-work.md` — status uppdaterad (CLAUDE.md §1.5).
- [ ] Session-logg i `docs/sessions/` om release var en egen session.
- [ ] `docs/steg-tracker.md` om STEG flyttat status.
- [ ] Tag + miljö noterad så nästa release vet senaste prod-state.

---

## Referenser

- ADR 0019 (direct-push + tag-semantik), ADR 0033/0034 (migrations/DB-roller),
  ADR 0035 (audit-wire), ADR 0050 (Hetzner-deploy: CAX31 + Caddy + Compose +
  rollback-modell) / ADR 0066 (AWS-exit), ADR 0036 (ops-alarms — supersederad av
  ADR 0050:s health-check/uptime-monitor-modell), ADR 0044 (coverage-gate),
  ADR 0045 (perf observe-only-signaler); TD-106 (konkret Compose-stack) / TD-104
  (logg-sink/observability)
- CLAUDE.md §6.3 (granskningsspärrar), §8 (DoD), §9.2 (deploy kräver Klas-GO)
- BUILD.md §15 (deployment/rollback)
- `docs/runbooks/v0.2-prod-launch-checklist.md` — engångs-checklist för
  *första* prod-deployen (komplement, inte ersättning för denna)
