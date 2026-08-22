# security-auditor — PR #1438 (#1349)

- **Agent:** `security-auditor` (§9.2, auth-yta + GDPR-framing)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, HEAD `17ba6a4c`, bas `076851fd`. 8 filer.
- **Omkontroll skope:** fix-delta `d8179433`, rapport-only
- **Status runda 1:** BLOCKED — **0 Blocker, 1 Major, 1 Minor**
- **Status omkontroll:** Major **EJ STÄNGD** av `d8179433`; stängd av den efterföljande strykningen (hennes
  variant (a)), som är mekanisk — noll tillagda rader, ingen ytterligare omkontroll skyldig
- **Auktoritet:** GDPR Art. 4(1), 5(1)(a)(b)(c)(e), 12(1), 12(2), 25(1), 32 · §5 `Comments:`/`Security:` · §9.6

**Område 8 (supply-chain) TRIGGADE INTE** — mätt: ingen av suppression-ytorna (paketmanifest, låsfil,
workspace, dependabot-config, audit-nivå, NuGetAudit, `Directory.*.props`, pnpm-action-pinnen, `.github/`)
finns i diffens filnamnslista. **Deklarerad skip, inte tyst utelämnande.**

## Blockers

Inga. Ingen GDPR-överträdelse, ingen secret leak, ingen auth bypass, ingen PII-exponering, ingen RCE.

## Major

**1. Art. 25(1)-meningen påstår att den omvända orphan-raden saknar adress. Det är falskt.**
— `AccountHardDeleter.cs:32-33`. Mätt: en reverse orphan bär den registrerades **egen** e-postadress —
`PersonalInfo.Email` persisterad i `resume_versions.content_enc`, och `ParsedContact.Email` i
`parsed_resumes`, vars egen docstring säger *"This is CV-PII"*. Båda ligger i JobSeeker-kaskaden.
**Samma fil motsäger sig själv 67 rader ned:** reverse-orphan-detektorn säger *"no name/email/CV PII is
logged"*, vilket förutsätter att raden bär det. **Och CTO-rapporten meningen citerar säger motsatsen i sin
egen grund:** *"The single remaining trace sits inside the DEK-encrypted CV content."*

*"No address at all"* säger att raden är icke-identifierbar → ingen Art. 15/17-exponering, ingen
Art. 5(1)(e)-klocka. Det uppmätta läget är att den **är** identifierbar, bara genom att dekryptera den
registrerades CV — ett Art. 5(1)(b)-problem, inte en omöjlighet. Två olika rättslägen, och det falska
underskattar exponeringen hos precis den felmod meningen kontrasterar mot.

`content-free` är den svagare halvan: bredvid en Art. 25(1)-citering läser det som "inga personuppgifter",
medan Identity-raden bär Email, NormalizedEmail, UserName och PasswordHash — samtliga personuppgifter
(Art. 4(1)). Den **jämförande** utsagan är SANN och är den som ska överleva.

⚠ **Rör INTE `docs/reviews/2026-08-22-orphan-model-cto.md`.** Samma påstående finns i dess grund, men en
promotad granskningsrapport är ett daterat protokoll; att redigera det förstör dess bevisvärde.

## Minor

**2. R1 är rätt graderad som accepterad rest — men dess disposal vilar på ett villkor som inget upprätthåller.**

**Transparensfrågan, avgjord:** den nya copyn ÄR Art. 12(1)/5(1)(a)-tillräcklig. *"Din e-postadress är
bekräftad"* är sant för **varje** population inklusive forward orphan — den raden är just den som har
`EmailConfirmed` satt och ingen profil. **PR:en flyttar Art. 5(1)(a)-läget från brott-format till
compliant-format.**

Resten är ingen GDPR-defekt, och det är mätt: (i) den uniforma 401:an är en ratificerad Art. 32-åtgärd, och
Art. 12(1) styr Art. 13/14-information samt Art. 15–22/34-kommunikation, inte ett autentiseringsfel;
(ii) Art. 12(2) hindras inte i sak — raden självraderas inom ≤25 h, vilket frigör den UNIQUE e-postadressen;
(iii) Art. 5(1)(e) uppfylls av samma ≤25 h. **Glappet är smalare än R1 skriver:** efter svepet ger länken
uniform 400, FE kollapsar varje 4xx till `invalidBody`, och den strängen säger *"Registrera dig igen för att
få en ny länk."* Kvar är bara fönstret mellan bekräftelse och svep, och bara på login-ytan.

**Hålet:** §9.6 säger uttryckligen att en rad i en PR-body inte är disposal — den har ingen läsare. Det som
bär R1 är HEM C-följd-PR:n, vars villkor upprätthålls av ingenting.
**Krävs:** skapas HEM C inte innan sessionen slutar → fila R1 som issue med `area:auth`, `P2`, `BE+FE`,
**`mvp`**.

## Praise

- Anti-enumerering (#714) **orörd, mätt på tre ytor**: skärmens split drivs enbart av tokenvaliditet,
  `/verify-email` ger fortfarande uniform 400, och mejlets subject är oförändrat.
- Preheadern röjer **strikt mindre** i inbox-previewen än förut; ingen ny upplysning om kontostatus.
- Den nya pinnen läser **båda** renderingarna och öppnar med en counterfactual, och stänger ett verkligt hål:
  alla 8 befintliga assertions läste bara plaintext-renderingen.

## Omkontroll (delta `d8179433`) — Major EJ STÄNGD

*"The smaller data footprint of the two"* håller i raderingsriktningen, **inte** i registreringsriktningen:

| halva | vad raden bär |
|---|---|
| kreditiv (**överlevaren**) | adressen **fyra gånger** — `UserName`, `Email`, `NormalizedUserName`, `NormalizedEmail` — samt `PasswordHash`, `SecurityStamp` |
| domän (spegeln) | ett `DisplayName`, en `UserId`-Guid, tom `Preferences`, `MatchPreferences.Empty`, `CreatedAt`. **Ingen adress. Noll DEK:ar** |

På den enda axel Art. 5(1)(c) graderar är kreditiv-halvan här den som bär den direkta kontaktidentifieraren
och kredentialhemligheten. *"The smaller data footprint"* pekar snarast åt andra hållet.

**Fråga 2 — citeringen bär inte längre sin vikt.** Art. 5(1)(c) hänger enbart på den klausul som fallerar.
**Art. 25(1) är den skarpare halvan:** i registreringsriktningen är ordningen **inte ett designval** —
`JobSeeker.Register` avvisar ett tomt `userId` och tar det som `CreateUserAsync` returnerar, så domänhalvan
**kan inte** skrivas först. "By design" citeras för en ordning framtvingad av ett databeroende.

**Verdikt: variant (a) — stryk hela meningen.** Noll tillagda rader, mekanisk stängning, ingen ytterligare
omkontroll skyldig. *(Icke-åtgärd, hör hemma i HEM C/R1: den egenskap som faktiskt håller åt båda hållen är
sopbarhet, inte storlek. Föreslå ingen tredje prosavariant — taket är slut.)*

**Minor 2 STÄNGD** (accepterad routing). **Övriga agenters fixar: inga nya Blocker/Major.** Den nya literalen
korsgardar dessutom mot fel-mall-regression.

**#714 — ingen yta blev tillståndsberoende.** Komponenten läser **inget kontotillstånd**; den nya strängen
lägger till ingen gren och ingen fetch. Ytan renderas identiskt för giltig token, ogiltig token, redan
bekräftad adress och obefintlig användare.

## Eskaleringar

**Till Klas: nej.** Ingen GDPR-Blocker och inget område-8-Major föreligger. Ingen accepterad-risk-väg enligt
§9.6 (2) eller (3) är begärd, behövd eller signerad.
