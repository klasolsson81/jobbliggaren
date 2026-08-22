# security-auditor — PR #1439 (#1409)

- **Agent:** `security-auditor` (§9.2, GDPR Art. 17 / Art. 5(1)(e) raderingsprocedur)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, bas `4c8ad6e1` · **Omkontroll:** fix-delta `c40b1e7a`
- **Status runda 1:** BLOCKED — **0 Blocker, 3 Major, 2 Minor**
- **Status omkontroll:** ✓ **Approved** — alla 3 Major + 2 Minor stängda; 1 ny Minor (icke-blockerande)
- **Område 8:** SKIP, deklarerad med mätning — diffen är två filer, ingen träffar suppression-ytan.

## Svar på de sex frågorna (runda 1)

**1. Fungerar proceduren? Ja, båda leden.** `now-31d < now-30d` → raden plockas upp nästa pass.
Kolumnen är `timestamp with time zone`, `NOW()` är timestamptz → ingen tidszonsfälla. Steg 2h gör
`FindByIdAsync` → `null` → **no-op, kastar inte**, och ligger **efter** `CommitAsync`, så kaskaden kan
inte rullas tillbaka av det. Alla `userId`-nycklade aggregat tar `jobSeeker.UserId` från raden själv,
inte från Identity. **Ingen Art. 17-Blocker.**

**2. Är `NOW()`-varianten försvarbar? Ja — och den ska förbli default.** Art. 5(1)(e) kräver en
*definierad och dokumenterad* period, inte omedelbar radering. 30 dagar är systemets redan dokumenterade
period. Fönstret bär här ett **annat** syfte än restore-fönstret: det skyddar mot oåterkallelig radering
av en **felidentifierad** rad, och den false-positive-populationen är mätt (detektorns två
snapshot-läsningar är osynkroniserade). Den registrerade förlorar inget på dröjsmålet — hen kan inte
utöva någon rättighet under det heller.

**3. Är ett medvetet felaktigt `deleted_at` ett Art. 5(1)(d)-problem? Nej.** Art. 5(1)(d) prövar
riktighet *"having regard to the purposes"*. Kolumnens syfte är **control-plane-trigger**, inte att
journalföra när radering begärdes; ingen yta visar värdet för en registrerad. **Men "räcker
ops-kanalen" är nej — och skälet är Art. 5(2), inte 5(1)(d).**

**4. Producentmängden — verifierad självständigt.** `JobSeeker`-skapande i `src/`: **exakt ett** ställe.
Identity-radering: **exakt tre**, och två av dem kan strukturellt inte lämna en `JobSeeker` kvar. Kvar:
sweepen, mid-registrering. **Viktigt negativt resultat:** kontoraderingen raderar **inte** Identity-raden
(den soft-deletar och planterar tombsten), och sweepens snapshot kör `IgnoreQueryFilters()`, så en
soft-deletad JobSeeker **skyddar fortfarande sin Identity-rad**. Inget fullbordat konto kan svepas.

**5. Röjer proceduren PII? Nej.** §4.2 selekterar aldrig `display_name`, aldrig innehåll, och instruerar
ingen dekryptering. Tombsten-nyckeln använder rätt identifierare.

**6. Område 8:** skip, mätt.

## Major (runda 1)

**1. Steg 3 är skrivet för backdaterings-varianten och är falskt för defaultvarianten.** Med
`deleted_at = NOW()` plockas raden inte upp på **30 dagar**; manuell trigger gör då ingenting.
Verifieringen kan inte lyckas under tiden: detektorn läser `IgnoreQueryFilters()` och §3.3 har **inget
`deleted_at`-predikat** — raden returneras varje dag och EventId 2503 fyrar varje natt. **Felmoden:**
operatören drar slutsatsen att den vaktade proceduren failade, och de två utvägarna är exakt de Beslut 3
förbjuder respektive grindar. Dessutom maskerar den kvarstående Warningen en **ny** reverse orphan i
samma fönster — signalen är count-only.

**2. Backdaterings-varianten kräver en verifiering den inte ger operatören något sätt att utföra.**
§4.1:s två namngivna metoder är **otillgängliga**: inget konto att legitimera sig mot, ingen alternativ
adress på fil. Kvar är `display_name` i klartext (varken unikt eller verifierat) och DEK-krypterat
CV-innehåll — att dekryptera det konsumerar precis det intresse åtgärden skyddar. **Det lagliga svaret:
Art. 11(2) + Art. 12(6). Att inte radera på en overifierbar begäran är rätt svar, inte ett undantag.**
Art. 5(1)(f) / Art. 32(1)(b)-(c): en felmatchad backdatering förstör en **annan** registrerads CV och DEK
inom 24 h.

**3. "Ops-kanalen" är inget namngivet hem för det juridiskt avgörande datumet.** Art. 5(2). När
`deleted_at` medvetet görs osant är begäransposten den **enda** riktiga uppgiften om det faktum
Art. 12(3) och Art. 17(1) mäts mot. §4.1:s precedens bär inte vikten — den dokumenterar en *reversibel*
restore utan frist.

## Minor (runda 1)
**1.** Producentmeningen en trigger för smal (klockskevhet **eller** en registrering som stannar längre
än grace-fönstret). **2.** Tombsten-kommandot misslyckas tyst vid fel skiftläge — Redis-nycklar är
skiftlägeskänsliga och koden bygger nyckeln ur en GUID i gemener.

## Omkontroll (delta `c40b1e7a`) — Approved

**Major 1–3: STÄNGDA.** Alla tre grunder ommätta i koden respektive i dokumentet — bl.a. att
`gdpr-processing-register.md` bär posten ordagrant och säger *"It lives only in the controller's case
record, outside the system"*, alltså husets befintliga standard. **Minor 1–2: STÄNGDA** — `EXISTS`
verifierar **exakt** produktionens predikat (`KeyExistsAsync`; värdet läses aldrig).

**`AND deleted_at IS NULL`: rätt.** Att nollställa en löpande klocka fördröjer Art. 17(1) / Art. 5(1)(e)
med upp till 30 dagar, och felmoden är **självförvållad av proceduren** — operatören armerar, ser 2503
fortsätta fyra, kör om §3.3, ser raden och kör Steg 2 igen. Fixen träffar precis den slingan.

**Ny Minor (icke-blockerande):** *"NOLL rader = klockan går redan"* är inte uttömmande — noll rader
uppstår **också** vid fel eller inaktuellt id. En operatör som stannar där har inte armerat raden hen
avsåg, och en Art. 17-begäran förblir tyst obesvarad. `SELECT deleted_at` skiljer grenarna; det är
sammanfattningsraden som är för tvärsäker.

**Backdaterade varianten utan `IS NULL`: rätt avvägning, och ett val.** Satserna har motsatt semantik mot
en löpande klocka — `NOW()` förlänger, den backdaterade **kortar**. Villkoret där hade gjort satsen till
en no-op i exakt det fall en verifierad Art. 17-begäran mest behöver den. Monotonicitet kontrollerad.

## Eskaleringar
**Till Klas: nej** (båda ronderna). Noterat för framtiden, från runda 1: att skeppa backdaterings-varianten
utan Major 2 eller 3 åtgärdade hade varit en accepterad risk på en GDPR-implicerad Major — §9.6 (3) —
**och hon hade inte signerat den**, eftersom skaderiktningen är en **annan** registrerads rättigheter,
vilket §9.6 (3) uttryckligen inte beviljar.
