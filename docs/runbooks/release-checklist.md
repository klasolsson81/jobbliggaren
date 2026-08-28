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
> ⚠ **GRINDEN ÄR RELEVANT SEDAN 2026-08-16, OCH DEN HAR ALDRIG PASSERATS.** `Email:Provider` sattes
> till `Scaleway` på lådan medan led (a), (b), (c) och (e) alla bar KVAR, och utskicken är mätta
> leverantörssidigt samma dag. *(Meningen som stod här — "Tills dess kör `NullEmailSender` — ingen
> e-post skickas, och denna grind är inte relevant" — var den här grindens egen
> **tillämplighetsmening**, och den sa från 2026-08-16 att grinden inte gäller. Alla tre
> påståendena var falska: providern är satt, e-post skickas, och grinden är relevant. Det är exakt
> den felriktning stycket **nedanför** fäller för Resend→SES-bytet — *"grinden läste därmed
> permanent 'inte relevant'"* — samma mening, ny orsak, och den här gången i den riktning som
> avväpnar en merge-blockerande grind i stället för att över-trigga den.)*
> **Defaulten är fortfarande `Console`→`NullEmailSender` i non-dev — läs den aldrig som ett
> driftläge**, eftersom lådans `.env` sätter providern.
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
> hela den här filen är obockade **utom en**: **38 obockade + 1 bockad** vid 2026-08-16 (§2.6
> punkt 3, **bockad på Klas uttryckliga beslut** — se punkten själv för varför undantaget
> beviljades och att det gäller den punkten ensam). ⚠ **Varje tal här bär sitt kommando, för
> "ett rått grep" var tvetydigt och blev läst på båda sätten** — `code-reviewer` gav 41 i en
> rond och 42 i nästa, av samma text. Talen, med det grep som producerar dem:
> `grep -cE '^- \[ \]'` = **38** · `grep -cE '^- \[x\]'` = **1** ·
> `grep -oE -- '- \[[ x]\]' | wc -l` = **42** (**båda** literalerna, oavsett indrag) ·
> prosacitat = `42 − (38+1)` = **3**, samtliga av den obockade literalen.
> ⚠ **Regenerera ALLA FYRA siffrorna ur greppen efter varje ändring som greppen kan räkna** —
> de tre ovan plus prosacitat-antalet, som är `rått − radinitialt` och alltså inte överlever att
> någon av de två regenereras utan att det räknas om. Inte
> efter "varje tillagd punkt", vilket var den gamla regeln och som är för smal på två axlar.
> **Bockning** är den ena (2026-08-16 gjorde första bocken *"38 av 38"* falskt utan att regeln löste
> ut — en vakt mot tillväxt ser ingen tillståndsändring), och **ett tillkommande prosacitat av
> literalen** är den andra: 40:an var sann när den skrevs 2026-08-04 med två citat, och ruttnade
> tyst när ett tredje tillkom. Räkna alltså **rader greppen träffar**, aldrig punkter. Avbockning
> och borttagen punkt hör till samma mängd — punkt 5.5
> tillkom i samma ändring som skrev "35", och punkt 5 i den som skrev "36", båda gjordes falska
> i samma andetag; och 2026-08-16 gjorde den första bocken *"38 av 38"* falskt medan
> regenereringsregeln bara var nycklad på **tillägg** och därför inte löste ut. En vakt mot
> tillväxt ser inte en tillståndsändring) och bockas i övrigt av den som **utför** releasen; statusen
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
        ⚠ **HALVA INDATAN ÄR MÄTT 2026-08-16 och ledet är därmed närmare, inte stängt.**
        Faktureringssidan: `No current invoices`, konsumtion €0,00, **jurisdiktion `SE`** (endast
        jurisdiktionen recordas — adressen är den ansvariges bostad). Regelns *indata* är alltså
        inte längre ett antagande. **Utfallet är det fortfarande inte mätt**, och kan inte läsas ur
        en faktura som inte finns. **Vad som ersätter fakturan står i förutsättning 1**, i
        `security-auditor`s verbatim — upprepa det inte här.
        ⚠ **En kontroll till, som är vår och inte leverantörens:** DPA Art. 7.4 ger 30 dagars
        förhandsnotis vid ändring i underbiträdeslistan **endast** *"providing that it has
        previously subscribed to updates notifications"*. En ansvarig som inte prenumererar har
        avstått invändningsrätten tyst — ⚠ **och den slutsatsen är VÅR, inte klausulens.** Det
        citerade villkoret betingar **30-dagarsnotisen** på prenumerationen; att invändningsrätten
        därmed är avstådd är vår slutledning ur att notisen är dess mekanism. Skriv den som vår
        (samma disciplin som ROPA:n tillämpar på leverantörens FAQ-brasklapp). **Prenumerationen är
        inte gjord** (mätt 2026-08-15; endast kontoinnehavaren kan göra den).
        ⚠ **POSTEN BYTTE KARAKTÄR 2026-08-16, OCH DET ÄR SAMMA FORMSKIFTE SOM FÖRUTSÄTTNING 1:S
        FAKTURA: inte "inte gjord" utan "kanske inte GÖRBAR" som självbetjäning.** Mätt 2026-08-16
        mot leverantörens publika dokumentation: konsolens organisationsnotiser har **exakt fyra**
        kategorier — `Incident`, `Technical`, `Security`, `Billing` — plus en personlig
        `Newsletter`-växel, och **ingen** av dem rör juridik, avtal, DPA eller underbiträden. Varken
        underbiträdeslistan eller dess ändringshistorik namnger någon prenumerationsmekanism: ingen
        adress, inget formulär, ingen RSS, ingen konsolinställning. DPA:n villkorar alltså
        invändningsrätten på en prenumeration vars yta leverantören inte publicerar, och enda kända
        vägen dit var brevet — som är struket (ADR 0133).
        ⚠ **ERSÄTTNINGSKONTROLLEN ERSÄTTER KONTROLLEN, ALDRIG RÄTTEN** (`security-auditor`
        2026-08-16, Q2 — hennes ord). Art. 7.4 ger **förhandsnotis (30 dagar) plus möjlighet att
        invända**; en halvårsvis läsning av en publik ändringshistorik ger **efterhandsdetektion,
        upp till ett halvår sent**, av en ändring som redan är genomförd. Den återställer varken
        notisen eller invändningstillfället. Den är den bästa tillgängliga kontrollen för den risk
        den faktiskt kan täcka — att *"inga TEM-underbiträden"* upphör att gälla utan att vi märker
        det — och den ska behållas. **Skriv den aldrig som en ersättning för rätten.**
        **Kontrollens innehåll och kadens har sitt hem i ROPA:n** (Sub-processors), inte här.
        ⚠ **FÖRLUSTEN AV INVÄNDNINGSRÄTTEN ÄR ETT EGET FYND MED EGEN DISPOSITION, OCH DEN ÄR INTE
        GJORD.** Den är inte längre *"en åtgärd Klas inte hunnit göra"* utan en **brist i
        biträdesarrangemanget**: DPA:t villkorar en rätt på en yta leverantören inte publicerar.
        Formen är identisk med led (b):s och led (c):s — en fråga vars enda kända remedium var
        brevet — och därför är dispositionen densamma: **den hör hemma som ett tredje accepterat ben
        i ADR 0133**, under samma bindning och samma lapse-klausul. Ingen ny ADR; det hade blivit ett
        fjärde hem för samma bindning.
        ⚠ **DISPOSITIONEN ÄR VILLKORAD, OCH ORDNINGEN ÄR BINDANDE:** (1) Klas läser konsolens två
        notis-URL:er vid nästa besök; (2) finns ingen juridik-kategori recordas *"saknar yta"* som
        **mätt** — och **först då** skrivs benet in i ADR 0133. Finns det däremot en yta bakom
        inloggning är fyndet stängt genom att prenumerationen görs, och ingen acceptans behövs alls.
        **Avläsningen ovan är gjord mot PUBLIK dokumentation**, så en kontroll bakom inloggning som
        dokumentationen inte beskriver kan finnas.
        ⚠ **Kadensen har ingen ägare och ingen påminnare — ersättningskontrollens svagaste punkt.**
        En kalenderförpliktelse utan påminnare körs inte. Den hör ihop med #1267 AC 2:s påminnarhalva,
        som inte är byggd; lägg den där, fila ingen egen post;
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
        utkastet är **villkorat av bekräftelsen** — och bekräftelsen skulle ha kommit via Klas-brevet
        i led (c), som är struket (nästa stycke).
        ⚠ **MÄTNING 2026-08-16 (#183 FU-1) UR KONTOTS EGET AVTAL — OCH DEN GÖR LEDET SVÅRARE, INTE
        LÄTTARE.** Underlaget är GTS:en under kontots egen `Validated`-rad i konsolens
        `Settings → Organization contracts`, alltså inte en publik sida. **Art. 10** bär på
        avtalsrang: *"for diagnosis and analysis purposes toward resolving the Technical Incident,
        **Technical Support may be required to access the Client's resources** in compliance with the
        confidentiality and security obligations incumbent upon it."* Supportens åtkomst är alltså
        **avtalsmässigt förutsedd som en rättighet**, och avtalet säger fortfarande **ingenting om
        varifrån**. **Asymmetrin ska läsas rakt: åtkomsten har rang, geografin har det inte.**
        ⚠ **Formuleringen ovan — *"sökt … noll träffar"* — underdrev därmed sitt eget underlag.**
        Det saknas inte en åtkomstklausul; det saknas en **ortsklausul till en åtkomstklausul som
        finns**. Skillnaden är materiell: en lucka fylls, en asymmetri måste rättas.
        ⚠ **Samma dokument vidgar dessutom underbiträdesytan:** GTS **Art. 21.2** — *"Scaleway may,
        **without restriction**, use the services of service providers and/or sub-contractors"* —
        parat med DPA **Art. 7.1**:s generella underbiträdesauktorisation. Det upphäver **inte**
        mätningen att leverantörens publika lista namnger **inget** TEM-underbiträde i dag; det säger
        att inget i avtalet hindrar att den ändras — vilket är precis varför Art. 7.4-kontrollen
        (ADR 0133 Amendment, led (d)) betyder något.
        **`security-auditor` 2026-08-16 (FU-1) — KARAKTERISERINGEN RATIFICERAD ORDAGRANT, LEDET STÅR
        KVAR.** De tre raderna ändrar inte statusen; de ändrar ledets **art**. Underlaget är inte
        längre *"ett påstående utan rang"* utan **"ett påstående utan rang mot en klausul som har
        rang"**. ⚠ **Bekräftelsen som skulle stänga ledet behövs därför nu MOT EN AVTALSRANGIG
        ÅTKOMSTRÄTT** — inte för att lyfta en dokumentationsmening från noll. En lucka fylls av en
        bekräftelse; en asymmetri måste **rättas**.
        ⚠ **Art. 21.2 + DPA Art. 7.1 graderas INTE på nytt här:** ADR 0133:s Amendment äger den ytan
        som led (d) (Art. 7.4-invändningsrätten, förverkad i brist på prenumerationsyta). Vidgningen
        förstärker det redan graderade ledet och skapar inget nytt fynd; graden tillhör den som satte
        den (§9.6).
        ⚠ **ADR 0133:s Context underbeskriver ledet efter den här mätningen** och ska amendas —
        acceptansen står, men en acceptans vars protokollförda grund underskattar motbevisningen
        förnyas nästa gång på en premiss som inte längre gäller (`security-auditor` Major 4, eskalerad
        till Klas).
        ⚠ **BREVET ÄR STRUKET 2026-08-16 (Klas-beslut) — ÅTGÄRDEN ÄR BORTA, INTE FYNDET.** Grunden
        ovan står oförändrad: meningen saknar fortfarande avtalsrang, och utkastet är fortfarande
        villkorat. Vad som ändrats är att ingen bekräftelse är på väg. **Beslutet, dess grund och
        dess lapse-klausul har sitt hem i ADR 0133** — läs det där; den här raden citerar och räknar
        inte. **Statusen på det här ledet sätts fortfarande av `security-auditor`, inte av
        beslutet** — ett struket brev ratificerar ingenting.
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
        dokumentation **utan avtalsrang**. *(Kvalifikationen är hennes egen precisering: dokumentation
        MED avtalsrang — en TOM-bilaga inkorporerad i DPA:t, en Art. 28(3)(a)-instruktion — skulle
        stänga ledet, och utan de två orden kan meningen senare åberopas mot just den artefakt som
        löser den.)* En slutsats villkorad av ett obesvarat
        brev är ett utkast, inte en dom.
        **Vad som stänger ledet, uttömmande:** **en artefakt med avtalsrang** som säger att support-
        och driftåtkomst till TEM-data sker uteslutande inifrån EU/EES — en TOM-bilaga inkorporerad i
        DPA:t eller en Art. 28(3)(a)-instruktion — eller, om åtkomsten inte är EU/EES-begränsad, en
        Kap. V-grund för just den åtkomsten. ⚠ **Ett skriftligt svar UTAN avtalsrang stänger inte
        ledet** (2026-08-16 kväll, ADR 0133 Amendment): ledet bär en **asymmetri**, inte en lucka, och
        en utfästelse lägger sig bredvid GTS Art. 10 utan att binda den. Ingenting mer krävs.
        ⚠ **UPPRÄKNINGEN GICK FRÅN TRE FORMER TILL TVÅ, OCH DET ÄR EN REVIDERING PÅ PLATS — INTE ETT
        TILLÄGG.** Den räknade till 2026-08-16 morgon *"ett svar på brevet"* som en egen stängande
        form, och en not sa att **uppräkningen var oförändrad** och att formen bara var *obemannad*
        (brevet struket, alltså inget svar på väg). **Båda halvorna är överspelade av kvällens
        beslut:** brevet är inte otillgängligt utan **fel instrument** — det var skopat att lyfta en
        FAQ-mening från dokumentation till avtalsrang, vilket är rätt remedie för en **lucka**, medan
        ledet bär en **åtkomstklausul med rang som saknar ortsklausul**. Asymmetrin hade överlevt sin
        egen åtgärd. **Vid lapse blir därför en artefakt med avtalsrang owed, aldrig brevet.**
        ⚠ **Det här är ett LEVANDE kriterium och revideras därför på plats**, till skillnad från en
        **record**-sektion som behåller sin ordalydelse och rättas av ett tillägg (ADR 0132
        Amendment §3:s princip). Att lägga ett fjärde ⚠-lager ovanpå en uppräkning som fem rader
        ovanför förklarades oförändrad hade lämnat **två levande svar på ett stängningskriterium** —
        kom ett Scaleway-svar in hade den ena raden sagt att ledet stänger och den andra att det inte
        gör det. *(`security-auditor` Major 6 + `dotnet-architect`, 2026-08-16.)*
        ⚠ **Frågans FORM är hennes, inte valfri:** *"sker support- och driftåtkomst till TEM-data
        uteslutande inifrån EU/EES?"* — inte "var finns supporten", som besvaras med en kontorsadress
        som inte binder. **Formen överlever strykningen:** skulle frågan någonsin ställas igen — av
        Klas, eller därför att lapse-klausulen i ADR 0133 fyrat — är det den här formuleringen som
        gäller, inte en nyskriven.
        ⚠ **DETTA LED GRINDAR INTE ETT UTSKICK DÄR ENDA MOTTAGAREN ÄR PERSONUPPGIFTSANSVARIG SJÄLV**
        (`security-auditor` 2026-08-16, och skälet är ett ANNAT än förutsättning 5:s — slå inte ihop
        dem). Ledet bär ett **negativt faktum**; faller det uppstår en överföring som behöver grund.
        I det fönstret är den enda registrerade vars uppgifter når providern den ansvarige själv, och
        då finns grunden oberoende: **Art. 49(1)(a)**, uttryckligt samtycke efter information om
        risken — ovanligt oproblematiskt just här eftersom den registrerade och den som bedömer
        risken är samma person. Det intresse Kap. V skyddar (en registrerad som varken kan veta eller
        styra vart uppgifterna går) **saknar bärare** i fönstret.
        **Det är en derogation, inte en rutingrund, och den skalar inte** — den bär denna
        mottagarmängd och ingenting bortom den. Ledet grindar vid samma gräns som förutsättning 5:s
        (b)/(d): när tredjepartsdata når providern. **Ledet är därmed INTE stängt**, och *"Kap. V ej
        tillämplig"* är fortfarande ett utkast. *(Meningen sa till 2026-08-16 att "brevet ska
        fortfarande skickas". Det ska det inte — se ADR 0133. Ledet är oförändrat ostängt; det som
        försvann är åtgärden, inte statusen.)*;
      - **ROPA-posten** i `docs/runbooks/gdpr-processing-register.md` (lokal) — **KVAR (delvis)**,
        omskriven 2026-08-15 (#183): ombunden till behandlingen *"Utgående
        transaktionell e-post (Scaleway Transactional Email, `fr-par`)"*, som täcker **samtliga
        e-postmallar** (antalet står i blockquoten ovan), båda mottagarklasserna och Kap. V-utkastet
        ovan. **Tre saker är nya i den omskrivningen och har sitt hem DÄR, inte här:** (i)
        **blocklists** — providern lagrar studsade mottagaradresser på eget initiativ, med en
        egen retentionstrappa och en egen Art. 17-väg (**trappan står i ROPA:n; upprepa den aldrig
        här**); (ii) **webhooks är opt-in och ingen är registrerad**, så event-payloadens `email_to`
        aldrig uppstår — **mätt leverantörssidigt i konsolen 2026-08-16 (`Webhooks 0`), vilket är
        den enda sidan påståendet kan mätas på.** *(Raden namngav till 2026-08-16 ett `git grep`
        över vår källkod. Det kommandot är kvar i ROPA:n och bevisar en annan sak — att VI inte
        skeppar en webhook — aldrig att ingen finns, eftersom en webhook registreras i konsolen utan
        att en rad kod ändras.)*;
        (iii) **TEM:s content-/loggretention är EJ MÄTT** — och står sedan 2026-08-16 som
        **accepterad medan den är omätt** (ADR 0133), aldrig som antagande och aldrig som mätt.
        ⚠ **BREVET SOM SKULLE STÄNGA BÅDA ÄR STRUKET 2026-08-16 (Klas-beslut, ADR 0133).** Det bar
        två omätta frågor — retentionen ovan och support-geografin i led (b) — och skulle ha gått
        via *Specific Conditions Transactional Email* (produktvillkoret finns listat på leverantörens
        avtalssida men kräver inloggat konto) eller en skriftlig fråga till leverantörens
        integritetsfunktion. **Båda frågorna står kvar som omätta; det är remedierna som är borta.**
        ⚠ **Klausulen *"Ingen flip innan båda är besvarade"* är struken, och den var redan
        verkningslös på TVÅ oberoende grunder innan den ströks** — en klausul är en **norm** och kan
        överträdas eller upphävas, aldrig vara falsk: (1) den **överträddes** 2026-08-16, när flippen
        skedde med båda frågorna obesvarade; (2) dess villkor blev **ouppfyllbart** samma dag, när
        brevet ströks och inget svar längre kan komma. **Läs den aldrig som en levande grind** —
        vad som faktiskt binder är ADR 0133:s lapse-klausul, som fyrar på **första personuppgift som
        når Scaleway och inte är den ansvariges** — operativt på **någon** av förutsättning 5:s fyra
        triggers, och skarpast på **(d), första utskick till annan mottagare än Klas**, eftersom
        uppgifter når providern vid ett **utskick** och inte vid en kontoskapelse. Förutsättning 5
        äger alla fyra definitionerna; den här raden räknar dem inte.
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
        ⚠ **LEDET ÄR RETROAKTIVT SEDAN 2026-08-16 OCH DET ÄR EN ANNAN SORTS LED NU.**
        CC1-lanen flippade `Email:Provider` till Scaleway under registreringsbesöket, med Klas
        vid terminalen, medan led (a), (b), (c) och (e) alla bar KVAR — vilket preambelns
        *"VARJE numrerad punkt MÅSTE vara grön"* inte medger. Armen skickar skarpt. Ledet grindar
        alltså inte längre en flipp som ska ske; det avgör en konfiguration som redan kör.
        Statusen står oförändrad KVAR, och det är avsiktligt: en grind som bockas av att den
        kringgicks är ingen grind.
        **`security-auditor` 2026-08-16 (E5) — DELRATIFICERING. LEDET STÅR KVAR.**
        **INGEN Blocker.** Hela mottagarmängden är personuppgiftsansvarig själv: båda kontona är
        Klas egna `+`-alias, samtliga utskick nådde honom, och den publicerade copyn är oläsbar
        (mätt 2026-08-16: `dev` 401 på varje väg, apex/www 000 över HTTPS, apex över HTTP en
        STRATO-parkeringssida utan en rad Jobbliggaren-copy — ADR 0132:s mätning, reproducerad).
        Bärarfrånvaro, samma struktur som led (b):s Art. 49(1)(a)-fönster och förutsättning 5.
        ⚠ **DEN HÄR DOMEN VILAR PÅ KONTOTABELLEN OCH INGENTING ANNAT.** Se förutsättning 5:s
        trigger (b) — den konverterande händelsens hem.
        **Förutsättning 1 — SIGNERAS INTE.** Projekthalvan är stängd av Klas konsolavläsning.
        Organisationshalvan är inte omätt utan **odefinierad**: ledet kräver match mot den
        organisation led (a):s avtalsmätning gjordes mot, och led (a) mätte en REGEL (GTS Art. 23),
        inte ett UTFALL. Högerledet finns inte. **Förutsättning 1 är därmed beroende av led (a)
        och transitivt Klas ensam**, trots att den står skriven som ett CC-körbart kommando.
        Kommandoformen stryks: en nyckel som kan utföra den är per definition bredare än ledet
        kräver (403 `permissions_denied` är rätt svar), och konsolen är den utförbara formen.
        ⚠ **Vad gapet INTE är:** en Kap. V-risk. Samtliga tre grenar i GTS Art. 23 landar i en
        EU-entitet, och DPA:n gäller oavsett gren. Residualen är **Art. 5(2) ansvarsskyldighet**.
        **Förutsättning 2 — SIGNERAS DELVIS.** Fem av sex grunder ratificeras som prövade i E4,
        `PasswordChangedNotice` inräknad (6(1)(f) på Art. 6(3); min öppna fråga är stängd).
        **`EmailChangeConfirmation` ratificeras INTE:** dess grund 6(1)(b) resonerar från
        KONTOINNEHAVAREN, och mottagarklass (3) är per konstruktion inte part i något avtal med
        oss. Grunden måste delas — 6(1)(b) mot innehavaren, **6(1)(f) mot klass (3)** — varvid
        (f)-mängden växer från tre mallar till fyra och Art. 13(1)(d)-posten måste följa med.
        **En behandling som körs utan redovisad grund är en Blocker i det ögonblicket:** ledet
        blir Blocker vid första utskick där mottagaren inte är kontoinnehavaren.
        **Förutsättning 3 — SIGNERAS DELVIS.** Utgångsdatum registrerat med proveniens,
        obligatoriskt under Scaleway, frånvaro fail-loud. Delningen tystnad/förvarning ratificeras.
        **Rotationsförfarandet finns inte** — `master-key-ops.md` §4 är masternyckeln, en annan
        livscykel. Ett registrerat datum plus en närvarokontroll är en **inventering, inte en
        rotationsstrategi**. #198 äger förfarandet, #1267 AC 2 påminnaren; ingendera är byggd.
        **Förutsättning 4 — SIGNERAS DELVIS.** API-referenshalvan ratificeras och är oberoende
        verifierad i vår ände: armen sänder en fast nyttolast utan spårningsfält
        (`ScalewayEmailSender.cs:311-318`). **Changeloghalvan är inte redundant och är omätt** —
        API-referensen visar att spårning inte kan sättas I REQUESTEN, medan changeloggen finns
        för precis det den inte kan täcka: providersidigt tillstånd satt utanför requesten, alltså
        den felmod ledet ärvde från SES. **Stäng den inte med en bättre changelog-läsning:**
        konsolen är den auktoritativa läsaren av providersidigt tillstånd och är strikt bättre än
        release notes, som är en slutledning om funktioner. **Den avläsningen är Klas**, samma
        upplösning som förutsättning 1.
        ⚠ **DEN AVLÄSNINGEN GJORDES 2026-08-16 — se förutsättning 4 nedan för vad den gav.**
        Domstexten ovan står **oförändrad som dom**, daterad 2026-08-15/16, och sessionen skriver
        inte om den: en dom är `security-auditor`s och stängs av henne, inte av att underlaget
        ändrats. **Statusen på förutsättning 4:s changeloghalva sätts därför i hennes nästa scopade
        omkontroll**, inte här. Det här är en daterad pekare, ingen omgradering (arkitektens V-3,
        2026-08-16).
        **Förutsättning 5 — SIGNERAS INTE. Graden är oförändrad Major.** Trigger (a) fyrade under
        besöket och rullades tillbaka. **Det återinför INTE Blockern** — inte för att en fyrad
        trigger av-fyras av en återställning (det gör den inte), utan för att **Klas avgjorde
        frågan i förväg**: ADR 0132, skriven före besöket, accepterar uttryckligen trigger (a)
        bunden till besökets två konton och lapsar i samma stund någon annan trigger fyrar.
        Ingen annan fyrade — (b) och (d) är stängda av `+`-aliaskravet, (c) är mätt falsk.
        ⚠ **ADR 0132:s UTFALL ÄR RÄTT OCH DESS ÅBEROPADE GRUND ÄR FEL.** Den anger CLAUDE.md
        §9.6:s accepterad-risk-väg, som gäller *"a security Major **without GDPR implication**"*;
        förutsättning 5:s Major vilar på Art. 12(2). Det som gör beslutet lagligt är **bindningen,
        inte vägen**: båda kontona är Klas egna, så den enda vars Art. 12(2)/15-22-läge försämras
        är den ansvarige själv. **Bindningen bär hela beslutet.** Vidgas den med ett enda konto
        blir det en acceptans av tredje mans rättigheter, vilket §9.6 inte medger och jag inte
        signerar.
        ⚠ **CITATET AV §9.6 OVAN ÄR TVÅUNDANTAGSFORMEN OCH ÄR DATERAT 2026-08-16 (morgon).** §9.6
        bär sedan samma dag (kväll) en **TREDJE** väg, keyed på **bindningen** — härledd ur bland
        annat den här domen. **Domens utfall är oförändrat** och dess omgradering är hennes; det som
        ändrats är att en senare instans inte längre behöver härleda grunden igen. **Den här raden är
        en daterad pekare, ingen omgradering** (samma form som förutsättning 4:s pekare ovan).
        ⚠ Domens sista mening — *"vilket §9.6 inte medger och jag inte signerar"* — är sedan
        2026-08-16 **ordagrant kodifierad i CLAUDE.md §9.6 (3)**. Det är alltså inte två oberoende
        hem: spec:t bär regeln i rapportörens egna ord, och den här domen är instansen den kom ur.
        **Förutsättning 6 (NY, 2026-08-16) — Art. 14 för mottagarklass (3).** `EmailChangeConfirmation`
        går till en adress som per konstruktion står på inget konto. En felstavad adress levererar
        meddelande och adress till någon som varken är användare eller lämnat uppgiften själv.
        **Art. 14 gäller, och inget undantag i 14(5) är tillgängligt** — 14(5)(b) faller
        avgörande, eftersom vi redan komponerar och levererar ett meddelande till exakt den
        personen; informationen kostar ett stycke. **Tidpunkten är Art. 14(3)(b): senast vid
        första kommunikationen, och den kommunikationen ÄR mejlet.**
        ⚠ **MENINGEN SOM STOD HÄR ÄR FALSK SEDAN 2026-08-16 (#183 FU-1, `code-reviewer` Major 1).**
        Den löd: *"Mallen bär i dag varken kontaktuppgift, rättslig grund, mottagare, lagringstid,
        rättigheter eller källa, och är den enda kontolivscykel-mallen helt utan `ContactAddress`."*
        **Commit `5046cec7` gav mallen samtliga sex plus `ContactAddress`**, i båda delarna av
        `multipart/alternative`. Förvärrande när den fick stå: `EmailTemplates.cs` pekar uttryckligen
        hit för resonemanget, så en läsare som följde pekaren landade på påståendet att mallen inte
        bär något av det hon just läst i koden ovanför. **Statusen på förutsättning 6 rörs inte av
        den här rättelsen** — den är `security-auditor`s, och stängningslistan nedan räknar
        Art. 14-stycket som ett av flera led. **Informationen ska bäras i
        VARJE utskick, inte villkorat:** vi kan inte vid sändningstillfället veta om mottagaren är
        innehavaren eller en främling, och det är hela skälet. Art. 14(2)(f) besvaras med en
        KATEGORI ("en användare som angav den här adressen") — att namnge användaren vore i sig
        ett röjande åt andra hållet.
        **EN ENDA HÄNDELSE KONVERTERAR HELA GRINDEN, och den ska läsas som en:** **första
        icke-Klas-konto**, definierat i förutsättning 5:s trigger (b) — **definitionens hem,
        upprepa den inte här**. Då eskalerar förutsättning 6, förutsättning 2:s klass-(3)-grund,
        förutsättning 5:s trigger (b) och §2.6:s falska rader **samtidigt**. Det är inte fyra
        risker med fyra odds; det är en händelse.
        **Vad som stänger ledet, uttömmande:** led (a):s faktureringsavläsning · en konsolavläsning
        av att ingen spårningsyta finns i TEM-projektet · den delade grunden för
        `EmailChangeConfirmation` i ROPA:n med matchande Art. 13(1)(d)-copy · Art. 14-stycket i
        mallen · och antingen en levererande `kontakt@`-brevlåda eller en publicerad kanal som
        levererar. Rotationsförfarandet (#198) och påminnaren (#1267 AC 2) grindar inte ledet men
        står kvar som skyldigheter. Ingenting mer krävs.
        **Namngivna förutsättningar för sign-off (security-auditor + code-reviewer
        2026-08-09, #1169) — hon signerar inte utan dem.** *(Medvetet utan numeral: listan räknar
        sig själv, och ett tal här hade blivit ytterligare ett hem medan blockquoten ovan räknar upp
        sina och säger att övriga tal inte är hem. Den raden bar en numeral och en hem-deklaration
        till 2026-08-09; `dotnet-architect` mätte att den gjorde uppräkningen falsk i samma commit
        som skrev den — filens egen dokumenterade felmod.)*
        1. **Organisations- och projektbindning.** Avtalsparten är en egenskap hos en
           ORGANISATION — GTS Art. 23 bestämmer entiteten ur faktureringsadressen — och hela
           ej-tillämplig-bedömningen i led (b) hänger på vilken part. **Mekanismen bytte med
           providern 2026-08-15; skyldigheten gjorde det inte.** **Mekanismen har sitt hem i
           E5-domen ovan och står inte här** (`security-auditor` 2026-08-16). Kravet självt är
           oförändrat: **Organization == den organisation ledet (a):s avtalsmätning gjordes mot**
           och **Project == `Email:Scaleway:ProjectId`**.
           ⚠ **Den andra halvan är ny och lätt att missa:** `ProjectId` är konfigurationssidigt och
           skickas i varje request-kropp, men **bindningen mellan NYCKELN och projektet följer inte
           av konfigurationen** — den är ett tillstånd hos leverantören. Utan mätningen kan
           avtalsmätningen vara gjord mot en organisation medan nyckeln tillhör en annan, vilket är
           exakt fällan AWS-erans kontobindning fanns för.

           ⚠ **FÖRUTSÄTTNING 1 ÄR INTE OSATISFIERBAR — DET NAMNGIVNA INSTRUMENTET ÄR OTILLGÄNGLIGT,
           SKYLDIGHETEN ÄR DET INTE** (`security-auditor` 2026-08-16, #183 FU-2, verbatim; frågan
           ställdes till henne med Klas faktureringsavläsning som underlag).

           > Klas konsolavläsning 2026-08-16 ändrar ledets form i vår favör: `No current invoices`,
           > konsumtion €0,00, och **faktureringsadressens jurisdiktion är MÄTT: `SE`**. Det gör
           > GTS Art. 23:s *indata* till en avläsning i stället för ett antagande — vilket var precis
           > det E5 fällde ledet på (*"led (a) mätte en regel, inte ett utfall"*). Regeln tillämpad
           > på en mätt indata ger *"any other region"* → **Scaleway S.A.S.** Det är inte längre en
           > regel utan utfall; det är avtalets egen bestämningsregel tillämpad på ett mätt faktum.
           >
           > **Varför detta INTE är punkt 4:s fall.** Punkt 4:s idempotensled ströks därför att
           > egenskapen **strukturellt saknades hos varje tänkbar motpart** — ingen artefakt kunde
           > uppfylla det, och ett KVAR hade låst prod-grinden permanent. Här finns egenskapen: vem
           > vår avtalspart är, och att den sändande nyckeln tillhör samma organisation, är fullt
           > fastställbart. Vad som saknas är **en av flera möjliga bevisformer**, och den saknas
           > **medan vi ligger på gratisnivån** — inte för alltid. Första betalda förbrukning
           > ställer ut en faktura. *"Omätbar medan gratis"* är inte *"omätbar"*. En strykning här
           > hade dessutom raderat ett **levande Kap. V-beroende** — led (b):s ej-tillämplig-bedömning
           > hänger på vilken part — alltså motsatsen till vad punkt 4:s strykning gjorde.
           > **Grammatiken skapar därför ingen permanent låsning här.**
           >
           > **LEDET SKA OMINSTRUMENTERAS, inte strykas och inte lämnas KVAR mot en faktura som
           > kanske aldrig ställs ut.** Egenskapen är oförändrad: *Organization == den organisation
           > led (a):s avtalsmätning gjordes mot* och *Project == `Email:Scaleway:ProjectId`*.
           > **Ersättningsinstrumentet är en INLOGGAD konsolavläsning, och den är Klas:** (i)
           > organisationens identitet och dess **faktureringsjurisdiktion** — `SE`, mätt 2026-08-16,
           > vilket är GTS Art. 23:s indata; (ii) **den GTS-version som faktiskt binder vårt konto**,
           > hämtad inloggad från avtalssidan; (iii) att TEM-projektet i konsolen bär samma
           > `project_id` som `Email:Scaleway:ProjectId`. Samma inloggning stänger alla tre, och den
           > kräver ingen bredare nyckel än ledet behöver — vilket var E4:s `403 permissions_denied`.
           >
           > ⚠ **Ledets kvarvarande svaghet, namngiven och inte bortskriven:** avtalssidan
           > etiketterade `v.09/06/26` medan dokumentet bar `Version du 07/04/2026`. Den
           > Art. 23-regel vi tillämpar kan alltså vara hämtad ur en GTS-version som inte binder oss.
           > Det är punkt (ii) ovan, och det är skälet den inloggade avläsningen inte kan ersättas
           > av en publik läsning.
           >
           > **Fakturan är inte avskriven.** Den förblir det starkaste beviset och tas när den finns
           > — som **bekräftelse av härledningen**, aldrig som ledets enda väg.
           >
           > **Art. 5(2)-residualen försvinner inte, men den byter art och krymper.** Den var
           > *"indatan är antagen"*. Den är nu: *"utfallet är härlett ur avtalets egen
           > bestämningsregel tillämpad på ett mätt indata, utan bekräftelse från motparten, och ur
           > en GTS-version vars bindande status inte är avläst."* **En ansvarsskyldighet som inte
           > kan uppfyllas genom att vänta uppfylls genom att härledningen och dess gräns skrivs
           > ned** — vilket är vad det här stycket gör — inte genom att vänta. **Ledet bär KVAR tills
           > den inloggade avläsningen är gjord.**

           ⚠ **DEN INLOGGADE AVLÄSNINGEN ÄR GJORD 2026-08-16 (kväll, #183 FU-1). Domen ovan står
           oförändrad som dom** — den är `security-auditor`s och stängs av henne, inte av att
           underlaget ändrats. Det här är en **daterad transkribering av mätningarna**, ingen
           omgradering; statusen på förutsättning 1 sätts i hennes nästa scopade omkontroll (samma
           form som förutsättning 4:s pekare ovan). Tre mätningar:
           1. **Punkt (ii) — den bindande GTS-versionen — är avläst, och ledets namngivna svaghet
              är därmed stängd i sak.** Klas laddade ner GTS:en ur konsolens
              `Settings → Organization contracts`, där raden står som **`Validated`**, och dokumentet
              under den raden bär `Version of 07/04/2026` med Art. 23 i tre grenar. **Den Art. 23 vi
              tillämpar står alltså i det dokument som ligger under kontots egen validerade rad, inte
              på en publik sida.**
              ⚠ **Versionsetiketterna är dock TRE och stämmer inte överens:** publika avtalssidan
              `v.09/06/26` (mätt 2026-08-15), konsolraden **`05/2026`**, dokumentet `07/04/2026`.
              *Att `05/2026` sannolikt är en valideringsstämpel på 07/04/2026-utgåvan —
              nedladdningens filnamn bär "Mon May 18 2026" — är **vår slutledning, inte en
              mätning**, och får inte skrivas som leverantörens utsaga.* Det som håller oavsett
              etikett är meningen ovan: dokumentet ligger under den validerade raden.
           2. **Punkt (iii) — ORGANISATIONSHALVAN ÄR STÄNGD, OCH STARKARE ÄN LEDET BAD OM.** Lådans
              injicerade `Email__Scaleway__ProjectId` jämfördes mot det id konsolen visar; utfallet
              var **lika**. De kontrakt som står `Validated` tillhör den organisationen, och armen
              skickar med den organisationens id i varje request-kropp — alltså är avtalsmätningen
              och den sändande armen samma organisation. Det var fällan ledet finns för.
              ⚠ **PROJEKTHALVAN ÄR DÄREMOT ICKE-DISKRIMINERANDE BY DESIGN och får inte läsas som en
              oberoende kontroll:** Scaleways **default-projekt ärver organisationens ID**, och den
              statusen kan inte flyttas till ett annat projekt. Samma sträng är alltså både org-id
              och projekt-id, så likheten kan inte skilja *"armen skickar in i det TEM-projekt
              konsolen lästes på"* från *"armen skickar in i default-projektet, som råkar vara
              samma"*. I dag sammanfaller de; **skapas någon gång ett separat TEM-projekt passerar
              kontrollen fortfarande och mäter då ingenting.**
              **Vad som faktiskt binder nyckeln till projektet är empiriskt, inte konfigurationellt:**
              en Scaleway-nyckel är scopad, E4 mätte `403 permissions_denied` och
              registreringsbesöket mätte 403 ända tills IAM-policyerna knöts till applikationen —
              därefter levererade utskicken. En nyckel i en annan organisation som skickar mot det
              projektet hade fått 403. Leverans → nyckeln har behörighet på projektet.
              **Slutledningen är vår; 403-beteendet är mätt.**
              ⚠ **ORGANISATIONSHALVAN BÄR SAMMA FÖRFALLOVILLKOR SOM PROJEKTHALVAN, och det stod
              utskrivet bara för den senare** (`security-auditor` Minor 5, 2026-08-16). Likheten
              binder organisationen **enbart därför att default-projektet ärver org-id:t**. Skapas
              ett separat TEM-projekt bär `ProjectId` inte längre org-id:t, och då säger jämförelsen
              ingenting om organisationen heller. **Läs inte organisationshalvan som ovillkorligt
              stängd** — den är stängd så länge armen skickar in i default-projektet, vilket är
              samma villkor som ovan och inte ett svagare.
           3. **Punkt (i) — faktureringsjurisdiktionen `SE`** — stod redan i domen ovan och räknas
              inte om här.
           ⚠ **OMÄTT, OCH DET SKRIVS UT HELLRE ÄN BORT** (`security-auditor` Minor 6, 2026-08-16):
           om GTS:en bär en **ensidig ändrings- eller notifieringsklausul** under vilken den publika
           `v.09/06/26`-utgåvan slår igenom mot kontots validerade `07/04/2026` — och om Art. 23
           lyder lika i de två utgåvorna. Slutsatsen *"dokumentet ligger under den validerade
           raden"* är rätt test och håller, men den håller **bara så länge inget i avtalet gör en
           senare utgåva bindande på notis**. Att etikettfrågan i övrigt är löst får inte läsas som
           att den här är det.
           ⚠ **INGA ID:N I REPOT.** Repot är publikt, och ledet behöver **jämförelsen**, inte
           identifieraren. Varken org-id, projekt-id eller nyckel-id skrivs här eller någon
           annanstans i repot.

           ⚠ **PII:** endast jurisdiktionen `SE` står i repot. Faktureringsadressens gata, postnummer
           och ort är den ansvariges bostad och recordas aldrig, lika lite som kortuppgifter,
           SMTP-användarnamn eller nyckel-id.
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
           krävs en matchande Art. 13(1)(d)-post i policyn FÖRE flippen.**
           ⚠ **POSTEN FINNS SEDAN 2026-08-16 OCH MENINGEN SOM STOD HÄR ÄR FALSK SEDAN SAMMA DAG.**
           Raden löd *"den träffar då tre mallar, inte två. Dagens berättigat-intresse-avsnitt
           räknar upp fyra behandlingar och ingen av dem är e-post."* Den var sann när den skrevs
           2026-08-09 (#1169) och föll i samma andetag som #183 E4 (`f09755b1`) lade in
           Art. 13(1)(d)-posten som andra punkten i `privacy.sections[3].list`. **Mätt 2026-08-16
           (#183 FU-1): fem poster, och post 2 ÄR e-post.** Regenerera mot
           `privacy.sections[3].list`, räkna aldrig ur den här raden — det är samma decay-form som
           §2.6 punkt 1:s radmängd, och skälet talet inte får bo på en andra plats.
           **Mängden växte 3 → 4 i samma ändring som skriver den här raden** (#183 FU-1):
           `EmailChangeConfirmation` mot **mottagarklass (3)** vilar på 6(1)(f), eftersom dess
           6(1)(b) resonerar från kontoinnehavaren och klass (3) per konstruktion inte är part i
           något avtal med oss (förutsättning 2:s E5-dom ovan). Posten är vidgad till den fjärde i
           samma PR; **`security-auditor` graderar om vidgningen räcker, sessionen graderar inte.**
           Faller grunderna i stället ut som 6(1)(b) täcks de av befintlig copy och luckan stänger
           sig själv. **En behandling som körs utan redovisad grund är en Blocker i det
           ögonblicket**, inte en Minor.
        3. **Nyckelrotation för den statiska providernyckeln** — ingen instance role finns, så
           nyckeln är långlivad per definition. Skyldigheten är oförändrad sedan 2026-08-08 och
           återregistreras här så den inte tappas; ägs även av #198. **Sedan 2026-08-15 gäller den
           `Email:Scaleway:SecretKey`.** ⚠ **`ProjectId` roterar INTE och ska inte behandlas som en
           nyckel** — det är en identifierare, inte en hemlighet, men den injiceras som en egen fil
           med egen livscykel (E2) och loggas aldrig. De två har alltså skilda regimer trots att de
           levereras genom samma söm.
           ⚠ **UTGÅNGSDATUMET SKRIVS IN I SAMMA `.env`-EDIT SOM SJÄLVA FLIPPEN, och det är inte
           en ordningspreferens** (#183 E4, 2026-08-16). `EMAIL_SCALEWAY_KEY_EXPIRES_AT` är
           **obligatorisk** under `EMAIL_PROVIDER=Scaleway`: sätts providern utan den exitar
           `--check` non-zero var tionde minut på en låda vars stack är fullt frisk, och
           `systemctl --failed` latchar — alltså precis det alltid-tända tillstånd hela
           kontrollen finns för att undvika. **Stegordningen bor i `deploy/.env.example`:s
           outbound-block, inte här**; den här raden säger bara att ledet inte är uppfyllt av en
           injicerad nyckel ensam. **Nyckelns utgångsdatum och dess proveniens har sitt hem i
           `master-key-ops.md` §2** — läs det där.
           ⚠ **Vad kontrollen INTE gör, så att ingen läser in en påminnelse som inte finns:**
           den varnar i journalen inom sitt fönster men **exitar 0** där — en förvarning på en
           latchande yta hade undertryckt den övergång varje annan heartbeat-predikat behöver för
           att notifiera. Förvarningens leverans är en **kalenderförpliktelse** och ägs av
           [#1267](https://github.com/klasolsson81/jobbliggaren/issues/1267), vars påminnarhalva
           **inte är byggd**. Ingenting pagar någon före utgången.
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
           ommätning av frånvaron hos leverantören** — **läs leverantörens KONSOL** och bekräfta att
           ingen spårningskonfiguration tillkommit; **API-referensen är korroborering, aldrig
           instrumentet**, eftersom den bara kan visa att spårning inte går att sätta *i requesten*.
           ~~och produktens changelog~~ är **struken som instrument** (2026-08-16, `security-auditor`
           M-2) och står kvar enbart som proveniens: changeloggen är en slutledning om funktioner,
           och avläsningen längre ned i det här ledet bevisar den blind — den hittade en **påslagen**
           inställning som ingen changelog-post annonserade. *(De två meningarna stod tidigare 25
           rader isär och pekade ut olika instrument för samma framtida mätning — samma defektklass
           som `get-email-identity` fälldes för i det här ledet samma dag. En operatör som läser
           uppifrån mötte den svagaste först.)* En feature request kan skeppas mellan två mätningar;
           2026-08-15 var bevis för den dagen och ingen inlösen.
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
           men **skriv aldrig om posten som om skäl 1 vore garanterat av testet**,
           och ommät **frånvaron av spårningsyta i leverantörens konsol** vid flippen —
           `get-email-identity` är struket som instrument här: raden ovan säger själv att det är
           ett AWS-begrepp utan Scaleway-motsvarighet, och två meningar i samma led som beordrar
           respektive omöjliggör samma kommando skickar flippoperatören till ett kommando som
           inte kan finnas (`security-auditor` 2026-08-16). Den mätning som gjordes 2026-08-12 var
           bevis för den
           dagen och inte en inlösen av förutsättningen.
           ✅ **KONSOLHALVAN ÄR STÄNGD 2026-08-16 — Klas läste av TEM-projektet, och avläsningen
           gjorde EGENSKAPEN grön och FRÅNVAROPÅSTÅENDET falskt.** Det är hela skälet posten
           skrivs, och skillnaden är inte kosmetisk: **egenskapen** är att ingen
           mottagarnivå-spårning uppstår, och den håller — men *"ingen leverantörssidig
           konfiguration existerar"* hade varit **falskt**, för en leverantörssidig inställning är
           **påslagen**. **Vilken den är, vad den bär och vad som gör den ofarlig står i ROPA:n,
           som är dess hem — upprepa det inte här** (ETT HEM PER TAL). Vad som hör hit är bara
           domen: aggregatet är per domän och når ingen mottagarnivå, så förutsättningen håller.
           Samma avläsning stängde webhook- och blocklist-frågorna **leverantörssidigt**, vilket är
           den enda sidan de kan mätas på — ett `git grep` över vår källkod bevisar att *vi* inte
           skapar en webhook och aldrig att ingen finns. Talen bor i ROPA:n.
           ⚠ **Läs INTE detta som att changeloggen dög.** Konsolen är det auktoritativa instrumentet
           för providersidigt tillstånd och release notes är en slutledning om funktioner — det var
           `security-auditor`s egen dom 2026-08-16, och avläsningen ovan bekräftar den empiriskt:
           en påslagen inställning som ingen changelog-post annonserade var precis det changeloggen
           inte kunde se. **Öppna aldrig changeloggen som ersättning för konsolen.**
           ⚠ **Avläsningen är dagsfärsk, inte en inlösen.** Den binder 2026-08-16 och ingenting
           därefter: en påslagen rapport kan byta innehåll och en spårningsfunktion kan skeppas
           mellan två avläsningar. Ommät i konsolen — inte i changeloggen — när led (e) faktiskt
           signeras.

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
           Klas har skjutit upp reparationen i väntan på STRATO:s e-postpaket. Instrumentet är
           `vps-deploy-stack.md` rad 36 — återställ inte den gamla förväntan som en "reparation",
           recorda vad som resolverar.
           ⚠ **FÖRUTSÄTTNINGEN ÄR INTE UPPFYLLD — VÄG (a) ÄR VALD, OCH DEN BÄR EN ORDNINGSREGEL
           (Klas-beslut 2026-08-16).** *(Rubriken bar en ✅ till 2026-08-16. Fel glyf: i en fil vars
           grammatik är "grön = inget led bär KVAR" hade den markerat ett **vägval** på en
           förutsättning som fortfarande är **osignerad och Major** — samma glyf, två jobb, och det
           ena i den farliga riktningen. `security-auditor` m-3.)* Ledet
           namnger tre vägar igenom förutsättningen — brevlådan börjar ta emot, en publicerad kanal
           som levererar, eller en accepterad risk. **Klas valde den första**, och den är inte
           längre en öppen fråga utan ett **schemalagt åtagande**: STRATO:s e-postpaket köps **inom
           ~1 vecka från 2026-08-16**, långt före MVP-lansering, och Klas band ordningen — **inga
           riktiga användare innan brevlådan tar emot.**
           **Det binder ihop två klausuler som hittills hängt löst.** Trigger (b) — första konto
           vars adress Klas inte själv innehar — förutsätter nu att kanalen fungerar först, alltså
           kan den händelse som konverterar hela grinden inte längre inträffa medan
           rättighetskanalen är död. Ordningsregeln är därmed en **förutsättning för** triggern, inte
           ett alternativ till den, och den flyttar ingen gräns: trigger (b):s definition står
           oförändrad **nedan**, i den här förutsättningens eget eskaleringsschema, och ägs där.
           ⚠ **ORDNINGSREGELN ÄR ETT ÅTAGANDE, INTE EN MÄTNING — och det är hela varningen.**
           Ingenting i den säger vad MX resolverar till; den säger vad Klas har åtagit sig att göra.
           **MX SKA OMMÄTAS NÄR PAKETET ÄR PÅ PLATS, ALDRIG ANTAS** — instrumentet är
           `vps-deploy-stack.md` rad 36:s MX-ben, körningen är `nslookup -type=MX jobbliggaren.se
           8.8.8.8`, och förutsättningen är uppfylld först av det som resolverar. **Apex-MX:en blev
           falsk på exakt det sättet förra gången:** raden bar `smtp.rzone.de` som förväntan tills
           någon faktiskt läste, och leverantörens domänverifiering hade då redan skrivit över den
           med `blackhole.tem.scaleway.com` utan att någon hade rört DNS. Ett köpt paket är inte en
           levererande brevlåda förrän avläsningen visar det.
           ⚠ **OMGRADERAD 2026-08-16 (`security-auditor`) MOT MÄTT DRIFTLÄGE.** Graden är oförändrad
           **Major**; eskaleringsschemat är omskrivet. Klausulen löd tidigare *"Blocker vid första
           riktiga användare eller vid flippen, vilket som kommer först"*. **Flippen är INTE längre en
           utlösande händelse:** mätt 2026-08-16 serverar apex ingenting (000), `dev.jobbliggaren.se`
           svarar 401 Basic på varje väg (även `/integritetspolicy` och `/kontakt`),
           `Auth:RegistrationsOpen` saknas i basfilen och i `deploy/` (prod-default false), och ingen
           väg mailar en oregistrerad adress (`UserAccountService.TryPreparePasswordResetAsync`
           skickar till kontots egen lagrade adress, aldrig den inskickade stavningen). Den
           publicerade rättighetskanalen har därmed **ingen läsare**, och utskicket når endast
           personuppgiftsansvarig själv.
           **Blocker återinförs vid, vilket som kommer först:** (a) `RegistrationsOpen=true` utanför
           Development; (b) **första konto vars adress Klas inte själv innehar** — "innehar"
           betyder här: en adress vars inkorg han kan läsa och som inte tillhör någon annan
           fysisk person. **Egenskapen,
           inte rollen:** en CC-verifikationsadress är undantagen **endast** när den är ett
           `+`-alias av hans egen inkorg, aldrig i kraft av att kallas verifikationsadress.
           *(Formuleringen löd till 2026-08-16 "inte är Klas egen eller en CC-verifikationsadress" —
           ett NAMN på en roll där grunden är en EGENSKAP, alltså namnbaserat i exakt den riktning
           preambeln fäller: en verifikationsadress han inte innehar hade undantagits tyst, och det
           är den bärare hela Major-graderingen vilar på inte finns. Redan namngiven som skip i
           registreringsgrind-PR:en med baskvalificeraren routad hit; stängd av `security-auditor`
           2026-08-16, E5-omkontroll.)* **Den här raden äger DEFINITIONEN, och §2.5:s statusblock
           och §2.6 pekar hit utan att återge den.** ⚠ **`registration-gate.md` TILLÄMPAR den och
           skriver ut egenskapen två gånger** (precondition 3 och 5), därför att det är där
           operatören väljer adresserna och en pekare hade varit oläsbar i det ögonblicket —
           samma avvägning som credentialens två ytor nedan. **Förfinas egenskapen här ska BÅDA
           ställena i den filen ändras**; ingenting länkar dem;
           (c) copyn blir publikt läsbar — borttagen Basic-auth, apex/www börjar serva, eller första
           `v*` — **§2.6:s trigger, oförändrad**; (d) första utskick till annan mottagare än Klas.
           ⚠ **Mätningarna förfaller: ommät (a) och (c) VID flippen, ärv dem inte ur den här raden.**
           ⚠ **Basic-auth-credentialen på `dev` bär EN GDPR-slutsats, och det här är hemmet för
           GRADERINGEN av den.** (1) Tas den bort för en demo blir blackholen Blocker i samma
           ögonblick, och ingenting varnar.
           ⚠ ~~(2) Borttagningen **publicerar** dessutom de markörrader §2.6 punkt 1 namnger som
           falska, om en levande behandling (ADR 0090 D3).~~ **SLUTSATS (2) ÄR UTSLÄCKT 2026-08-16
           (#183 FU-2b) — struken som proveniens, inte raderad.** Den tillkom 2026-08-16 (#183 E5)
           och upphörde samma dygn, av att raderna flippades i stället för att credentialen
           behölls. Den står kvar därför att en läsare annars inte kan se att slutsatsen fanns:
           **`basic_auth` bar under ett dygn en transparensrisk utöver åtkomstkontroll, och den
           risken är åtgärdad vid källan.** ⚠ **Återuppstår den om en markörrad någon gång blir
           falsk igen** — rad 82 är den enda kvarvarande kandidaten, och den är i dag sann.
           **Slutsats (1) är oförändrad och är den som gäller.**
           ⚠ **TVÅ YTOR MED OLIKA UPPGIFTER, OCH DET ÄR AVSIKTLIGT — läs inte den ena som drift.**
           Den här raden bär graderingen och dess grund. `basic_auth`-direktivet i
           `deploy/caddy/Caddyfile` bär **slutsatserna i sin helhet**, därför att det är där
           operatören står i det ögonblick handlingen utförs; en pekare där hade varit oläsbar för
           den som är på väg att kommentera bort blocket. **Tillkommer — eller upphör — en slutsats
           ska BÅDA ytorna ändras** — det är priset för att direktivet är operativt, och det är
           billigare än en varning operatören aldrig möter. ⚠ **Kopplingen gäller i BÅDA
           riktningarna, och det mättes 2026-08-16:** när slutsats (2) släcktes hade en fix på bara
           den här ytan lämnat `Caddyfile` med en varning om tre falska rader som inte längre är
           falska — en operatör hade då avstått från en handling på en grund som inte finns.
           **En fix på en av två ytor är ingen fix**, oavsett riktning. `registration-gate.md`, ADR 0132 och
           `test-accounts.local.md` **pekar hit och räknar inte själva.**
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
      som kanske inte används. Alltså **(iii) → (ii)**. ~~Och listans första post — strykningen av
      e-poststyckets avtalsreservation — när DPA:n är verifierad gällande för Scaleway S.A.S.~~
      ⚠ **(i)-klausulen i den här raden är FÖRBRUKAD 2026-08-16 (#183 FU-2b, `code-reviewer`
      Major 2) — struken som proveniens.** Den schemalade en strykning som samma PR redan utfört,
      och på ett annat villkor än det som faktiskt utlöste den: reservationen ströks för att den
      blivit **falsk**, inte för att avtalet verifierats. Se residual (i) nedan, som äger skälet.
      **Ordningen `(iii) → (ii)` står kvar oförändrad** med sitt eget skäl — det gäller en Kap.
      V-återaktivering och rör inte (i).
      *(Denna routing-rad sa till 2026-08-15 "flytten in i `Mottagare`-listan … när avtalet
      **signeras**". Båda halvorna var fel: `list`-nyckeln finns inte, och (i):s egen kropp säger
      att "på plats" inte är detsamma som "signerat".)*
      (i) **e-postleverantörens stycke får stryka sin egen avtalsreservation när avtalet är på
      plats.** ⚠ **Mekanismen är omskriven 2026-08-15 (`security-auditor` Major 2), för att den
      skyddsmekanism residualen tidigare namngav inte finns.** Posten sa att *"prosaformen är vald
      just för att listrubriken påstår ett tecknat avtal"* — men `privacy.sections[7]` har **ingen
      `list`-nyckel** (mätt 2026-08-15 på dåvarande index 6, ommätt 2026-08-19 på index 7 sedan
      sökhistorik-sektionen sköt in på 4: `heading` + sex `paragraphs`, noll `list`); nyckeln
      försvann i #1199, och e-poststyckena ligger i exakt samma strukturella position som
      netcup-stycket. **En residual vars angivna skydd inte existerar läses som uppfylld**, och den
      här lurade sin egen granskare två gånger. Vad som bar ärligheten **till 2026-08-16** var
      styckets **egen** mening — *"Innan vi börjar skicka säkerställer vi att
      personuppgiftsbiträdesavtalet med Scaleway SAS gäller"* — och villkoret för att stryka den var
      att avtalet faktiskt gäller
      för Scaleway S.A.S. *Sedan ADR 0131 är motparten Scaleway S.A.S., och
      "på plats" är inte detsamma som "signerat": DPA:n gäller automatiskt (avtalsdokument nr 1 i
      GTS Art. 3, mätt 2026-08-15) — precis som AWS-DPA:t gjorde. Villkoret är oförändrat genom
      båda bytena, men det gäller **reservationen och inte en lista**: den får bara strykas för en
      part vars avtal faktiskt gäller.*
      ⚠ **RESERVATIONEN ÄR STRUKEN 2026-08-16 (#183 FU-2b) — MEN INTE PÅ DEN HÄR RESIDUALENS
      VILLKOR, och skillnaden ska inte suddas.** Villkoret ovan ger tillstånd att stryka meningen
      **när avtalet är på plats**. Det är inte varför den ströks. Den ströks därför att den blivit
      **falsk**: *"Innan vi börjar skicka säkerställer vi …"* är ett framtidspåstående om en
      handling som redan har skett, och utskicken började 2026-08-16. En falsk mening får inte stå
      kvar i väntan på att villkoret för att ta bort den ska uppfyllas.
      **Vad som INTE följer av strykningen:** att led (a) skulle vara grönt. Det bär oförändrat
      KVAR — faktureringsavläsningen är inte gjord, och avtalsparten är **härledd** ur GTS Art. 23
      tillämpad på en mätt `SE`-jurisdiktion, inte avläst ur en faktura. Copyn påstår efter
      strykningen **ingenting** om avtalet i det stycket, vilket är det enda läge som är sant
      oavsett hur led (a) faller ut.
      ⚠ **Residualen är därmed FÖRBRUKAD I SIN NUVARANDE FORM** — det finns ingen reservation kvar
      att stryka. Skulle en avtalsmening någon gång skrivas in igen är villkoret ovan fortfarande
      det som gäller för den.
      ⚠ **DÄRMED BÄR MOTTAGARAVSNITTETS INGRESS AVTALSPÅSTÅENDET ENSAM** — *"Med dem har vi
      personuppgiftsbiträdesavtal"* är efter strykningen den enda meningen i policyn som säger
      något om biträdesavtal. **Den är redan omvärderad och står rätt** (§2.6 punkt 3, 2026-08-16:
      netcups AVV tecknat 2026-08-03, Scaleways gäller automatiskt per GTS Art. 3), så den här
      ändringen skapar ingen ny skyldighet där — men den flyttar hela vikten dit, och det som
      punkt 3 skriver som *"kvar att bevaka"* blir därmed skarpare: **en ny biträdespart får aldrig
      hinna in i uppräkningen före sitt avtal**, eftersom ingen reservationsmening längre fångar
      henne. **Graderingen av om den härledda avtalsparten räcker för ingressens presens är
      `security-auditor`s.**
      (ii) **Art. 13(1)(f)** — "means to obtain a copy" av skyddsåtgärderna — **LEVERERAD
      2026-08-09 (#1169)** och **UPPHÖRD 2026-08-15 (#183, ADR 0131)**: formuleringen hängde på att
      en överföring fanns att skydda, och den grunden finns inte mot en fransk avtalspart utan
      tredjelandsmoder. Stycket är struket ur copyn **med sin grund**, inte omskrivet. *Skulle
      Kap. V återaktiveras återkommer både grunden och den här residualen.* ⚠ **Exemplet som stod
      här till 2026-08-16 — "om Klas-brevet visar att support har åtkomst från tredjeland" — är
      struket med brevet (ADR 0133), inte för att villkoret ändrats.** De vägar som faktiskt kan
      återaktivera Kap. V är kvar och står i led (b): en artefakt med avtalsrang som säger motsatsen,
      eller en åtkomst som visar sig inte vara EU/EES-begränsad. **Ett struket brev tar bort ett
      exempel, aldrig en residual.**
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
      det står med flit inte här), och boxen bockas **i övrigt** av den som **utför** releasen,
      inte av den som levererar en förutsättning. *(⚠ "I övrigt": §2.6 punkt 3 bär sedan
      2026-08-16 ett **Klas-beviljat undantag**, skopat till den punkten. Konventionen här är
      alltså regeln, inte en undantagslös utsaga — se punkten själv.)* Sakinnehållet (förutsättningen ÄR uppfylld)
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
      ⚠ **VERKSTÄLLD I EFTERHAND 2026-08-16 — OCH RUTAN BOCKAS INTE.** Flippen skedde 2026-08-16
      utan att den här punkten kördes, så raderna stod mätt falska i den fil varje CC-invokation
      läser. De är reparerade nu (#183 FU-2). **Rutan står kvar obockad med avsikt: en grind som
      bockas av att den kringgicks är ingen grind** (E5:s dom, samma skäl som §2.5:s statusblock
      står oförändrat KVAR). Punkten är alltså *utförd*, inte *uppfylld*.
      ⚠ **UPPRÄKNINGEN VAR INTE GRÄNSEN — REGELN VAR, och den träffade en fjärde hemvist.** Kört
      2026-08-16 med plattat blanktecken: de fyra söksträngarna ovan gav **fem träffar i fyra
      hemvister**, och den fjärde — **§8:s ADR 0071-preambel**, *"e-postvägen är en separat, grindad
      överföring"* — står **inte** i uppräkningen. Den bar dessutom ordet *överföring*, som är fel
      på egna meriter: avtalsparten är fransk och Kap. V-bedömningen är *ej tillämplig*.
      **Bevisformen är noll träffar mot söksträngarna UTANFÖR CITERAD PROVENIENS, aldrig antalet
      redigerade rader** (`senior-cto-advisor`-bind 2026-08-16, kvalifikationen `security-auditor`s
      N-3 samma dag). ⚠ **Kvalifikationen är inte en uppmjukning — utan den är bevisformen
      ouppfyllbar:** reparationen av en falsk rad recordar den pensionerade lydelsen (*"Posten sa
      …"*), så söksträngarna träffar permanent sina egna citat. Mätt 2026-08-16 mot `BUILD.md`:
      **tre kvarvarande träffar, alla inne i daterade citat.** En operatör som jagar noll når det
      aldrig, och tre kända träffar blir en brusbaslinje där en fjärde och äkta försvinner. Läs
      varje träff och avgör om den är **levande eller citerad**; det är den avgörningen som är
      beviset. **Kör regeln, inte den här listan** — regeln är dessutom själv en uppräkning en nivå
      ned, och den missade §2.5:s egen tillämplighetsmening (*"ingen e-post skickas"*), som ingen
      av de fyra söksträngarna kan träffa.
      ⚠ **Ersättningstextens form är bunden:** skriv *"aktiverad utan att §2.5-grinden passerades"*,
      aldrig bara *"aktiverad"* — den kortare formen läser som att grinden gav grönt. Och importera
      **inte** den här punktens ord *tredjelandsöverföring* till de flippade raderna; det byter en
      falsk premiss mot en farligare.

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

Det är samma lucka som den redan eskalerade frågan om kontot/autentiseringen (Art. 30(1)) —
**och den luckan stängdes INTE av #1169**: den nya posten täcker e-postbehandlingen, inte kontot/autentiseringen som sådan.
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
> eftersom det bevisar kännedom (Art. 5(2)/24(1)).
> ⚠ **EXPONERINGSFÖNSTRET ÄR INTE LÄNGRE TOMT, OCH DET ÄR TVÅ SKYLDIGHETER SOM INTE
> LÖSER UT VARANDRA** (2026-08-16, #183 E5). Den **mekaniska** grinden behövs alltjämt
> inte före en prod-deploy, och #1034:s mekanism rider samma prod-pipeline — den
> halvan av det som stod här är oförändrad, och tidplanen är fortfarande en
> tillfällighet tills den skrivs ut, vilket den härmed är. Men **markörernas
> sanningshalt är en egen skyldighet som redan har fallit ut:** e-postarmen
> aktiverades 2026-08-16 medan §2.5 punkt 1 bar KVAR, och punkt 1 nedan namnger
> vilka rader som därmed är falska i dag och vilken som inte är det. **Läs den
> skyldigheten där; den räknas inte här.** Att copyn saknar läsare är uttryckligen
> **inte** grunden att låta dem stå — punkt 1 mäter varför.
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
> ⚠ **DEN ANDRA HALVAN AV DET VILLKORET ÄR FÖRBRUKAD** (`security-auditor` 2026-08-16, #183 E5):
> **flippen är inte längre en utlösande händelse** — den skedde 2026-08-16 och domen är INGEN
> Blocker. Prod-deploy-halvan står oförändrad och är §2.6:s egen trigger. **Schemat har ett enda
> hem — §2.5 punkt 1 led (e) förutsättning 5 — och den här raden citerar det, den bär det inte.**
>
> ⚠ **OCH BREVLÅDAN GÖR STRATO TILL BITRÄDE I EN ANDRA FUNKTION.** Registrets bestämning
> (`gdpr-processing-register.md`, lokal) säger att *DNS* hos STRATO inte är en biträdesrad,
> eftersom en DNS-operatör *"tar inte emot registrerades uppgifter för vår räkning"*. En brevlåda
> gör precis det. Grunden är sann om DNS och faller för post. Krävs innan brevlådan tas i bruk:
> ROPA-amendment (Art. 30(1)(d)) + retentionsbeslut för inkommande korrespondens (Art. 5(1)(e)) +
> `Mottagare`-stycke i policyn (Art. 13(1)(e)) + **AVV med STRATO (Art. 28)**. *Förläget var sämre
> på varje axel — en privat Gmail gjorde Google till de facto inbound-biträde, US-domicilierat,
> utan möjligt Art. 28-avtal på ett konsumentkonto; **STRATO GmbH** är tyskt och tecknar AVV.
> Bytet förbättrar läget, det skapar inte luckan.*
>
> ⚠ **TRE PÅSTÅENDEN I STYCKET OVAN VAR FALSKA OCH ÄR RÄTTADE 2026-08-28 (#183, `code-reviewer`
> Major 1/2/4 + `security-auditor` Minor 1). Alla tre mättes; ingen är framräknad.**
> 1. Retentionsledet sa *"saknas helt i dag"*. **Beslutat 2026-08-28 av personuppgiftsansvarig:
>    tolv månader efter att ärendet har avslutats.** Beslutets hem är ROPA:ns STRATO-post
>    (gitignorerad); den publicerade formen står i `content-legal.json` under Art. 13(2)(a).
>    ⚠ **Beslutat är inte mekaniserat** — ingenting raderar automatiskt i en STRATO-brevlåda.
>    Skriv aldrig ledet som uppfyllt på grundval av att beslutet är fattat.
>
>    ✅ **LÄSAREN ÄR UTPEKAD SEDAN 2026-08-28, OCH DET HÄR ÄR HANS HEM (#183 led 6).**
>    **Läsare: Klas Olsson, personuppgiftsansvarig.** CC ska aldrig ha brevlådeåtkomst — ingen
>    credentialförvaring, och en sådan åtkomst vidgar en behandling registret nyss villkorade.
>    ⚠ **Detta är en operativ kontroll, inte en riskaccept.** §9.6 (3):s väg är **otillgänglig**
>    här och behövs inte: föremålet är inkommande rättighetskorrespondens, alltså tredje parts
>    uppgifter **per konstruktion**, och en accept vidgad med en enda icke-ansvarig registrerad är
>    en accept av någon annans rättigheter. En **manuell** retentionsrutin är däremot fullt laglig
>    under Art. 5(1)(e) jämförd med Art. 24(1)
>    (`security-auditor`, `docs/reviews/2026-08-28-183-led5-security-auditor.md`,
>    eskaleringspunkt 3 — gitignorerad, huvudkopian).
>
>    **RETENTIONSTRIGGRARNA `R1`–`R4` — uppräknade, aldrig en grundmening.** En enda mening är ingen
>    triggermängd; den formen graderades under-triggering (`security-auditor` M-1, ADR 0133).
>    ⚠ **Egna etiketter med flit, och förväxlingen de förhindrar är verklig:** §2.5 punkt 1 led (e)
>    förutsättning 5 äger en **annan** fyrmedlemsmängd som också heter (a)–(d), och §2.6 citerar den
>    på flera ställen. **`R`-prefixet gör en framtida hänvisning till "trigger (d)" entydig igen.**
>    De två mängderna mäter olika saker — förutsättning 5:s eskalerar en GRAD, dessa utlöser en
>    GENOMGÅNG — och ingen av dem får någonsin läsas in i den andra.
>    Genomgången görs vid **vilken som helst** av:
>    **`R1` varje avslutat ärende** — det är då tolvmånadersklockan startar, och ingen annan trigger
>    ser den händelsen;
>    **`R2` halvårsvis** (Klas-beslut 2026-08-28), samordnat med underbiträdesläsningen ovan så att
>    två obevakade kalenderförpliktelser blir ett tillfälle — **kadensens hem är ROPA:n, och den
>    återges inte här**;
>    **`R3` varje körning av §2.5 eller §2.6** — checklistan är en yta som faktiskt läses;
>    **`R4` varje ändring av apex-MX eller av brevlådans mottagande** — flytten själv inkluderad.
>
>    ⚠ **LATENSEN ÄR EN AVVIKELSE MOT ETT PUBLICERAT LÖFTE OCH SKA STÅ SKRIVEN, INTE UPPTÄCKAS.**
>    I lågtrafikläget — det förväntade — är `R1` tyst och varken
>    `R3` eller `R4` fyrar, så bara `R2` återstår: en post som förfaller vid tolv månader raderas då
>    **upp till arton**. Godtagbart under Art. 24(1) vid dagens volym, som är noll inkommande. **Det
>    gör det inte till tolv.**
>
>    ⛔ **INGEN PÅMINNARE ÄR BYGGD, och den meningen får aldrig strykas av att åtgärden godkänts.**
>    Ingenting pagar någon, **ingenting mäter att genomgången skett**, och en kalenderförpliktelse
>    utan påminnare körs inte. Det är recordet av vad som **inte** finns.
>    ⚠ **`#1267 AC 2` är INTE hemmet för den här kontrollen**, och den här raden lägger ingen
>    förpliktelse där — en issue-AC rider varken §6:s PR-flöde eller §9.2:s agenter, har ingen
>    läsarplikt och inget svep som tittar där. Hemmet är den här raden, **trackad med flit**: led 6:s
>    hela poäng är en mänsklig läsare som inte har det gitignorerade registret.
>
>    **Mätt 2026-08-28, samma svar på varje yta — ingen åldersbaserad radering finns att hänga den
>    på:** Open-Xchange-regelmotorn i STRATO:s webmail kan bara jämföra `Aktuellt datum` mot ett
>    **fast** `YYYY-M-D` och agerar vid **leverans** (åtgärderna är
>    arkivera/omdirigera/kasta/avslå/behåll), alltså varken meddelandeålder eller ett svep över
>    lagrad post. ⚠ **Den avläsningen kan bara personuppgiftsansvarig ta om** — raden förbjuder CC
>    brevlådeåtkomst — till skillnad från de två nedan, som vem som helst kan ommäta ·
>    STRATO:s FAQ för E-postadministrationen
>    räknar upp vad som går att ställa in per konto (namn, lösenord, postfackstorlek, filterregler,
>    alias, spamskydd) och **ingen retention** · den svenska produktsidan beskriver ingen
>    bevarandetid. ⚠ **Gränsen: "beskrivs inte" är inte "existerar inte".** En odokumenterad
>    automatisk rensning i kundpanelen vore ett **annat** fynd med motsatt riktning — för mycket
>    radering mot vårt tolvmånaderslöfte, inte för lite — och den är **omätt**.
> 2. AVV-ledet sa *"som är Klas-åtgärd och aldrig CC:s"* i presens, som om det vore ogjort.
>    **Avtalet är tecknat 2026-01-29 21:15** — *Data Processing Agreement according to Art. 28(3)*,
>    version 3.6. Att teckna förblir Klas, men handlingen är utförd.
> 3. Entiteten stod som **STRATO AG**. Avtalsdokumentet säger **STRATO GmbH**,
>    Otto-Ostrowski-Straße 7, 10249 Berlin. ⚠ **Formen är den part-bärande, samma precisionsstandard
>    som `netcup GmbH` och `Scaleway SAS`,** och den publiceras nu under Art. 13(1)(e) —
>    varumärket hade inte dugt. *Mätt mot avtalsdokumentet självt 2026-08-28. En rå `grep -r`
>    (utan gitignore-filtrering) fann före rättelsen tre förekomster i trädet och ingen fjärde:
>    den här raden med `AG`, och copyns två med `GmbH`. Ingen av dem bar då en registrerad
>    mätning — den här raden är hädanefter den tracked mätningens hem.*
>
> **Läget idag är korrekt för de rader som fortfarande beskriver något planerat, och
> trasigt för dem som inte gör det.** Policyn beskriver ansökningshistorik/
> företagsöversikt och SCB-uppslag som planerade, vilket de är.
> ⚠ **E-postleverantörsraderna beskriver INTE längre något planerat OCH bär fortfarande sin
> markör — de är alltså falska, och skyldigheten är öppen:** armen aktiverades 2026-08-16 medan
> §2.5 punkt 1 bar KVAR. Punkt 1 nedan är hemmet för vilka rader det gäller och varför, och den
> enda som räknar dem.
> ⚠ **Värdraden är den MOTSATTA sortens fall och är AVKLARAD, inte trasig — blanda inte ihop
> dem:** #1199 tog bort dess markör 2026-08-09, eftersom lådan kör
> (`dev.jobbliggaren.se` sedan 2026-08-05) och en markör där hade förnekat en pågående
> drift — samma defekt som en förtidig flip, i spegelvänd form. Koden är
> skeppad till dev, men det finns ingen prod-deploy — policyn styr den *driftsatta* tjänsten. **Flippa aldrig i
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
      **Regenererad 2026-08-28 (#183, STRATO-mottagarstycket): 8 + 8** (rad 37, 50, 82, 92, **95**,
      116, 117, 152 — identiska i sv och en, alla äkta statuspåståenden, ingen falsk träff).
      **Den här gången VÄXTE mängden, och förskjutningen är enhetlig:** det nya lövet är
      STRATO-raden på 95, och de tre raderna under den flyttade **+1** (115→116, 116→117, 151→152).
      Rad 37, 50, 82 och 92 ligger ovanför insättningen och står stilla — inklusive notisraden på
      82 med sina tolv egna hem, som alltså inte behövde röras.
      ⚠ **Ett stycke lades till i `Mottagare av uppgifter` utan att någon spärr fällde**, och det
      är väntat: både e-post- och värdtripwiren är term-scopade (`Scaleway` respektive
      `netcup GmbH`) och itererar inte lövet. Sedan 2026-08-28 har STRATO-raden **en egen spärr** i
      `content-legal-parity.test.ts` med golv, path-paritet och positiv markörpinne. Den tar den
      här inventeringens plats som mekanisk läsare **för just den raden**, aldrig för de övriga sju.
      *(Föregående regenerering, kvar som daterad proveniens: **2026-08-19**, sökhistorik-disclosuren,
      ADR 0060 rad 152 — **7 + 7** på rad 37, 50, 82, 92, 115, 116, 151.)*
      **Vid den regenereringen var mängden oförändrad medan fem av sju rader flyttade, med TVÅ
      OLIKA förskjutningar** —
      och det är den detalj som gör att mängden inte får framräknas. Två insättningar skedde i
      samma PR: sökhistorik-sektionen på `privacy.sections[4]` (18 rader) och dess retentionsrad i
      `Hur länge vi sparar uppgifter` (1 rad, hamnade på rad 113). Rad 37 och 50 ligger ovanför
      båda och står stilla; 64 och 74 ligger emellan och flyttade **+18**; 96, 97 och 132 ligger
      under båda och flyttade **+19**. ⚠ **Notisraden flyttade 64 → 82, och den har flera egna hem** —
      i den här runbooken och i paritetssvitens egen docblock
      (`content-legal-parity.test.ts`), samtliga presens-påståenden eller körinstruktioner, inte
      historik. **Antalet räknas inte här:** mängden regenereras ur egenskapen, aldrig ur ett
      nedskrivet tal. De ommäts i samma PR, **ett kommando per locale eftersom strängen skiljer
      sig** — `grep -n "Notiserna planeras att skickas med e-post" …/sv/content-legal.json` och
      `grep -n "The notifications are planned to be sent by email" …/en/content-legal.json`, båda →
      **82**. (Ett enda svenskt kommando med slutsatsen "båda locales" ger noll på den engelska
      filen: samma halvmätning som §2.6 finns för att förbjuda.) **Egenskapen är "levande radnummer-pekare in i
      `content-legal.json`", och §2.6-mängden är bara ett av hemmen.** ⚠ Den här noten fick
      räknas om en gång: första svepet täckte bara runbooken, och `code-reviewer` mätte bredare
      och hittade ett till — **i just den fil §2.6 utpekar som radens mekaniska
      läsare**, så runbooken sa 82 medan dess egen citerade läsare sa 64.
      ⚠ **Läs inget svep här som uttömmande.** Ett senare svep nådde en yta bortom både
      runbooken och sviten. Svep på egenskapen, inte
      på filen du råkar ha öppen. En enda offset applicerad på hela den gamla mängden hade
      alltså gett tre fel rader. Sökhistorik-sektionen bär avsiktligt **ingen** markör:
      behandlingen är i drift, och en markör där hade förnekat en levande behandling.
      *(Föregående regenerering, 2026-08-16 (#183 FU-2b, EFTER flippen): 7 + 7 på rad 37, 50, 64,
      74, 96, 97, 132. Talet sjönk då med TRE och ingen rad flyttade, av ett enda skäl: rad 47, 75
      och 76 förlorade sina markörer i flippen, och ingen rad lades till eller togs bort — en
      redigering **inuti** en JSON-sträng flyttar ingenting under sig.)*
      *(Dessförinnan, 2026-08-16 (#183 E4, Art. 13(1)(d)-posten): 10 + 10 på rad 37, 47,
      50, 64, 74, 75, 76, 96, 97, 132. Talet steg då med ETT — `security-auditor` Major 4 krävde en
      Art. 13(1)(d)-post för de kontolivscykel-mallar som vilar på Art. 6(1)(f) — och raderna under
      insättningen flyttade ner ett steg: 49/63/73/74/75/95/96/131 blev
      50/64/74/75/76/96/97/132.)*
      Mängden är **körd ur greppen ovan, aldrig framräknad ur den gamla** — se nästa stycke om
      varför det senare inte är en genväg.
      ⚠ **RAD 47, 75 OCH 76 VAR FALSKA OCH ÄR FLIPPADE 2026-08-16 (#183 FU-2b, Klas väg A).**
      Karakteriseringen nedan är `security-auditor`s (#183 E5) och står kvar som **grunden för
      åtgärden**, inte som ett öppet tillstånd. Sektionens ordningsantagande — sajten går live
      först, markörerna flippar då — var falsifierat: **e-posten gick live först** (CC1:s
      registreringsbesök 2026-08-16, `Email:Provider=Scaleway`). Rad **47** (säkerhetsaviseringar
      om kontot) var falsk därför att en `AccountExistsNotice` **faktiskt levererades** under
      besöket; rad **75** påstod att tjänsten inte skickar någon e-post och att inga uppgifter
      lämnas till någon e-postleverantör — en **affirmativ presensförnekelse**, Art. 13(1)(e),
      5(1)(a) och 12(1), aldrig en föråldrad markör under Art. 13(3); rad **76** påstod dels att
      uppgifterna *planeras* lämnas, dels att avtalet säkerställs *innan vi börjar skicka*.
      **Vad flippen gjorde med var och en:** 47 och 75 fick sina markörer strukna och sina
      påståenden satta i presens; 75:s förnekelsemening är **struken helt**, eftersom den inte har
      någon sann presensform; 76 fick markören struken och **avtalsreservationen struken med den**
      — se §2.5 punkt 1:s residual (i), som äger det ledet och är uppdaterad i samma ändring.
      **Rad 82 är SANN och är INTE flippad:** bevakningsnotiserna är samtyckesgrindade med opt-in
      default OFF och ingen notis har skickats.
      ⚠ **RAD 82:s TEXT ÄR DÄREMOT RÄTTAD 2026-08-16 (#183 FU-1, `security-auditor` Major 3) — OCH
      ATT RÄTTA ÄR INTE ATT FLIPPA.** Raden sa *"behandlar e-posten i Paris inom EU"*, vilket är en
      finare kornighet än grunden ger: DPA Art. 11 utfäster **EU-nivå, inte regionsnivå**, och
      "Paris" är härlett ur att `fr-par` är TEM:s enda region. **Markören botar tempus, inte
      kornigheten** — en framtidsutsaga kan vara exakt lika över-precis som en presensutsaga, och
      mot båda står GTS Art. 10:s avtalsrangiga åtkomsträtt utan ortsklausul. Raden bär nu
      regionformen plus avtalets egen utfästelse, och **markörsatsen är ordagrant orörd**.
      **Det skyddade i rad 82 är dess STATUSMARKÖR, inte varje ord i strängen.** Paritetssvitens
      mörka gren binder `/planerat och ännu inte i drift/i`, som satsen fortfarande matchar; raden
      står kvar i mängden ovan, och flippdisciplinen är oberörd. *(Sessionen lämnade först raden
      orörd och läste den som en framtidsutsaga under sin markör. Graderingen föll åt andra hållet,
      och det var rätt: en fix på en delmängd av N är ingen fix.)* **Ommät rad 82 mot lådan före varje flipp** — dess
      grind är en användarreglage, inte en operatörsgrind, så den kan bli falsk utan att någon gör
      något på infrastruktursidan. ⚠ **Rad 82 har sedan 2026-08-16 en MEKANISK läsare:**
      `content-legal-parity.test.ts` kräver markören på samtyckesavsnittets omnämnanden och
      **förbjuder** den på mottagaravsnittets. Sviten faller alltså vid notisernas flipp, precis som
      den föll vid den här — men den ersätter inte ommätningen, den kräver den.
      **Varför "ingen läsare" inte är grunden att låta dem stå:** #1199 flippade värdraden
      2026-08-09, fyra dagar EFTER att Basic auth landade (`aef6a853`, 2026-08-05). Sajten hade
      alltså redan ingen läsare när huset valde att flippa, och commit-rubriken säger det ordagrant.
      Läsarfrånvaro friade inte värdraden och friar inte de här. ADR 0090 D3 är standarden, och
      dess ordning — policyn ärlig FÖRE aktiveringen — är den som inverterades här.
      **Detta är en Major, inte en Blocker:** varje registrerad vars uppgifter nått providern är
      personuppgiftsansvarig själv, och copyn är oläsbar. Graden ändras vid §2.5 punkt 1 led (e):s
      konverterande händelse — **första icke-Klas-konto**, definierat i §2.5 punkt 1
      förutsättning 5:s trigger (b).
      *(Föregående regenerering, 2026-08-15 (#183, providerbytet AWS SES → Scaleway): 9 + 9 på
      rad 37, 49, 63, 73, 74, 75, 95, 96, 131. Talet sjönk då med ETT, av ett enda skäl:
      tredjelandsavsnittets e-poststycke (förra rad 82) är **struket med sin grund** — Scaleway
      S.A.S. är franskt, ingen överföring uppstår, och copyn ska då vara tyst om Kap. V precis som
      värdraden är (senior-cto-advisor bindande 2026-08-15).)*
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
      **Grepa INTE bara på `"planerat och ännu inte i drift"`** — det missar de TVÅ
      retentionsposterna, som bär `(planerat)` utan avslutningsmeningen. *(Raden bar talet **7**
      "mätt 2026-08-15" till 2026-08-16, och `f09755b1` lade till en markörbärande rad dygnet efter.
      Talet togs bort i stället för att räknas om — det är precis den sortens siffra det här stycket
      säger ska **regenereras ur greppet** och aldrig läsas ur filen, så en ersättningssiffra hade
      återinfört defekten en generation senare.)* Den första (organisationsnumret i en annons, #880) nämner
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
- [ ] **2. Avgör vad releasen faktiskt aktiverar** — klasserna nedan, blanda dem
      inte:
      - **Kod-aktiverad:** ansökningshistorik/företagsöversikt — kategorilistans
        ansökningshistorik-punkt, BÅDA retentionsposterna och stycket i "Inga automatiserade
        beslut". *(Identifieras med innehåll, inte radnummer: punkt 1:s mängd är hemmet, och
        raderna flyttar vid varje styckeändring.)*
        Handlers + endpoints + FE är skeppade utan feature-flagga → aktiveras av
        att tjänsten alls går i drift.
      - **Konfigurations-grindad:** SCB (ändamålsavsnittets företagsuppslag + mottagarstycket)
        **och e-postleverantören Scaleway** (samtyckesavsnittet + mottagaravsnittets TVÅ
        e-poststycken; #186 + #1169 + #183) **samt, sedan 2026-08-16, Art. 13(1)(d)-posten om
        säkerhetsaviseringar** i "Säkerhet, drift och produktfunktioner" (#183 E4). *(Innehålls-
        benämningar, inte radnummer — punkt 1 är mängdens hem, och den här bulleten bar sin egen
        kopia av numren tills 2026-08-15.)*
        ⚠ **DEN FJÄRDE YTAN BÄR MARKÖREN MEN VAKTAS INTE AV E-POST-TRIPWIREN, och det är
        avsiktligt i båda leden.** `content-legal-parity.test.ts` itererar `/Scaleway/` över
        katalogen; Art. 13(1)(d)-posten namnger **ingen leverantör** — den redovisar det
        berättigade intresset, vilket är vad artikeln kräver — och faller därför utanför den
        loopen. Att skriva in leverantörsnamnet enbart för att fångas av spärren vore samma
        inversion som golv-resonemanget i den testfilen förbjuder: **en test-assertion får inte
        forma publicerad juridisk copy.** Konsekvensen är att **en flipp som tar de tre
        Scaleway-styckena men lämnar den här posten passerar CI grön** med en falsk
        planerat-mening kvar. Den luckan stängs av punkt 1:s grepp, som är markörernas hem och
        fångar **hela** mängden oavsett vad raderna namnger — kör det, lita inte på sviten här.
        *(Raden räknade mängden här till 2026-08-16 och gick stale i samma andetag som FU-2b:s
        flipp ändrade den; talet bor i punkt 1, med sitt grep.)*
        ⚠ **LUCKAN ÄR MÄTT, INTE HYPOTETISK: den fyrade i FU-2b.** Flippen tog rad 47 därför att
        punkt 1:s grepp namnger den, inte därför att någon svit krävde det — sviten var grön med
        rad 47 oflippad. Nästa flipp (notiserna) möter samma lucka.
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
        ⚠ **E-POSTHALVAN AV DEN INSTRUKTIONEN ÄR FÖRBRUKAD SEDAN 2026-08-16 och kan inte utföras
        igen.** Armen aktiverades utan att grinden passerades, och copyn är därefter flippad för att
        stämma (#183 FU-2b) — **inte** för att grinden passerades. `Email:Provider` är alltså inte
        längre `NullEmailSender` i drift, oavsett vad defaulten säger. **Läs bulleten som SCB-only
        framåt**; e-postraderna har ingen kvarvarande flipp utom notisernas (rad 82), och den
        grindas av ett användarreglage, inte av den här punkten.
        *Raderna 63/74/75/82 namngav Resend, Inc. (USA) till 2026-08-09; #1169 skrev om dem till
        Amazon Web Services EMEA SARL (Luxemburg) med behandling i `eu-north-1`. **Det var en
        korrigering av en falsk motpartsuppgift, inte en flip** — markörmeningen stod kvar i alla
        fyra styckena i båda språken, och armen var fortfarande mörk.*
        ⚠ **2026-08-15 (#183, ADR 0131) skrevs de om igen, till Scaleway S.A.S. (Frankrike,
        `fr-par`) — och den gången ändrades MÄNGDEN, inte bara namnet:** rad 82 (tredjelands-
        stycket) är **struken med sin grund**, så e-posten bär nu **tre** markörbärande stycken per
        språk, inte fyra. Också detta var en motpartskorrigering och **ingen flip** — markörmeningen
        står kvar i alla tre styckena i båda språken, och armen är fortfarande mörk.
      - **Panel-aktiverad (utanför repot) — NY KLASS 2026-08-28 (#183 led 5).** Mottagar-
        avsnittets **STRATO GmbH**-stycke om inkommande post till `kontakt@jobbliggaren.se`,
        i dag klassens enda medlem. **Aktiveringshändelsen är att apex-MX flyttas i STRATO:s
        kontrollpanel** — ingen release, ingen tagg, ingen konfigurationsnyckel i repot, ingen
        deploy, alltså inget repo-event att haka i. Varken §2.5:s predikat (*en providerarm
        som når en extern processor*) eller §2.6:s egen trigger (*första `v*`, eller en release
        som aktiverar en behandling copyn kallar planerad*) fyrar på den. Stycket landade
        2026-08-28 och blev **tyst oklassat**.
        ⚠ **Klassen bockas därför ALDRIG av att releasen inte aktiverar något** — dess händelse
        är inte en release, så punkt 2:s vanliga utfall (*bocka hela sektionen och sluta*) är
        blint för den. **Proceduren bor i `vps-deploy-stack.md` rad 36:s MX-ben**, som är där
        operatören står i det ögonblick handlingen utförs; den här bulleten **klassificerar,
        den bär inte proceduren**. Den mekaniska läsaren för just det stycket är STRATO-spärren
        i `content-legal-parity.test.ts`, och den fäller **bara** en copy-flip utan MX-flytt —
        aldrig det omvända, eftersom det inte finns något repo-event att fälla på.
      - **Användar-aktiverad — NY KLASS 2026-08-28, i samma ändring som den tredje.** Notisernas
        stycke, som bulleten om konfigurations-grindade ovan redan namnger och uttryckligen
        utesluter (*"grindas av ett användarreglage, inte av den här punkten"*) utan att ge det
        en klass. Aktiveringshändelsen är varken kod, en repo-nyckel eller en leverantörspanel:
        **en användare slår på sitt eget reglage.** ⚠ **Klassen säger ingenting om styckets
        sanningsstatus** — den ägs av `security-auditor` och avgörs inte av en triage-etikett.
        Mekanisk läsare: e-post-tripwirens mörka gren i `content-legal-parity.test.ts`.
      ⚠ **"ARMEN ÄR FORTFARANDE MÖRK" ÄR FALSKT SEDAN 2026-08-16, OCH DET ÄR INTE EN TEMPUSFRÅGA**
      (`security-auditor` N-1, 2026-08-16). Armen aktiverades 2026-08-16 utan att §2.5-grinden
      passerades, och den publicerade copyn bär **en affirmativ presensförnekelse av en utlämning som
      har skett**: *"I dagsläget skickar Jobbliggaren ingen e-post, och inga uppgifter om dig lämnas
      till någon e-postleverantör"* (`en`: *"currently does not send any email, and no data about you
      is disclosed to any email provider"*), mot leverantörssidigt mätt `Processed 4 / Delivered 4`
      samma dag. **Det är ett osant sakpåstående om mottagare — Art. 13(1)(e), Art. 5(1)(a),
      Art. 12(1) — inte en föråldrad markör under Art. 13(3).** *(Den här posten kallade det
      "tempus-avvikelse" till 2026-08-16; underskattningen ändrade vad Klas ombads acceptera, vilket
      är hela skälet formuleringen rättas.)*
      ⚠ **FEL-MÄNGDEN ÄGS INTE AV DEN HÄR POSTEN, OCH DEN STOD REDAN RÄTT I REPOT.**
      `deploy/caddy/Caddyfile`:s `basic_auth`-direktiv bär den — trackat, landat i `74cf8a04`, alltså
      före den här ändringen — och **den här punkten räknar inte om den.** *(Posten namngav till
      2026-08-16 en egen, smalare mängd och skopade följd-PR:en efter den. Den utelämnade en rad som
      #183:s egen `f09755b1` införde samma dygn med motiveringen "the arm is dark" — falsk när den
      skrevs. Två trackade hemvister med olika mängd för samma faktum, och den felaktiga styrde
      skopet: exakt det ETT HEM PER TAL finns för.)* **Rad 82 flippas INTE**, och de återstående planerat-meningarna — SCB-vägen och
      ansökningshistoriken — är fortfarande sanna; en vidare läsning skulle göra sanna påståenden
      falska, vilket är den dyra riktningen.
      ⚠ **RAD 82:s SANNING ÄR OMÄTT, och det skrivs ut hellre än antas.** Varje annan siffra i det
      här blocket bär datum och instrument; rad 82:s gör det inte. Grunden är rimlig — notis-e-post
      kräver opt-in plus en matchningskörning, och `Processed 4` är leverantörssidigt **aggregat** som
      inte identifierar mallar — men rimlig är inte mätt.
      **Vad som skulle fastställa den, och det är två tabeller och inte en:** att **ingen rad någonsin
      lämnat `Pending`** i `user_job_ad_matches` respektive `followed_company_ad_hits` — de två
      tabellerna som ÄR notis-armen. ⚠ **Formen är `Pending`, aldrig `sent_at IS NULL`:** i
      claim-then-send-spinen sätts `MarkQueued` **före** utskicket, så en `Queued`-rad kan ha
      genererat ett utskick vars commit föll — det är vad `StrandedMatchReaperJob` finns för. Att
      läsa `sent_at` ensamt mäter alltså i den farliga riktningen.
      *(Raden namngav till 2026-08-16 `email_log`. **Den tabellen finns inte** — noll träffar i
      `src/` och `tests/`, ingen migration; den är ett planerat schema i `BUILD.md` §7 och inget mer,
      så frågan hade returnerat `relation does not exist`. Det är PR:ens och husets egen defektklass:
      commiten omedelbart före den här PR:en heter *"two of its own instruments cannot be run"*, och
      det här stycket lade till ett tredje — värre än den utelämnade mätningen det ersatte, eftersom
      stycket gör en poäng av att varje annan siffra bär instrument och raden därför **läser som
      körbar**. `security-auditor` + `code-reviewer`, oberoende, samma dag.)*
      Tills mätningen är gjord: skyddad från flip på en omätt grund, och det är medvetet den
      försiktiga riktningen.
      ⚠ **Statusordet är `security-auditor`s, inte den här postens.** §2.6:s E5-dom skriver rad 82 som
      **SANN**; det här blocket skriver dess **grund** som omätt, och hon ratificerade den skärpningen
      i FU-2:s fjärde omkontroll. De konvergerar operativt — raden flippas inte — men **domen ägs av
      henne**, och en läsare som vill ändra status går till henne, inte hit.
      ⚠ **Den här punktens egen instruktion är samtidigt OUPPFYLLBAR** — *"flippa styckena först när
      respektive grind är passerad"* kan inte utföras för en grind som redan kringgåtts. Läs den som
      överträdd, aldrig som en väntande ordning; det är samma form som §2.5:s strukna
      *"Ingen flip innan båda är besvarade"*.
      ⚠ **DETTA ÄR INTE BLOCKER I DAG, OCH SKÄLET ÄR EN BINDNING SOM INGEN ADR BÄR.** Enda
      registrerade är personuppgiftsansvarig själv, och copyn ligger bakom `basic_auth` i
      `deploy/caddy/Caddyfile` — en bindning som är **mätt**, inte antagen. Men **ADR 0133 täcker led
      (b) och (c), ADR 0132 täcker registreringsgrinden, och det publicerade läget ligger utanför
      båda** — det lever på deras bindning utan att vara skrivet någonstans. **Det eskalerar till
      Blocker automatiskt** när förutsättning 5:s eskaleringsschema fyrar. ⚠ **Triggermängden
      räknas INTE här** — förutsättning 5 äger alla fyra definitionerna, och den skarpaste är **(d),
      första utskick till annan mottagare än Klas**, eftersom uppgifter når providern vid ett
      utskick. *(Posten räknade "(a), (b) eller (c)" till 2026-08-16. Tre fel i en mening: (a) hade
      redan fyrat och återinför per förutsättning 5 inte Blockern, (d) saknades trots att samma delta
      utpekar den som skarpast, och uppräkningen tillskrev mängden ADR 0132 när ägaren är
      förutsättning 5 — vilket motsäger den här sessionens egen rättelse av ADR 0133:s `Related`.)*
      ⚠ **VÄG B FÖRFALLER NÄR `basic_auth` TAS BORT**, och `Caddyfile`:s egen not är i dag den enda
      hemvist som bär den kopplingen.
      ⚠ **ÅTAGANDET ÄR INFRIAT 2026-08-16 (#183 FU-2b) OCH VÄGVALET ÄR STÄNGT — läs inte den här
      posten som en öppen fråga.** Den löd: copy-halvan levereras i en **följd-PR**, och *"Klas
      äger vägvalet: antingen flippas raderna, eller så recordas det publicerade läget som ett
      uttryckligen bundet accepterat läge i ADR 0133."* **Klas valde väg A 2026-08-16:**
      *"integritetspolicyn kan uppdateras så den stämmer korrekt."* Det är verkställt ovan.
      ⚠ **Följd-PR-formen upphörde med sitt eget skäl, och det är en supersession — inte att någon
      ändrade sig.** `senior-cto-advisor` band splitten på grunden *"copy-ändringen kodar ett vägval
      Klas äger"*, och `security-auditor` gick längre och kallade den **påtvingad** — men **på
      samma grund**, ordagrant: *"och därför inte får verkställas in-block."* Väg A-beslutet
      uppfyller det villkoret. CTO:n ombands binda om frågan innan en rad skrevs och band **en PR**
      (2026-08-16, FU-1): rad 47 är Art. 13(1)(d)-posten, så FU-1 vidgar dess kropp medan FU-2b
      flippar dess slutmening — **samma sträng, och en delning hade lämnat rad 47 falsk i
      mellanperioden i båda möjliga ordningar.** Ingen av granskarnas grund är omgraderad; den
      villkorsklausul de själva skrev ut är uppfylld.
      Kvarstår hos Klas: ingenting i den här punkten. **Publiceringen är en egen handling** och
      grindas av §2.6, inte av att copyn är sann i källan.
      Kvarstående planerat-meningar för behandlingar som fortfarande inte är i
      drift ska stå kvar. Släpper releasen ingen av dem är rätt utfall att **inte
      ändra något**.
- [x] **3. Art. 28 innan personuppgifter når lådan** — **BOCKAD 2026-08-16 PÅ KLAS BESLUT**
      (speglar §2.5 punkt 1).
      ⚠ **Bocken är beviljad av Klas, inte satt av en session.** Filens konvention är att rutan
      bockas av den som **utför** releasen och aldrig för att en förutsättning är levererad
      (blockquoten ovan, och §2.5-noten där en ruta återställdes till `- [ ]` på `dotnet-architect`s
      och `code-reviewer`s grund mot en CTO-bind som sa *"Item 3 keeps `[x]`"*). **Klas övertrumfade
      den konventionen uttryckligen 2026-08-16** med skälet att *"jag vill inte att nästa CC frågar
      mig samma saker, om det redan är avklarat"* — bocken är alltså en **beständighetsmekanism**,
      inte ett releaseutförande. Undantaget är hans att bevilja (§9.6) och gäller **den här punkten**;
      konventionen står oförändrad för filens övriga rutor. *(Rutan bockades, återställdes och
      bockades igen 2026-08-16 — den mellanliggande återställningen var `code-reviewer`s Blocker, som
      var rätt om idiomet och ovetande om Klas beslut.)*
      **LEDEN, mätta 2026-08-16, var och ett med adjudikator:** AVV med netcup GmbH **tecknat
      2026-08-03** (Klas i Customer Control Panel; oberoende mätt mot det genererade dokumentet av
      `code-reviewer`) · *"circle of affected persons"* namnger **rekryterar-kontaktpersoner**
      ordagrant (Klas) · AVV-bilagans underbiträdeslista **läst**: ANNEX 2 namnger tre underbiträden,
      **samtliga inom EU** (Klagenfurt ×2 AT, Karlsruhe DE), så villkoret nedan om ett
      icke-EU-underbiträde **fyrade inte** (Klas läsning, recordad i ROPA:ns värdpost) ·
      ROPA-posterna uppdaterade + **`security-auditor`-sign-off 2026-08-16: SIGNED**, `recruiterNotice`
      omprövad och **länkvägen intakt** (samma sign-off: CLOSED) ·
      ✅ **`ACME_EMAIL` = `klasolsson81@gmail.com`, bekräftat av Klas mot lådan 2026-08-16**
      (`sudo grep ACME_EMAIL /opt/jobbliggaren/deploy/.env`) — **personuppgiftsansvariges egen
      adress**, alltså ingen biträdesrad skyldig och **ISRG (USA) blir inte mottagare av
      användardata**. ⚠ **Fråga aldrig om detta igen:** värdet går inte att mäta ur repot
      (`deploy/.env.example` föreskriver, den mäter inte), så ett svep hittar bara en tom
      platshållare. Klas hade bekräftat det redan före 2026-08-16 och fick frågan igen — **det var
      då bekräftelsen äntligen skrevs ner.** Ändras bedömningen bara om adressen delas, blir
      funktionsbrevlåda eller vänds mot användare.
      ⚠ **OCH BOCKEN SLÄPPER INTE KORPUSET.** Villkoret för att ladda är **Klas uttryckliga
      skriftliga GO** — se punkt 3.5 nedan. Läs aldrig den här bocken, eller något annat urladdat
      led, som tillstånd.
      ⚠ **Och även fullt urladdad släpper punkten INTE korpusladdningen** — det gör punkt 3.5.
      Vad som står framför `JobTech__IngestEnabled=true` bor **där**, inte här.
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
        korpusladdningen. ✅ **LÄST 2026-08-16: ANNEX 2 namnger tre underbiträden, samtliga inom EU**
        (Klagenfurt ×2 AT, Karlsruhe DE) — utlösaren fyrade **inte**, och det absoluta påståendet
        står kvar på en läst bilaga i stället för på tystnad.
        ⚠ **Mottagaravsnittets ingress — den här raden är omvärderad 2026-08-16 och sa tidigare
        motsatsen.** Den påstår i presens *"Med dem har vi personuppgiftsbiträdesavtal"*, och raden
        sa att meningen *"bärs i dag av att ingen listad part behandlar något"* och **blir falsk vid
        (i)**. Båda halvorna är överspelade: de listade **biträdena** är netcup GmbH och Scaleway SAS
        — SCB är i copyn uttryckligen deklarerad som självständigt personuppgiftsansvarig och JobTech
        som källa, inte mottagare. netcups AVV är **tecknat 2026-08-03** och Scaleways gäller
        automatiskt (GTS Art. 3, mätt 2026-08-15). **Meningen är alltså SANN i dag, och blir det inte
        först vid (i) — den bärs numera av avtal i stället för av tomhet.** Kvar att bevaka: att en
        *ny* biträdespart aldrig hinner in i uppräkningen före sitt avtal.
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
- [x] **3.5 KORPUSLADDNINGEN — KLAS UTTRYCKLIGA SKRIFTLIGA GO, och ingenting annat.**
      **Detta är hemmet för villkoret som grindar `JobTech__IngestEnabled=true`**, och
      vakterna i `src/`, `tests/` och runbookerna pekar hit.
      **Villkoret är ett BESLUT, inte ett härledbart tillstånd** (Klas 2026-08-16, på
      `code-reviewer`s förslag): *inga rekryterar-kontaktposter landar på lådan förrän Klas ger ett
      uttryckligt skriftligt GO.* Ingen urladdad grind, ingen bockad ruta, ingen stängd issue och
      ingen mätning uppfyller det.
      ⚠ **Varför formen är ett beslut — fyra tillståndsformer föll öppna på en enda dag
      (2026-08-16), var och en när sitt delvillkor laddades ur:**
      *"until B-1 is closed"* föll när B-1 stängde · *"until the CORPUS GATE is ticked"* ärvde
      defekten inom timmar när Art. 28-leden laddades ur · *"until the item authorises it"* var
      falsk vid ankomst, för punkten har en binär och inget `authorises`-tillstånd · *"while #1240
      is open"* faller om issuen stängs som duplicate, superseded eller av en grann-PR:s squash,
      **utan att någon rättslig grind rört sig**. Ett beslut kan inte inferens-uppfyllas.
      **Vad som ÄNDÅ måste vara sant när GO:t ges** — sammanhang, inte villkorsmängd, och det som
      gör GO:t informerat snarare än formellt: Art. 28-punkten ovan (bockad) ·
      [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201) gate M-7 ·
      [#1199](https://github.com/klasolsson81/jobbliggaren/issues/1199):s
      övriga led (policy-copy, ROPA, `BUILD.md`, paritetstestet) ·
      [#1240](https://github.com/klasolsson81/jobbliggaren/issues/1240), som äger själva laddningen
      och bär den mätta grindlistan i sin kropp.
      ⛔ **M-7 KONVERTERAR TILL `Blocker` VID FÖRSTA RIKTIGA ANVÄNDARDATA — `security-auditor`s dom
      2026-08-17, och den är hennes att sätta.** Den här raden påstod motsatsen till dess.
      #1201:s eskalering är en **disjunktion med två armar**: *"M-7 becomes a `Blocker` if ADR 0123
      is still **ungranted OR unmitigated** at first real user data"*. Hennes tre grunder, var och
      en tillräcklig: (1) **`unmitigated` är mätt SANN** — de två mitigeringarna (egen
      automationsnyckel med `restrict,command=,from=`; `Cmnd_Alias`-avgränsning) finns bara som
      prosa som beskriver dem som obyggda, medan `vps-base-hardening.md`:s provisioneringssteg
      alltjämt sätter `jpadmin ALL=(ALL) NOPASSWD:ALL`. (2) ⚠ **Beviljandet ger ingen täckning vid
      utvärderingsögonblicket:** acceptansen gäller uttryckligen bara *"while the box carries no
      real user data"*, och M-7 utvärderas **vid** riktig användardata — **Klas beviljade
      2026-08-16**, så bokstavligt är `ungranted` urladdad, men funktionellt täcker beviljandet
      inte det tillstånd som bedöms. *(Datum och adjudikator står här med flit: ADR 0123 är
      gitignorerad och alltså oläsbar för en granskare utan lokala docs, och den här punktens egen
      konvention kräver adjudikator och datum.)* (3) **båda M-7-benen är
      overifierade på `host-detection.md`:s verifikationsrader**, så scope-gränsens utgångsvillkor
      går inte att verifiera.
      ⚠ **ATT BYGGA DE TVÅ MITIGERINGARNA RÄCKER INTE.** Hennes gradering av vad som krävs, och
      den är inte en senare läsares att härleda: **risken i ADR 0123 mätbart reducerad** (egenskapen
      — se omformulerat krav (1) nedan, `security-auditor` 2026-08-17), **OCH ett NYTT
      uttryckligt Klas-beviljande som täcker tillståndet MED riktig användardata** (det nuvarande
      upphör av egen kraft), **OCH båda M-7-benen levererade OCH verifierade på
      `host-detection.md`:s verifikationsrader**. ⚠ **Villkoret ställs på FÖRMÅGAN, aldrig på
      issue-nummer:** raden sa till 2026-08-17 *"#196 + #198 levererade"*, och **#196 är stängd
      sedan 2026-08-08** — ett av två led läste alltså som uppfyllt vid inspektion, i samma punkt som
      absolutet som säger att ingen stängd issue uppfyller något. ADR 0050:s daterade not
      2026-08-10 hemmar dessutom **båda** benen hos **#1201**, på Klas-beslut 2026-08-06 med skälet
      utskrivet: *"att lämna pekaren mot en stängande issue hade pensionerat skyldigheten av
      misstag"*. Behövs ett nummer är det **#1201**. Detta hör till det informerade GO:t.
      ⛔ **KRAV (1) VAR INTE UPPFYLLBART SOM TIDIGARE FORMULERAT — mätt 2026-08-17.** Båda
      mitigeringarna är **void as written**. Derivationen har **ett** hem —
      `vps-base-hardening.md` §11. **`security-auditor` omformulerade kravet 2026-08-17**; den
      gällande formen står i blockquotens led (1) nedan, och den är hennes text (§9.6).

      **GO:T RECORDAS HÄR NÄR DET GES — med adjudikator, datum och var det gavs**, samma form som
      punkt 3:s led. En bock utan upphovsman visar ingenting, och Art. 5(2) kräver att efterlevnad
      är *visbar*; att det inte är teoretiskt mäter den här filen själv — punkt 3:s ruta bockades,
      återställdes och bockades igen under ett dygn.
      > **GO givet av:** Klas Olsson · **Datum:** 2026-08-17 · **Var:** Claude Code-session, i svar
      > på en `AskUserQuestion` som ställde valet mellan full ingest, en variant utan
      > rekryterarkontakter, och att bara verifiera bakgrundsjobben först. Klas valde full ingest.
      > **Vad GO:t uttryckligen omfattar:** `JobTech__IngestEnabled=true` på lådan, alltså både
      > jobbannonser och **deklarerade rekryterarkontakter** (`application_contacts` →
      > `AdContact`), på den registrerade tio-minuterskadensen.
      > **M-7 var namngiven i frågan och är därmed del av det informerade GO:t.** Klas tillägg i
      > samma tur, som faktapremiss och inte som grindavgörande: rekryterarkontakter bär aldrig
      > personnummer utan namn, roll, e-post och telefon, och uppgifterna är redan publicerade på
      > Platsbanken. Premissen är mätt mot koden — `PlatsbankenJobSource.MapContacts` anropar
      > `AdContact.TryCreate(name, role, email, phone, Declared)` och har inget
      > personnummerfält. **Att uppgifterna är publika gör dem inte till icke-personuppgifter**,
      > och det är därför Art. 14-notisen levererades 2026-08-16 i stället för att avfärdas; den
      > punkten är bockad ovan och tas inte upp igen.
      > ⚠ **Vad GO:t INTE avgör:** om M-7:s konvertering får accepteras eller måste byggas bort
      > först. `security-auditor` äger den graderingen och den frågan ställdes till henne, inte
      > till Klas en gång till.
      > ⛔ **HENNES SVAR 2026-08-17: `JobTech__IngestEnabled=true` FÅR INTE SÄTTAS ÄNNU.** GO:t står
      > kvar som giltigt affärsbeslut; det som är blockerat är **lådhandlingen**, inte GO:t, inte
      > den här punkten och inte dokumentarbetet.
      > **Klausulen utlöses av rekryterarkontakter.** Art. 33 löper på *personuppgiftsincident*
      > (Art. 4(12)) definierad över *personuppgifter* (Art. 4(1)) — aldrig över kontoinnehavare.
      > Skillnaden ligger i **triggerformuleringen**, inte i dokumenten: grindraden, §6b och
      > `security-auditor`s charter säger alla *"riktig data"* där de anger vad som utlöser, medan
      > ordet *user* står i #1201:s AC. Att samma dokument på andra ställen talar om
      > *användardata* är just poängen — §6b skriver acceptansens scope som *"användardata"* och
      > triggern som *"riktig data"* i samma avsnitt, och säger uttryckligen att triggern är samma
      > text som grindraden. Räkna inte hemmen här; det talet underräknade redan en gång.
      > Konverteringen har **ingen acceptansväg**: §9.6 stänger både (2) och (3) för en
      > GDPR-Blocker, och M-7:s grund är Art. 32(1)(b)/33/5(2).
      > ⚠ **Ads-only var ingen väg runt.** Tier A strippar e-post och telefon ur annonstexten men
      > når inte namn (ingen NER, ADR 0106 D5), och `job_ads.organization_number` **är ett
      > personnummer för en enskild firma**. Varianten utan rekryterarkontakter hade utlöst samma
      > klausul.
      > **Tre kumulativa krav före flippen**, alla mätta 2026-08-17 och ingen av dem uppfylld:
      > (1) **RISKEN I ADR 0123 ÄR MÄTBART REDUCERAD — egenskapen, aldrig en namngiven kontroll.**
      > ⚠ **Omformulerat av `security-auditor` 2026-08-17.** Den tidigare formen (*"båda ADR
      > 0123-mitigeringarna byggda — egen automationsnyckel med `restrict,command=,from=`, och
      > `Cmnd_Alias`-avgränsning av `jpadmin`:s NOPASSWD"*) namnger två kontroller som är **void as
      > written**: de vilar på en inkommande automationsaktör som inte finns. Derivationen har
      > **ett** hem — `vps-base-hardening.md` §11 — och återges inte här.
      > **Egenskapen som ska hålla:** *stöld av den enda SSH-nyckeln ger inte i sig root på lådan.*
      > I ADR 0123:s egna ord: *icke-interaktiv drift kräver **ingen prompt**, inte **obegränsad
      > root*** — och de två får inte längre vara samma sak.
      > **Ingen mekanism namnges här, avsiktligt.** Kravet är ställt på egenskapen så att det inte
      > kan bockas av att en kontroll byggs som inte bär den; vilken mekanism som helst som bär
      > egenskapen räknas.
      > ⛔ **Två mekanismklasser är uttryckligen UTESLUTNA och får inte byggas för att bocka den
      > här punkten — uteslutningen gäller KLASSEN, aldrig en mätt mängd.** En kurerad delmängd
      > faller utanför en mängdmätning, men inte utanför det här.
      > **(a) En `Cmnd_Alias` över någon delmängd som lämnar lådan driftbar.** Grunden är inte att
      > operatörens *nuvarande* sudo-mängd är rot-ekvivalent — det är den, mätt 2026-08-17, och §11
      > bär kommandot som regenererar mätningen — utan att **varje driftbar delmängd behåller minst
      > en rot-ekvivalent medlem**, och att `sudo`-kommandobegränsning bygger en gräns på
      > **integritetsaxeln** (vad som får ändras) medan risken ADR 0123 namnger ligger på
      > **konfidentialitetsaxeln** (root läser masternyckeln ur processminnet och ur en
      > `0400`-fil på tmpfs). Derivationen har **ett** hem —
      > `vps-base-hardening.md` §11 — och återges inte här.
      > **(b) En separat automationsnyckel** — ingen aktör att ge den till.
      > Att bygga någon av dem uppfyller inte kravet och gör posturen **sämre**: en kontroll som
      > *läser* som en privilegiegräns utan att vara en är värre än det ärliga `NOPASSWD:ALL` den
      > ersätter.
      > ⚠ **En read-only-delmängd är INTE en gräns här — den är det värsta fallet.** Den tar bort
      > varje skrivväg och inte en enda läsväg, och en läsning är precis så nyckeln lämnar lådan
      > (`host-detection.md` §5:s D1-drill läser den filen med `sudo dd`). På
      > konfidentialitetsaxeln finns ingen gräns att bygga så länge lådan är driftbar; det som
      > vore kvar är inte en avgränsning av `NOPASSWD` utan att operatörens sudo tas bort.
      > Derivationen står i §11.
      > **Det som faktiskt skulle reducera risken är en modell där root inte håller en läsbar
      > masternyckel** — ett ADR-beslut med mätning och min signatur, aldrig en bockad punkt här.
      > Båda mekanismerna som vägts för det är mätt uttömda på den här värden 2026-08-09
      > (`master-key-ops.md`).
      > **Hur punkten laddas ur:** en mätning på lådan av att egenskapen håller — instrument,
      > datum, adjudikator, `host-detection.md` §7:s form — **plus `security-auditor`s signatur**.
      > Ingen byggd artefakt, ingen stängd issue och ingen bockad ruta laddar ur den.
      > **Status 2026-08-17: OUPPFYLLD, och ingen kandidatmekanism är namngiven.**
      > `restrict,pty,from=` är hygien och inte den här egenskapen (§11).
      > (2) **ett NYTT Klas-beviljande som täcker tillståndet MED riktig data** — det nuvarande
      > upphör av egen kraft vid gränsen, eftersom det gäller *"while the box carries no real user
      > data"*. **Den frågan ställs när (1) och (3) är klara, inte före.**
      > ⚠ **OCH (2) KAN INTE ERSÄTTA (1) — `security-auditor` 2026-08-17.** Ett nytt beviljande är
      > fortfarande **krävt**, men det är inte mätt **tillgängligt**: ADR 0123 restes som en Major
      > *utan* GDPR-implikation och gick §9.6 (2). I tillståndet MED riktig data har samma fynd en
      > direkt Art. 32(1)(b)-implikation (root läser masternyckeln, masternyckeln packar upp varje
      > DEK), så vägen blir §9.6 (3) — som kräver bäraravsaknad. Med korpuset laddat finns bärare
      > fyndet **når** (rekryterarkontakter; `job_ads.organization_number` för enskild firma), så
      > bindningen faller. **Följd: risken ska REPARERAS före riktig data, inte accepteras igen.**
      > (3) **båda M-7-benen verifierade på `host-detection.md` §7** — insamlingen är verifierad,
      > och **väcknings- och paging-kedjan är sedan 2026-08-17 mätt ända fram** (PR
      > [#1374](https://github.com/klasolsson81/jobbliggaren/pull/1374): **fyra** §7-rader urladdade
      > mot Klas läsning hos expectern, korroborerad mot lådans journal; D5 demonstrerade
      > **tystnadsarmen**, som aldrig hade visats). ⚠ **Fyra, inte fem** — den femte
      > `Discharged 2026-08-17`-raden i §7 är key-tmpfs-raden och kom i **PR #1370**
      > (`5462f8d9`), en låd-sidig `sudo dd`-drill och ingen expecter-läsning. #1374:s egen
      > commit-text säger *"four M-7 rows discharge at the expecter"*. Femman stod i
      > sessionsstaten och ärvdes hit omätt. Det är den kedjan som gör Art. 33:s frist datbar — en auditd-regel som
      > skriver till en logg ingen läser producerar ingen medvetenhet. ⚠ **Kravet är ändå INTE urladdat:**
      > två §7-rader står kvar, **reboot-överlevnad** (Klas att auktorisera — kräver omstart av
      > produktionslådan) och **baseline-noise**.

      ⛔ **FLIPPEN ÄR UTFÖRD 2026-08-17, OCH DEN UTFÖRDES MOT BLOCKERINGEN OVAN.** Raden ovan säger
      att inget av de tre kraven är uppfyllt; det står kvar för att det var sant när det skrevs och
      är sant än. Det som ändrades är inte kravbilden utan **vem som bär risken**.
      **Flippen utförd av:** Klas Olsson som **personuppgiftsansvarig** · **Utförd:** 2026-08-17 ·
      **Plats:** `/opt/jobbliggaren/deploy/.env` (`JOBTECH_INGEST_ENABLED=true`), med skälet skrivet
      **inline i samma fil** — den är lådans, inte repots, och därför är den citerad och inte pekad på.
      ⚠ **`security-auditor`s M-7-gradering står oförändrad och är HENNES** (§9.6: severity tillhör
      den agent som rapporterade fyndet, och en senare läsare omgraderar den inte). Acceptansen är
      **personuppgiftsansvariges** — den går alltså varken via §9.6 (2) eller (3), och åberopar ingen
      bäraravsaknad. **Läs därför inte hennes gradering som en öppen grind, och stäng inte ingesten
      igen** på den grunden.
      ⛔ **OCH LÄS INTE DEN HÄR PUNKTEN SOM EN §9.6-ACCEPTANS — den är det inte, och skillnaden är
      inte kosmetisk.** §9.6 (3) kräver Klas beviljande **plus `security-auditor`s signatur**, ett hem
      i **ADR eller CLAUDE.md-uppdatering**, och en skriven lapse-trigger. **Ingen signatur finns, och
      det här är en runbook.** Vad punkten gör är att **recorda en handling som personuppgiftsansvarig
      utförde** — inte att bevilja den efterhand.
      ⚠ **ACCEPTANSEN TÄCKER FLIPPEN, ALDRIG M-7:s KONVERTERING — och de två meningarna hör ihop, så
      glesa aldrig ut dem.** Satsen som säger att konverteringen **saknar acceptansväg** (§9.6 stänger
      både (2) och (3) för en GDPR-Blocker) står kvar oförändrad i blockquoten ovan, på raden som
      börjar `Konverteringen har` — **den citeras vid sin text och inte med ett radavstånd**,
      eftersom ett sådant tal ruttnar vid varje redigering och redan mätts fel en gång i den här
      punkten. ⚠ **Ankaret är avsiktligt ett fragment, och skälet är mätt:** målraden bär
      fetstilsmarkörer mitt i meningen, så ett markup-fritt svep på **hela** meningen ger noll
      träffar. Ankaret är därför ett fragment som undviker markörerna.
      De två påståendena motsäger inte varandra just för att det här inte är en acceptans; risken är
      en senare redigering som skiljer dem åt och låter recordet läsas som om det täckte
      konverteringen också (`security-auditor` 2026-08-17).
      **Om Klas vill att beslutet ska stå som en formell §9.6-acceptans krävs ADR + hennes signatur —
      den frågan är eskalerad till honom, inte avgjord här** (`code-reviewer` 2026-08-17; §9.2 hindrar
      varje subagent från att fråga honom). ⚠ **`security-auditor`s eget svar, givet i omkontrollen
      2026-08-17: nej — signera inte, och be henne inte signera.** §9.6 (2)/(3) är vägar för en
      *session* att disponera ett *agentfynd*; det som skedde här är att personuppgiftsansvarig fattade
      ett beslut om sin egen behandling (Art. 24(1)) — annan aktör, annan handling. En formell
      §9.6 (3)-acceptans vore dessutom **otillgänglig**: bindningen kräver bäraravsaknad, och den
      faller på de 532 rekryterarna. **Nuvarande inramning — ett record av en ansvarshandling — är den
      korrekta.**

      ⛔ **ART. 14(3)(a)-KLOCKAN GÅR SEDAN 2026-08-17, OCH FRISTEN ÄR `2026-09-17`**
      (`security-auditor` 2026-08-17, M1). Recordet ovan säger uttömmande vad som **ligger** på lådan
      och ingenting om vad som är **skyldigt** — det är den lucka den här raden stänger. Tre led,
      och de hänger ihop:
      - **Fristen:** en månad från första registreringen av personuppgifterna, alltså **2026-09-17**
        för de 532 deklarerade rekryterarkontakterna.
      - **Art. 14(5)(b):s mitigering kräver att notisen är *allmänt tillgänglig*** — och §2.5:s egen
        mätning 2026-08-16 säger att den **inte** är det (apex svarar `000`, dev `401 Basic` på varje
        väg). En blackholad `recruiterNotice` bär alltså inte undantaget.
      - **Ingen av §2.5 förutsättning 5:s Blocker-triggrar (a)–(d) fyrar på 532 rekryterare.**
        Premissen *"den publicerade rättighetskanalen har därmed ingen läsare"* skrevs 2026-08-16
        och ändrades dagen efter.
      **Klas väljer väg** — direkt tillhandahållande, 14(5)(b) med notisen gjord allmänt tillgänglig
      före fristen, eller ett dokumenterat beslut på annan grund. **Beslutet är hans; datumet är
      förordningens.** Konverterar till Blocker **2026-09-17**, eller tidigare om copyn blir publik
      medan brevlådan är blackholad.
      **RESIDUALEN — MÄTT PÅ LÅDAN 2026-08-17 ~16:00Z, OCH DEN ÄR INTE NOLL.** En tidigare läsning
      samma dag gav noll deklarerade kontaktposter; den togs **innan strömmen hunnit köra** och är
      överspelad. Mätt efter ~50 minuters ström (`*/10`), alltså **före** den första fullbackfillen
      02:00 UTC: **822 kontaktposter över 462 av 668 annonser — 532 `Declared`, samtliga med namn,
      och 290 `ExtractedFromBody`, ingen med namn.** Att exakt de deklarerade bär namn är väntat och
      inte ett fynd: Tier A når e-post och telefon men aldrig namn (ingen NER, ADR 0106 D5).
      Talen är daterade och avser **beslutsögonblicket**; de växer med varje ingest. Regenerera:

      ```bash
      sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "
      WITH e AS (SELECT a.id, c FROM job_ads a CROSS JOIN LATERAL jsonb_array_elements(a.contacts) AS c)
      SELECT count(*) AS entries, count(DISTINCT id) AS ads_with_entries,
             (SELECT count(*) FROM job_ads) AS ads_total,
             count(*) FILTER (WHERE c->>'Origin' = 'Declared') AS declared,
             count(*) FILTER (WHERE c->>'Name' IS NOT NULL) AS with_name,
             count(*) FILTER (WHERE c->>'Origin' = 'Declared' AND c->>'Name' IS NOT NULL)
               AS declared_with_name FROM e;"
      ```

      ⚠ **`declared_with_name` är med av en anledning:** meningen ovan är en **korstabell**, och tre
      marginaler belägger den inte — 500 + 32 ger samma `with_name`. Instrumentet returnerar bara
      heltal och skriver aldrig ut ett namn, en adress eller ett telefonnummer.

      **`job_ads.organization_number` är ett SEPARAT fält och namnges som ett** — inte som en broms.
      Det **är** personnummer-format för en enskild firma (#841), och den formen försvinner inte av
      att den mäts. Mätt ~17:10Z mot **råvärdet**: **687** annonser bär org.nr, **noll
      personnummer-formade** — och noll som ens avviker från tio rena siffror. Nollan är en egenskap
      hos **den population som låg där då**, aldrig en strukturell garanti, och den ska mätas om och
      inte ärvas. Antalet växer med varje ingest: **652 (~16:00Z) → 676 (~16:50Z) → 687 (~17:10Z)**,
      egenskapen oförändrad. Paret står kvar med flit — det **visar** att talet förfaller medan
      egenskapen håller, i stället för att påstå det.
      ⚠ **DE TVÅ FÖRSTA TALEN TOGS MED ETT FAIL-OPET INSTRUMENT, och det står här i stället för att
      städas bort** (`security-auditor` M2a + `code-reviewer` Major B, oberoende, 2026-08-17). Den
      förra queryn normaliserade med `regexp_replace(… '[^0-9]' …)` **före** mätningen och dödade
      därmed metodens **fail-safe-ben**: `556012-5790` ger `IsPersonnummerShaped() == true` i
      produktion men blev tio rena siffror med trean `6` och räknades som org.nr-formad. Att svaret
      ändå var rätt beror på populationen, inte på instrumentet — **`not_ten_raw_digits = 0`**, mätt
      samtidigt. Ett fail-opet instrument som råkar ha rätt är fortfarande ett fail-opet instrument.
      ⛔ **Regenerera med DEN HÄR queryn, aldrig med en egenhändigt skriven** — kolumnen **är** ett
      personnummer för en enskild firma, och den naturliga formen (`SELECT organization_number …`)
      skriver ut personnummer i en terminal på lådan. Den här returnerar tre heltal och rör aldrig ett
      värde. Den testar **råvärdet**, eftersom kolumnen bär JobTechs sträng orörd
      (`JobAdFacets.Normalize` är `value.Trim()`, aldrig `OrganizationNumber.Create`) och
      produktionens egen visningsgräns läser just den strängen
      (`DisambiguateEmployersQueryHandler` → `FromTrusted(...).IsPersonnummerShaped()`). Predikatet
      speglar **alla tre** benen i `OrganizationNumber.IsPersonnummerShaped()` — inte längd, inte
      rena ASCII-siffror, eller tredje tecknet `< '2'` — och `[0-9]` är den smalaste klassen, så varje
      avvikelse kan bara flagga **fler** rader, aldrig färre. Ingen `::int`-cast: en missformad rad
      skulle kasta `invalid input syntax` och ge noll svar i stället för en rapport.

      ```bash
      sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "
      SELECT count(*) AS orgnr_rows,
             count(*) FILTER (WHERE organization_number ~ '^[0-9]{10}\$'
                                AND substring(organization_number,3,1) >= '2') AS shape_orgnr,
             count(*) FILTER (WHERE organization_number !~ '^[0-9]{10}\$'
                                OR substring(organization_number,3,1) < '2')  AS shape_personnummer
      FROM job_ads WHERE organization_number IS NOT NULL;"
      ```

      ⚠ **KLAS-BESLUT 2026-08-17 — SCB-SYNKEN PAUSAS PÅ LÅDAN, OCH LÅDAN SKA INTE HA NÅGOT
      CERTIFIKAT.** **Beslut av:** Klas Olsson · **Datum:** 2026-08-17 · **Plats:** CC-session,
      recordat här.
      **Skälet är att arbetet är kastat:** SCB byter självt till API om 1–2 månader, så cert-infra
      på lådan hinner aldrig löna sig. **Marginalen som avstås är namngiven och accepterad:** de
      allra nyaste företagen, och de som avregistrerats sedan extraktet. Synken kan i stället köras
      live i CC-chatten en lördag om behovet uppstår.
      ⛔ **DETTA ÄR REDAN ÖNSKAT TILLSTÅND — BYGG INTE CERTVÄGEN OCH "LAGA" INTE DET SAKNADE
      CERTET.** Mätt 2026-08-17, fyra oberoende led: `ScbRegister:Enabled` är `false` i båda hemmen
      (`src/Jobbliggaren.Worker/appsettings.json` och C#-defaulten på `ScbRegisterOptions`) ·
      **noll** `ScbRegister`-nycklar under `deploy/` (regenerera: `grep -rn "ScbRegister" deploy/`,
      förvänta exit 1) · **noll** SCB-nycklar i lådans `.env` (regenerera:
      `sudo grep -ci scb /opt/jobbliggaren/deploy/.env`) · **inget certifikat på lådan**
      (`ScbClientCertificateProvider` läser OS-certarkivet, som bara finns på Klas maskin).
      Jobbet är alltjämt registrerat på `0 6 * * 6` och **no-op:ar** när `Enabled=false`
      (`ScbCompanyRegisterRefresher`s tidiga retur — *"ingen SCB-anrop, inget cert"*), så schemat
      håller sig drift-fritt mot `RecurringJobIds`-allowlisten utan att någonting körs.
      ⚠ **Varför en felaktig flipp är dyrare än den ser ut** (`security-auditor` 2026-08-17, m2).
      Utan cert kan `ScbRegister:Enabled=true` inte lyckas — `ScbClientCertificateProvider` är
      fail-loud på `CertThumbprint`. **Och Workern bär en GDPR-retentionkontroll som inte har med
      SCB att göra:** `JobAd.Archive()` sätter `Contacts = null`, och `ExpireJobAdsJob` rensar samma
      kolumn via `ExecuteUpdateAsync` — det är Art. 5(1)(e)-vägen som gör att en arkiverad annons
      inte behåller rekryterarkontakter (**mätt i koden 2026-08-17**, båda skrivarna). Slutar
      Workern fungera stannar den sweepen **tyst**, och med korpuset live är det just den population
      som växer.
      **Felläget är mätt, och det beror på INPUT** (`security-auditor` 2026-08-17, i
      `DependencyInjection.cs`s registreringsgren):
      - `Enabled=false` → tidig `return services;`, inget kast. **Lådans nuläge.**
      - `Enabled=true` **utan** thumbprint → kastet ligger som **rak kod i registreringsmetoden**, så
        det fyrar när **`AddScbCompanyRegister(...)`** anropas, alltså **före `builder.Build()`**.
        Hosten startar aldrig, och `ValidateOnBuild` är irrelevant eftersom `Build()` aldrig nås.
        ⚠ **Grinden heter INTE `AddInfrastructure` — och skillnaden är operativ, inte namnpetig.**
        Modulen är Worker-only och dess egen doc säger *"deliberately NOT part of
        `AddInfrastructure`"*; Workern drar över huvud taget inte in `AddInfrastructure`
        (`Worker/Program.cs`: *"Worker drar INTE in AddInfrastructure (HTTP-fri, ADR 0023)"*), och
        enda anropsplatsen i `src/` är Api:ts `Program.cs`. **Alltså dör bara Workern av en felaktig
        flipp — aldrig Api:t**, vilket är precis den skillnad en operatör läser stycket för att
        bedöma.
        ⚠ **Det är den realistiska olyckan här:** lådan bär noll SCB-nycklar, så ett blankt
        `ScbRegister__Enabled=true` har ingen thumbprint.
      - `Enabled=true` **med** thumbprint men utan cert → registreringen går igenom och `Load()`
        kastar först när den typade klienten resolvas, alltså vid jobbinvokation. Det är det fall
        `Program.cs`s `ValidateOnBuild=false`-not beskriver.
      *(En tidigare version av den här raden hävdade att felläget var oprövat, och en ännu tidigare att
      det alltid var DI-registreringen. Båda var fel; det här är mätningen.)*
      En obockad ruta här betyder *"GO ej givet"* och ingenting annat — till skillnad från filens
      övriga rutor, där en obockad ruta inte får läsas som "inte levererat" (blockquoten ovan).
      ⚠ **Och en bockad ruta här är inte heller tillstånd i sig** — den är ett **record av** GO:t.
      Vakternas absolut gäller även den här rutan, **i punktens egen vidaste form** — den står
      i punktens beslutsmening ovan: *ingen urladdad grind, ingen bockad ruta, ingen stängd issue och ingen mätning
      uppfyller GO:t.* Det som auktoriserar är GO:t; rutan registrerar bara att det gavs.
      ⚠ **CITERA ALDRIG ABSOLUTET SMALARE ÄN SÅ — och gör inga påståenden om vakternas antal
      eller inbördes likhet här.** Den här raden har smalnat av absolutet två gånger, och båda
      gångerna följde felet samma mekanik: omskrivningen tillfogade ett nytt **kvantifierat
      påstående om vakterna**, och det var påståendet som fallerade — aldrig absolutet självt.
      Först en form som saknade två av medlemmarna, sedan ett uniformitetspåstående som mättes
      falskt. **Kvantifiera därför ingenting här.** Absolutet i sin vidaste form står ovan; hur
      många vakter som finns och hur de formulerar sig räknas **där de bor**, inte i den här
      punkten. En rad som beskriver vakterna är en rad som ruttnar när en vakt ändras.
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
      §2.5.** Riskaccepten är **bunden till besökets två konton**, båda hållna av den
      personuppgiftsansvarige själv, och den täcker **registreringsgrinden — inte villkoren
      nedan**.
      **Triggern är den första konfiguration utanför `Development` som
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
      åstadkom. **(b) gör det inte:** Art. 30-posten för konto/auth bärs av ingen annan
      mekanism.
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
      - **(b) ROPA:n måste bära en behandling för användarkontot/autentiseringen** (Art. 30(1)).
        Registret är gitignorerat (ADR 0072) och speglar (#1040), så skyldigheten bor här.
        **Det är plikten som står här, aldrig registrets tillstånd** — ett trackat påstående om en
        gitignorerad fils innehåll kan varken CI, en PR-granskare eller en parallell session
        verifiera. Triggern fyrade **2026-08-16**, inte i framtiden: villkoret var alltså öppet
        under det fönstret. Villkor (a) står kvar — 5.5 är inte urladdad.
      Bocka aldrig 5.5 på att §2.5 är ogrindad — det är två olika trigger.
- [ ] **6. Tidsordning — två olika fall, blanda dem inte:**
      - **(a) Första prod-taggen:** flippen deployas **samtidigt** med
        aktiveringen. Förhandsinformation är då varken möjlig eller krävd — men läs (b) nedan
        först: finns registrerade konton redan, är det (b) som gäller, inte den här punkten.
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

> **OBS om ADR-referenserna ovan:** ADR 0071+ är **gitignorerade** (CLAUDE.md
> §6.5) och finns bara i huvudkopian — alltså osynliga för CI, för en
> PR-granskare och för en parallell CC-session, precis som ROPA-filen i punkt 8.
> Därför är de lastbärande citaten **inlinade ordagrant** i punkterna ovan
> ("unlawful-by-transparency-defect until the policy is honest", "a new purpose
> section under 6(1)(b)", "prior to that further processing"): sektionen ska stå
> självständigt utan sina källor. Citaten finns kvar för Klas' egen
> revisionskedja, inte som något en granskare kan följa.

---

## 2.7 HÅRD GRIND: riv dev-verktygen före första riktiga användare

**Hemvist för avvecklingen av `/api/v1/dev/*`.** Skriven i samma PR som gjorde
`reset-my-data` nåbar på lådan (Klas-direktiv 2026-08-27), därför att ett verktyg vars
borttagning är ingens uppgift är ett verktyg som följer med till produktion.

**Varför det är en grind och inte en städpunkt:** `reset-my-data` är en **destruktiv**
operation, och `confirm-email` är en **oautentiserad** seam som tvångsbekräftar en
e-postadress. Ingen av dem får finnas när riktiga användare gör det.

**Ordningen är inte godtycklig — stäng av först, riv sedan.** Ett avstängt verktyg är
overksamt inom en omstart; en halvriven kodbas är inte.

1. **Stäng av flaggan på lådan.** Ta bort `DevTools__EnableResetMyData` ur
   `deploy/.env` och `DEV_TOOLS_RESET_ENABLED` ur webbtjänstens miljö, och starta om.
   Verifiera: `POST /api/v1/dev/reset-my-data` → **404**, och knappen syns inte på
   `/oversikt`. Det är hela skyddet, redan innan någon kod tas bort.
2. **Riv koden**, i en egen PR:
   - `src/Jobbliggaren.Api/Endpoints/DevEndpoints.cs` (hela filen)
   - de två map-grindarna och boot-annonseringen i `src/Jobbliggaren.Api/Program.cs`
   - `src/Jobbliggaren.Api/Observability/DevToolsLog.cs`
   - `src/Jobbliggaren.Application/Dev/` (hela katalogen: `Configuration/DevToolsOptions.cs`,
     `Commands/ResetMyData/`, `Commands/ConfirmEmail/`, `Abstractions/`)
   - `DevToolsOptions`-bindningen i `src/Jobbliggaren.Infrastructure/DependencyInjection.cs`
     och `AddDevOnlyTestingSupport` i samma fil
   - `"DevTools"`-sektionen i `src/Jobbliggaren.Api/appsettings.Development.json`
   - `deploy/docker-compose.yml` (`DevTools__EnableResetMyData` på `api`,
     `DEV_TOOLS_RESET_ENABLED` på `web`), `deploy/.env.example`-blocket, och
     `tests/Jobbliggaren.Migrate.UnitTests/DeployComposeDevToolsGateTests.cs`
   - `web/jobbliggaren-web/src/components/dev/`, `src/lib/dev/`,
     `DEV_TOOLS_RESET_ENABLED` i `src/lib/env.ts`, och renderingen i
     `src/app/(app)/oversikt/page.tsx`
   - `dev.*`-nycklarna i `messages/{sv,en}/common.json`
   - `tests/Jobbliggaren.Application.UnitTests/Dev/`,
     `web/jobbliggaren-web/src/lib/env.test.ts`,
     `tests/Jobbliggaren.Api.IntegrationTests/Auth/DevConfirmEmailEndpointTests.cs`
   - **Playwright-sviten kallar `confirm-email`** — den måste få en annan inloggningsväg
     i samma PR, annars faller e2e-lanen. Detta är det ENDA steget som inte är ren
     strykning, och det är därför avstängningen i steg 1 kommer först.
3. **Behåll grindtesterna tills koden är borta, riv dem sist.**
   `ProductionStartupSmokeTests` mäter att båda rutterna är omappade; de är meningslösa
   först när det inte finns någon rutt att mappa.
4. **Verifiera efteråt:** `grep -rnE --exclude-dir=node_modules --exclude-dir=.next --exclude-dir=bin
   --exclude-dir=obj "api/v1/dev|DevTools|DEV_TOOLS" src web tests deploy`
   → noll träffar utanför den här filen. **Den vidare formen är avsiktlig:** token
   `api/v1/dev` finns varken i `DevToolsLog.cs`, `env.ts`,
   `"DevTools"`-sektionen, compose-sloten, `.env.example`-raden eller
   `DeployComposeDevToolsGateTests` — en grind som mäter en annan mängd än steg 2
   river är sämre än ingen grind.
   ⚠ **Uteslutningarna är inte kosmetik — utan dem kan kriteriet aldrig uppnås.** Mätt
   2026-08-27 på ett byggt träd: **90 filer med bara `node_modules`/`.next` uteslutna, 22
   när `bin`/`obj` också utesluts.** Resten är kompilerade `.dll`/`.pdb`, `.next`-chunks
   och testernas kopior av `appsettings`/`docker-compose`. En operatör som möter brus vid
   lansering ögonfiltrerar eller lägger in ad-hoc-undantag, och den enda träff som betyder
   något göms i bruset.
   ⚠ **Mät med RÅ `grep -r`, aldrig `git grep`** — den senare hoppar över gitignorerat och
   ser därför inte byggutdata alls, så den ger ett falskt godkänt på exakt den här grinden.

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
