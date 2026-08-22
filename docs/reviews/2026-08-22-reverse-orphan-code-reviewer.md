# code-reviewer — PR #1439 (#1409)

- **Agent:** `code-reviewer` (§9.2, sista kvalitetsgrind)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, bas `4c8ad6e1` · **Omkontroll:** fix-delta `c40b1e7a`
- **Status runda 1:** ⚠ Changes requested — **0 Blocker, 3 Major, 4 Minor**
- **Status omkontroll:** 3 Major stängda, 4 Minor stängda/skip-accepterade, **1 new-in-delta Major**
  (stängd mekaniskt av `0e195866`)

## Major (runda 1)

**1. Diffen gör en levande §-pekare stale — och den pekar nu in i en container vars andra halva
RADERAR.** `docs/runbooks/account-deletion.md:21` sade *"manuell SQL-procedur i §4 tills dess"*. Före
diffen löste §4 exakt; nu är §4 en container med §4.1 (restore) och §4.2 (radering). En operatör som
under press följer "restore-proceduren i §4" landar i en sektion vars andra halva är irreversibel.
**Detta är axeln sessionen svepte fel:** mätningen *"ingen kod citerar §4"* är KORREKT — den enda
sektionspekaren i tracked kod är `AccountHardDeleter.cs → §3.2`, orörd — men **den enda levande
§4-pekaren ligger i runbooken själv.** Ett kompletterande repo-vitt svep (inklusive gitignorerat, alltså
axeln `git grep` inte ser) gav 61 träffar på `account-deletion`, varav **exakt en** bär en sektionspekare.
Fixen är därmed komplett, inte en fix på ett hem av N.

**2. Steg 3:s verifiering är falsk för Steg 2:s rekommenderade default-variant.** Med
`deleted_at = NOW()` plockas raden **inte** upp vid nästa 04:00 — bara den backdaterade varianten
raderas nästa pass. Felmoden är konkret: operatören triggar manuellt, ser raden kvar, drar slutsatsen
att proceduren failade — och närmaste utväg är precis den hand-rullade SQL-kaskad ⚠-blocket förbjuder.

**3. "får aldrig `deleted_at` satt" är obetingat skrivet, och Steg 2 kan därför nollställa en löpande
raderingsklocka.** Den ena halvan håller — påståendet är sant för **varje producent i `src/`**, och alla
tre Identity-raderingsställen gicks igenom. Falskt är **räckvidden**: dokumentet inför själv den manuellt
raderade Identity-raden, och §3.3:s query har **ingen `deleted_at`-filtrering** och **selekterar inte**
`deleted_at`. Steg 1 kan alltså lämna över ett `js.id` vars 30-dagarsfönster redan löper, osynligt, och
Steg 2:s obetingade UPDATE startar om det: **en rad som var 29 dagar gammal får 30 nya.** Asymmetrin är
defekten — tombsten-påståendet har redan en carve-out, `deleted_at`-påståendet inte. Billigaste
stängningen lägger till noll påstående-meningar: `AND deleted_at IS NULL`, så noll rader blir signalen.

## Minor (runda 1)

**4.** `DELETE /me` är ett endpoint som inte finns (`POST /me/delete`), men strängen finns redan i fyra
pre-existerande hem — följd-PR, inte in-block. **5.** `2592000` ruttnar: enda pinnen asserterar `>=`, så
en höjd default går tyst. **Stabila:** `#508`/`#1409`, `EventId 2503`, `AccountHardDeleteCascadeFitnessTests`,
`SessionStoreOptions.DeletionTombstoneTtl` som symbol. **Ruttnar:** `2592000`, `04:00 UTC`
(`Cron.Daily(4)` är **inte** schemapinnad). **6.** ~5 reducerbara rader — ⚠-blockets motivering
om-argumenterar ett beslut CTO-bindet redan äger. **7.** "raderats ur banden" läses som backup-band.

## Omkontroll (delta `c40b1e7a`)

**Major 1–3 STÄNGDA**, alla ommätta i koden. **Minor 4 SKIP-ACCEPTERAD** — `grep -c 'DELETE /me'` ger
**5 på HEAD och 5 på `origin/main`**, ingen femte instans tillagd. **Minor 5 SKIP-ACCEPTERAD** — fyndet
står, körbarhetsskälet håller, `04:00 UTC` struket ur §4.2. **Minor 6–7 STÄNGDA.**
**Verifieringspunkter:** CRLF 450/450, 0 rena LF; §5–§8 orörda; `§4.2`-pekaren korrekt.

### New-in-delta Major (stängd mekaniskt av `0e195866`)

**Påståendet om 2503-signalen är faktiskt fel, och motsäger sin egen åtgärd.** Texten sade *"Warningen är
count-only, så en **ny** reverse-orphan i samma fönster är osynlig i signalen (1 → 2 går inte att
skilja)"*. Mätt: EventId 2503 emitterar `{Count}`, så 1 → 2 syns som ett ändrat tal. Det som *är*
osynligt är **vilken** rad. Nästa mening (*"så nästa läsare kan räkna bort den"*) förutsätter dessutom
att talet går att läsa — motsatsen till satsen den motiverar. **Stängd genom ren strykning** (noll
`+`-rader); åtgärdsmeningen står korrekt på egen hand.

## Bra gjort
- Art. 11.2/12.6-armen är juridiskt korrekt citerad, och **hem + läsare** är namngivna mot en verifierad
  ROPA-post som ordagrant lägger klartext-identifieraren i den personuppgiftsansvariges ärendeakt.
- `EXISTS`-raden plus gemen-GUID-varningen är mätbart korrekta mot `UserDeletedKey`.
- Premissen *"ingen session kan finnas"* håller även för den nya stall-armen.

## Eskaleringar
Inga.
