# LinkedIn api-host lever (follow-up to #1926) form round — dotnet-architect (report-only, before code)

- **Date:** 2026-10-03 (report returned ~2026-10-03T09:4xZ)
- **Agent:** `dotnet-architect`, report-only, against `.claude/worktrees/jobb-card-density-1828-79e055` at `4e0bf168a`
- **Brief:** `docs/sessions/2026-10-03-1926b-linkedin-api-host-form-brief.md`
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript
  (`subagents/agent-a1eaac158f728a967.jsonl`); nothing below the rule is the session's.

---

## Arkitektur-analys

*Report-only form ruling för uppföljningen av #1926 (LinkedIns api-host). Jag läste i worktree `jobb-card-density-1828-79e055` på `4e0bf168a`:*
- *briefen och min formrapport för #1926;*
- *ADR 0142 D8, (20) :1764-1787, (21) :2020-2039 och (22) :2116-2377;*
- *`vps-deploy-stack.md` §3d :726-810 och registrets LinkedIn-post för kap. V (:1673-1703);*
- *de tre adapters, web start- och callback-routen, `external-login.ts`, de fyra pinnarna, mirror-testet, `ScriptedLinkedIn` och `oauth.spec.ts`.*

*Session-loggen som prompten nämner (`docs/sessions/2026-10-03-1926b-closeout-linkedin-api-host.md`) finns inte i worktree. Mätningarna nedan är därför briefens, och #1926:s kommentar har jag inte läst.*

*Två källor läste jag själv 2026-10-03 med WebFetch (CLAUDE.md §9.5), genom en sammanfattande fetch:*
- *Supabase `internal/api/provider/linkedin_oidc.go` på master: `defaultLinkedinOIDCAPIBase = "api.linkedin.com"`, `AuthURL` och `TokenURL` båda under den, ingen `prompt`.*
- *`https://api.linkedin.com/oauth/.well-known/openid-configuration`: den namnger `www.linkedin.com` för både `authorization_endpoint` och `token_endpoint`, och `api.linkedin.com/v2/userinfo` för userinfo.*

*Fetchens radnummer skilde sig från briefens. (23) citerar därför innehåll och datum, inte rader, och sessionen läser om båda innan något citeras. Jag skrev ingenting.*

### Sammanfattning
Formen håller som arkitektur, med villkor: **0 Kritiskt, 9 Viktigt (skyldigt i PR:en eller dess plan), 3 Nice-to-have.** Ingen Clean Architecture-gräns flyttas. Ändringen är en protokollkonstant i en Infrastructure-adapter, dess web-mirror och fyra pinnar. Porten, handlers, api:t och callbacken ändras inte, och under (a) inte heller `ScriptedLinkedIn`.
- **Q1:** formen är acceptabel, eftersom varje sätt api-hosten kan ändras på fallerar stängt och beroendet är en konstant plus dess mirror. Det som inte är acceptabelt är att lita på hosten utan en namngiven återkallelse-trigger. Den tysta återkallelsen, att api-hosten börjar respektera www-sessionen, skriver ingen rad någonstans (R5).
- **Q2:** (a) i första hand, (b) bara om mätningen visar att (a) vägras. `UserInfoEndpoint` ligger kvar (R2).
- **Q3:** båda. (i) före merge väljer mellan (a) och (b) och läser om redirect-matchningen på api-hosten. (ii) efter merge är utrullningsläsningen (R6).
- **Q4:** Klas beslut. Frågan och tre alternativ står sist, för ordagrann vidarebefordran.
- En PR, som går på `automerge` på `security-auditor`s verdikt. Ett undantag: under (b) måste hennes kap. V-läsning ske före merge, eftersom mergen är utrullningen (R9).

### Rekommendation

**[Viktigt] R1: konstanten behåller hem och namn, men kommentaren delas.** `src/Jobbliggaren.Infrastructure/Auth/ExternalLogins/LinkedInIdentityProvider.cs:31-35`
- **Hem:** `LinkedInIdentityProvider`, `internal static readonly Uri AuthorizationEndpoint`.
- **Namnet ändras inte.** Det namnger rollen, och rollen är vad mirror-dictionaryn (`ExternalLoginMirrorWireContractTests.cs:46-51`), web-mappen och de två andra adapters nycklar på. Ett namn med hosten i skulle bara upprepa värdet.
- **Inget `IOptions`-värde.** Det är en protokollkonstant som `Scope` och `Prompt` (min R1, 2026-10-03), och webben läser den som en compile-time-mirror. En inställning skulle låta en miljö avvika utan att något test ser det. En rollback är en revert, ingen växel, eftersom web-sidan ändå ändras med.
- **Ingen andra konstant för www-hosten.** Produktion läser den aldrig, så en pinne på den pinnar ingenting.
- **Kommentaren :31-32** ("Constants from LinkedIn's discovery document …") blir falsk för just den här raden, under båda formerna. Flytta `AuthorizationEndpoint` ut under en egen pekarrad:

      // LinkedIn's api host, not the documented www host, so that LinkedIn asks for the sign-in (#1926, ADR 0142
      // Amendment (23)).
      internal static readonly Uri AuthorizationEndpoint = new("https://api.linkedin.com/oauth/v2/authorization");

  - Under (a) täcker discovery-kommentaren `TokenEndpoint` och `UserInfoEndpoint`, och är fortfarande sann.
  - Under (b) täcker den bara `UserInfoEndpoint`, och pekarraden täcker båda api-konstanterna.
  - Skälen bor i (23) (AGENTS.md §5 `Comments:`). Pekaren anger en avsikt, inte en mätning som kan åldras.
- **Klassens docblock :12-16** ("read over TLS from the fixed token endpoint") förblir sann under båda formerna.

**[Viktigt] R2 (Q2): (a) i första hand, (b) bara om (a) mäts som vägrad.**
- **(a) Bara authorization på api.**
  - Det enda som ligger på den odokumenterade hosten är webbläsarens request. Adapterns två anrop, POST:en med hemligheten och userinfo-läsningen, är byte-identiska med i dag.
  - id_token-premissen ("over TLS to the pinned endpoint", (20) :1769) ligger kvar på den endpoint som båda discovery-dokumenten namnger.
  - Registrets lapse (4) (:1696) utlöses inte enligt sin ordalydelse, och kap. V påverkas inte, eftersom inget på serversidan ändras.
  - Kostnad: att www löser in en api-utfärdad kod är omätt.
- **(b) Authorization och token på api.**
  - Det här är Supabases par. Det visar att inloggningar fullföljs hos någon annan, inte hos oss.
  - Formen flyttar klienthemligheten och id_token-källan till en endpoint som inte ens api-hostens eget discovery-dokument namnger.
  - Om "token-ändpunkten" i (4) betyder rollen eller den dokumenterade URL:en är tvetydigt. `security-auditor` läser det, och §9.6 lämnar en tvetydig text till dess ägare.
  - Under (b) ändras `ScriptedLinkedIn.TokenEndpoint` (:36) och dess docblock (R3).
- **Varför (a) först.** (a) ger ett odokumenterat beroende i stället för två, och halvan som bär hemligheten och identiteten förblir dokumenterad. Snävar LinkedIn senare in api-hosten är bara webbläsarens del utsatt.
- **(c) Andra former, avvisade:**
  - en inställning per miljö (se R1);
  - ett utloggningshopp hos LinkedIn före authorization: det loggar ut medlemmen från LinkedIn i webbläsaren utan att fråga;
  - ett eget bekräftelsesteg efter callbacken. Det använder bara dokumenterade endpoints, men är ny produktyta, visar en annan persons adress på vår sida vid en delad dator (`security-auditor`s fråga) och kan bara stoppa, inte byta konto. Det är ett eget change-reason med en egen formrunda, och det finns inte med i Q4 om inte `senior-cto-advisor` routar det dit.
- **`UserInfoEndpoint` ligger kvar** (`https://api.linkedin.com/v2/userinfo`). Båda discovery-dokumenten namnger den, och ingen av formerna rör den.
- **Kap. V och cookie-domänen.** Authorization-requesten går mellan medlemmens webbläsare och LinkedIn. API Terms namnger en avtalspart, ingen host. Arkitekturen ser ingen ny mottagare i någon av formerna. Läsningen är hennes.

**[Viktigt] R3: de fyra pinnarna, mirror-testet, `ScriptedLinkedIn` och e2e-raden.**
- **Unit A1** (`LinkedInIdentityProviderTests.cs:97`): literalen blir `https://api.linkedin.com/oauth/v2/authorization`. Nycklarna (:100-101) och kommentaren (:98-99) är oförändrade och fortfarande sanna. Ingen kommentar läggs till, eftersom produktionspekaren bär skälet.
- **Integration** (`LinkedInLoginTests.cs:159`): samma literal. Namnet ("…five_documented_parameters") är fortfarande sant: parametrarna är dokumenterade, hosten är det inte.
- **Arkitekturens literal-pinne** (`ExternalLoginMirrorWireContractTests.cs:70-73`): behåll en literal, och låt den peka på api-hosten.
  - Mirror-teorin (:55-59) släpper ensam igenom en konsekvent revert av båda sidor. Literalen är dess oberoende orakel, och GitHubs pinne (:65-68) satte mönstret.
  - Namn och kommentar blir falska ("documented", "the live discovery document, read 2026-09-27"). Byt namn till exempelvis `The_linkedin_adapter_points_at_linkedins_api_host_authorize_endpoint`, med en kommentar som namnger (23) och mätningen 2026-10-03.
- **Mirror-teorin** (:55-59): ingen kodändring, båda värdena flyttar.
- **Web** (`route.test.ts:15`, `LINKEDIN_AUTHORIZE`): hosten blir api.
  - Fixturen är deklarerad som "`LinkedInIdentityProvider.BuildAuthorizeUrl`'s shape" (:14), och efter ändringen vägrar routen www-formen ändå. Ändringen är alltså tvingad.
  - Raderna med fel provider (:186-200) förblir gröna.
- **`ScriptedLinkedIn`:**
  - Under (a): oförändrad. Den modellerar bara serverns anrop. En double som modellerade webbläsarens host skulle hävda LinkedIn-beteende som inget mäter (AGENTS.md §5 `Tests:`).
  - Under (b): `TokenEndpoint` (:36) blir api. Docblockens "behave as LinkedIn documents them" (:13) får en DECLARED-klausul: hosten är odokumenterad, formen är Supabases, och den mättes på det namngivna datumet. T1:s `ShouldBe(ScriptedLinkedIn.TokenEndpoint)` (:143) blir då token-hostens pinne.
- **e2e: ingen rad.**
  - `oauth.spec.ts` täcker bara Google, `e2e.yml` registrerar ingen LinkedIn-klient (:76-78), och sviten är observe-only (:51).
  - Namnge skippen i PR-bodyn med de skälen. Att lägga till LinkedIn där vore en CI-config-ändring utan change-reason i den här PR:en.

**[Viktigt] R4: mutationer. Varje mutation körs mot den committade koden, och "röd" betyder röd i `ci`.**
- **Adapterns `AuthorizationEndpoint` ensam tillbaka till www** → röda: unit A1 :97, integration :159, arkitekturliteralen :73 och mirror-teorins `linkedin`-rad.
- **Webbens `AUTHORIZATION_ENDPOINTS.linkedin` ensam tillbaka till www** → röda: mirror-teorins `linkedin`-rad och `route.test.ts`s LinkedIn-rad (:167-181). Routen vägrar då api-fixturen och svarar med `location` `/logga-in`.
- **Båda sidor tillbaka till www samtidigt** → röda: unit A1, integration, arkitekturliteralen och `route.test.ts`s LinkedIn-rad. Mirror-teorin förblir grön, och det är därför literalerna finns.
- **Stavfel i path eller host på någon sida** (`/authorize`, ett avslutande snedstreck) → samma rader som för den muterade sidan.
- **Under (a): `TokenEndpoint` flyttad till api, eller `UserInfoEndpoint` till www** (den typiska "konsekvens"-ändringen):
  - `ScriptedLinkedIn` kastar för en host den inte betjänar (:134).
  - Vare sig undantaget går igenom eller läses som ett transportfel blir varje exchange-rad som väntar en identitet röd, i unit- och i integrationssviten.
- **Under (b): adapterns `TokenEndpoint` ensam tillbaka till www** → samma rader, via fakens api-konstant.
- **`prompt` tillagd för LinkedIn** (#1926:s vakt) → båda femnyckelspinnarna, oförändrade.
- **Pre-commit** kör arkitekturtesterna bara när en `.cs`-fil är stagad, och kör aldrig vitest. En mutation enbart i web blir därför röd i `ci`, inte vid commit.

**[Viktigt] R5 (Q1): varje återkallelse fallerar stängt, men bara en form av den skriver en rad.**
- **Vad användaren ser**, beroende på vad LinkedIn ändrar:
  - **api-hosten slutar betjäna authorization:** en felsida hos LinkedIn eller i webbläsaren. Inget kommer tillbaka till oss, och ingen rad skrivs någonstans.
  - **den svarar vår `redirect_uri` med `error`:** callbackens error-gren (`callback/route.ts:46`) skickar till `/logga-in` med "Inloggningen med LinkedIn slutfördes inte". Inget når api:t, och ingen rad skrivs.
  - **under (a) slutar www lösa in api-utfärdade koder, eller under (b) försvinner api:s token-endpoint:** api-callbacken svarar 410 och samma notis visas. Här skrivs **EventId 1022**, med `TokenInvalidRequest` 401 (dokumenterat för en okänd kod), `TokenRefused` eller `Transport`.
  - **den börjar respektera www-sessionen, eller redirectar till www:** inloggningen fungerar och frågar ingenting, vilket är läget i dag. **Ingen rad skiljer sig från en vanlig `login_succeeded`.**
- **Utrullningsfönstret.** Kör api och web olika revisioner vägrar web-starten den andra hostens URL (`route.ts:53-56, :98-100`). Samma notis visas innan LinkedIn nås, och ingen rad skrivs. Det fallerar stängt och är därför acceptabelt, men box-läsningen binder båda images (R6).
- **Ingen form ger en session utan adapterns hela kedja:** koden inlöst med vår hemlighet, `aud` enbart vår och userinfo med samma `sub`. Ett hostbyte kan alltså inte logga in någon i ett konto som LinkedIn inte har autentiserat. Mix-up-skyddet, en callback-path per provider, är oförändrat.
- **Ingen automatik kan upptäcka den tysta återkallelsen.** Beteendet beror på medlemmens LinkedIn-session, och en automatisk prob skulle behöva hålla en sådan, det vill säga credentials i automatiken.
  - Triggern behöver därför ett hem, en läsare och en åtgärd, nedskrivna före merge (R7).
  - Sannolikheten är inte låg: Supabase #50831 rapporterar samma beteende som en bugg.
- **Hosten är en egenskap hos våra requests, inte hos klienten.** En egenbyggd request namnger fortfarande www.
  - (22):s residual "a crafted request mints a code for a signed-in member without prompting" är därför oförändrad, och PR:en får inte påstå att den stängs.
  - Formuleringen är `security-auditor`s.

**[Viktigt] R6 (Q3): mät före merge, och läs igen efter.**
- **(i) Före merge (rekommenderas).**
  - Den lokala klienten är boxens (m-4; §3d punkt 4), och dess localhost-callback är registrerad (mätt 2026-10-02). Ett lokalt flöde mäter därför samma LinkedIn-app.
  - Bygg (a) på branchen, i stack-ägarens stack. Klas är inloggad på LinkedIn i webbläsaren och gör:
    1. två LinkedIn-inloggningar i rad. Startens `Location` (origin+path) är api-hosten, LinkedIns inloggningssida visas båda gångerna, och ingen tillåtelsesida visas;
    2. en Cancel på den sidan, som ska landa på `/logga-in` med notisen.
  - Lokal api-logg över fönstret:
    - **0 × EventId 1022**, vilket är själva mätningen av det blandade paret;
    - 0 × 1021, 1023, 1024 och 1025;
    - ett utfall bortom exchange vid varje inloggning (`login_succeeded … Method=LinkedIn` om hans lokala konto har adressen).
  - **Om 1022 `TokenInvalidRequest` visas: byt till (b)** på samma branch och upprepa. `security-auditor` läser sedan lapse (4) före merge.
- **Också före merge, anonymt och utan inloggning:** redirect-matchningen på api-hosten, med samma metod som 2026-09-27 (en subpath och en oregistrerad redirect). (21):s levande bound "exact redirect matching at the authorization endpoint (measured 2026-09-27 and 2026-09-29)" (:2030) mättes på www.
- **Varför inte bara (ii).** En merge deployar sig själv inom ungefär två timmar, och boxens LinkedIn är aktiv, så mergen blir själva experimentet. Konsekvenserna:
  - ett vägrat par bryter varje LinkedIn-inloggning tills en andra PR har landat;
  - valet mellan (a) och (b) görs i blindo;
  - (23) och §3d skulle bära omätt text.
- **(ii) Efter merge, skyldig i båda fallen**, som daterad kommentar på #1732 som nästa amendment transkriberar:
  - api **och** web på images vars `:sha-<short>`-digest härstammar från mergen;
  - web-starten, läst inifrån web-containern: en 302 till `https://api.linkedin.com/oauth/v2/authorization` med exakt de fem nycklarna och samma värden som i dagens punkt, plus flödescookiens attribut;
  - två inloggningar i rad av Klas, inloggad på LinkedIn och med webbläsaren namngiven (Amendment (15):s WebKit-residual): inloggningssidan båda gångerna, ingen tillåtelsesida;
  - sedan api:ts recreate: 2 × `login_succeeded … Method=LinkedIn`; 0 × 1021, 1022, 1023, 1024 och 1025; `AspNetUserLogins` oförändrat `github:1,google:1,linkedin:1`; 0 nya `User.ExternalLoginLinked`; 2 konton, båda den personuppgiftsansvariges, 0 skapade;
  - en Cancel som ger notisen och 0 api-rader.

**[Viktigt] R7: Amendment (23), i samma ordning som (20)–(22).**
1. Ett kursivt förord: formrundan och dess rapporter, markeringarna på plats nedan och "this block records why".
2. Klas ord ordagrant: "undersöka detta i ny session" (2026-10-03), och hans svar på Q4 med fråga och alternativtext, så som (22) registrerar dem.
3. Dokumentationen, läst 2026-10-03 (§9.5):
   - "3-Legged OAuth Flow" (bara www);
   - **båda** discovery-dokumenten (api-hostens namnger www för authorization och token);
   - Supabases `linkedin_oidc.go` (api för båda, ingen `prompt`);
   - #50831 (öppen, rapporterad som bugg).
4. Mätningarna: dagens fyra prober och box-fönstret, från #1926:s kommentar eftersom session-loggen saknas. Sedan läsningarna från (i), inklusive redirect-matchningen på api-hosten.
5. Formen (`dotnet-architect`, bindande): R1–R4, och det par som (i) mätte.
6. Fail-safe och upptäckt: R5:s tabell, i prosa.
7. `security-auditor`s text ordagrant, avslutad med "(End of …)".
8. **LinkedIns triggers, med ett enda hem.** Hem: den här amendmenten. Läsare: Klas Olsson. Inget upptäcker (T1) eller (T3) automatiskt.
   - (T1) En inloggning av en medlem som är inloggad på LinkedIn visar ingen inloggningssida → revert till den dokumenterade hosten i en PR.
   - (T2) EventId 1022 vid ett LinkedIn-exchange, eller en LinkedIn-felsida vid authorization → avaktivera enligt §3d, och reverta sedan.
   - (T3) LinkedIn dokumenterar en parameter som frågar → www plus den parametern. Triggern flyttas hit från (22).
   - (T4) Bara under (b): registrets lapse (4), så som hon läser den.
9. Lapse-triggers lästa för den här PR:en, så som (22) avslutas.
- **Markeringar på plats:**
  - (22) :2244, "honoured no undocumented one": proben gällde `prompt`-värden, och nu frågar en odokumenterad host.
  - (22) :2277-2278, "The question returns … Home: this amendment": flyttad till (23), så att triggern har ett enda hem.
  - (21) :2030: en framåtpekare till läsningen från (i) på api-hosten.
  - Under (b) också (20) :1769: "the pinned endpoint" är nu api:s.
- **Redigeras aldrig:** de daterade www-läsningarna (:1824, :2154 och §3d:s Cancel från 2026-10-02). De är proveniens.
- README :87 får en "(23)"-klausul i radens svenska form. Implementation status :3567 får PR:en och (23).

**[Viktigt] R8: `vps-deploy-stack.md` §3d, LinkedIn-avsnittet.** Luckor inom hakparentes fylls bara från namngivna läsningar.
- **:728**, "Three things differ", blir fyra: "And its authorization request goes to LinkedIn's api host, which LinkedIn does not document (ADR 0142 Amendment (23))."
- **:779-781**, web-start-punkten: hosten blir `https://api.linkedin.com/oauth/v2/authorization`, resten är oförändrat.
- **:801-804**, "A repeat login asks nothing", ersätts av:
  > **Every login asks for the sign-in.** The start sends the browser to LinkedIn's api host, not the documented www host (ADR 0142 Amendment (23)). There LinkedIn showed its sign-in page to a member already signed in to LinkedIn in that browser at each login read [dates, count], and no permission page to a member with a grant. Someone at a shared computer chooses the account by signing in. If a LinkedIn login by a member signed in to LinkedIn shows no sign-in page, Amendment (23)'s (T1) has fired: tell Klas the same day.
- **:795-799**, felstycket, får en mening till: "After the host change, EventId 1022 with `TokenInvalidRequest` or `TokenRefused`, or a LinkedIn error page instead of the sign-in page, is (23)'s (T2): deactivate at once and keep the keys out until the revert has merged."
- **:806-807**, "A Cancel": behåll www-läsningen från 2026-10-02. Lägg till "[on the api host: read on <date>, landing <where>]" först när den är läst.
- **Utrullningsläsningen:** ett eget stycke med listan från R6 (ii), eller listan tillagd under "The reading" med "after #<PR>".

**[Viktigt] R9: en PR. Den går på `automerge`, med ett ordningsvillkor under (b).**
- **Ett change-reason:** LinkedIn frågar efter konto vid varje inloggning (#1926 för LinkedIn).
  - Konstanten, mirrorn, pinnarna, (23), markeringarna, §3d, README och Implementation status går tillsammans.
  - Adaptern och web-mappen kan inte delas upp: var och en ensam fallerar stängt vid varje LinkedIn-start, och mirror-testet blir rött.
- **Ordning:** Klas svar på Q4 → kod → (i) → paret väljs → docs → panelen → merge → (ii).
  - Väljer han 2 eller 3 i Q4 blir det ingen kod-PR. Dagens prob står kvar i #1926:s kommentar, och en pekare i (22) följer med nästa auth-kod-PR (ADR 0065 förbjuder en PR med bara docs).
  - Under 3 är avaktiveringen ett box-steg utan diff.
- **`automerge`:** inget §5-antimönster, ingen gräns, inget bibliotek, ingen token. En säkerhetskritisk ändring med tester och `security-auditor`s APPROVE mot den slutliga diffen går det vanliga flödet (förtydligandet i AGENTS.md §12). Den rör ingen rad som M-1, Major 2 eller Major 4 citerar.
  - **Under (b):** läser hon lapse (4) som utlöst måste registrets egen åtgärd ("Kap. V läses om innan LinkedIn fortsätter", :1699) vara klar **före merge**, eftersom mergen är utrullningen.
- **Panel:** `security-auditor`, samt `code-reviewer` + `dotnet-architect` eftersom fler än fem filer ändras. `test-writer` triggas inte, eftersom ingen ny domain-typ eller handler tillkommer. Registret är gitignorerat, så det hon beslutar där skrivs i huvudkopian.
- **Ett misslyckat (ii)** är en defekt i levererad kod och filas, med reverten som egen PR.

**[Nice-to-have] N1:** `route.test.ts` får en rad till: "a LinkedIn start answered with the www endpoint" → `/logga-in`, LinkedIn-notisen och ingen state-cookie.
- Tillståndet kan produceras: det är vad api:ts föregående image svarar den nya webben under en utrullning (R5). Raden är alltså inte deklarerat onåbar, och aktören namnges i raden.
- Raden pinnar R5:s påstående om utrullningen. Den generiska raden för annan host (:138) täcker det bara för Google.

**[Nice-to-have] N2:** vägen utan grant på api-hosten är det en riktig testanvändare möter, och i dag mäter inget den: första inloggningen, med inloggning och sedan kanske tillåtelsesidan.
- Det kan läsas valfritt i (i): Klas återkallar appens grant hos LinkedIn före den första lokala inloggningen.
- Granten tillhör den delade klienten, så återkallelsen är en och samma handling för lokalt och box. Den parvisa `sub` är per klient och bör inte flytta, men bekräfta det med boxens oförändrade `linkedin:1` i (ii).
- Tills den vägen är läst får ingen text beskriva tillåtelsesidan på api-hosten.

**[Nice-to-have] N3:** briefen namnger en session-logg som saknas i worktree.
- (23):s mätstycke citerar därför #1926:s kommentar från 2026-10-03, som sessionen läser själv.
- Supabases radnummer (briefens :13, 52-53, som min fetch inte bekräftade) läses om eller stryks.

### Referenser
- AGENTS.md §2.1, §5 (`Tests:`, `Comments:`) och §12; CLAUDE.md §9.5 och §9.6; ADR 0065.
- ADR 0142:
  - D8 (:670-725);
  - (20) :1764-1787 och :1824;
  - (21) :2020-2039;
  - (22) :2116-2377, särskilt :2244-2278;
  - Implementation status :3567;
  - `docs/decisions/README.md:87`.
- `docs/runbooks/vps-deploy-stack.md` §3d :726-810; `docs/runbooks/gdpr-processing-register.md` :1673-1703.
- Min formrapport `docs/reviews/2026-10-03-1926-form-dotnet-architect.md` (R1, R3, R5a, N1).
- Läst 2026-10-03:
  - Supabase `internal/api/provider/linkedin_oidc.go` (master);
  - `https://api.linkedin.com/oauth/.well-known/openid-configuration`;
  - briefens citat från LinkedIns "3-Legged OAuth Flow" och www-discovery-dokumentet, samt Supabase #50831.

Verdict-table line: "dotnet-architect, #1926b form round (report-only): OK as a form under conditions, 0 Kritiskt / 9 Viktigt / 3 Nice-to-have. The constant keeps its name and home and gets its own pointer comment; (a) authorization-only by preference, (b) only on a measured refusal of (a); `UserInfoEndpoint` stays; the four pins move to the api host and the architecture literal is kept and renamed; measure locally before merge (i), including api-host redirect matching, and on the box after (ii); triggers T1–T4 in one home, (23); one PR, `automerge` on `security-auditor`'s final-diff verdict, and under (b) her lapse-(4) reading before merge. Q4 escalated to Klas."

Eskalering till Klas: ja. Q4, att vidarebefordra ordagrant:

> **Ska LinkedIn visa sin inloggningssida varje gång någon loggar in hos oss med LinkedIn?**
>
> **1. Inloggningssidan varje gång.** Vi skickar LinkedIn-inloggningen till en adress hos LinkedIn som LinkedIn inte dokumenterar (api.linkedin.com). Där visade LinkedIn i dag sin inloggningssida trots att du redan var inloggad på LinkedIn, och därefter ingen tillåtelsesida. Vid en delad dator väljer man alltså konto genom att logga in på LinkedIn med sitt eget lösenord. LinkedIn frågar efter kontot, inte om tillåtelse. Varje LinkedIn-inloggning kostar ett lösenord mer, mot målet så få klick som möjligt. I dag var din e-postadress redan ifylld. Vems adress som står där vid en delad dator är inte mätt. LinkedIn kan sluta visa sidan, eller stänga adressen, utan förvarning, och en annan tjänst har rapporterat samma beteende som ett fel hos LinkedIn. Inget märker det automatiskt. Slutar sidan visas går vi tillbaka till den dokumenterade adressen. Slutar inloggningen fungera stängs LinkedIn av tills det är rättat. Före merge loggar du in två gånger lokalt och efter merge två gånger på servern. Går inloggningen inte igenom i mätningen före merge gäller 2, utan att du behöver svara igen.
>
> **2. Som i dag.** Vi behåller LinkedIns dokumenterade adress. Den som har godkänt Jobbliggaren förut skickas vidare utan någon sida. Vid en delad dator loggas den som klickar på LinkedIn in i kontot för den som fortfarande är inloggad på LinkedIn i webbläsaren, och ser det kontots cv och ansökningar. Den som vill använda ett annat LinkedIn-konto loggar först ut från LinkedIn. Inget beror på en odokumenterad adress, och det står kvar som en begränsning hos LinkedIn.
>
> **3. Ingen LinkedIn-inloggning.** LinkedIn-nycklarna tas bort på servern, och ingen kod ändras. LinkedIn-raden står kvar på inloggningssidan men går inte att välja. Google och GitHub är byggda för att fråga efter konto vid varje inloggning, och e-postkoden finns kvar. Din LinkedIn-koppling ligger kvar och fungerar igen om nycklarna läggs tillbaka.

(Arkitekturen bär alternativ 1, 2 och 3 utan att någon gräns flyttas. Valet gäller produkten, och därför sätter jag ingen rekommendationsmarkering.)

Filer:
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\lib\auth\external-login.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\start\route.test.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\src\app\api\auth\oauth\[provider]\callback\route.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Architecture.Tests\ExternalLoginMirrorWireContractTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Application.UnitTests\Auth\ExternalLogins\LinkedInIdentityProviderTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Jobbliggaren.Api.IntegrationTests\Auth\LinkedInLoginTests.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\tests\Shared\ScriptedLinkedIn.cs`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\web\jobbliggaren-web\tests\e2e\oauth.spec.ts`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\.github\workflows\e2e.yml`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\README.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\runbooks\vps-deploy-stack.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\runbooks\gdpr-processing-register.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\sessions\2026-10-03-1926b-linkedin-api-host-form-brief.md`
