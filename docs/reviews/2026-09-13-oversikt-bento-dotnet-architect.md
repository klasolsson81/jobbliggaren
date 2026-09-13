# Arkitektur-analys: `/oversikt` bento-grid (PR #1724)

**Agent:** dotnet-architect · **Datum:** 2026-09-13 · **Head:** `b0e98b75` · **Issue:** #1723

### Sammanfattning
Behöver åtgärdas — 2 viktiga, 3 nice-to-have. Inget backend-delta, så område Domain/Application/Infrastructure aktiveras inte. Lagerreglerna i AGENTS.md §4 håller: fem kort är RSC, `oversikt-page.tsx` är fortsatt synkron RSC, `CriteriaCard` är RSC (så `criterionReference`-trädet stannar på servern), och de tre `"use client"`-modulerna har var sitt namngivet skäl. Inga `any`, `console.log`, `useEffect`-fetch, inga nya beroenden.

### Svar på de sju frågorna
1. **lib/components-splitten:** ren. `summariseWatches` och `applicationBars` är rena, testade och ligger där huset lägger sådant (`lib/<kontext>/`) — men se NTH-3 om mappnamnet.
2. **`notice-section`-seamen:** rätt. `NoticeSection` bär rubrik, `summary`/`summaryOwns` och källbegreppet — att komponera den in i ett kort hade tvingat kort-props genom en ledger-komponent vars enda konsument nu är gäst-demon. Extrahera state + popover och dela `NoticeListCard` är rätt snitt. Men typerna följde inte med: **Viktigt 2**.
3. **`matchingStatedByCaller`:** propen är rätt val framför ett syskon (ADR 0139:s "en stege, inte sju kopierade grenar"), men den är inte ett självständigt faktum — se NTH-1.
4. **Tokens:** vokabulären är korrekt — `-fill`/`-hover` oskiftade är exakt `--jp-accent-800`-kontraktet, `-border` skiftar med tinten. Guarden (`scripts/guard-css.mjs` sweep 2+4) är tvåriktad och blockerande, så token och konsument **måste** ligga i samma commit; det är en korrekt konsekvens, ingen invändning. `DESIGN.md` §1.2/§6 och skillen är uppdaterade i samma PR — de exekverande hemmen är svepta.
5. **ADR 0140:** binder rätt saker med negativ skop (uppräknade routes), tokentabell med mätning, alternativ, "vad som inte ändras". Inget överskjutande.
6. **Deletions:** en dinglande rest, se NTH-2. `.jp-notice-group*` är #1582:s, inte denna PR:s.
7. **CTO:** nej. De två Viktigt är otvetydigt in-block (bägge är en flytt respektive ett test).

### Fynd

**[Viktigt]** `web/jobbliggaren-web/src/lib/applications/application-bars.ts:21`
**Vad:** `BARS` är en andra, handskriven uppräkning av pipelinens aktiva steg. `applicationBars` läser `PIPELINE_ORDER` och `ACTIVE_PIPELINE_STATUSES` för `total`/`active`/`terminal`, men raderna läser `BARS` — en literal som inte härleds ur någondera. Kortet renderar `bars.active` som sitt stora tal och staplarna direkt under som dess uppdelning.
**Varför:** `pipeline-counts.ts` docblock: *"Delad … så de två ytorna aldrig kan säga olika saker om samma konto (CLAUDE.md §9.1 DRY)"*. Här finns ingen sådan koppling: varken typsystemet eller något test pinnar att `∪ bar.statuses === ACTIVE_PIPELINE_STATUSES`. Mätt: de sex aktiva statusarna täcks i dag av fem staplar, men `application-bars.test.ts` (fem tester) prövar aldrig den likheten. Samma PR bygger `NOTICE_ICONS` och `prefLabels` som `Record<NoticeType, …>` **just för** att en ny typ ska bli ett kompileringsfel — mönstret finns, det tillämpas bara inte här.
**Felscenario:** en sjunde aktiv status läggs till `ACTIVE_PIPELINE_STATUSES` (ADR 0092 D3 håller stegen öppna). `bars.active` räknar den, ingen stapel visar den; kortets staplar summerar till mindre än kortets eget stora tal, utan kompileringsfel och utan rött test.
**Föreslagen åtgärd:** en mekanisk pinne i `application-bars.test.ts` — den fäller mutationen och kostar fyra rader:

    it("staplarna partitionerar ACTIVE_PIPELINE_STATUSES", () => {
      const bars = applicationBars(countByStatus(PIPELINE_ORDER.map((s) => group(s, 1))));
      expect(bars.rows.reduce((n, r) => n + r.count, 0)).toBe(bars.active);
    });

---

**[Viktigt]** `web/jobbliggaren-web/src/components/oversikt/notice-section.tsx:25`
**Vad:** `SectionNoticeData` bor kvar i `notice-section.tsx` och `NoticeData`/`NoticeKind` i `notice-row.tsx` (`notice-row.tsx:8,10`) — två `"use client"`-**komponentmoduler** vars enda kvarvarande renderande konsument är gäst-demon. Efter deltat typberoer sex app-sidiga moduler av dem: `oversikt-page.tsx:47` (RSC-orkestratorn), `notice-list-card.tsx:5`, `requires-you-card.tsx:9`, `recent-events-card.tsx:9`, `mark-all-read-row.tsx:8` och den delade hooken `use-notice-list.ts:11`.
**Varför:** beroenderiktningen är inverterad — sidans nya primära yta hämtar sitt datakontrakt ur den yta som är på väg bort (#1585), och en delad hook importerar en typ ur en komponentmodul. Huset har redan etablerat rätt hem och skälet: `notice-types.ts` docblock beskriver exakt den här fällan (`NOTICE_TYPES` i en `"use client"`-modul kraschade server-rendern i #726) och `NoticeSource`/`NoticeType` ligger därför där. `NoticePrefType` gick i motsatt riktning i deltat — flyttad *in* i `notice-prefs-popover.tsx` och re-exporterad ur `notice-section.tsx` — så re-exportlagret växer i stället för att tunnas ut. Clean Architecture-principen: det stabila (kontraktet) får inte bero på det flyktiga (den deprekerade vyn).
**Felscenario:** #1585 raderar `notice-section.tsx`/`notice-row.tsx` och sex app-moduler slutar kompilera — en ren borttagning blir ett delta över hela bento-familjen.
**Föreslagen åtgärd:** flytta `SectionNoticeData`, `NoticeData` och `NoticeKind` till `notice-types.ts` (eller ett `notice-data.ts` bredvid). Lämna `export type { … }` kvar i de två komponentmodulerna så gäst-ytan och testerna är orörda. Ingen ny prosa, inget beteende, ingen körtidsimport tillkommer.

---

**[Nice-to-have]** `web/jobbliggaren-web/src/components/company-criteria/criterion-ad-lines.tsx:99`
**Vad:** `matchingStatedByCaller` beskrivs som *"A FOURTH independent fact"*. De tre befintliga propparna beskriver **anroparens yta** (`variant`, hoistat råd, erbjuden handling); den här beskriver att anroparen redan renderat *den här komponentens egen utdata*, och den är dessutom härledd — `CriteriaCard:142` skickar `counted !== null`. Ingenting i typen binder ihop de två: `true` med ett tal som anroparen inte renderar tystar talet spårlöst.
**Varför:** en suppressions-flagga per anropare är vägen till konfigurationsyta i stället för komposition. Formen är ändå rätt vald — ADR 0139 förbjuder en andra kopia av vägransstegen, och den säkra grenen är default (`false`).
**Föreslagen åtgärd:** behåll propen, men namnge den efter vad den gör (`omitMatchingCount`) och skriv invarianten i docblocket: *får vara `true` endast när `matching.count !== null` och anroparen renderar talet*. Räkneordet i typens docblock (rad 31) är redan rättat till fyra.

---

**[Nice-to-have]** `web/jobbliggaren-web/messages/sv/oversikt.json:133`
**Vad:** `oversikt.notices.calloutLabel` är föräldralös efter att `SetupCallout` raderats — noll referenser i `src/` (mätt över samtliga 126 löv i namespacet; den är den enda). Finns kvar i både `sv` och `en` (`messages/en/oversikt.json:133`). `MatchingCard` använder `calloutText`/`calloutCta`/`calloutHint` men ingen overline-etikett.
**Varför:** `oversikt` deklareras i `(app)/layout.tsx:64` som klient-namespace, så den döda nyckeln skickas i payloaden — och `client-namespace-payload.test.ts` är en ratchet mot just återuppblåsning. Ingen guard sveper oanvända i18n-nycklar, så den upptäcks inte automatiskt.
**Föreslagen åtgärd:** stryk nyckeln ur båda katalogerna. Ren radering, stänger mekaniskt.

---

**[Nice-to-have]** `web/jobbliggaren-web/src/lib/company-watches/watch-summary.ts:1`
**Vad:** ny mapp `lib/company-watches/` bredvid befintliga `lib/company-follows/` (`org-nr.ts`) — två namn för samma bounded context i samma lager, plus `lib/company-criteria/` och `lib/company-search/`.
**Varför:** `lib/<kontext>/` är husets placering och mappnamnet är det enda som säger vilken kontext en helper hör till. `company-watches` är det **mer** korrekta namnet (aggregatet heter `CompanyWatch`, endpointen `/me/company-watches`); `company-follows` är arvet — men två hem betyder att nästa helper hamnar godtyckligt.
**Föreslagen åtgärd:** låt filen ligga; fila en issue på att flytta `company-follows/org-nr.ts` till `company-watches/` och pensionera det gamla namnet. Namnge skippen i PR-kroppen om §9.6:s filnings-cap binder.

### Referenser
- AGENTS.md §4 (Server Components by default, Server Actions-undantagen), §5 `Comments:`/`Tests:`, §9.1 DRY
- CLAUDE.md §9.6 (severity tillhör rapporterande agent; omkontroll rapport-only, skopad till fix-deltat)
- `docs/decisions/0140-oversikt-som-bento-dashboard-scoped-undantag.md` (Beslut 1–5)
- DESIGN.md §1.2 / §6 (scoped undantag) · `.claude/skills/jobbpilot-design-tokens/SKILL.md`
- ADR 0139 (en stege för vägransgrenarna) · #1582 (`notice-list.tsx` + `.jp-notice-group*`) · #1585 (gäst-migreringen)

---

## Omkontroll (rapport-only, skopad till fix-deltat `b0e98b75..8a8edfce`)

### Sammanfattning
4 av 5 fynd stängda. NTH-3:s disposition är **inte verifierbar än** — den namngivna skippen finns inte i PR-kroppen vid mätning. Inga nya fynd; deltat introducerar ingen ny kod, bara en typflytt, en prop-omdöpning, en raderad nyckel och ett test.

### Fynd

**[Viktigt 1 — STÄNGT]** `application-bars.test.ts:63` — pinnen finns, bygger fixturen över hela `PIPELINE_ORDER` (varje status = 1). Mätt: `active` = 6, raderna summerar till 6. Diskriminerar åt båda håll (sjunde aktiv status utan stapel → 7 ≠ 6; stapel borttagen → 5 ≠ 6). Kvarstående gräns, medvetet accepterad: en status i `ACTIVE_PIPELINE_STATUSES` men inte i `PIPELINE_ORDER` passerar tomt — malformad per konstruktion. Inget owed.

**[Viktigt 2 — STÄNGT]** `notice-types.ts:31,38,61` — `NoticeKind`, `NoticeData`, `SectionNoticeData` definierade exakt en gång var; modulen bär ingen `"use client"`, enda nya import är typ-only. Riktningen vänd: `oversikt-page.tsx` har noll importer från `notice-section`; hooken, listkortet, de två korten och mark-all-raden läser `notice-types`. Re-exporter kvar i `notice-row.tsx`/`notice-section.tsx` så gäst-ytan är orörd. Två app-sidiga testfiler resolvar via re-exporten — följden av villkoret, inget fynd. `NoticePrefType` kvar i popovern är rätt.

**[NTH-1 — STÄNGT]** `criterion-ad-lines.tsx:87` — omdöpt till `omitMatchingCount`, invarianten skriven, typens docblock namnger den som fjärde propen. `matchingStatedByCaller` har noll förekomster i `src/`; kvarvarande träffar i daterade rapporter är proveniens (AGENTS.md §1.6).

**[NTH-2 — STÄNGT]** `messages/{sv,en}/oversikt.json` — `notices.calloutLabel` borttagen; noll förekomster i `src/` och `messages/`. Ren radering.

**[NTH-3 — INTE STÄNGT: dispositionen är inte gjord än]** `src/lib/company-watches/watch-summary.ts:1` — mätt mot den levande PR-kroppen: avsnittet *"Out of scope (named)"* nämner inte `lib/company-watches/`. Väntat: §9.2 tillåter EN PR-kroppsredigering efter sista verdiktet och den har inte skett. Stängs när den landar och namnger skippen med det som gör den osynlig för en parallell lane. **Ta med i samma redigering:** mutationslistan räknar upp `matchingStatedByCaller` — namnet finns inte längre i koden.

### Noteringar (inga fynd)
- Verifieringssiffrorna är sessionens, inte agentens. 611/68 och 562/64 är delmängdskörningar och ska inte läsas som "hela sviten grön" i verdikt-tabellen.
- Den transkriberade rapporten är ordagrann och rätt huvud-stämplad (`b0e98b75`).
