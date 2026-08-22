# code-reviewer — PR #1438 (#1349)

- **Agent:** `code-reviewer` (§9.2, mandatory: >5 files)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, HEAD `17ba6a4c`, bas `076851fd` (trepunkt, färsk bas)
- **Omkontroll skope:** fix-delta `d8179433`, rapport-only
- **Status runda 1:** ⚠ Changes requested — **0 Blocker, 1 Major, 3 Minor**
- **Status omkontroll:** ✓ alla fyra stängda, 0 new-in-delta

## Major

**1. "which carries every DEK" är falskt för den ena av de två riktningar meningen påstår sig täcka**
— `src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs:33`. DEK skapas **lazily** vid första
krypterade skrivningen (`UserDataKeyStore.cs:32` `GetOrCreateDataKeyAsync`), och registreringsvägen rör
ingen DEK-port alls — noll träffar på `IUserDataKeyStore`/`IDataKeyProvider` i `Application/Auth/`.
Spegel-överlevaren i **registreringsriktningen** är en nyss skapad `JobSeeker` med `DisplayName` och
**noll DEK:ar**. Klausulen är sann enbart i raderingsriktningen. §5 `Comments:` — en faktiskt felaktig
kommentar är en defekt och lagas. **Grunden själv faller inte**; det är attributet som överreacher.
*Delegerat till dotnet-architect.*

## Minor

**2. Testnamnet påstår en egenskap; assertionerna mäter två literaler** —
`EmailTemplatesEmailConfirmationTests.cs:83`. En omformulerad utfästelse passerar grönt. Kollisionsfrågan
mätt och besvarad: `"skapat ett konto"` kolliderar **inte** med avslutningsraden ("skapat **något** konto");
en kollision hade dessutom gett falskt **fail**, inte fail-open. *Delegerat till test-writer.*

**3. "makes this the renderings-agree check at the same time" överskopar** — samma fil `:99`.
Counterfactualen pinnar två meningar, inte parity; avslutningsraden och sign-off är fortfarande oparade.

**4. Docstring-omslaget lämnar en trasig rad** — `EmailTemplates.cs:478-479`, bryter mitt i en parentes.

## Observation (ograderad)

**HEM D existerar och är oberört:** `AuthErrorCodes.EmailNotConfirmedMessage` + `auth.actions.emailNotConfirmed`
— *"Bekräfta din e-postadress för att logga in."* Mätt reachability: `ValidateCredentialsAsync` returnerar
`EmailNotConfirmed` och kortsluter i `LoginCommandHandler.cs:23-38` **före** JobSeeker-vakten. En obekräftad
orphan får alltså den meningen, inte den uniforma 401:an, och för honom är bekräftelsen inte tillräcklig —
samma semantik som den strukna mejlmeningen. **Graderas inte**; CTO OUT (g) lägger login-failure-ytan i ett
eget change-reason. Routing är `senior-cto-advisor`s.

## Bra gjort
- Halv-strykningen är faktiskt hel; renderingarna mäter ordagrant lika efter diffen.
- Counterfactualen är verklig: `HtmlEncoder.Create(UnicodeRanges.All)` låter åäö överleva oescapade.
- i18n-paritet 448/448 nycklar, noll drift; zero-svepet håller.

## Omkontroll (delta `d8179433`)
1. Major 1 **STÄNGD** — mekaniskt genom strykning; restklausulen tillhör `security-auditor`, som namngav den.
2. Minor 2 **STÄNGD** — namnet namnger nu de strukna claim-strängarna; kollisionsfriheten ommätt mot mallen.
3. Minor 3 **STÄNGD** — struken, inte omformulerad.
4. Minor 4 **STÄNGD** — inga parentespar spänner längre en radbrytning.
**New-in-delta:** inga. Två andras fixar mättes och håller (symbolpekaren `#508 grace filter` är ett verkligt
namngivet hem; producent-1-vidgningen är sann mot `UnitOfWorkBehavior`, och båda pinnarna existerar).

## Eskaleringar
Inga.
