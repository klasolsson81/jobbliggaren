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
- [ ] **Öppna issues märkta `P0`/`P1` mot release-scope** genomgångna (GitHub Issues)
      — varje launch-blocker löst eller medvetet deferrad med motiv. Issues märkta
      `mvp` är de som krävs för riktiga användare. (TD-registret retirerades
      2026-08-02, ADR 0121; parkerade poster ligger i #1172.)
- [ ] **Migrations** — om EF Core-migration ingår: verifiera schema-mode-
      dispatch (ADR 0033) och DB-roll-separation (ADR 0034); Identity-schema-
      ändring → manuell procedur (parkerad, #1172).
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

> Gäller ENDAST en release som aktiverar `Email:Provider=Resend` i non-dev.
> Tills dess kör `NullEmailSender` — ingen
> e-post skickas, och denna grind är inte relevant. Resend är en **US-processor**
> → mottagar-adress **+ meddelandets innehåll** är en tredjelandsöverföring (för notiserna
> **avslöjar** leveransen opt-in-faktumet, och `EmailTemplates` skriver det dessutom i klartext
> i själva kroppen — själva *flaggan* i vår DB överförs aldrig, men faktumet gör det). Ett kontolivscykel-mejl har inget opt-in — men adressen och innehållet
> når providern lika fullt. **VARJE numrerad punkt i DEN HÄR sektionen (§2.5) MÅSTE vara grön innan `Email:Provider`
> flippas** (ADR 0080
> prod-flip-checklista). CC får ALDRIG flippa providern eller signera DPA:t.
>
> **"Grön" = INGET led i punkten bär KVAR — inte att rutan är bockad.** (Negation med flit:
> ett led kan bära **båda** markeringarna — ROPA-ledet är **KLAR för notis-vägen** och **KVAR
> för kontolivscykel-mallarna** — och "bär KLAR" hade då räknat det som grönt.) Rutorna i
> hela den här filen är obockade (**37 av 37** vid 2026-07-26 — greppa **radinitialt**
> (`^- \[ \]`); ett rått grep ger 39 och räknar prosacitaten av literalen längre ned.
> **Regenerera siffran ur greppet efter varje tillagd punkt** — punkt 5.5 tillkom i samma
> ändring som skrev "35", och punkt 5 i den som skrev "36" — båda gjordes falska i samma andetag) och bockas av den som **utför** releasen; statusen
> bärs av **KLAR**-markeringarna. Punkt 1:s led står uppräknade i punkten själv, och ett led kan
> vara **delvis** KVAR
> (ROPA-ledet är det i dag) — **ett delvis KVAR led är KVAR**, så punkten är grön först när
> inget av **punktens led** bär KVAR i någon form. Läs aldrig en obockad ruta som "inte levererat",
> och bocka aldrig en ruta för att en förutsättning är levererad.
>
> **Grinden gäller ALL utgående e-post, inte bara bakgrundsmatchnings-notiserna**
> (widening 2026-07-26, #186). `Email:Provider` är EN switch, och `EmailTemplates`
> har **sex** sorter varav **fyra är kontolivscykel** (`EmailConfirmation`,
> `EmailChangeConfirmation`, `EmailChangedNotification`, `AccountExistsNotice`) och
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
      - signerad **DPA** med Resend på fil — **KVAR** (Klas, aldrig CC);
      - dokumenterad **Kap. V-grund** — **KVAR**, och disjunktionen "SCC **eller**
        adekvans/DPF" måste upplösas till **en** grund före första överföringen;
      - Resend-posten i `docs/runbooks/gdpr-processing-register.md` (ROPA, lokal) —
        **KLAR för notis-vägen** (PR #213), **KVAR för kontolivscykel-mallarna** (se `Källa:`
        nedan). Registret speglar och grindar inte (#1040) — men sign-off-ledet nedan kan
        inte ges utan en behandling att signera mot;
      - **integritetspolicy-post som namnger Resend** — **KLAR** (#186 / PR #1083).
        Denna halva stod tidigare inte i punkten alls: transkriberingen ur ADR 0080
        punkt 1 tappade den och behöll bara ROPA-halvan;
      - **security-auditor-sign-off på prod-e-post-konfigen** — **KVAR**. Det gamla
        TD-116:s sign-off är PR-4:s, inte prod-konfigens; bocka aldrig punkten på den.
        (TD-116 stängdes 2026-07-26; residualen ägs av #183.)

      **Kvarstående policy-residualer under denna punkt, inte under punkt 3.**
      **ORDNINGEN STÅR FÖRST, för att den styr posterna under sig:** upplös
      SCC/adekvans-disjunktionen **före** du skriver Art. 13(1)(f)-formuleringen —
      kopia-formuleringen hänger på Art. 46/47-grunden, så tvärtom påstår du en SCC-grund
      som kanske inte används. Alltså **(iii) → (ii)**, och listans första post — flytten in i `Mottagare`-listan —
      när avtalet signeras.
      (i) flytta Resend in i `Mottagare`-listan när biträdesavtalet är signerat —
      prosaformen är vald just för att listrubriken påstår ett tecknat avtal, och det
      förbudet **upphör med signeringen**; (ii) **Art. 13(1)(f)** — "means to obtain a
      copy" av skyddsåtgärderna saknas i policyn; (iii) upplös SCC/adekvans-
      disjunktionen.
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
- [ ] **4. TD-114** — stranded-Queued-reaper (#184 / PR #212 — **KLAR**) +
      **Resend `Idempotency-Key`** på real-send-vägen (#187 / PR #230 — **KLAR**;
      VO `MatchNotificationIdempotencyKey`, ad-scoped Direct + content-hash Digest).
- [ ] **5. `BUILD.md` flippas i SAMMA ändring** — den här checklistan räknade tidigare bara upp
      `content-legal.json` och ROPA:n, och nämnde **aldrig** `BUILD.md` som flip-yta. Vid flippen
      blir följande falska utan att något kräver att de rörs: **§13.4**:s e-postpost
      (*"planerad, ännu inte aktiverad … ingen e-post lämnar systemet"*), **§3.1 rad 39**
      (*"prod-utskick grindat"*) och **rad 126** (*"Resend, grindad"*). Rad 761 är
      provider-neutral och blir INTE falsk — kontrollera den, ändra sannolikt inget.
      `BUILD.md` läses av varje CC-invokation (CLAUDE.md §9.1), så en oflippad rad där får varje
      efterföljande session att resonera från en falsk premiss om en **levande**
      tredjelandsöverföring. **Hör här på TRIGGERN, inte på sektionskaraktären** — §2.6 kallar
      sig själv också en aktiveringshändelse. Raderna blir falska när `Email:Provider` flippas
      (§2.5), inte vid första `v*`-taggen (§2.6).
      Tillagt 2026-07-26 på dotnet-architects mätning — och just denna PR **ökade** ytan.

Källa: ADR 0080 §"Prod-Resend-flip pre-condition checklist"; ROPA-behandlingen
"Bakgrundsmatchnings-notiser via e-post (Resend)" — som i dag täcker **endast**
notis-vägen. Efter wideningen ovan gäller grinden all utgående e-post, men ingen
Art. 30-behandling täcker de **fyra kontolivscykel-mallarna** — och **TVÅ**
av dem är ogrindade: `EmailChangeConfirmation` (`ChangeEmailCommandHandler:66`) och
`EmailChangedNotification` (`ConfirmEmailChangeCommandHandler:45`, vars enda villkor är att
den gamla adressen finns). **Den senare går till den GAMLA adressen** — en annan
mottagarklass än den användaren just skrev, så en Art. 30-behandling som bara skopas till
den första lämnar en mottagare oregistrerad. (`EmailConfirmation` är däremot grindad på `RequireEmailConfirmation`,
`RegisterCommandHandler.cs:81`, som defaultar **false** — se blockquoten ovan. En
prod-lansering tvingar alltså inte i sig grinden.)

Det är samma lucka som den redan eskalerade frågan om att ROPA:n saknar behandling för
användarkontot/autentiseringen helt (Art. 30(1)). **Luckan grindar inte via registret** —
registret speglar (#1040) — men den blockerar **security-auditor-sign-off-ledet** i punkt 1:
det finns ingen Art. 30-behandling att signera prod-e-post-konfigen mot för
kontolivscykel-vägen. Registret är gitignorerat och kan inte rida en PR (ADR 0072), så
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
      Vid 2026-07-26 (efter #186 + dess remediation): **12 + 12** (rad 37, 49, 63, 72,
      73, 74, 77, 78, 85, 99, 100, 135 — identiska i
      sv och en, alla äkta statuspåståenden, ingen falsk träff med detta mönster).
      **Grepa INTE bara på `"planerat och ännu inte i drift"`** — det ger 10 och
      missar de TVÅ retentionsposterna på rad 99 och 100, som bär `(planerat)` utan
      avslutningsmeningen. Rad 99 (organisationsnumret i en annons, #880) nämner
      ansökningshistoriken som ett ÄNDAMÅL med att arbetsgivarens identitet sparas;
      rad 100 är ansökningshistorikens egen post. **Regenerera den här listan ur
      greppen ovan efter varje redigering av `privacy`-sektionerna** — inte bara
      retentionsavsnittet: #880 delade en
      punkt i två och flyttade fyra av åtta rader, och #186 rörde tre andra avsnitt
      (samtycke, mottagare, tredje land) och flyttade **sex av åtta** medan tre nya
      tillkom, så en handlappad siffra blir
      falsk vid nästa redigering. Lagringstiden är en egen obligatorisk
      uppgift (Art. 13(2)(a)) och ADR 0090 D3 räknar uttryckligen upp
      retentionsraden som del av samma leverans. Flippar du 6 och lämnar 1 säger
      kategorilistan drift medan retentionsavsnittet säger planerat.
- [ ] **2. Avgör vad releasen faktiskt aktiverar** — tre olika klasser, blanda dem
      inte:
      - **Kod-aktiverad:** ansökningshistorik/företagsöversikt (rad 37, 99, 100, 135).
        Handlers + endpoints + FE är skeppade utan feature-flagga → aktiveras av
        att tjänsten alls går i drift.
      - **Deploy-aktiverad:** Hetzner, Cloudflare (rad 77, 78) → aktiveras av att
        stacken körs hos dem. Se punkt 3 — dessa får inte flippas på egen hand.
      - **Konfigurations-grindad:** SCB (rad 49, 72) **och Resend (rad 63, 73, 74, 85,
        #186)**. **Aktiveras INTE av en
        `v*`-tagg.** Tre skilda mekanismer, alla mörka i prod: per-sökningens
        `ICompanyRegistry` (ADR 0088) får `NullCompanyRegistry` — valet styrs av
        `CompanyRegistry:Provider`, den riktiga adaptern siktar på SCB:s nya
        API (~sept 2026) och dess **första verkliga överföring är hårt grindad på
        DPIA #456 + SCB terms review** (ADR 0088 D3); bulk-populeringen
        `IScbCompanyRegisterSource` (ADR 0091) är Worker-only och grindad på
        `ScbRegister:Enabled=true` + klientcert, och skickar aldrig ett
        användarskrivet org.nr. Resend styrs av `Email:Provider`, som defaultar till
        `Console` och i non-dev löser till `NullEmailSender` — flippen är grindad av
        **§2.5 punkt 1** (uppräkningen bor DÄR, inte här — och därför står antalet inte heller här), inte av en
        tagg, och gäller **all** utgående e-post (§2.5:s widening). **Flippa rad 49/72 (SCB) respektive 63/73/74/85 (Resend) först när respektive grind är
        passerad** — inte när koden deployas.
      Kvarstående planerat-meningar för behandlingar som fortfarande inte är i
      drift ska stå kvar. Släpper releasen ingen av dem är rätt utfall att **inte
      ändra något**.
- [ ] **3. Art. 28 + Kap. V innan Hetzner/Cloudflare flippas** (speglar §2.5
      punkt 1 — utan detta blir två redan presens-formulerade meningar falska i
      samma ögonblick):
      - signerat **personuppgiftsbiträdesavtal** med **Hetzner** och med
        **Cloudflare** på fil (rad 70 påstår redan *"Med dem har vi
        personuppgiftsbiträdesavtal"* — idag finns inga aktiva biträden alls, och
        #186:s Resend-stycke säger uttryckligen att avtalet tecknas *innan* utskicken
        börjar, just för att inte ärva den raden);
      - dokumenterad **Kap. V-grund** för Cloudflare (US-domicilierat bolag; även
        en EU-only-konfiguration kräver grunden dokumenterad) — rad 84 är ett
        **absolut** påstående: *"I dagsläget sker inga överföringar av dina
        personuppgifter till länder utanför EU/EES"*, och det måste omprövas som
        del av samma flip. **Detsamma gäller Resend-flippen** (§2.5), som är den
        andra av två oberoende händelser som gör rad 84 falsk; #186 la därför rad 85
        **bredvid** den absoluta meningen i stället för att ersätta den — båda är
        sanna samtidigt så länge inget skickas;
      - ROPA-posterna uppdaterade + **security-auditor-sign-off**.
      DPA-signering = **Klas**, aldrig CC.
- [ ] **4. Paritet sv + en** — båda språken i samma ändring. Formuleringen bärs av
      elementen i `privacy.sections` som bär formuleringen — tillsammans **exakt den radmängd
      punkt 1 producerar** (antalet står där, med sitt grep; det står med flit inte här):
      kategorilistan (rad 37), ändamåls-/SCB-avsnittet (49), samtyckesavsnittet
      "Bevakningsnotiser i bakgrunden" (63, #186), mottagare + tredjeland
      — mottagaravsnittet (72/**73/74**/77/78) och tredjelandsavsnittet (85) är TVÅ skilda
      sections, inte ett — retentionslistan (99/100) och "Inga automatiserade beslut"
      (135). Missa inte retentionsposten — och notera att **både** retentionslistan **och**
      Resend-prosan i mottagaravsnittet bär **två** rader var, inte en.
- [ ] **5. Bumpa `privacy.updated`** ("Senast uppdaterad: YYYY-MM-DD"), båda
      språken. Skopa till **`privacy.updated`** — filen har fem `updated`-nycklar
      (privacy/terms/cookies/accessibility/recruiterNotice).
- [ ] **5.5 TVÅ VILLKOR SOM UPPHÖR VID FÖRSTA PRODUKTIONSANVÄNDAREN — de hör HÄR, inte i
      §2.5.** Båda accepteras i dag enbart därför att det finns **noll registrerade
      produktionsanvändare**. Triggern är **den första `v*`-taggen som öppnar registrering**,
      och den passerar **inte** §2.5: en prod-tagg med `Email:Provider` osatt (dokumenterad
      default) ger `NullEmailSender`, registrering fungerar (`RequireEmailConfirmation`
      defaultar `false`), och Resend-flippen kan ligga månader senare. Villkoren upphör alltså
      **strikt före** §2.5 någonsin läses (security-auditor 2026-07-26).
      - **(a) `settings.json` påstår ett utskick som inte sker.** Fyra publicerade strängar
        (`:218`, `:220`, `:224`, `:229`) säger att en bekräftelselänk skickats, medan
        `ChangeEmailCommandHandler:66` skickar ogrindat in i `NullEmailSender`: `Result.Success`,
        auditrad stämplad, **adressen byts aldrig**, ingen väg framåt. Ägare **#1087**
        (port-capability-predikat). Copyn får INTE mjukas upp först — det falska påståendet är
        enda användarsynliga tecknet att flödet är trasigt. Art. 5(1)(a) + 12(1).
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
        (rad 154) säger *"Vid mer betydande ändringar informerar vi dig på lämpligt
        sätt"*. Formulera som förhandsbesked (*"från och med &lt;datum&gt; behandlar vi
        även …"*), aldrig som påstående om pågående drift.
      Aldrig **efter** aktiveringen i något av fallen.
- [ ] **7. Konsistenskontroll efter flippen** (per behandling, båda språken). För
      varje behandling ska **alla** dess omnämnanden ha samma status.
      Ansökningshistoriken nämns på fyra ställen (kategorilistan, retentionslistan,
      "Inga automatiserade beslut" och Art. 30-registret); SCB på tre
      (ändamålslistan, mottagarstycket — tredjelandsavsnittet nämner INTE SCB; uppräkningen
      sa "tre" ända till 2026-07-26); **Resend på fyra** (samtyckesavsnittet, TVÅ stycken i
      mottagaravsnittet, "Överföring till tredje land") —
      och Resend-flippen styrs av **§2.5**, inte av taggen, så den kan mycket väl
      inte höra till releasen alls medan de andra gör det. **En
      mottagare får aldrig stå som planerad medan behandlingen som skickar till
      den står som i drift, och omvänt.** Kör inventeringsgreppet igen efter
      flippen: antalet träffar ska minska med **exakt** antalet poster releasen
      aktiverar, aldrig med fler.
      **Rad 135 kräver särskild kontroll — den är den enda rad greppet inte
      självskyddar.** Dess inledning (`planerar` / `plans`) matchas INTE av
      inventeringsmönstret (verifierat: 0 träffar), så raden syns bara via sin
      avslutande mening. Tas bara den bort faller raden ur greppet helt, räkne-
      testet ovan säger "minskade med exakt 1 — korrekt", och policyn påstår
      fortfarande *"Jobbliggaren planerar en översikt av din egen
      ansökningshistorik"* — mitt i avsnittet **"Inga automatiserade beslut"**,
      dvs. i Art. 22-negationen. Läs rad 135 i sin helhet: hela stycket skrivs om
      till presens, aldrig trunkeras. (Varje **annan** rad ur punkt 1:s mängd bär `(planerat)`/
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
> service-namn/kommandon finalize:ras med **#196** (Compose-stack + proxy
> + härdning) — stegen nedan är på modell-altitud tills dess.

- [ ] **Compose-tjänster startar** (api + worker) — `docker compose ps` på boxen
      visar dem `healthy` (konkret service-namn/compose-fil: #196).
- [ ] **`/api/ready` → 200** mot målmiljöns domän (strict readiness: DB +
      Redis dependency-checks, TD-29).
- [ ] **`/api/health` → 200** (liveness).
- [ ] **Hangfire-jobben** kör enligt schema om release rör Worker
      (`*/10`-cron etc.) — verifiera i Hangfire-dashboard/loggar.
- [ ] **Audit-wire** — om release rör audit-genererande flöden: bevisa
      INSERT i `audit_log` via den strukturerade logg-sinken (MEL → Seq; full
      prod-sink = #196) + direkt `audit_log`-query (ADR 0035).
- [ ] **Ops-signaler granskade** — health-checks + extern uptime-monitor
      (UptimeRobot/BetterStack, ADR 0050 — ersätter ALB/CloudWatch-health);
      jobtech-sync-/auditor-write-/log-pipeline-health läses via logg-sinken.
      Konkret alerting-konfig: #196.
- [ ] **Frontend** (om i scope) — Lighthouse observe-signal mot
      ADR 0045-budgetar; manuell rök-test av kritiska flöden.
- [ ] **Rollback känd** — återställ föregående byggda image-tag via Compose
      (se §5); konkret procedur #196.

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
# Konkret tag-mekanism + service-namn finalize:ras med #196 (ADR 0050).
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
  ADR 0045 (perf observe-only-signaler); #196 (konkret Compose-stack + prod-sink)
  (logg-sink/observability)
- CLAUDE.md §6.3 (granskningsspärrar), §8 (DoD), §9.2 (deploy kräver Klas-GO)
- BUILD.md §15 (deployment/rollback)
- `docs/runbooks/v0.2-prod-launch-checklist.md` — engångs-checklist för
  *första* prod-deployen (komplement, inte ersättning för denna)
