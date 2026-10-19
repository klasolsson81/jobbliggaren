# 2026-10-19 — #1981 new PR 2, the outcomes of probe 9 — security-auditor

- **Agent:** security-auditor (scoped ruling, report only)
- **Subject:** G1 (read application `jobbliggaren-tem-read`, rule: `TransactionalEmailEmailReadOnly` + `TransactionalEmailOfferSubscriptionReadOnly`, project Jobbliggaren, one key, expiry 2027-10-19), G2 (`600 root 36`), the delivery check (a code sent within 5 minutes before GO), probe 9 run once at ~18:59Z with its verbatim output
- **Base:** worktree `c:/tmp/jbl-admin-epost-stats-1981` at `6bd8c6d00`
- **Transcription:** verbatim by the driving session from the agent's final message.

---

## Security-audit: #1981 nya PR 2, utfallen av prov 9 (avgränsat beslut, endast rapport)

**Status:** ✓ Approved. 4b passerar, och nyckeln och läsapplikationen står. A2 står kvar signerad. Syftet med 5b är uppfyllt genom månadslikheten. Kontrollfönstret på 20 minuter är däremot inte uppfyllt, och det skrivs ned som ej uppfyllt. Inget utfall utlöser STOPP. Bygget, commit D och framåt, får börja. 0 Blocker, 0 Major, 1 Minor.

**Auktoritet:** GDPR Art. 5(1)(a), 5(1)(c), 5(2), 25(2), 30(1), 32(1)(b), 32(1)(d) · CLAUDE.md §9.2 (a)–(e), §9.6 · AGENTS.md §5 Comments · ADR 0150 D2 · ADR 0178 (D1, rad 27, utlösare 4) · mina domar 2026-10-19: statistikdomen (5b, 5c), planläsningen (dom 2 (a)–(f), dom 4, dom 6, 8f) och omkontrollen (Major 2 med villkoret om det körda kommandot)

### Mätningar i detta beslut (2026-10-19)
- **Det körda kommandot.** I uppdraget fanns pythonen bara som sammanfattning. Jag läste därför kommandot ordagrant i sessionens transkription. Det finns en enda träff på `jbl-probe-read` och `reaches_activation`, tool_use 18:59:46Z, så provet kördes en gång.
  - Pythonen bär `DOC`, `TRIES` och `show()` med samma innebörd som i min dom 2 (c).
  - Utskriften är en sluten mängd rader. `n()` släpper bara igenom uint32-heltal, och nyckelnamn vitlistas mot `[a-z_]`. Varje undantag ger `<tag>_body=unreadable`. Id:t skrivs bara till fil, och bara efter UUID-kontroll.
  - Nyckeln, projekt-id:t och mejl-id:t når curl-konfigurationen bara via den inbyggda `printf`, aldrig via argv. Kommandot innehåller inget `-v`, `--trace` eller `set -x`.
  - Den enda avvikelsen från min form är att läsgränsen är 1 MiB i stället för 64 KiB. Det kan bara ge färre `unreadable` och släpper inte ut något.
  - Villkoret i omkontrollen är därmed uppfyllt. Skicka nästa gång den ordagranna texten, inte en sammanfattning.
- **Utdata mot kod.** Varje rad i utdata följer av koden ovan. Den enda rad koden kan skriva ut men som saknas är `by_id=skipped`, och den uteblev korrekt eftersom `any` gav ett giltigt id.
- **Ämnesraderna** (`EmailTemplates*.cs`, trädet `6bd8c6d00`):
  - Det finns 15 `Subject:`-rader. Alla är fasta strängar utom två: matchningsnotisen väljer mellan tre fasta strängar, och `Ny feedback: {page}` går till admin.
  - Ingen ämnesrad bär kod, länk eller token. Koden är pinnad ur ämnet (`EmailTemplatesLoginChallengeTests.cs:138`, #1737 villkor 22). Länken är inte pinnad.
- **Aktiveringen:** armen aktiverades 2026-08-16, och de fyra första utskicken gjordes den dagen (`gdpr-processing-register.md:382`, `:458-466`).

### Domar

**1. 4b: godkänt. Nyckeln och läsapplikationen står.**
- Ingen nyckel ligger utanför min uppsättning. Det gäller listposten, `emails/{id}` och `last_tries[0]`: `_extra` är tom på alla tre ställena, och nyckelmängderna är exakt `DOC` och `TRIES`. Inget innehållsnamn och inget `<withheld>` förekommer.
- Detta, tillsammans med dokumentsvaret (API-referensen v1.9008.0), är den 4b-mätning jag krävde. Dokumentsvaret är sessionens läsning, och jag har inte läst om det. ADR 0179 daterar både dokumentsvaret och provet.
- **Vad 4b visar:** läs-API:t returnerar inget fält som bär innehåll.
- **Vad 4b inte visar:** att leverantören saknar innehållet. Den frågan hör till retentionen.
- Nyckeln når `subject`. Ämnet saknar i dag kod och länk, se Minor 1.
- **G1-avläsningen uppfyller Minor 1 vid G1 från planläsningen.** ADR 0179 skriver in den daterad 2026-10-19, bara med namn:
  - en regel med exakt `TransactionalEmailEmailReadOnly` och `TransactionalEmailOfferSubscriptionReadOnly`;
  - scope projektet Jobbliggaren, utan villkor;
  - exakt en API-nyckel, med utgång 2027-10-19 enligt Klas avläsning;
  - inget förvalt projekt för Object Storage;
  - sändapplikationen orörd fram till G5.

  Raden i `master-key-ops.md` bär utgångsdatum, utfärdandedatum 2026-10-19 och namnet `jobbliggaren-tem-read`.

**2. 5b: uppfyllt i sitt syfte. Kontrollfönstret är inte uppfyllt och skrivs ned så. Ingen omkörning.**
- **Syftet med 5b** var att utesluta en blind nyckel, det vill säga sändnyckelns 200 med nollor. Det visas starkare än jag krävde. Jag krävde samma storleksordning som `monthly_emails_count`. Utfallet är exakt likhet, 149 = 149, läst med samma nyckel mot en annan ändpunkt. Därtill:
  - Alla sex räknare finns i varje svar.
  - 32 d, d7, d3 och h24 är alla ≥ 1.

  Den första nollan får därför visas.
- **Delvillkoret** "ett fönster runt en känd leveranskontroll ger total ≥ 1" gav 0. Det är inte uppfyllt. ADR 0179 skriver det som 0 och aldrig som godkänt.
- **Orsaken är omätt.** Tre läsningar är förenliga med utfallet:
  - (i) `statistics` släpar efter realtid, alltså vid fönstrets slutkant.
  - (ii) `since` jämförs mot tidshinkar, så att den pågående hinken faller utanför ett kort fönster, alltså vid startkanten. Ett fönster från ca 18:39Z och en sändning kort före 18:59Z passar exakt med timhinkar.
  - (iii) Lådans klocka går före. Det kan uteslutas med en läsning av NTP-status under §9.2 (a), utan GO.

  Månadslikheten skiljer inte (i) från (ii). Den håller om `project-consumption` delar källa med `statistics`, och den håller också om sändningen räknas men fönstret skär bort den. Sessionens "sannolika läsning" är alltså en av två. ADR:n skriver båda, och (iii) också om den inte är utesluten.
- **Ingen omkörning.** En omkörning kräver G2 igen, alltså en nyckel till på lådan, och den skulle mäta samma okända kant. Det vore exponering utan ny information om det 5b gäller.
- **Bindande:**
  - (a) Varken sidan, ADR 0179, runbooken eller registret påstår att perioderna räknar fram till läsögonblicket, eller att kanterna stämmer på minuten. Källnotens tid är när vi frågade, inte var fönstret slutar. Formuleringen är design-reviewers, sanningsvillkoret mitt.
  - (b) Kanariefågeln ändras inte. Både (i) och (ii) kan i 32 d-fönstret bara ge ett falskt NotObservable, vilket är det säkra hållet.
  - (c) Sanningstabellen för fellistan ändras inte.
    - Under (i) syns ett nytt fel i listan innan statistiken räknar det. En icke-tom lista ger Observed.
    - Under (ii) ger en kantförskjutning åt ena hållet en icke-tom lista. Åt andra hållet ger den `failed > 0` med tom lista, alltså NotObservable.
    - Inget fall ger "inga misslyckade utskick" som statistiken själv inte ger.
  - (d) Under (i) är en nolla för de senaste minuterna en eftersläpande observation. Det är inget okänt värde som visas som 0 (ADR 0150 D2), förutsatt att (a) gäller.
- *Rekommendation, inte krav:* Klas kan mäta (i) utan någon ny nyckelhantering, vid leveranskontrollen efter G5 som redan är planerad:
  - Läs h24 Skickade före utskicket.
  - Läs igen efter varje memo-hållning (≥ 60 s) tills värdet ändras.
  - Dokumentera antalet minuter, med datum, i ADR 0179.

  Först efter en sådan mätning får något färskhetsanspråk göras.

**3. Utfallen för Major 2: ingen STOPP.**
- Det finns inget okänt namn, inget `<withheld>`, ingen `unreadable` och inget innehållsnamn. Statistiken är inte noll, så Major 4 utlöses inte. Reserven användes som dom 2 (a) föreskriver, och `last_tries[0]` lästes.
- **ADR 0179 ska hålla isär vad provet mätte och vad det inte mätte:**
  - **Mätt:**
    - `statuses=failed` ignoreras inte. Listan gav total 0, medan statistiken gav 60 utskick i samma 7-dygnsfönster. Om filtret hade ignorerats hade listan burit periodens alla mejl.
    - Felen på 7 dygn är 0 enligt båda källorna.
    - `order_by` påverkar svaret. Första posten skiljer sig mellan stigande och fallande ordning (693 respektive 686 byte, samma `total_count`), och posten i stigande ordning är från före 2026-08-17.
  - **Tomt sant:** `failed_not_failed=0 order_desc=yes since_ok=yes` gäller för varje tom lista och mäter ingenting. `since`, fallande ordning och `status` per post är omätta på fellistan. Kontraktet bärs av adapterns kontroll vid körning, där hela läsningen blir Malformed (arkitektens V4), och av testerna. Det bärs aldrig av prov 9.
  - **Omätt:** en misslyckad posts form, flaggor och storlek.
  - **Radering:** `probe_file_deleted=yes` mäter bara nyckelfilen. Kropparna och `$d` raderades genom kommandots konstruktion (`rm` efter varje läsning, `rm -rf` i trappen, tmpfs), inte enligt någon utskriven rad. ADR:n skriver det så.

**4. `reaches_activation=yes`: räckvidden får skrivas som uppmätt, i den här formen.**
- **Mätt:**
  - Läsnyckeln listar 166 utskick, och det äldsta skapades före 2026-08-17T00:00Z. Armen aktiverades 2026-08-16.
  - Leverantören hade alltså behållit mottagarbärande metadata i mer än 63 dygn.
- **Inte mätt:**
  - att varje utskick sedan aktiveringen finns kvar. Det är en slutsats av åldersbaserad lagring. 166 = 162 + de fyra utskicken 2026-08-16 är förenligt med en komplett lista, men bara om inget skickades mellan 2026-08-17 och 2026-09-17. Det är omätt, och någon sändlogg finns inte (ADR 0178:27);
  - retentionens övre gräns;
  - innehållets retention.
- **Registrets restriskrad får lyda:** "En läckt läsnyckel når adress, ämne, leveransstatus och leveransförsök för utskick till alla tre mottagarklasserna. Mätt 2026-10-19 med läsnyckeln (prov 9, ADR 0179): listan bar 166 utskick, och det äldsta var skapat före 2026-08-17 (armen aktiverades 2026-08-16). Biträdet hade alltså behållit mottagarbärande metadata i mer än 63 dygn. Det är en nedre gräns. Hur länge biträdet behåller uppgifterna och innehållet är omätt och skyldigt, inte accepterat. Läs-API:t returnerar inget innehåll (4b, samma prov)."
- **EJ MÄTT** får bära den nedre gränsen som daterat underlag. Statusen förblir "omätt, skyldig" (Minor 4 i planläsningen). Den avgör inte retentionsfrågan, som Klas tar om i en egen session.
- **ADR 0179** skriver samma innehåll på engelska.
- **Förbjudet i båda:**
  - "varje" eller "alla" utskick;
  - "retentionen är mätt";
  - "64 dygn", eftersom provet bara skrev ut booleanen;
  - "anonym";
  - "nyckeln når inget innehåll hos leverantören".

**5. Taket 64 KiB godtas, med en ärlig härledning.**
- **Mätt:** en levererad post med ett försök väger ca 657 byte, alltså 686 byte minus omslaget i ett tomt svar (ca 30 byte). En sida med 10 sådana poster väger ca 6,6 KB, och 64 KiB är ungefär 10 gånger så mycket.
- **Inte mätt:** en misslyckad post.
  - Antag högst tre försök, som sessionen anger. Varje försök bär mottagarserverns svarstext. RFC 5321 begränsar en svarsrad till 512 oktetter, men inte svar över flera rader.
  - Då hamnar en felsida på ca 20–25 KB. Marginalen är alltså ungefär 3 gånger, inte 9.
- **Därför godtas taket:**
  - Ett överskott stänger säkert. Hela läsningen blir Malformed, aldrig en avkortad lista och aldrig "inga misslyckade utskick".
  - Totalerna bär fortfarande `failed` från `statistics`, så adminens signal finns kvar.
  - Ett större tak vore också godtagbart från min sida. Valet är arkitektens.
- **Villkor:**
  - ADR 0179 för in härledningen ovan, daterad, och anger att ingen misslyckad post mättes.
  - Ett test pinnar att fellistans läsning över sitt tak ger Malformed.
  - Kroppen läses i en icke-poolad buffert, oberoende av taket.
  - Finner omkörningen enligt 8f en misslyckad post, förs dess storlek in.

**6. Bygget: ja, commit D och framåt får börja.**
- Villkoren från omkontrollen binder vid PR-granskningen: tillämpningen ska lyckas vid framflyttningen före G4, och kroppen ska ligga i en icke-poolad buffert. Kravet på en sluten utskrift är uppfyllt.
- Därtill binder dom 2 (a), 3, 4 och 5 samt Minor 1 ovan.

### Minor
1. **Ämnet är det enda innehållsnära fältet som läsnyckeln når. Att ämnet saknar kod och länk är nu en förutsättning för 4b, men bara koden är pinnad.** Fil: `tests/Jobbliggaren.Application.UnitTests/Email/EmailTemplatesLoginChallengeTests.cs:138`
   - **Nuvarande:**
     - `Subject.ShouldNotContain(SampleCode)` gäller för varje variant som bär en kod. Pinnen finns för förhandsvisningen i inkorg och på låsskärm, inte för läsvägen.
     - Ingen pinne håller länken, eller dess token, borta från ämnet.
     - I dag är alla ämnen fasta strängar.
   - **Krävs:**
     - (a) Pinnen utökas så att ingen mall som bär en kod eller länk i kroppen bär koden, länken eller dess token i ämnet.
     - (b) 4b-posten i ADR 0179 anger att `subject` nås, och att pinnen är 4b:s instrument på vår sida.
     - (c) Utlösaren "4b ändras" omfattar fallet att en mall får kod, länk eller token i ämnet.
   - **Motivering:**
     - Koder och länkar gäller i upp till 15 minuter. Ett sådant ämne skulle göra läsnyckeln till en väg förbi inloggningen, vilket är Blocker-klass (auth bypass).
     - Kod i ämnet är ett vanligt UX-mönster och därmed en realistisk regression.
     - Art. 25(2), 32(1)(b).
     - Hemmet är denna PR, eftersom det är läsnyckeln som gör invarianten bärande.
   - **Delegera till:** test-writer (pinnen) och sessionen (ADR:n).

### Praise
- Sessionen upptäckte valet av `TransactionalEmailReadOnly` innan regeln sparades ✓
- Provet kördes en gång med sluten utskrift. Id:t gick bara via fil och inbyggd `printf`, och reserven slog till exakt som formen föreskriver ✓

### Sammanfattning
0 blockers, 0 major och 1 minor.
- 4b passerar, och A2 och läsapplikationen står.
- Syftet med 5b är uppfyllt genom exakt månadslikhet.
- Kontrollfönstret på 20 minuter gav 0 och skrivs ned så. Orsaken är omätt, och ingen omkörning görs.
- Räckvidden och taket får skrivas i formerna ovan.
- Bygget får börja.

Omgranskning efter fix görs av samma agent, i rapportläge och avgränsad till fixens delta (CLAUDE.md §9.6). Nästa granskning från mig gäller PR:en.

**Eskalering till Klas:** ja.
1. Vid G4 ska läsapplikationen `jobbliggaren-tem-read` fortfarande ha exakt en API-nyckel. Har du inte kvar hemligheten från G1: skapa en ny nyckel, radera den gamla i samma konsolbesök och läs av utgångsdatumet på nytt. Utgångsraden i `master-key-ops.md` följer den nyckel som faktiskt injiceras.
