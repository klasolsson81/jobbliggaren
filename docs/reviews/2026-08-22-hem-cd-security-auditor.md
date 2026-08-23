# security-auditor — PR #1442 (#1349, HEM C + HEM D)

- **Agent:** `security-auditor` (§9.2, auth-ytor + anti-enumerering)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan` · **Skop:** `6a03fee3...e8230193`, 8 filer
- **Runda 1:** ⛔ BLOCKED — **0 Blocker, 1 Major, 1 Minor**
- **Auktoritet:** GDPR Art. 5(1)(a), 12(1), 32 · #714 · §9.6, §6 · §12 · CTO-bindet Tillägg (2)

## Mätningar

**1. Anti-enumerering (#714): orörd. Subject-bytet röjer strikt MINDRE.** HTTP-svaret är byte-identiskt
— diffen rör inte `RegisterCommandHandler`, `AuthEndpoints`, `UserAccountService` eller
`LoginCommandHandler`. Egenskapen är **pinnad och lever** (`RegisterConfirmationTests` asserterar
`takenBody.ShouldBe(freshBody)` plus identisk status). Båda subjecten röjer samma enda bit till den som
ser ämnesraden; det gamla la till ett påstående om **personen**, det nya säger bara något om
**adressen**. Preheadern tappade dessutom ett kontoägarskapspåstående. **Min beröm i #1438 gällde att
subjectet var oförändrat; ommätt håller den inte som invändning mot bytet.**

**2. HEM D: samma oracle-yta, varken mer eller mindre.** `AuthEndpoints` lägger redan `title:
"Auth.EmailNotConfirmed"` maskinläsbart på wire, så `detail`-formuleringen kan inte tillföra
information. Grinden är fortsatt lösenordsgated; re-auth normaliserar alltjämt till uniform 401.

**3. Pinnen är rätt formad, och tätare än PR-bodyn påstår.** Fyra vägar runt mätta: ny parameter faller;
**overload mätt empiriskt** (`GetMethod` kastar — probe körd, inte antaget); `static readonly` faller;
`EmailTemplates` är inte partial. Kvarvarande hål (annat-namngivet syskon) är stängt **av konstruktion**
— porten har ingen tillståndsparameter och `ToErrorResult(DomainError)` är `static` med enbart felet i
scope.

**4. Art. 12(1): sann för alla tre populationerna.** *"Ingenting har ändrats"* är sant i sak —
duplicate-grenen skriver **ingenting** till domänen — och bär försäkran **starkare**: den svarar på
samma oro utan att presupponera kontoexistens.

**5. R1: villkoret uppfyllt i sak. Jag släpper fallbacken — fila ingen issue.** Villkorssatsen är
tillståndsoberoende, mejlet byte-identiskt, och den routar till en **verklig** adress som ett test
speglar mot den publicerade kontakten. Populationen nås faktiskt.
⚠ **Gränsen exakt:** rutten når bara den orphan som **registrerar om sig**. Login-sidan har enbart en
`/registrera`-länk, ingen kontaktväg. *"Delvis stängd"* är den enda korrekta formuleringen.

**6. Område 8: KÖRD, inte skippad.** Diffen rör noll suppression-filer, men guarden kördes ändå per
charter: `pnpm --version` = 10.28.2 (alltså inte `SKIPPED`), resultat **`no findings`**.

## Major

**1. Levande E2E-assertion pinnar den HEM D-sträng PR:en just raderade — tredje hemmet, missat.**
`tests/e2e/auth.spec.ts` bär konstanten och asserterar den live. **Varför Major:** det som går sönder är
**#714:s egen regressionsvakt på login-403-ytan** — precis den yta PR:en ändrar — och eftersom
copy-assertionen ligger först faller assertionerna nedanför också ur körningen. **Att det är tyst gör det
värre:** jobbet är observe-only, och automerge dämpar observe-only ytterligare. *"En säkerhetsvakt som
slutar köra utan att någon grind rodnar är inte en testdetalj."*
PR-bodyns svep är *"sant om sitt angivna skop och falskt om sitt ämne"*.

## Minor

**1. `EmailTemplatesAccountExistsNoticeTests` läser bara `PlainTextBody`.** Samtliga sex
innehållsassertioner, inklusive `ShouldNotContain("token=")` / `("bekrafta-konto")` — som klassens egen
docstring kallar load-bearing. **Egenskapen håller idag** (mätt), så det är avsaknad av vakt, inte
exponering → Minor. Men det är **samma hål jag i #1438 skrev ut och berömde den PR:en för att stänga** —
stängt i syskonmallen, öppet här.

## Praise
- Anti-enumereringen ommätt på fyra ytor och orörd.
- Den strukturella pinnen är den rätta formen på förbudet och tätare än den utger sig för.
- `LoginEmailConfirmationTests` asserterar mot konstanten — därför kunde BE-sidan inte drabbas av Major 1,
  och det är mönstret e2e:n borde ha följt.

## Systemisk observation (ej eskalering, men hör hemma i PR-bodyn)
`e2e.yml` är observe-only, så en trasig E2E-assertion på en auth-yta producerar **ingen** blockerande
signal, och automerge dämpar ytterligare. **Major 1 hade gått omärkt in i main** om svepet inte körts om
mot hela repot i stället för mot PR-bodyns angivna skop.

## Eskaleringar
**Till Klas: nej.** Ingen GDPR-Blocker, ingen accepterad risk, inget område-8-fynd.
