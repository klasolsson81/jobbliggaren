# code-reviewer — PR #1442 (#1349, HEM C + HEM D)

- **Agent:** `code-reviewer` (§9.2, sista kvalitetsgrind)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan` · **Bas:** `6a03fee3`, trepunkt
- **Runda 1:** ⚠ Changes requested — **0 Blocker, 4 Major, 2 Minor**

## Major

**1. HEM D har ett TREDJE hem, och det är osynligt i CI** — `web/jobbliggaren-web/tests/e2e/auth.spec.ts`
bär `LOGIN_403_COPY` som **tracked literal** och asserterar den live. Ingen väg i `src/` eller
`messages/` producerar strängen längre.
**Skärpningen:** `e2e.yml` kör på `pull_request`, jobbet heter *"playwright e2e (observe-only)"* och är
`continue-on-error: true`, utanför `ci`-aggregatet → **brottet rapporteras som success och blockerar
ingenting.** #791:s regressionstest dör tyst. Och eftersom copy-assertionen ligger **först** i testet
faller #733- och #791-assertionerna nedanför den ur körningen.
⚠ **PR-bodyns svep namnger sitt eget skope** — *"src, tests, messages och web/src"* —
och `web/jobbliggaren-web/tests/` ligger i inget av dem. **Det är fail-open-deklarationen, inte en
slarvmiss.**
Kontrast som gör fyndet lätt att missa: BE-sidan har det rätt — `LoginEmailConfirmationTests`
asserterar mot **konstanten**, inte literalen, och följer därför med automatiskt.

**2. Fel grind namngiven, och fel citat på bindet** — den nya `AuthErrorCodes`-docstringen kallade
403:an en *"unauthenticated surface"*. Mätt: den är nåbar **endast efter korrekt lösenord**, vilket
`UserAccountService` och **samma fils docstring 13 rader upp** båda säger. Och bindet nycklar const-pinnen
till **förbud 2** (förgrena inte på profilnärvaro), inte till någon oautentiserad-yta-grund — den grunden
är bindets OUT (d) och gäller en annan yta. §5 `Comments:` (*"wrong gate name"*). → ren strykning.

**3. Testkommentaren påstår ett orakel bindet mätte bort** — *"…so state-dependent copy on it is an
account-existence oracle."* Premissen är sann om **grenen**; slutsatsen falsk därför att **utkanalen är
ägarens inkorg**. Motsagt **fyra gånger** i repot, inklusive av bindets egen mätning. Det är ett
**säkerhetspåstående**, vilket gör det dyrare än vanlig prosa. → ren strykning.

**4. `IsInitOnly.ShouldBeFalse()` är vakuös** — `static readonly` fälls redan av `IsLiteral.ShouldBeTrue`
en rad ovanför, som kastar först, och ingen C#-källa producerar ett fält som är både `literal` och
`initonly`. Raden **kan inte falla**, och dess meddelande namnger just det fall raden ovanför äger.
⚠ **Mutationen träffade bara parameter-armen** — fyra av fem assertions var omuterade, och den vakuösa
låg bland dem. → strykning. **Inte** en §5 `Tests:`-Blocker: assertionen vilar på fältets verkliga
metadata; det är utfallet som är omöjligt, inte premissen.

## Minor
**5.** Text/HTML-pariteten är fortfarande opinnad — bindet ⚠-markerade den tre gånger. Jag verifierade
den för hand den här ronden; **att en granskare måste göra det är signalen att pinnen saknas.**
**6.** Vakten är namn-nycklad, inte form-nycklad. Fångas: overload (`AmbiguousMatchException`),
`static readonly`, property, rename. Fångas **inte**: en syskonmetod eller en växt port-signatur.
Räckviddsgräns, inte defekt.

## Bra gjort
- **Alla nio renderingarna är faktiskt ändrade**, inklusive `title:` — hemmet uppräkningen missade förra
  gången.
- **Text och HTML är ordagrant lika efter diffen**, mätt genom normaliserad extraktion av båda halvorna.
- Tre andra testfiler refererar konstanten/nyckeln i stället för literalen — därför bar bara ett hem en
  stale sträng, inte fyra.

## Mätt utan fynd
§5 `Tests:` fyrar inte på reflektionstestet. `AccountExistsNotice`s docstring överlever. `#1369` är rätt
nummer. **Inget fjärde hem** för HEM D utöver e2e:n — svep på morfologin över tracked-filer, inga
snapshots.

## Eskaleringar
Inga.
