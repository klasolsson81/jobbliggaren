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
- [ ] **Öppna issues märkta `P0`/`P1`/`mvp` mot release-scope** genomgångna (GitHub Issues)
      — varje launch-blocker löst eller medvetet deferrad med motiv. Issues märkta
      `mvp` är de som krävs för riktiga användare (etikettens regel: CLAUDE.md §6.5). (TD-registret retirerades
      2026-08-02, ADR 0121; parkerade poster ligger i #1172.)
- [ ] **Migrations** — om EF Core-migration ingår: verifiera schema-mode-
      dispatch (ADR 0033) och DB-roll-separation (ADR 0034); Identity-schema-
      ändring → manuell procedur (parkerad, #1172).
- [ ] **Kollations-version — ENDAST vid Postgres-image-bump eller major-uppgradering**
      (#884, **ADR 0110** — den tidigare pekaren till ADR 0109 var fel; 0109 är
      "The engine describes, the user classifies" och rör CV-lanen). Ett btree-index på
      text är byggt **med** en kollation. Ändras
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

      **DEN HÄR GRINDEN LÄSER DEN TAGGADE MILJÖN, OCH DET RÄCKER INTE SEDAN 2026-08-04**
      (#1197 / PR #1206). Dependabot har nu en `docker-compose`-post, så `postgres:18.3`
      bumpas automatiskt i `docker-compose.yml`. Basimagen bär ICU-biblioteket, migration
      `20260714170816` deklarerar `public.swedish` som en **ICU**-kollation, och
      **dev-databasen är den enda som i dag håller riktiga data** (106 071 annonser,
      1 066 938 företagsrader). Grinden ovan ser aldrig den bumpen — den läser den taggade
      miljön vid tag-tillfället. **Kör därför steg 1 mot dev-DB:n också efter varje
      postgres-bump**, inte bara före tag.
      *(Samma PR gjorde **varje** image-bump icke auto-mergebar i
      `dependabot-automerge.yml` — det generella skälet är att ingenting läser den image
      som ändras; att just compose-felmoden är tyst kommer utöver det. En människa läser
      dem numera. Den här raden finns ändå: en människa som läser en grön diff ser inte
      att ICU-versionen rörde sig.)*
- [ ] **Om en migration faller på `lock_timeout` — kör om den, det är säkert.** Migrationen
      som sätter kollationen (#884) tar ACCESS EXCLUSIVE och binder sin väntan till 3 s.
      Krockar den med en långkörande transaktion får du
      `canceling statement due to lock timeout` och **hela migrationen rullas tillbaka
      atomärt** (verifierat mot riktig Postgres med en konkurrerande AccessShareLock:
      avbrott efter 3001 ms, databasen orörd). Inget delvis applicerat tillstånd kan
      uppstå. Vänta ut den blockerande transaktionen — typiskt nattsynken — och kör om.
      Det är felläget guarden **finns** för: ett högljutt deploy-fel i stället för ett
      tyst läs-avbrott.
- [ ] **`ForwardedHeaders:KnownNetworks` + per-IP-kontrollen — TVÅ led, och det andra
      följer INTE av det första** (#1202, ADR 0050 `Amendment 2026-08-04` §5:s punkt *"Per-IP-rate-limiting fungerar"* —
      citerad på sin text, inte på sitt nummer).
      Gäller varje release mot en miljö bakom reverse-proxy.
      - **Led 1 — HÅRD (fail-loud boot).** Värdet måste vara satt för miljön som taggas,
        via Compose-/env-overlay — **aldrig** genom att redigera den committade
        `appsettings.Production.json`, där `[]` är avsiktligt (Klas-beslut 2026-08-04,
        PR #1203: ingen compose-fil i repot deklarerar ett nätverk, så en ifylld gissning
        hade avväpnat grinden). Utan värdet kastar `ForwardedHeadersConfig.EnsureSafeForEnvironment`
        och API:t bootar inte alls. Det ledet kan inte hoppas över — det stoppar sig självt.
      - **Led 2 — MÄNSKLIG, och rubriken säger därför inte "HÅRD" om det.** Att fylla i
        värdet **tystar startkontrollen utan att göra per-IP-limiteringen levande**:
        `UseForwardedHeaders` skriver om `RemoteIpAddress` bara när en `X-Forwarded-For`
        faktiskt anländer, och mätt 2026-08-04 skickar ingen komponent i Option B-stacken
        någon — sex IP-partitionerade rate-limit-policies delar en hink oavsett värde.
        **Beviset läses på SVARSSIDAN:** en request från en känd klient-IP ska synas med
        den IP:n i rate-limit-partitionen **och** i auth-revisionsspåret. **En grön
        `EnsureSafeForEnvironment` är inte beviset.** Ingenting hindrar taggaren från att
        hoppa över det här ledet; det är därför #1202 dessutom är ett blockerande
        acceptanskriterium på #196 (spärrhaken i Klas-beslutet).
- [ ] **GDPR-konsekvens** för nytt scope bedömd (CLAUDE.md §8 punkt 8) — ny
      PII? loggning? retention? Audit-wire intakt (ADR 0035)?
- [ ] **Secrets-hygien** — inga nya secrets i klartext; gitignored
      `appsettings.Local.json` lokalt / managed secrets-store i ops + DEK-envelope
      (`IDataKeyProvider`, ADR 0066/0049) för allt känsligt (CLAUDE.md §5; AWS
      Secrets Manager + KMS rivet, ADR 0066).
- [ ] **Lokal diff-granskning** (CLAUDE.md §6.3 mekanism 4) — Klas läser
      `git log` + `git diff` för release-spannet.

---

## 2.5 HÅRD GRIND: e-post-prod-flip (ADR 0080; provider bytt i ADR 0124, bytt igen i ADR 0131)

> **ETT HEM PER TAL (regel, 2026-07-26).** Varje räknebart påstående i §2.5/§2.6 står på
> **exakt ett** ställe, tillsammans med greppet som regenererar det. Alla andra omnämnanden är
> pekare **utan siffra**. Regeln gäller de **grep-regenererbara** talen, och de har tre hem:
> rutantalet (blockquoten nedan), punkt 1:s led (punkten själv) och §2.6:s inventering (§2.6
> punkt 1). Övriga tal i sektionerna är **inte** hem och skyddas inte av regeln — den kvarvarande
> spegeln är mall-antalet, som bor på: blockquoten nedan, `Källa:`-stycket och `BUILD.md` §13.4.
> En femte kontolivscykel-mall gör alltså **varje mening i den uppräkningen** falsk, i två filer —
> skrivet ut här i stället för att låtsas att uppräkningen ovan är fullständig. (Citatet av själva
> talet är struket: ett tal i ett citat är fortfarande ett tal på en andra plats — samma skäl som
> att rutgreppet nedan räknar prosacitatet av `- [ ]`.)
>
> **Varför regeln finns, mätt:** under #186 gick **sex** tal stale i den här filen — och två av
> dem falsifierades av tillägg i **samma commit som skrev siffran**. Rond efter rond synkades
> speglarna, vilket botar instansen och inte generatorn: så länge ett tal bor på fler än ett
> ställe är nästa tillagda punkt garanterad att producera nästa fynd. **Lägg aldrig till ett tal
> på en andra plats** — skriv "antalet står i ‹hem›" i stället.

> Gäller ENDAST en release som i non-dev aktiverar **en providerarm som når en extern
> processor** — alltså varje `Email:Provider`-värde vars arm gör det. **Mängdens hem är
> `AddEmailSender` i `src/Jobbliggaren.Infrastructure/DependencyInjection.cs`, inte den här
> raden**; mätt 2026-08-15 är det enda sådana värdet `Scaleway`.
> ⚠ **`IEmailSender.CanDeliver` är INTE predikatet** — den svarar `true` även för
> `ConsoleEmailSender`, som loggar lokalt och inte når någon extern processor, och en läsare som
> tar den för predikatet drar in Development i grinden (`dotnet-architect` N3). Läs armen, inte
> förmågan.
> Tills dess kör `NullEmailSender` — ingen
> e-post skickas, och denna grind är inte relevant.
>
> ⚠ **PREDIKATET ÄR FORMBASERAT SEDAN 2026-08-15, OCH DET ÄR EN REPARATION AV EN MÄTT DEFEKT**
> (senior-cto-advisor, bindande). Det löd tidigare *"aktiverar `Email:Provider=Ses`"* — ett
> namn, inte en form. När E1 gjorde `Ses` till ett registreringsfel blev villkoret **omöjligt
> att uppfylla**, och grinden läste därmed permanent "inte relevant" medan flippen som faktiskt
> är på väg heter något annat. Felriktningen är det avgörande: ett formbaserat predikat
> över-triggar på sin höjd (en läsning till), medan ett namnbaserat **under**-triggar och släpper
> igenom en verklig processor tyst. Providernamnet står därför kvar som **daterad mätning**, aldrig
> som rekvisit — samma skäl som `mvp`-etiketten och antalsraderna nedan bär sina datum.
>
> **PROVIDERN BYTTES 2026-08-08 (ADR 0124, #1237) OCH GRINDENS PREMISS ÖVERLEVER INTE BYTET
> OFÖRÄNDRAD.** Sektionen skrevs mot Resend, Inc. — ett **amerikanskt** biträde. Motparten är nu
> **Amazon Web Services EMEA SARL (Luxemburg)** med behandling i `eu-north-1`, vilket är en annan
> juridisk person, ett annat avtal och ett annat överföringsläge. Den bedömningen är
> `security-auditor`:s tillsammans med Klas och var **inte gjord här** — så varje Resend-specifikt
> led i punkt 1 återöppnades till **KVAR**. Det var avsiktligt strängare än läget före bytet:
> en grind får aldrig ärva ett grönt led från en motpart som inte längre är part.
> **DEN ÅTERÖPPNINGEN ÄR DELVIS UPPHÄVD 2026-08-09 (#1169), och på återöppningens EGET villkor:**
> villkoret var att bedömningen inte var gjord, och `security-auditor` gjorde den 2026-08-08.
> Led (b) och (d) bär därför ingen KVAR-markering längre, och led (c) står **KVAR (delvis)**.
> **PROVIDERN BYTTES IGEN 2026-08-15 (ADR 0131, #183), OCH LEDEN ÅTERÖPPNADES PÅ DEN HÄR
> PREAMBELNS EGEN DOKTRIN — andra gången, samma regel.** AWS vägrade 2026-08-14 permanent att
> häva sandbox-läget (200 mejl/dygn, enbart till verifierade mottagaridentiteter), vilket gör
> riktiga testanvändare omöjliga och avslutade SES-spåret; Klas valde **Scaleway Transactional
> Email** i `fr-par`. Varje AWS-specifikt led återöppnades: *en grind får aldrig ärva ett grönt
> led från en motpart som inte längre är part.* ⚠ **Led (d) var den farliga** — det bar ingen
> KVAR-markering och hade därför läst grönt medan den publicerade policyn namngav Amazon Web
> Services EMEA SARL, vilket är ordagrant den felmod styckena ovan dokumenterar från
> Resend→AWS. **Samma ändring som återöppnade skrev också om:** led (b) är omprövat (Kap. V
> **upphör att vara tillämplig** — en annan sak än att vara uppfylld), led (d) är levererat i
> källan med live-verifiering kvar i §2.6, led (c) är ombundet till Scaleway. **En strykning
> ärvs inte heller** — punkt 4:s strukna idempotens-led är ommätt mot Scaleway, se punkten.
> **Punkten är fortsatt inte grön** — och **vilka** led som bär KVAR står i leden själva,
> aldrig här. *(Uppräkningen stod här till 2026-08-15 och var ett andra hem som gick stale i
> samma andetag som återöppningen ovan ändrade mängden. Läs statusen på leden.)*
> Vad som INTE ändras av bytet: mottagar-adress **+ meddelandets innehåll** når en extern
> processor oavsett jurisdiktion (för notiserna
> **avslöjar** leveransen opt-in-faktumet, och `EmailTemplates` skriver det dessutom i klartext
> i själva kroppen — själva *flaggan* i vår DB överförs aldrig, men faktumet gör det). Ett kontolivscykel-mejl har inget opt-in — men adressen och innehållet
> når providern lika fullt. **VARJE numrerad punkt i DEN HÄR sektionen (§2.5) MÅSTE vara grön innan `Email:Provider`
> flippas** (ADR 0080
> prod-flip-checklista). CC får ALDRIG flippa providern eller signera DPA:t.
>
> **"Grön" = INGET led i punkten bär KVAR — inte att rutan är bockad.** (Negation med flit:
> ett led kan bära **båda** markeringarna — ROPA-ledet är sedan 2026-08-09 **levererat för samtliga
> e-postmallar** men **KVAR (delvis)**, eftersom kontolivscykel-mallarnas rättsliga grunder är
> ett oprövat utkast — och "bär KLAR" hade då räknat det som grönt.) Rutorna i
> hela den här filen är obockade (**38 av 38** vid 2026-08-04 — greppa **radinitialt**
> (`^- \[ \]`); ett rått grep ger 40 och räknar prosacitaten av literalen längre ned.
> **Regenerera siffran ur greppet efter varje tillagd punkt** — punkt 5.5 tillkom i samma
> ändring som skrev "35", och punkt 5 i den som skrev "36" — båda gjordes falska i samma andetag) och bockas av den som **utför** releasen; statusen
> bärs av **KLAR**-markeringarna. Punkt 1:s led står uppräknade i punkten själv, och ett led kan
> vara **delvis** KVAR
> (ROPA-ledet är det i dag, av ett annat skäl än före 2026-08-09: då saknades hela
> kontolivscykel-vägen, nu finns den men dess grunder är oprövade) — **ett delvis KVAR led är
> KVAR**, så punkten är grön först när
> inget av **punktens led** bär KVAR i någon form. Läs aldrig en obockad ruta som "inte levererat",
> och bocka aldrig en ruta för att en förutsättning är levererad.
>
> **Grinden gäller ALL utgående e-post, inte bara bakgrundsmatchnings-notiserna**
> (widening 2026-07-26, #186). `Email:Provider` är EN switch, och `EmailTemplates`
> har **åtta** sorter varav **sex är kontolivscykel** (`EmailConfirmation`,
> `EmailChangeConfirmation`, `EmailChangedNotification`, `AccountExistsNotice`,
> `PasswordReset`, `PasswordChangedNotice`) och
> två är notiser (`MatchNotification`, `FollowedCompanyNotification`). En release som
> aktiverar providern **bara** för e-postbekräftelse triggar därför varje punkt nedan
> lika fullt — mottagar-adressen når en US-processor oavsett vilken mall som skickas.
> Den tidigare avgränsningen "(bakgrundsmatchnings-notiser)" i den här blockquoten är
> därför borttagen: den var ingen avgränsning, och ingenting annat i sektionen skopar
> grinden till notis-vägen. (Prod-lansering
> tvingar inte i sig flippen: `AuthOptions.RequireEmailConfirmation` defaultar
> **false** och sätts `true` bara i `appsettings.Development.json`.)

- [ ] **1. Tredjelands-grund** — **fem** led, per behandling-status (ägare: **#183**).
      *Detta är talets hem: räkna om leden i punkten efter varje tillägg, och lägg det inte någon
      annanstans.*
      - **biträdesavtal med Scaleway på fil** — **KVAR** (Klas, aldrig CC). Mätt 2026-08-15 mot
        Scaleways egna avtalsdokument: DPA:n (gällande version daterad 2024-06-01; ingen senare
        revision hittad) är avtalsdokument **nr 1** i GTS:ens prioritetsordning (version
        07/04/2026, Art. 3) och säger om sig själv att den *"forms an integral part of the
        contract"* — det
        finns alltså **inget dokument att signera**, samma läge som AWS-DPA:t hade och till
        skillnad från netcup (#1199). Ledet är
        ändå KVAR: att verifiera och skriva ned att avtalet gäller, och för vilken avtalspart,
        är inte samma sak som att anta det.
        ⚠ **AVTALSPARTEN ÄR HÄRLEDD, INTE AVLÄST — och det är en SVAGARE mätform än AWS-eran hade.**
        GTS Art. 23 bestämmer entiteten ur kundens faktureringsadress (Frankrike → Scaleway S.A.S.;
        Italien → Scaleway Italia S.R.L.; *"any other region"* → **Scaleway S.A.S.**, R.C.S. Paris
        433 115 904, 8 rue de la Ville l'Évêque, 75008 Paris), och en svensk adress faller i den
        tredje grenen. **Vad som INTE är gjort:** en avläsning av vårt EGET konto som visar vilken
        entitet som faktiskt fakturerar oss. AWS-erans motsvarighet var två oberoende API-svar över
        fem faktureringsperioder; här finns bara regeln, inte utfallet. **Den avläsningen är detta
        leds kärna och är Klas.**
        ⚠ **En kontroll till, som är vår och inte leverantörens:** DPA Art. 7.4 ger 30 dagars
        förhandsnotis vid ändring i underbiträdeslistan **endast** *"providing that it has
        previously subscribed to updates notifications"*. En ansvarig som inte prenumererar har
        avstått invändningsrätten tyst. **Prenumerationen är inte gjord** (mätt 2026-08-15; endast
        kontoinnehavaren kan göra den);
      - dokumenterad **Kap. V-grund** — **KVAR (omprövning ligger i #183:s E3-PR)**. ⚠ **Den
        tidigare statusen "UPPLÖST 2026-08-08" gällde AWS och ärvs INTE** — den domen sa att
        överföringen **ska** redovisas trots `eu-north-1`, med grund **SCC Art. 46(2)(c)**,
        eftersom `BUILD.md` §15.1:s tillämpade standard behandlar ett **US-ägt** biträde som en
        tredjelandsfråga oavsett EU-region. Den domen står som dom över sin egen part och sin egen
        era; ingen personuppgift nådde någonsin SES.
        **UTKAST 2026-08-15 (#183, ADR 0131) — och utfallet är av ett ANNAT SLAG än förr: Kap. V
        blir EJ TILLÄMPLIG, inte uppfylld.** Underlaget: avtalsparten är fransk (ledet ovan),
        behandlingen sker i `fr-par` (residensen vilar på **DPA Art. 11.1/11.2.2**, som utfäster
        EU-nivå — *inte* regionsnivå — i kombination med att `fr-par` är TEM:s enda region; armen
        pinnar regionen i URL:ens path-segment, så DNS kan aldrig belägga den), TEM har **inga
        underbiträden** (leverantörens TEM-FAQ, dokumentationsrang), och ägarkedjan är fransk hela
        vägen upp (Scaleway S.A.S. ← iliad S.A. ← Holdco II ← iliad Holding ← Niel-familjens grupp;
        iliad Holdings årsredovisning 2024 §5.1–5.3). **Kroken som fällde AWS-posten — en
        koncernmoder i tredjeland som kan NÅ uppgifterna — saknas därmed**, och §15.1-standarden
        slår inte. Ingen SCC, ingen adekvans, ingen DPF: inte för att de är avklarade, utan för att
        det inte finns någon överföring att grunda.
        ⚠ **TVÅ FÖRBEHÅLL, del av bedömningen och inte fotnoter:** (1) **`Scaleway US Corporation`
        (Chicago) finns nedströms i koncernen** utan TEM-roll — det ändrar inte ägarriktningen, men
        påståendet "ingen US-enhet i koncernen" är mätt falskt och får inte skrivas; (2) **var
        leverantörens support-/driftpersonal har åtkomst ifrån SAKNAR AVTALSRANG.** ⚠ *Ledet sa
        "ODOKUMENTERAT" till 2026-08-15/16, och det underdrev sitt eget underlag
        (`security-auditor`): TEM-FAQ:ns TIA-svar säger verbatim* "all data is hosted and processed
        entirely within the European Union"*, och under Art. 4(2) omfattar behandling **åtkomst** —
        meningen träffar alltså frågan. Vad som saknas är dess **rang**: dokumentation binder inte
        som DPA Art. 11 gör.* Åtgärden är därför att få **just den meningen bekräftad skriftligt**,
        inte att fylla en lucka från noll — sökt utan avtalsrangigt stöd i TOM-dokumentet, DPA
        Art. 6 och integritetspolicyn. Fjärråtkomst från tredjeland vore i sig en överföring, så
        utkastet är **villkorat av bekräftelsen** (Klas-brevet i led (c)).
        **KARAKTERISERINGEN ÄR `security-auditor`s MED KLAS, inte sessionens.** Ledet bär KVAR tills
        hon ratificerat; hennes dom skrivs in HÄR och statusen läses här, aldrig ur preambeln.
        **`security-auditor` 2026-08-15/16 — DELRATIFICERING. LEDET STÅR KVAR.**
        **Ratificerat, och bär inte på brevet:** den strukturella analysen håller. Kroken som fällde
        SES-posten — en EU-avtalspart under en tredjelandsmoder som kan nå uppgifterna — saknas här,
        och `BUILD.md` §15.1-standarden slår därför inte. Det är oberoende av Klas-brevet och står
        som avgjort.
        **Ratificeras INTE ännu:** slutsatsen att Kap. V är **ej tillämplig**. Den är ett påstående
        om ett **negativt faktum** — att inga personuppgifter görs tillgängliga för en mottagare i
        tredjeland. Under EDPB Guidelines 05/2021 uppfylls transfer-rekvisit 2 redan av att uppgifter
        *görs tillgängliga*, och fjärråtkomst räknas (Rec. 01/2020). **Var support- och driftpersonal
        har åtkomst ifrån är därmed ett KONSTITUTIVT ELEMENT i slutsatsen, inte en fotnot** — och
        förbehåll 2 säger själv att elementet saknar avtalsrang — leverantörens FAQ-mening träffar
        frågan men binder inte, och ett negativt faktum som bär hela slutsatsen kan inte vila på
        dokumentation. En slutsats villkorad av ett obesvarat
        brev är ett utkast, inte en dom.
        **Vad som stänger ledet, uttömmande:** ett skriftligt svar från Scaleway som säger att
        support-/driftåtkomst till TEM-data sker uteslutande inifrån EU/EES — eller, om den inte gör
        det, en Kap. V-grund för just den åtkomsten. Ingenting mer krävs.
        ⚠ **Frågans FORM är hennes, inte valfri:** *"sker support- och driftåtkomst till TEM-data
        uteslutande inifrån EU/EES?"* — inte "var finns supporten", som besvaras med en kontorsadress
        som inte binder;
      - **ROPA-posten** i `docs/runbooks/gdpr-processing-register.md` (lokal) — **KVAR (delvis)**,
        omskriven 2026-08-15 (#183): ombunden till behandlingen *"Utgående
        transaktionell e-post (Scaleway Transactional Email, `fr-par`)"*, som täcker **samtliga
        e-postmallar** (antalet står i blockquoten ovan), båda mottagarklasserna och Kap. V-utkastet
        ovan. **Tre saker är nya i den omskrivningen och har sitt hem DÄR, inte här:** (i)
        **blocklists** — providern lagrar studsade mottagaradresser på eget initiativ, med en
        egen retentionstrappa och en egen Art. 17-väg (**trappan står i ROPA:n; upprepa den aldrig
        här**); (ii) **webhooks är opt-in och ingen är registrerad**, så event-payloadens `email_to`
        aldrig uppstår — mätt med `git grep -in "webhook" -- src deploy web/jobbliggaren-web/src`;
        (iii) **TEM:s content-/loggretention är EJ MÄTT** och står som schemaläggning, aldrig som
        antagande.
        ⚠ **ETT BREV STÄNGER TVÅ OMÄTTA FRÅGOR, OCH DET ÄR KLAS ATT SKICKA:** retentionen ovan och
        support-geografin i led (b). Vägarna är *Specific Conditions Transactional Email*
        (produktvillkoret finns listat på leverantörens avtalssida men kräver inloggat konto) eller
        en skriftlig fråga till leverantörens integritetsfunktion. **Ingen flip innan båda är
        besvarade.**
        Registret speglar och grindar inte
        (#1040), och **statusen på det här ledet sätts av sign-off-ledet nedan, inte av att
        posten finns** — kontolivscykel-mallarnas rättsliga grunder är CC:s utkast och
        har aldrig prövats av `security-auditor`;
      - **integritetspolicy-post som namnger providern** — **ÅTERÖPPNAD OCH OMSKRIVEN I KÄLLAN
        2026-08-15 (#183)**. ⚠ **Detta led bar ingen KVAR-markering när providern byttes, och det
        var grindens farligaste punkt:** hade E3 stängt de övriga leden mot Scaleway utan att röra
        det här, hade grinden lästs grön medan den publicerade policyn namngav Amazon Web Services
        EMEA SARL. Nu: **tre** stycken × två språk namnger Scaleway SAS (Frankrike) med behandling i
        `fr-par`. **Det fjärde stycket är struket MED SIN GRUND** — tredjelandsavsnittets
        e-poststycke, SCC-grunden och Art. 13(1)(f)-vägen till en kopia av skyddsåtgärderna
        förutsatte alla en överföring som inte längre uppstår; copyn är därmed **tyst** om Kap. V för
        e-posten, precis som den redan är för värden. Markörmeningen står kvar i alla strängarna —
        **detta var inte flippen**.
        ⚠ **KÄLLA ÄR INTE PUBLICERAD SAJT.** Ledet läses grönt för **källan**; den publicerade copyn
        namnger den gamla providern tills närmast följande webb-deploy, som är en **annan händelse**
        och grindas av **§2.6**, inte av `Email:Provider`. `content-legal-parity.test.ts` är
        ompinnad till `Scaleway SAS` i samma ändring, så en halvflippad katalog kan inte bli grön;
      - **security-auditor-sign-off på prod-e-post-konfigen** — **KVAR**. Det gamla
        TD-116:s sign-off är PR-4:s, inte prod-konfigens; bocka aldrig punkten på den.
        (TD-116 stängdes 2026-07-26; residualen ägs av #183.)
        **Namngivna förutsättningar för sign-off (security-auditor + code-reviewer
        2026-08-09, #1169) — hon signerar inte utan dem.** *(Medvetet utan numeral: listan räknar
        sig själv, och ett tal här hade blivit ytterligare ett hem medan blockquoten ovan räknar upp
        sina och säger att övriga tal inte är hem. Den raden bar en numeral och en hem-deklaration
        till 2026-08-09; `dotnet-architect` mätte att den gjorde uppräkningen falsk i samma commit
        som skrev den — filens egen dokumenterade felmod.)*
        1. **Organisations- och projektbindning.** Avtalsparten är en egenskap hos en
           ORGANISATION — GTS Art. 23 bestämmer entiteten ur faktureringsadressen — och hela
           ej-tillämplig-bedömningen i led (b) hänger på vilken part. **Mekanismen bytte med
           providern 2026-08-15; skyldigheten gjorde det inte.** Kör med **prod-nyckeln** (den som
           hamnar i `Email:Scaleway:SecretKey`) ett autentiserat anrop som returnerar nyckelns
           organisation och projekt, och kräv **Organization == den organisation ledet (a):s
           avtalsmätning gjordes mot** och **Project == `Email:Scaleway:ProjectId`**.
           ⚠ **Den andra halvan är ny och lätt att missa:** `ProjectId` är konfigurationssidigt och
           skickas i varje request-kropp, men **bindningen mellan NYCKELN och projektet följer inte
           av konfigurationen** — den är ett tillstånd hos leverantören. Utan mätningen kan
           avtalsmätningen vara gjord mot en organisation medan nyckeln tillhör en annan, vilket är
           exakt fällan AWS-erans kontobindning fanns för.
        2. **Kontolivscykel-mallarnas rättsliga grunder prövas — SEX mallar, inte fyra.** ROPA:ns
           utkast är Art. 6(1)(b) för `EmailConfirmation`, `EmailChangeConfirmation` och
           **`PasswordReset`**, och **Art. 6(1)(f)** för `EmailChangedNotification`,
           `AccountExistsNotice` och **`PasswordChangedNotice`**.
           ⚠ **De två fetstilta lades till 2026-08-12 (#183) och är de yngsta och minst prövade.**
           `PasswordReset`/`PasswordChangedNotice` (#1171) hade fram till dess **ingen Art. 30-post
           alls** — de landade efter registrets omskrivning 2026-08-09 och togs aldrig upp. Den som
           arbetar detta led före 2026-08-12 prövade fyra grunder och trodde sig klar; räkna sex.
           `security-auditor` har dessutom rest en öppen fråga om `PasswordChangedNotice`: 6(1)(f)
           mot registrets egen 6(1)(c)+Art. 32-konstruktion, vilket avgör om en Art. 21-invändning
           måste kunna bemötas. **Står 6(1)(f) kvar efter prövningen
           krävs en matchande Art. 13(1)(d)-post i policyn FÖRE flippen** — den träffar då **tre**
           mallar, inte två. Dagens
           berättigat-intresse-avsnitt räknar upp fyra behandlingar och ingen av dem är e-post.
           Faller de i stället ut som 6(1)(b) täcks de av befintlig copy och luckan stänger sig
           själv. **En behandling som körs utan redovisad grund är en Blocker i det ögonblicket**,
           inte en Minor.
        3. **Nyckelrotation för den statiska providernyckeln** — ingen instance role finns, så
           nyckeln är långlivad per definition. Skyldigheten är oförändrad sedan 2026-08-08 och
           återregistreras här så den inte tappas; ägs även av #198. **Sedan 2026-08-15 gäller den
           `Email:Scaleway:SecretKey`.** ⚠ **`ProjectId` roterar INTE och ska inte behandlas som en
           nyckel** — det är en identifierare, inte en hemlighet, men den injiceras som en egen fil
           med egen livscykel (E2) och loggas aldrig. De två har alltså skilda regimer trots att de
           levereras genom samma söm.
        4. **Ingen mottagarnivå-spårning får uppstå på den sändande identiteten.** ⚠ **MEKANISMEN
           DOG MED PROVIDERN 2026-08-15, EGENSKAPEN ÖVERLEVDE — och ledet får därför INTE strykas.**
           Fram till dess löd det *"sändande identitet får inte bära ett default configuration
           set"*, verifierat med `aws sesv2 get-email-identity`: `ConfigurationSetName` är ett
           AWS-begrepp utan Scaleway-motsvarighet, så instrumentet är borta. Vad ledet finns för —
           att ingen mottagarnivå-metrik ska uppstå hos processorn — är providerneutralt och står
           kvar (`vps-deploy-stack.md` rad 35 bär samma bestämning).
           **Scaleway-grunden är STARKARE än SES-grunden var, och det är en skillnad i art:** för
           SES var frånvaron ett *tillstånd att underhålla* (requesten fick inte namnge ett
           configuration set, och ett default kunde ändå hängas på identiteten utan att synas i
           requesten). Scaleway TEM har **ingen öppnings- eller klickspårning alls** — inget fält i
           send-API:t, ingen configuration-set-analog, och funktionen finns som en **öppen feature
           request** hos leverantören (mätt 2026-08-15). Det finns alltså inget providersidigt
           tillstånd att sätta fel.
           ⚠ **Priset för den starkare grunden är att den inte kan pinnas:** det finns ingen
           requestegenskap kvar att asserta, så `ScalewayEmailSenderTests` bär ingen motsvarighet
           till den raderade `SesEmailSenderTests`-pinnen. **Verifieringen VID flippen är därför en
           ommätning av frånvaron hos leverantören** — läs API-referensen och produktens
           changelog och bekräfta att ingen spårningskonfiguration tillkommit. En feature request
           kan skeppas mellan två mätningar; 2026-08-15 var bevis för den dagen och ingen inlösen.
           ⚠ **Blocklists är den enda providersidiga lagringen av mottagaradresser som uppstår, och
           den uppstår automatiskt.** Retentionstrappan och Art. 17-vägen har sitt hem i ROPA:n —
           **upprepa dem inte här** (ETT HEM PER TAL). code-reviewer Minor 3, 2026-08-09.
           ⚠ **DET ANDRA SKÄLET ÄR BYTT 2026-08-12 (#183) — läs inte den gamla formuleringen.**
           Fram till dess var skäl 2 *"ingen HTML-del"*. Mejlen bär numera en HTML-del, så det skälet
           är **struket**. Ersättningen är **ingen fjärresurs i HTML-delen**, pinnad över alla åtta
           mallarna i `EmailHtmlNoRemoteResourceTests`. **Den exakta förbjudna mängden är detektorns
           egna arrayer i `RemoteResourceDetector`, inte den här raden** — en regel med tre prosa-hem
           är tre hem att revidera. Den här raden räknade tidigare upp mängden utan den kvalifikation
           detektorn faktiskt bär: attribut- och URL-armarna körs över **levande markup**, inte över
           hela dokumentet, eftersom kodad annonstext bokstavligen innehåller `src=` och en URL utan
           att kunna hämta något. Egenskapen som gör den
           dugbar är densamma som det gamla skälets: **oangripbar utifrån repot och pinnad med test**.
           Slutsatsen står alltså kvar oförändrad — posten håller på skäl 2 ensamt om skäl 1 faller —
           men **skriv aldrig om posten som om skäl 1 vore garanterat av testet**, och verifiera
           `get-email-identity` **vid** flippen: den mätning som gjordes 2026-08-12 var bevis för den
           dagen och inte en inlösen av förutsättningen.

        5. **Brevlådan `kontakt@jobbliggaren.se` finns, tar emot, och LÄSES.** Sedan 2026-08-12
           (#1327) är adressen Art. 13(1)(b)-kontaktväg, Art. 15–22-väg, Art. 13(1)(f)-vägen till en
           SCC-kopia, **och** rutten i tre säkerhetsnotiser. `EmailChangedNotification` har ingen
           annan väg alls — adressen på kontot är just ompekad, så en återställningslänk hade
           levererat återställningen till angriparen, och därför bär mejlet med flit noll sajtlänkar.
           Verifiera med ett **skarpt utskick från en utomstående adress**, och verifiera att det
           **inte** är en tyst catch-all som kastar. `Reply-To` på varje utskick är samma adress
           (`ScalewayEmailSender`, via `additional_headers`, pinnat) — så ett svar på en notis
           landar där, inte på `no-reply@`.
           ⚠ **MX-LÄGET ÄR MÄTT FALSKT 2026-08-15 och förutsättningen är därmed längre från
           uppfylld än den var.** Apex-MX är `blackhole.tem.scaleway.com` (mätt mot 8.8.8.8), satt
           av leverantörens domänverifiering, så `kontakt@jobbliggaren.se` **tar emot ingenting**.
           Klas har skjutit upp reparationen i väntan på STRATO:s e-postpaket; `security-auditor`
           graderar det till **Blocker vid första riktiga användare eller vid flippen, vilket som
           kommer först**. Instrumentet är `vps-deploy-stack.md` rad 36 — återställ inte den gamla
           förväntan som en "reparation", recorda vad som resolverar.
           ⚠ **En av adressens roller upphörde 2026-08-15:** vägen till en kopia av
           standardavtalsklausulerna (Art. 13(1)(f)) förutsatte en överföring som inte längre
           uppstår. **De två andra rollerna står kvar** — Art. 13(1)(b)-kontakt och Art. 15–22-kanal
           — och det är de som gör blackhole-läget allvarligt.
           ⚠ **DEN HÄR FÖRUTSÄTTNINGEN GRINDAR INTE HELA RISKEN, och det är fällan.** §2.5:s
           räckvidd bestäms av predikatet i preambeln — **läs det där, det upprepas inte här**. Den
           **publicerade copyn** går live med **webb-deployen** — en annan händelse — och den bär
           Art. 13(1)(b)-kontakten oavsett providerläge. Se §2.6 (security-auditor 2026-08-12).
      **Kvarstående policy-residualer under denna punkt, inte under punkt 3.**
      **ORDNINGEN STÅR FÖRST, för att den styr posterna under sig:** upplös
      SCC/adekvans-disjunktionen **före** du skriver Art. 13(1)(f)-formuleringen —
      kopia-formuleringen hänger på Art. 46/47-grunden, så tvärtom påstår du en SCC-grund
      som kanske inte används. Alltså **(iii) → (ii)**, och listans första post — strykningen av
      e-poststyckets avtalsreservation — **när DPA:n är verifierad gällande för Scaleway S.A.S.**
      *(Denna routing-rad sa till 2026-08-15 "flytten in i `Mottagare`-listan … när avtalet
      **signeras**". Båda halvorna var fel: `list`-nyckeln finns inte, och (i):s egen kropp säger
      att "på plats" inte är detsamma som "signerat".)*
      (i) **e-postleverantörens stycke får stryka sin egen avtalsreservation när avtalet är på
      plats.** ⚠ **Mekanismen är omskriven 2026-08-15 (`security-auditor` Major 2), för att den
      skyddsmekanism residualen tidigare namngav inte finns.** Posten sa att *"prosaformen är vald
      just för att listrubriken påstår ett tecknat avtal"* — men `privacy.sections[6]` har **ingen
      `list`-nyckel** (mätt 2026-08-15: `heading` + sex `paragraphs`, noll `list`); nyckeln
      försvann i #1199, och e-poststyckena ligger i exakt samma strukturella position som
      netcup-stycket. **En residual vars angivna skydd inte existerar läses som uppfylld**, och den
      här lurade sin egen granskare två gånger. Vad som faktiskt bär ärligheten i dag är styckets
      **egen** mening — *"Innan vi börjar skicka säkerställer vi att personuppgiftsbiträdesavtalet
      med Scaleway SAS gäller"* — och villkoret för att stryka den är att avtalet faktiskt gäller
      för Scaleway S.A.S. *Sedan ADR 0131 är motparten Scaleway S.A.S., och
      "på plats" är inte detsamma som "signerat": DPA:n gäller automatiskt (avtalsdokument nr 1 i
      GTS Art. 3, mätt 2026-08-15) — precis som AWS-DPA:t gjorde. Villkoret är oförändrat genom
      båda bytena, men det gäller **reservationen och inte en lista**: den får bara strykas för en
      part vars avtal faktiskt gäller.*
      **(i) är den enda residual som kvarstår.**
      (ii) **Art. 13(1)(f)** — "means to obtain a copy" av skyddsåtgärderna — **LEVERERAD
      2026-08-09 (#1169)** och **UPPHÖRD 2026-08-15 (#183, ADR 0131)**: formuleringen hängde på att
      en överföring fanns att skydda, och den grunden finns inte mot en fransk avtalspart utan
      tredjelandsmoder. Stycket är struket ur copyn **med sin grund**, inte omskrivet. *Skulle
      Kap. V återaktiveras — t.ex. om Klas-brevet visar att support har åtkomst från tredjeland —
      återkommer både grunden och den här residualen.*
      (iii) SCC/adekvans-disjunktionen — **UPPLÖST** till SCC Art. 46(2)(c) och struken ur
      copyn (`security-auditor` 2026-08-08; se Kap. V-ledet ovan). **Sedan 2026-08-15 är även den
      upplösningen historik** — det finns ingen disjunktion kvar att upplösa när ingen överföring
      uppstår.
      **Ordningskravet ovan hölls:** (iii) avgjordes i granskningen 2026-08-08, och (ii)
      skrevs först därefter — kopia-formuleringen namngav den grund som faktiskt användes.
      **Ordningen är fortfarande styrande om Kap. V någonsin återaktiveras**, och därför står den
      kvar i stället för att strykas med posterna den ordnar.
- [ ] **2. TD-115** — legacy opt-OUT-default sanerad (#185 / PR #211 — **KLAR**).
- [ ] **3. TD-116** — consent-/disclosure-copy avslöjar e-postleverans för
      användaren (**PR #182 — KLAR**; TD-116:s consent-copy-halva, fast-follow till #181,
      ingen closing issue). **Citera INTE #185 här** — det är TD-115, punkt 2:s issue, och stod
      felaktigt här till 2026-07-26. ADR 0080 punkt 3 skopar posten till
      `messages/{sv,en}/settings.json backgroundMatch.*`, och PR #182 levererade exakt
      det: `intro`/`toggleDescription`/`cadenceHint` namnger e-post explicit.
      **Rättelse 2026-07-26:** #186 bockades först här. Fel punkt — #186:s
      integritetspolicy-post är **punkt 1:s** fjärde led (se ovan), och PR #182:s egen
      security-auditor rutade uttryckligen resten dit. Utfallet var rätt, skälet fel.
      **Divergens att inte tillskriva CTO:n:** dess bind sa ordagrant *"Item 3 keeps `[x]`"*.
      Rutan är återställd till `- [ ]` på dotnet-architects och code-reviewers grund i stället
      — filens konvention är **obockade** rutor (antalet står i blockquoten ovan, med sitt grep;
      det står med flit inte här), och boxen bockas av den som **utför** releasen,
      inte av den som levererar en förutsättning. Sakinnehållet (förutsättningen ÄR uppfylld)
      är CTO:ns; idiomet är granskarnas.
      Den consent-copyn ska **aldrig** bära en `planerat`-markör: samtyckestext måste
      beskriva den behandling samtycket auktoriserar, i auktorisationens tempus — en
      markör där skulle svaga Art. 7(2). Den ligger dessutom utanför §2.6:s grep-scope
      (som bara täcker `content-legal.json`), så en glömd markör-borttagning vid flippen
      skulle falla i den farliga riktningen.
- [ ] **4. TD-114** — stranded-Queued-reaper (#184 / PR #212 — **KLAR**).
      *Ledet om en **provider-`Idempotency-Key`** (#187 / PR #230) är **struket 2026-08-08**, inte
      obockat: SES v2 `SendEmail` har ingen idempotensparameter (mätt mot API-referensen samma
      dag — inget `ClientToken`, ingen dedup), så ledet är **osatisfierbart**, och §2.5:s egen
      grammatik ("grön = inget led bär KVAR") hade gjort ett KVAR här till en permanent
      låsning av hela prod-grinden. Vad ledet skyddade bär spinen redan: raden är Queued före
      utskicket och `StrandedMatchReaperJob` markerar en strandad rad Failed utan att skicka om.
      senior-cto-advisor-bind + ADR 0124, #1237.*
      ⚠ **STRYKNINGEN ÄR OMMÄTT 2026-08-15 MOT SCALEWAY OCH ÄRVDES INTE.** Preambelns doktrin
      gäller åt båda hållen: **en strykning får lika lite som ett grönt led ärvas från en motpart
      som inte längre är part**, eftersom skälet — "SES v2 har ingen idempotensparameter" — är ett
      påstående om en part vi inte har. Mätt i E1 (`b71c14de`): Scaleways `POST /emails` bär ingen
      idempotensparameter heller. **Strykningen står — nu på en mätning mot den part vi faktiskt
      har, i stället för en ärvd från en vi inte har.** *(Formuleringen "två oberoende mätningar"
      stod här till 2026-08-15 och motsade styckets egen doktrin: de två är mätningar av två olika
      parter, och SES-mätningen bidrar per den doktrinen med noll. `dotnet-architect` N5.)*
- [ ] **5. `BUILD.md` flippas i SAMMA ändring** — den här checklistan räknade tidigare bara upp
      `content-legal.json` och ROPA:n, och nämnde **aldrig** `BUILD.md` som flip-yta. Vid flippen
      blir följande falska utan att något kräver att de rörs: **§13.4**:s e-postpost
      (*"planerad, ännu inte"* … *"ingen e-post lämnar systemet"* — det första citatet
      radbryts i BUILD.md, så grep på den KORTA formen), **§3.1:s e-postrad**
      (*"prod-utskick grindat"*) och **§3.2:s Email-rad** (*"grindad"*).
      *(Raderna namngav Resend till 2026-08-08 och AWS SES till 2026-08-15; ADR 0124 respektive
      ADR 0131 bytte dem, och citaten ovan är **regenererade ur filen efter Scaleway-omskrivningen**,
      inte översatta — mätta 2026-08-15: `planerad, ännu inte` och `ingen e-post lämnar systemet`
      ger vardera exakt en träff, `prod-utskick grindat` ligger i §3.1:s rad och `grindad` i §3.2:s
      Email-rad. **§13.4:s e-postpost är omskriven i samma ändring som denna rad**, och de två
      korta citaten bevarades ordagrant just för att den här punktens grep ska överleva bytet.)*
      **`provider_message_id`-kommentaren i §7:s `email_log`-schema** är provider-neutral
      och blir INTE falsk — kontrollera den, ändra sannolikt inget.
      *(Radnummer står medvetet inte här: punkten bar TRE, och två av dem föll när
      #1173 sköt in rader i §3.2:s och §3.3:s statusbanners. Det tredje överlevde av
      POSITION, inte design — hunken direkt ovanför blev netto noll. Citaten är sökbara,
      radnumren var det inte.)*
      `BUILD.md` läses av varje CC-invokation (CLAUDE.md §9.1), så en oflippad rad där får varje
      efterföljande session att resonera från en falsk premiss om en **levande**
      tredjelandsöverföring. **Hör här på TRIGGERN, inte på sektionskaraktären** — §2.6 kallar
      sig själv också en aktiveringshändelse. Raderna blir falska när `Email:Provider` flippas
      (§2.5), inte vid första `v*`-taggen (§2.6).
      Tillagt 2026-07-26 på dotnet-architects mätning — och just denna PR **ökade** ytan.

Källa: ADR 0080 §"Prod-Resend-flip pre-condition checklist"; ROPA-behandlingen
**"Utgående transaktionell e-post (Scaleway Transactional Email, `fr-par`)"** — omdöpt igen
2026-08-15 (#183, ADR 0131) från *"… (Amazon SES, `eu-north-1`)"*, och dessförinnan omdöpt och omskopad
2026-08-09 (#1169) från *"Bakgrundsmatchnings-notiser via e-post (Resend)"*, som täckte
**endast** notis-vägen. Efter wideningen ovan gäller grinden all utgående e-post, och
Art. 30-posten täcker sedan omskrivningen **de sex mallar som fanns 2026-08-09** — **men de fyra
kontolivscykel-mallarnas rättsliga grunder är CC:s utkast och är inte prövade**, så
sign-off-ledet i punkt 1 är oförändrat KVAR.
⚠ **Och sedan #1171 är täckningen dessutom OFULLSTÄNDIG:** `PasswordReset` och
`PasswordChangedNotice` saknar Art. 30-post **helt** och har ingen Art. 6-grund någonstans i
registret. Registret är gitignorerat (ADR 0072) och kan därför inte rida den PR som införde
mallarna — det åtgärdas **lokalt före flippen**, och ingenting i CI kommer någonsin att fälla
att det inte gjorts. Mängden mallar med oprövade grunder växer alltså från fyra till sex, vilket
`security-auditor` 2026-08-10 uttryckligen vägrar signera punkt 1:s sista led mot.
*(Sifferbumpen sex→åtta gjordes först på den här meningen och var fel: meningens subjekt är
REGISTRETS TÄCKNING, inte mallantalet, så bumpen konverterade ett sant påstående till ett falskt
i en merge-blockerande grind — i den lugnande riktningen. Mätt 2026-08-10 av dotnet-architect och
security-auditor oberoende.)*

**FYRA**
av mallarna är ogrindade: `EmailChangeConfirmation` (`ChangeEmailCommandHandler:66`),
`EmailChangedNotification` (`ConfirmEmailChangeCommandHandler:45`, vars enda villkor är att
den gamla adressen finns), samt sedan #1171 `PasswordReset`
(`RequestPasswordResetCommandHandler`) och `PasswordChangedNotice`
(`ResetPasswordCommandHandler`) — **båda utan feature-villkor alls**, så en flipp gör dem levande
vid första `/glomt-losenord`. *(Läs "grindad" som checklistan gör: ett villkor UTÖVER
providerswitchen. En `CanDeliver`-kontroll räknas inte — `CanDeliver` ÄR switchen, och
`EmailChangeConfirmation` har en och listas ändå här.)* **Den senare går till den GAMLA adressen** — en annan
mottagarklass än den användaren just skrev, så en Art. 30-behandling som bara skopas till
den första lämnar en mottagare oregistrerad. (`EmailConfirmation` är däremot grindad på `RequireEmailConfirmation`,
`RegisterCommandHandler.cs:81`, som defaultar **false** — se blockquoten ovan. En
prod-lansering tvingar alltså inte i sig grinden.)

Det är samma lucka som den redan eskalerade frågan om att ROPA:n saknar behandling för
användarkontot/autentiseringen helt (Art. 30(1)) — **och den luckan är INTE stängd av
#1169**: den nya posten täcker e-postbehandlingen, inte kontot/autentiseringen som sådan.
**Luckan grindar inte via registret** — registret speglar (#1040) — men den blockerade
**security-auditor-sign-off-ledet** i punkt 1, eftersom det inte fanns någon Art. 30-behandling
att signera prod-e-post-konfigen mot för kontolivscykel-vägen. **Efter #1169 finns behandlingen;
det som återstår är att den prövas.** Att posten existerar är alltså en förutsättning för
sign-off, aldrig sign-off i sig. Registret är gitignorerat och kan inte rida en PR (ADR 0072), så
residualen står här, i den trackade filen, och åtgärdas lokalt före flippen.

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
> ⚠ **NY RAD 2026-08-12 (#1327): kontaktvägen måste fungera vid DEN HÄR händelsen, inte vid
> flippen.** Policyn namnger sedan dess `kontakt@jobbliggaren.se` som personuppgiftsansvarigs
> kontakt (Art. 13(1)(b)), som Art. 15–22-väg och som vägen till en SCC-kopia (Art. 13(1)(f)).
> **Den copyn går live med webb-deployen och är inte grindad av `Email:Provider`** — så en rad
> enbart i §2.5 hade inte fallit ut på den release som faktiskt publicerar kontaktuppgiften.
> Verifiera därför här att brevlådan finns och tar emot innan copyn deployas. Art. 12(2) kräver
> att den ansvarige *underlättar* utövandet av rättigheterna; en publicerad rättighetskanal som
> studsar gör motsatsen. `security-auditor` 2026-08-12, som graderade det Major uttryckligen
> **med** eskaleringsvillkoret "blir Blocker vid första prod-deploy av copyn ELLER vid flippen,
> vilket som kommer först".
>
> ⚠ **OCH BREVLÅDAN GÖR STRATO TILL BITRÄDE I EN ANDRA FUNKTION.** Registrets bestämning
> (`gdpr-processing-register.md`, lokal) säger att *DNS* hos STRATO inte är en biträdesrad,
> eftersom en DNS-operatör *"tar inte emot registrerades uppgifter för vår räkning"*. En brevlåda
> gör precis det. Grunden är sann om DNS och faller för post. Krävs innan brevlådan tas i bruk:
> ROPA-amendment (Art. 30(1)(d)) + retentionsbeslut för inkommande korrespondens (Art. 5(1)(e),
> saknas helt i dag) + `Mottagare`-stycke i policyn (Art. 13(1)(e)) + **AVV med STRATO (Art. 28),
> som är Klas-åtgärd och aldrig CC:s**. *Förläget var sämre på varje axel — en privat Gmail
> gjorde Google till de facto inbound-biträde, US-domicilierat, utan möjligt Art. 28-avtal på ett
> konsumentkonto; STRATO AG är tyskt och tecknar AVV. Bytet förbättrar läget, det skapar inte
> luckan.*
>
> **Läget idag är korrekt, inte trasigt.** Policyn beskriver ansökningshistorik/
> företagsöversikt, SCB-uppslag och e-postleverantören som planerade. **Värdraden gör
> det INTE längre:** #1199 tog bort dess markör 2026-08-09, eftersom lådan kör
> (`dev.jobbliggaren.se` sedan 2026-08-05) och en markör där hade förnekat en pågående
> drift — samma defekt som en förtidig flip, i spegelvänd form. Koden är
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
      **Regenererad 2026-08-15 (#183, providerbytet AWS SES → Scaleway): 9 + 9** (rad 37, 49,
      63, 73, 74, 75, 95, 96, 131 — identiska i sv och en, alla äkta statuspåståenden, ingen
      falsk träff med detta mönster). **Talet sjönk med ETT och raderna under flyttade upp ett
      steg**, av ett enda skäl: tredjelandsavsnittets e-poststycke (förra rad 82) är **struket med
      sin grund** — Scaleway S.A.S. är franskt, ingen överföring uppstår, och copyn ska då vara
      tyst om Kap. V precis som värdraden är (senior-cto-advisor bindande 2026-08-15). Nettot:
      82 försvann, och 96/97/132 blev 95/96/131. Mängden är **körd ur greppen ovan, aldrig
      framräknad ur den gamla** — se nästa stycke om varför det senare inte är en genväg.
      *(Föregående regenerering, 2026-08-09 (#1199, värdbytet Hetzner → Netcup): 10 + 10 på rad
      37, 49, 63, 73, 74, 75, 82, 96, 97, 132.)* **Både talet och radmängden ändrades även då**, av tre
      skilda skäl i samma ändring: Cloudflare-posten raderades, värdposten skrevs om **utan**
      markör, och värdposten flyttades sedan ur `sections.6.list` till `paragraphs[1]` varvid
      hela `list`-nyckeln försvann. Nettot: **två markörbärande rader blev noll**, värdstycket
      hamnade **ovanför** SCB- och AWS-styckena i stället för under dem, och raderna rörde sig
      i **båda** riktningarna — mottagaravsnittets tre markörrader gick ett steg NER (72, 73,
      74 → 73, 74, 75) medan allt från tredjelandsavsnittet och neråt gick två steg UPP
      (85, 99, 100, 135 → 82, 96, 97, 132). **Att räkna fram den mängden ur den gamla är
      alltså inte bara opålitligt utan omöjligt** — en enda ändring flyttade rader åt två håll.
      Talet stod på **12 + 12** vid 2026-07-26 (#186) och var oförändrat vid 2026-08-09
      (#1169, providerbytet Resend → AWS SES), där fyra rader skrevs om i sak men behöll sin
      markörmening. **Det är ett mätresultat, inte en förutsägelse:** en ändring som tar bort
      ett arrayelement eller delar ett stycke flyttar varje rad under sig, så greppet ska
      köras om även när en ändring "bara" byter ord.
      **Grepa INTE bara på `"planerat och ännu inte i drift"`** — det ger 7 (mätt
      2026-08-15) och missar de TVÅ retentionsposterna, som bär `(planerat)` utan
      avslutningsmeningen. Den första (organisationsnumret i en annons, #880) nämner
      ansökningshistoriken som ett ÄNDAMÅL med att arbetsgivarens identitet sparas;
      den andra är ansökningshistorikens egen post. **Radnumren står medvetet inte här** —
      de bor i punkt 1:s mängd ovan och flyttar varje gång ett stycke läggs till eller stryks;
      den här PR:en flyttade dem två gånger på en dag. **Regenerera den här listan ur
      greppen ovan efter varje redigering av `privacy`-sektionerna** — inte bara
      retentionsavsnittet: #880 delade en
      punkt i två och flyttade fyra av åtta rader, och #186 rörde tre andra avsnitt
      (samtycke, mottagare, tredje land) och flyttade **sex av åtta** medan tre nya
      tillkom, så en handlappad siffra blir
      falsk vid nästa redigering. Lagringstiden är en egen obligatorisk
      uppgift (Art. 13(2)(a)) och ADR 0090 D3 räknar uttryckligen upp
      retentionsraden som del av samma leverans. Flippar du 6 och lämnar 1 säger
      kategorilistan drift medan retentionsavsnittet säger planerat.
- [ ] **2. Avgör vad releasen faktiskt aktiverar** — två olika klasser, blanda dem
      inte:
      - **Kod-aktiverad:** ansökningshistorik/företagsöversikt — kategorilistans
        ansökningshistorik-punkt, BÅDA retentionsposterna och stycket i "Inga automatiserade
        beslut". *(Identifieras med innehåll, inte radnummer: punkt 1:s mängd är hemmet, och
        raderna flyttar vid varje styckeändring.)*
        Handlers + endpoints + FE är skeppade utan feature-flagga → aktiveras av
        att tjänsten alls går i drift.
      - **Konfigurations-grindad:** SCB (ändamålsavsnittets företagsuppslag + mottagarstycket)
        **och e-postleverantören Scaleway** (samtyckesavsnittet + mottagaravsnittets TVÅ
        e-poststycken; #186 + #1169 + #183). *(Innehållsbenämningar, inte radnummer — punkt 1 är
        mängdens hem, och den här bulleten bar sin egen kopia av numren tills 2026-08-15.)*
        **Aktiveras INTE av en
        `v*`-tagg.** Tre skilda mekanismer, alla mörka i prod: per-sökningens
        `ICompanyRegistry` (ADR 0088) får `NullCompanyRegistry` — valet styrs av
        `CompanyRegistry:Provider`, den riktiga adaptern siktar på SCB:s nya
        API (~sept 2026) och dess **första verkliga överföring är hårt grindad på
        DPIA #456 + SCB terms review** (ADR 0088 D3); bulk-populeringen
        `IScbCompanyRegisterSource` (ADR 0091) är Worker-only och grindad på
        `ScbRegister:Enabled=true` + klientcert, och skickar aldrig ett
        användarskrivet org.nr. E-posten styrs av `Email:Provider`, som defaultar till
        `Console` och i non-dev löser till `NullEmailSender` — flippen är grindad av
        **§2.5 punkt 1** (uppräkningen bor DÄR, inte här — och därför står antalet inte heller här), inte av en
        tagg, och gäller **all** utgående e-post (§2.5:s widening). **Flippa SCB-styckena
        respektive e-poststyckena först när respektive grind är
        passerad** — inte när koden deployas.
        *Raderna 63/74/75/82 namngav Resend, Inc. (USA) till 2026-08-09; #1169 skrev om dem till
        Amazon Web Services EMEA SARL (Luxemburg) med behandling i `eu-north-1`. **Det var en
        korrigering av en falsk motpartsuppgift, inte en flip** — markörmeningen stod kvar i alla
        fyra styckena i båda språken, och armen var fortfarande mörk.*
        ⚠ **2026-08-15 (#183, ADR 0131) skrevs de om igen, till Scaleway S.A.S. (Frankrike,
        `fr-par`) — och den gången ändrades MÄNGDEN, inte bara namnet:** rad 82 (tredjelands-
        stycket) är **struken med sin grund**, så e-posten bär nu **tre** markörbärande stycken per
        språk, inte fyra. Också detta var en motpartskorrigering och **ingen flip** — markörmeningen
        står kvar i alla tre styckena i båda språken, och armen är fortfarande mörk.
      Kvarstående planerat-meningar för behandlingar som fortfarande inte är i
      drift ska stå kvar. Släpper releasen ingen av dem är rätt utfall att **inte
      ändra något**.
- [ ] **3. Art. 28 innan personuppgifter når lådan** (speglar §2.5 punkt 1).
      **Triggern är INTE längre en flip, och den gamla "Deploy-aktiverad"-klassen i punkt 2
      är struken i samma ändring.** #1199 tog bort värdradens markör 2026-08-09, så det finns
      ingen värd-flip kvar att grinda — men skyldigheten består och fick en ny utlösare
      (`security-auditor` 2026-08-09). Grinden biter vid **det tidigare av**:
      - **(i) varje ingest av JobTech-korpuset på lådan** ([#1240](https://github.com/klasolsson81/jobbliggaren/issues/1240) — 51 347 rekryterar-kontaktposter
        över 27 160 annonser, Art. 14-uppgifter om icke-användare), och
      - **(ii) första konfigurationen utanför `Development` som sätter `Auth:RegistrationsOpen=true`**.

      **(i) är den tidigare, och det är den ingen mental modell håller:** rekryterar-PII når
      lådan **före** den första användaren, i klartext i `job_ads.description`, fritextsökbart
      och utan purge-väg (`gdpr-processing-register.md`, JobTech-posten). Modellen "vi hinner
      teckna avtalet innan vi öppnar för användare" är fel med ett helt steg.

      Kravmängden:
      - **slutet personuppgiftsbiträdesavtal med `netcup GmbH`**, och **mekanismen är
        namngiven med flit**: netcups AVV gäller **inte** automatiskt (mätt förstahands
        2026-08-09 — generalisera aldrig e-postleverantörernas mätningar hit. **Två generationer i
        rad har haft automatiskt gällande DPA** (AWS-erans och Scaleways, den senare mätt
        2026-08-15 mot GTS Art. 3), vilket gör netcup till **undantaget bland biträdena och inte
        regeln** — och det är precis därför generaliseringen är frestande. Hos AWS uppgavs DPA:t gälla av sig
        självt). Den sluts av kunden i **Customer Control Panel → Stammdaten / Master Data →
        Auftragsverarbeitung / Order Processing → Generate DPA**; elektronisk signatur räcker
        och den kostar inget. "Signera ett DPA" antyder ett motpartsflöde netcup inte har.
        ⚠ **Generatorn ber om *"circle of affected persons"* — det är en materiell deklaration,
        inte ett formulärfält.** Den måste namnge **rekryterar-kontaktpersoner** (Art. 14,
        icke-användare), inte bara kontoinnehavare, annars blir avtalets räckvidd smalare än
        behandlingen. **Läs AVV-bilagans underbiträdeslista när den genereras** — netcup
        publicerar ingen (mätt: DPA-sidan, AVV-sidan, Impressum och DC-sidan bär noll), så
        bilagan är den enda mätningen av kedjan som finns. Namnger den ett icke-EU-underbiträde
        ska **tredjelandsavsnittets absoluta påstående** och värdraden omprövas **före**
        korpusladdningen.
        **Mottagaravsnittets ingress** påstår redan i presens *"Med dem har vi personuppgiftsbiträdesavtal"*; den
        meningen bärs i dag av att ingen listad part behandlar något, och den blir falsk
        vid (i) — inte vid (ii), och inte av mergen av #1199.
      - **inget Kap. V-led — det är raderat, inte ompekat.** Det gamla ledet krävde en
        dokumenterad grund för **Cloudflare** (US-domicilierat) och dog med parten
        (Klas-beslut K3). `security-auditor` 2026-08-09: netcup GmbH är tysk, behandlingen
        sker i Nürnberg, och Kap. V engageras inte av värdbenet. Tredjelandsavsnittets
        **enda kvarvarande stycke** är fortfarande ett
        **absolut** påstående (*"I dagsläget sker inga överföringar av dina personuppgifter
        till länder utanför EU/EES"*), men dess antecedent — *"Anlitar vi en leverantör
        **utanför EU/EES**"* — täcker inte netcup alls, så värdbytet rör den inte.
        ⚠ **OMSKRIVET 2026-08-15 (#183, ADR 0131) — meningen nedan påstod motsatsen till vad
        den här PR:en fastställer.** Den sa att **e-postflippen** är den händelse som gör det
        absoluta påståendet falskt, och att #186 därför la ett andra stycke *bredvid* det så att
        båda var sanna samtidigt. **Båda halvorna är överspelade:** det andra stycket är struket
        i den här ändringen, och med en fransk avtalspart utan tredjelandsmoder utlöser flippen
        **ingen** överföring — vilket är hela poängen med led (b). Det absoluta påståendet
        överlever alltså flippen i stället för att fällas av den, **under förutsättning att
        `security-auditor`s ratificering faller ut så**; tills dess är detta utkastets läsning,
        inte en dom. *(Historiken bevarad: #186 la ett e-poststycke **bredvid** det absoluta
        i stället för att ersätta det, och båda var sanna samtidigt så länge inget skickades.
        Styckena bytte radnummer två gånger — 2026-08-09 av att Cloudflare-posten raderades OCH
        värdraden flyttades ur `list` till `paragraphs`, och 2026-08-15 av strykningen ovan;
        radnumren skrivs därför inte längre ut här. Adekvans-disjunktionen ströks bara på det
        stycket — `security-auditor` 2026-08-08: EN
        grund, SCC Art. 46(2)(c).)*
      - **ROPA-posterna uppdaterade** + **security-auditor-sign-off**.
      - **`ACME_EMAIL` på lådan bekräftad som personuppgiftsansvariges egen adress.** Med
        Cloudflare borta går Caddy direkt mot Let's Encrypt, så **ISRG (USA)** är den enda nya
        part kanten fick. Är adressen Klas egen är den den ansvariges egna uppgifter och ingen
        biträdesrad är skyldig; blir den någon gång delad eller användarvänd är ISRG mottagare av
        användardata och Kap. V öppnas igen. Kravet står också i `deploy/.env.example` där värdet
        skrivs in — värdet självt bor bara på lådan och går inte att mäta ur repot.
      - **`recruiterNotice` prövas om i samma grind.** Den sidan blir Art. 14-notisen för
        exakt den population (i) skapar, och den namnger **noll** mottagare — den når
        mottagardisclosuren enbart via `relatedPrivacy`-länken till integritetspolicyn. Får
        policyns mottagarsektion någon gång **population-skopade** formuleringar bryts
        länkvägen för just den populationen (Art. 14(1)(e)). Det är också det direkta skälet
        att värdraden är skriven utan lägesmening och utan datamängds-klausul: en mening av
        formen "i dag finns inga uppgifter om dig hos leverantören" hade varit falsk om en
        rekryterare i samma sekund korpuset laddats.
      DPA-signering = **Klas**, aldrig CC.
- [ ] **4. Paritet sv + en** — båda språken i samma ändring. Formuleringen bärs av
      elementen i `privacy.sections` som bär formuleringen — tillsammans **exakt den radmängd
      punkt 1 producerar** (antalet står där, med sitt grep; det står med flit inte här):
      kategorilistan, ändamåls-/SCB-avsnittet, samtyckesavsnittet
      "Bevakningsnotiser i bakgrunden" (#186), mottagaravsnittet (SCB + **två**
      e-poststycken), retentionslistan och "Inga automatiserade beslut".
      Missa inte retentionsposten — och notera att **både** retentionslistan **och**
      e-postprosan i mottagaravsnittet bär **två** rader var, inte en.
      ⚠ **Tredjelandsavsnittet stod i den här mängden till 2026-08-15** och gör det inte
      längre: dess e-poststycke är struket med sin grund (#183, ADR 0131). Att det var en egen
      section, skild från mottagaravsnittet, är fortfarande sant om de två som finns kvar.
      **Radnumren är borttagna ur den här uppräkningen med flit** — de bodde här och i punkt 1,
      och en av de två gick stale varje gång ett stycke rördes. **Punkt 1 är hemmet.**
      **Värdstycket står INTE i den här mängden**:
      värdraden bär sedan #1199 ingen markör och äger därför ingen flip. Den vaktas i stället
      av `content-legal-parity.test.ts`, som pinnar att `netcup GmbH` är namngiven i båda
      språken **och** att raden inte bär markörmeningen.
- [ ] **5. Bumpa `privacy.updated`** ("Senast uppdaterad: YYYY-MM-DD"), båda
      språken. Skopa till **`privacy.updated`** — filen har fem `updated`-nycklar
      (privacy/terms/cookies/accessibility/recruiterNotice).
- [ ] **5.5 TVÅ VILLKOR SOM UPPHÖR VID FÖRSTA PRODUKTIONSANVÄNDAREN — de hör HÄR, inte i
      §2.5.** Båda accepteras i dag enbart därför att det finns **noll registrerade
      produktionsanvändare**. **Triggern är den första konfiguration utanför `Development` som
      sätter `Auth:RegistrationsOpen=true` — oavsett tagg, och `Test` räknas som utanför.**
      (Den tekniska spärren nedan undantar både `Development` och `Test`; den här grinden gör
      det inte. En nåbar host som kör med `ASPNETCORE_ENVIRONMENT=Test` är en produktionsstart
      i Art. 30-mening.)
      (Omskriven 2026-08-03, ADR 0083 Amendment, ordalydelse bekräftad av security-auditor: före
      den låg triggern på "den första `v*`-taggen som öppnar registrering", och den formuleringen
      är nu falsk — **ingen** tagg öppnar registrering längre. Läst bokstavligt hade den gamla
      triggern aldrig fyrat, och de två villkoren nedan hade fallit ur tyst.)
      Den passerar **inte** §2.5: `Email:Provider` osatt (dokumenterad default) ger
      `NullEmailSender`, och e-postflippen kan ligga månader senare. Villkoren upphör
      alltså **strikt före** §2.5 någonsin läses (security-auditor 2026-07-26).
      **Grinden bärs av #734, inte av den här sidan.** Efter ADR 0083 Amendment kan flippen inte
      ske utan `RequireEmailConfirmation=true` **och** en riktig `Email:Provider`, och båda
      förutsättningarna ägs av **#734**. Villkoren (a) och (b) nedan ska därför stå som
      **blockerande acceptanskriterier på #734**. Kan flippen inte ske utan #734, och kan #734 inte
      stängas utan (a) och (b), då har triggern en läsare. Den här sektionen är protokollet; #734 är
      grinden. *(Raden namngav till 2026-08-09 även **#196** som medägare "där env-konfigurationen
      faktiskt sätts". #196 är **STÄNGD**, och en stängd pekare i en merge-blockerande grind läses
      som utförd. Var env-konfigurationen sätts är mätt i stället för gissat: `deploy/docker-compose.yml`
      och `deploy/.env.example`, båda i repot.)* ⚠ **Mätt 2026-08-09: #734 bär inget av villkoren
      nedan**, och dess kropp namnger fortfarande Resend som levande leverantör trots ADR 0124.
      Transkriberingen är kommenterad på #734 samma dag; om acceptanslistan ska struktureras om i
      dess kropp är Klas beslut, liksom om flippen får ske med något villkor ouppfyllt.
      Notera också att **(a) upphör genom reparation på samma händelse**: en riktig
      `Email:Provider` är en förutsättning för flippen, och det är precis den som gör
      `ChangeEmailCommandHandler`:s `NullEmailSender`-svälj till ett minne. (a) och den tekniska
      spärren konvergerar alltså på ett enda arbetsmoment — något den gamla tagg-triggern aldrig
      åstadkom. **(b) gör det inte:** Art. 30-posten för konto/auth kristalliseras vid första
      verkliga registrerade användaren och bärs av ingen annan mekanism.
      *Not:* `AuthOptionsValidator` vägrar numera boota **Api:n** på två kombinationer utanför
      Development/Test — `RegistrationsOpen` utan `RequireEmailConfirmation`, och (sedan
      2026-08-09) `RegistrationsOpen` MED `RequireEmailConfirmation` när den registrerade
      avsändaren inte kan leverera. Allt som följer i den här noten gäller **båda** reglerna:
      garantin bärs av **den ivriga
      `IOptions<AuthOptions>`-läsningen** vid boot-announcement i `Program.cs`: den ligger
      bevisligen före `app.Run()` och därmed före att Kestrel binder socketen. `ValidateOnStart`
      är en redundant backstop — dess ordning relativt `GenericWebHostService` är **inte** pinnad
      av något i repot, så påstå den inte. Slutsatsen (ingen trafik i den osäkra kombinationen)
      håller på den första halvan ensam. **Worker:n valideras medvetet inte** och
      fortsätter köra; en operatör som ser jobb-loggar rulla vidare ska inte läsa det som att
      spärren inte slog till. Det är en teknisk spärr mot en osäker **kombination** — den
      ersätter inte den här grinden, som är juridisk, och den säger ingenting om (a) eller (b).
      - **(a) `settings.json` påstår ett utskick som inte sker.** Fyra publicerade strängar
        (`:218`, `:220`, `:224`, `:229`) säger att en bekräftelselänk skickas eller har skickats,
        medan `NullEmailSender` är den levande defaulten.
        **Kriteriet, utskrivet, eftersom uppräkningen ensam får nästa läsare att räkna fel åt andra
        hållet:** en yta hör hit om den **påstår en leverans som sakförhållande** — tre utlovar den i
        presens, en påstår den fullbordad. Ett grepp på verbstammen — mönstret
        `skickar|skickat|skicka\b|sänder|sent|send|sending`, skiftlägesokänsligt, över alla
        strängvärden under `account.changeEmail` i `messages/{sv,en}/settings.json` — ger **sex**
        träffar per språk, men de två extra är `submit` ("Skicka bekräftelselänk",
        imperativ som namnger den handling användaren begär) och `pending` ("Skickar…", som beskriver
        en pågående request). **Ingen av de två falsifieras av ett svalt utskick**, och båda förblir
        sanna under förhandsavslaget. Verbstammen är alltså en proxy för kriteriet och överskattar
        det: skillnaden ligger i talakten, inte i ordet. *(Mätt 2026-08-09 under #1087; issuens egen
        tabell placerade dessutom `success` på `:226`, vilket är `submit` — den här raden har haft
        rätt uppsättning sedan tidigare.)* **Villkoret, triggern och upphörandet
        står oförändrade; bara mekanismmeningen är omskriven, för att den blev falsk 2026-08-09
        (#1087, PR i samma ändring som denna rad).**
        Vad #1087 ändrade: `ChangeEmailCommandHandler` skickar inte längre ogrindat — porten bär
        `IEmailSender.CanDeliver`, handlern vägrar i förväg med **503**
        (`Auth.EmailDeliveryUnavailable`), ingen token mintas, och nedkylningsfönstret konsumeras
        inte. `:229` (`success`) är därmed **onåbar** när leverans är omöjlig. **Ingen
        `User.EmailChangeRequested`-rad skrivs — men läs varför rätt:** den gamla raden var **sann**
        (en begäran gjordes); det falska var 202:an och flödet den antydde. Raden försvinner för att
        flödet aldrig startar, inte för att den var ett falskt protokoll (security-auditor
        2026-08-09). Där **själva begäran** är den säkerhetsrelevanta händelsen binder i stället
        #842:s Art. 12(3)-opt-in, och frånvaro vore fel.
        **Användarytan är STÄNGD sedan 2026-08-10 (B-ii).** Tillståndet som stängdes: en 503 föll
        igenom till det generiska `changeEmailFailed`, så användaren fick ingen förklaring, inte
        veta att adressen var oförändrad, och submit-knappen levde kvar för ett omförsök som inte
        kan lyckas. `changeEmailAction` bär nu en 503-arm som returnerar ett `refused`-resultat, och
        kortet ersätter sig självt med en `role="status"`-panel utan trigger — affordansen tas bort,
        inte bara texten. Armen diskriminerar på ProblemDetails-**titeln**, aldrig på statusen
        ensam (grinden är konjunktiv: status 503 OCH exakt titel):
        rutten har minst två andra 503-producenter (`SessionStoreUnavailableException` via Redis,
        vars body saknar `title`-nyckeln, samt en omvänd proxy, vars body inte är JSON alls) — en
        statusbaserad arm skriver
        "e-post är inte aktiverat" mitt under ett driftavbrott och **maskerar incidenten**. **Båda
        kontrafaktumen är pinnade** (`me.change-email.test.ts`: Redis-bodyn `Program.cs` faktiskt
        skriver, främmande titel, icke-JSON-proxy, samt en 409 som bär vår egen titel och inte får
        fyra). Ingen användare kunde nå tillståndet före flippen, vilket är varför det var ett
        grindvillkor och inte en defekt i drift.
        **Löftestexten renderas inte i det vägrade läget** — strängarna `:218`/`:220`/`:224` är
        **orörda** i `settings.json`, så villkor (a) är oförändrat; det är villkorad rendering i ett
        läge, inte en uppmjukning av copy (Klas-beslut 2026-08-10). Den nya nyckeln ligger under
        `account.errors`, utanför verbstams-greppets skop, så **sexsiffran nedan är oförändrad**.
        **Vad #1087 INTE ändrade, och därför upphör villkoret inte:** `:218`, `:220` och `:224`
        publiceras fortfarande före handlingen och utlovar ett utskick som defaultkonfigurationen
        inte kan göra. Villkoret upphör vid **en riktig `Email:Provider`** — samma upphörande som
        stycket ovan redan namnger — aldrig vid att #1087 mergats.
        **Registerkedjan hör till samma trigger, och den tekniska halvan är STÄNGD sedan
        2026-08-09** ([PR #1282](https://github.com/klasolsson81/jobbliggaren/pull/1282), D1).
        Tillståndet som skulle stängas: utanför Development/Test bootade
        `RegistrationsOpen=true` + `RequireEmailConfirmation=true` med osatt `Email:Provider` rent —
        aktiveringslänken gick till `NullEmailSender`, `UserAccountService` spärrade inloggning på
        `EmailConfirmed`, och återsändningen var lika tyst: **kontot skapades och blev permanent
        onåbart.** Det är strikt värre än (a):s ursprungliga fall — ett misslyckat adressbyte
        lämnar användaren där hon var.
        Åtgärden landade som föreskriven: `AuthOptionsValidator` bär numera **två** vägransregler,
        och den andra frågar den registrerade avsändarens `IEmailSender.CanDeliver` i stället för
        att läsa om `Email:Provider`. Asymmetrin är löst som punkten krävde — regeln bor i
        validatorn, som binds i Api:ns identitetsmodul, och **inte** i `AddEmailSender`, den enda
        sömmen båda hostarna delar; Worker:n binder samma `Auth`-sektion med ett rent `Configure`
        och registrerar ingen validator. **Båda halvorna är pinnade vid anropsplatsen**, så
        paritets-editen åt endera hållet landar rött.
        ⚠ **Detta stänger INTE punkt 5.5, och inte heller B-ii gör det.** Villkor (a) upphör
        alltjämt först vid en riktig `Email:Provider` (`:218`/`:220`/`:224` publicerar fortfarande
        ett utlovat utskick som defaultkonfigurationen inte kan göra — B-ii döljer dem i **ett**
        vägrat läge, den ändrar ingen sträng och når inte den publicerade copyn i normalläget),
        (b) är orörd, och **`Test`-divergensen står kvar**: den tekniska spärren undantar
        Development/Test via allowlisten, medan den juridiska grinden här räknar en nåbar
        `Test`-host som produktionsstart. **Klientarmen är det enda villkor på triggern som B-ii
        stänger** — den står kvar i listan som levererad, inte som utestående.
        **Ingen release som öppnar registrering får ske innan de kvarvarande villkoren är gröna.**
        Copyn får INTE mjukas upp först — det falska påståendet är enda användarsynliga tecknet
        att flödet är trasigt. Art. 5(1)(a) + 12(1).
        Ägare av residualen: **#734** (bär flippens förutsättningar) och **#183** (e-post-prod-flippens
        GDPR-grind), båda öppna och `mvp`. *(Raden namngav tidigare **#1087**, som stängs med
        den här ändringen, och **#196**, som är **STÄNGD** sedan tidigare — en stängd pekare i en
        merge-blockerande grind läses som utförd. Var env-konfigurationen faktiskt sätts efter att
        #196 stängdes stod först här som en öppen fråga; den är nu **mätt** och svaret bor i
        punktens eget stycke ovan, inte på en andra plats.)*
      - **(b) ROPA:n saknar behandling för användarkontot/autentiseringen HELT** (Art. 30(1)).
        Mätt: nio behandlingar, ingen för konto/auth. Registret är gitignorerat (ADR 0072) och
        speglar (#1040), så skyldigheten bor här. Den kristalliseras vid **produktionsstart**,
        inte vid e-postflippen.
      Bocka aldrig 5.5 på att §2.5 är ogrindad — det är två olika trigger.
- [ ] **6. Tidsordning — två olika fall, blanda dem inte:**
      - **(a) Första prod-taggen:** flippen deployas **samtidigt** med
        aktiveringen. Inga registrerade finns före, så ingen förhandsinformation
        är möjlig eller krävd.
      - **(b) Senare release med befintliga registrerade:** informationen
        publiceras **FÖRE** aktiveringen. Ansökningshistoriken är enligt ADR 0090
        D3 *"a new purpose section under 6(1)(b)"*, dvs. vidarebehandling för ett
        nytt ändamål av redan insamlade uppgifter → **Art. 13(3) kräver
        information "prior to that further processing"**, och policyns eget löfte
        (policyns sista stycke, under rubriken "Ändringar i denna policy" — kvalifikatorn är
        bärande, rubriken förekommer två gånger i katalogen) säger *"Vid mer betydande ändringar informerar vi dig på lämpligt
        sätt"*. Formulera som förhandsbesked (*"från och med &lt;datum&gt; behandlar vi
        även …"*), aldrig som påstående om pågående drift.
      Aldrig **efter** aktiveringen i något av fallen.
- [ ] **7. Konsistenskontroll efter flippen** (per behandling, båda språken). För
      varje behandling ska **alla** dess omnämnanden ha samma status.
      Ansökningshistoriken nämns på fyra ställen (kategorilistan, retentionslistan,
      "Inga automatiserade beslut" och Art. 30-registret); SCB på tre
      (ändamålslistan, mottagarstycket — tredjelandsavsnittet nämner INTE SCB; uppräkningen
      sa "tre" ända till 2026-07-26); **e-postleverantören på tre** (samtyckesavsnittet och TVÅ
      stycken i mottagaravsnittet — *"Överföring till tredje land" räknades med till 2026-08-15
      och gör det inte längre; talet speglas av `content-legal-parity.test.ts`, vars golv står på
      samma tre*) —
      och e-postflippen styrs av **§2.5**, inte av taggen, så den kan mycket väl
      inte höra till releasen alls medan de andra gör det. **En
      mottagare får aldrig stå som planerad medan behandlingen som skickar till
      den står som i drift, och omvänt.** Kör inventeringsgreppet igen efter
      flippen: antalet träffar ska minska med **exakt** antalet poster releasen
      aktiverar, aldrig med fler.
      **Stycket i "Inga automatiserade beslut" kräver särskild kontroll — det är den enda rad
      greppet inte självskyddar.** Dess inledning (`planerar` / `plans`) matchas INTE av
      inventeringsmönstret (verifierat: 0 träffar), så raden syns bara via sin
      avslutande mening. Tas bara den bort faller raden ur greppet helt, räkne-
      testet ovan säger "minskade med exakt 1 — korrekt", och policyn påstår
      fortfarande *"Jobbliggaren planerar en översikt av din egen
      ansökningshistorik"* — mitt i avsnittet **"Inga automatiserade beslut"**,
      dvs. i Art. 22-negationen. Läs stycket i sin helhet: hela det skrivs om
      till presens, aldrig trunkeras. *(Identifierat med sitt avsnitt och inte med ett radnummer:
      det flyttade 2026-08-15 av en strykning två avsnitt ovanför.)* (Varje **annan** rad ur punkt 1:s mängd bär `(planerat)`/
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

> Compose-modell (ADR 0050 `Amendment 2026-08-04`/0122): hela stacken (API + Worker + Postgres +
> Redis + Caddy + Next.js) kör i Docker Compose på **netcup-lådan (RS 1000 G12)** bakom Caddy. Konkreta
> service-namn/kommandon finalize:ras med **#196** (Compose-stack + proxy
> + härdning) — stegen nedan är på modell-altitud tills dess.

- [ ] **Compose-tjänster startar** (api + worker) — `docker compose ps` på boxen
      visar dem `healthy` (konkret service-namn/compose-fil: #196).
- [ ] **`/api/ready` → 200** mot målmiljöns domän (strict readiness: DB +
      Redis dependency-checks, TD-29).
- [ ] **`/api/health` → 200** (liveness).
- [ ] **Hangfire-jobben** kör enligt schema om release rör Worker
      (`*/10`-cron etc.) — verifiera på `/admin/jobb` (read-side, ADR 0082) och i
      den strukturerade loggen. Den inbyggda Hangfire-dashboarden exponeras inte.
- [ ] **Audit-wire** — om release rör audit-genererande flöden: bevisa
      INSERT i `audit_log` via den strukturerade logg-sinken (MEL → Seq; full
      prod-sink = #1175) + direkt `audit_log`-query (ADR 0035).
- [ ] **Ops-signaler granskade** — health-checks + extern uptime-monitor
      (UptimeRobot/BetterStack, ADR 0050 — ersätter ALB/CloudWatch-health);
      jobtech-sync-/auditor-write-/log-pipeline-health läses via logg-sinken.
      Konkret alerting-konfig: #196 (box) + #1175 (sink).
- [ ] **Frontend** (om i scope) — Lighthouse observe-signal mot
      ADR 0045-budgetar; manuell rök-test av kritiska flöden.
- [ ] **Rollback känd** — pinna föregående image-tagg och kör reconcile-uniten
      (se §5); över en migrationsgräns vägrar `migrate` i stället (#1236,
      `vps-deploy-stack.md` §3a).

---

## 5. Rollback

Vid fel efter deploy (Netcup-lådan, ADR 0050/0122): rollback är en image-tagg —
**för kod, aldrig för schema** — och den går genom reconcile-uniten, **aldrig via
handskriven `docker compose up -d`**. En hand-apply tar ingen lock och kör ingen
attestationsverifiering; wrappern vaktar bara vägen genom uniten
(`vps-deploy-stack.md` §3b, "Manual applies go through the unit").

```bash
# På Netcup-lådan: pinna föregående publicerade tagg och kör uniten.
sudoedit /opt/jobbliggaren/deploy/.env        # sätt IMAGE_TAG=sha-<föregående>
sudo systemctl start jobbliggaren-reconcile.service
journalctl -u jobbliggaren-reconcile -n 40 --no-pager   # döm journalen, inte exit-koden
```

- **Schema-grinden (#1236):** över en migrationsgräns är en bakåtpinne ingen
  rollback — `migrate` vägrar (exit 3/4) och api/worker hålls nere, fail-closed.
  Vägrans anatomi, de tre utvägarna och override-nyckelns semantik:
  `vps-deploy-stack.md` §3a.
- **Attestationsfönstret:** en pinnad tagg måste vara publicerad MED attestation,
  annars vägrar wrappern hela applyn — fönstret ägs av `vps-deploy-stack.md` §3b.

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
  ADR 0045 (perf observe-only-signaler); #196 (Compose-stack) / #1175 (prod-sink)
  (logg-sink/observability)
- CLAUDE.md §6.3 (granskningsspärrar), §8 (DoD), §9.2 (deploy kräver Klas-GO)
- BUILD.md §15 (deployment/rollback)
- `docs/runbooks/v0.2-prod-launch-checklist.md` — engångs-checklist för
  *första* prod-deployen (komplement, inte ersättning för denna)
