import { describe, it, expect } from "vitest";
import svLegal from "../../../messages/sv/content-legal.json";
import enLegal from "../../../messages/en/content-legal.json";

/**
 * #263 — sv/en-paritet för `content-legal` (de juridiska innehållssidorna).
 * next-intl typar mot SV-katalogen (source of truth); EN är en plain JSON-import
 * som tsc INTE korslänkar, så en saknad EN-nyckel slinker igenom typkollen och
 * ger en tom sträng i runtime. Detta test pinnar IDENTISK nyckel-struktur
 * (rekursivt, inkl. array-längder) över båda katalogerna.
 */

function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

/**
 * `[path, leaf]` for every string leaf matching `term`. The path half exists so a tripwire can pin
 * sv/en parity by LOCATION, not merely by count: `en.length === sv.length` passes when one locale
 * moves its disclosure to a different section while the other keeps it (3 === 3), and losing the
 * disclosure from one locale's consent section is the single most likely real error (#880 class).
 */
function matchingLeaves(catalogue: unknown, term: RegExp): [string, string][] {
  return leafPaths(catalogue)
    .map(
      (path) =>
        [
          path,
          path
            .split(".")
            .reduce<unknown>((node, key) => (node as Record<string, unknown>)?.[key], catalogue),
        ] as const
    )
    .filter((entry): entry is [string, string] => typeof entry[1] === "string" && term.test(entry[1]));
}

/**
 * DE RATIFIERADE MARKÖRFORMERNA — ETT HEM, TVÅ POLARITETER (#1199, code-reviewer Major 2).
 *
 * Formerna är ratificerade av senior-cto-advisor (#186/TD-116) och binder hela MENINGEN, inte
 * ett token — resonemanget bor i e-post-tripwirens doc-kommentar nedan och upprepas inte här.
 *
 * De ligger som konstanter för att de sedan #1199 används i **båda** polariteterna: e-post- och
 * ansökningshistorik-spärrarna kräver att markören finns, värd-spärren kräver att den saknas.
 * Två textkopior av samma mönster hade gett "ETT HEM PER TAL" applicerat på ett regex — och
 * felmoden är inte symmetrisk: bara den POSITIVA assertionen körs mot text som faktiskt bär
 * markören, så en felstavning i en separat negativ kopia hade varit **osynlig för hela sviten**
 * (den negerade assertionen passerar på allt ett trasigt mönster inte matchar). Med ett delat
 * hem är den positiva spärrens gröna körning liveness-beviset för den negativa.
 *
 * Ingen `g`-flagga, med flit: ett delat `RegExp` med `g` bär `lastIndex` mellan anrop och hade
 * gjort assertionerna ordningsberoende.
 */
const SV_STATUS_MARKER = /planerat och ännu inte i drift/i;
const EN_STATUS_MARKER = /not yet in operation/i;

/**
 * E-postleverantörens UNION-form (bolag ELLER tjänst) — invariant 2:s term i e-post-spärren
 * nedan, och samma term i värd-spärrens fail-fast-kontroll. Ett hem, två läsare: skulle de
 * divergera skulle kollisionskontrollen vakta en annan mängd än den som faktiskt kolliderar.
 * **Golvet i e-post-spärren använder AVSIKTLIGT inte den här** utan den snävare part-bärande
 * formen — resonemanget, och mätningen bakom det, står i den spärrens doc-kommentar.
 *
 * **OMRIKTAD 2026-08-15 (#183, ADR 0131) — och unionen behövde inte längre vara en alternation,
 * vilket är en observation om NAMNEN och inte en försvagning av regeln.** Under AWS SES bar copyn
 * två namnformer utan gemensam delsträng (bolaget respektive tjänsten), så unionen krävde ett
 * `|`. Scaleways två former är `Scaleway SAS` (avtalsparten) och `Scaleway Transactional Email`
 * (tjänsten), och **båda innehåller `Scaleway`** — ett prefix räcker därför för att iterera varje
 * omnämnande. **EN TERM PER INVARIANT står kvar oförändrad:** det finns fortfarande två termer,
 * unionen här och den part-bärande `Scaleway SAS` i golvet nedan, och de betecknar fortfarande
 * olika mängder. Att kollapsa dem till en vore samma fel som förr — ett stycke som tappar
 * avtalsparten men behåller tjänstenamnet ska falla på golvet, och gör det bara så länge golvet
 * bär den snävare formen.
 */
const EMAIL_PROVIDER_ANY = /Scaleway/;

describe("content-legal i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enLegal)).toEqual(leafPaths(svLegal));
  });

  /**
   * #824 PR 4 / #852 — STATUS-MARKÖR-TRIPWIRE (senior-cto-advisor, bindande).
   *
   * "(planerat) … ännu inte i drift" är INTE "obyggd". Det är ett ratificerat hus-idiom med en
   * definierad betydelse — `docs/runbooks/gdpr-processing-register.md` ("Statusgrind: behandlingen är
   * BYGGD men ännu INTE i prod-drift … formuleringarna flippas från 'planerat' till aktiv drift VID
   * prod-aktivering") — och det bärs av sju behandlingar, flera av dem kod-skeppade (bl.a.
   * original-cv-filen). Flippen är en AKTIVERINGSHÄNDELSE, inte en copy-händelse: den sker i lockstep
   * med första `v*`-taggen (ADR 0090 Ruling 3 item 4), spårad i **#852**.
   *
   * Varför testet finns: definitionen levde bara i gitignorerade filer, och TVÅ obligatoriska granskare
   * i rad lästes vilse av den — design-reviewer krävde att markören skulle strykas ur just de här
   * styckena, i tron att den betydde "funktionen finns inte". Hade den strykts hade policyn påstått att
   * behandlingen är i drift innan lanseringsgrindarna passerats: den motsatta osanningen. Kunskapen bor
   * här nu, där den faller ut i CI i stället för i en granskares minne (Beyoncé-regeln: if you liked it
   * you should have put a test on it).
   *
   * Testet ska FALLA vid prod-aktivering. Det är meningen — det är grinden. Ta då bort det i samma
   * ändring som flippar copyn, och stäng #852.
   */
  it("ansökningshistoriken bär status-markören 'planerat' i policyn tills #852 flippar den", () => {
    // Scoped to `privacy` DELIBERATELY, unlike the email-provider tripwire below: widening to the whole
    // catalogue pulls in `recruiterNotice.sections.2.paragraphs.1`, which describes the same feature
    // to a different audience and carries no status marker. Measured, not assumed.
    const sv = matchingLeaves(svLegal.privacy, /ansökningshistorik/i);
    const en = matchingLeaves(enLegal.privacy, /application history/i);

    // Guard against a vacuous pass: if the paragraphs are ever renamed away, the filter would match
    // nothing and every assertion below would trivially hold. FOUR known sites today (Art. 13
    // data-categories list, TWO retention rows — #880 split that bullet in two — and "Inga
    // automatiserade beslut"). The floor said three until 2026-07-26; the extra row had been
    // uncounted since #880.
    expect(sv.length).toBeGreaterThanOrEqual(4);

    // Parity by LOCATION, not count — 4 === 4 passes while sv loses its row in one section and en
    // loses a different one. Measured identical today.
    expect(en.map(([path]) => path)).toEqual(sv.map(([path]) => path));

    for (const [path, paragraph] of sv) expect(paragraph, path).toMatch(/planerat/i);
    for (const [path, paragraph] of en) expect(paragraph, path).toMatch(/planned/i);
  });

  /**
   * #186 / TD-116 — E-POSTLEVERANTÖRS-TRIPWIRE (senior-cto-advisor, bindande scope-bind
   * 2026-07-26). **TERMEN HAR RIKTATS OM TVÅ GÅNGER.** 2026-08-09 (#1169): ADR 0124 bytte
   * providern från Resend, Inc. (USA) till AWS SES, så `/Resend/` hade blivit en spärr som vaktar
   * en part vi inte längre har. **2026-08-15 (#183, ADR 0131):** AWS vägrade permanent häva
   * sandbox-läget, providern är Scaleway SAS (Frankrike, `fr-par`), och termen följde med igen.
   * Vad som INTE ändrades någon av gångerna: path-pariteten och markör-halvan. Detta är inte
   * prod-flippen — armen förblir mörk.
   *
   * ⚠ **GOLVET ÄNDRADES DÄREMOT DEN HÄR GÅNGEN, 4 → 3, och det är HÄRLETT och inte sänkt tills
   * grönt** (senior-cto-advisor, bindande 2026-08-15). Skälet är att Scaleway SAS är franskt och
   * fransk-ägt: ingen tredjelandsöverföring uppstår, så tredjelandsavsnittets e-poststycke är
   * **struket med sin grund** i stället för omskrivet — samma form som värdraden redan har, där
   * copyn med flit är tyst om Kap. V när ingen överföring finns. Kvar är tre uppräknade platser
   * (samtyckesavsnittet + två i Mottagare). **Golvet hölls medvetet INTE på 4 genom att skriva in
   * ett fjärde omnämnande:** en test-assertion får inte forma publicerad juridisk copy, vilket är
   * samma inversion som husets `DependencyInjection`-prejudikat förbjuder för composition roots.
   *
   * **Båda omriktningarna är mätta icke-vakuösa, i den ordning som är det enda beviset:** termen
   * byttes FÖRST, med `content-legal.json` orörd, och testet föll på golvet — 2026-08-09 respektive
   * 2026-08-15, båda gångerna med `AssertionError: expected 0 to be greater than or equal to 4`
   * (golvet var 4 vid mättillfället och sänktes till 3 först när copyn skrevs). Hade mätningen
   * gjorts efter copy-redigeringen hade den inte skilt en fungerande spärr från en som matchar vad
   * som helst — jfr #1237, där leverantörsnamnet med en avslutande punkt gav 10/10 grönt medan
   * spärren asserterade ingenting.
   *
   * Två invarianter i ett test, båda riktningarna av samma defekt:
   *
   * 1. **Leverantören ÄR namngiven.** Detta är hela #186:s leverans (Art. 13(1)(e)/28 — en
   *    mottagare av personuppgifter måste framgå). Före den ändringen bar policyn ett
   *    e-poststycke som var *sant* men aldrig nämnde en leverantör, vilket gjorde frånvaron
   *    OSYNLIG för varje token-grep: leverantörstoken hade noll träffar i hela katalogen, och tre
   *    nollträffs-scopingar i rad missade därför att stycket alls fanns. Ett räknat golv är
   *    det enda som fäller en tystnad.
   * 2. **Varje omnämnande bär status-markören** tills `Email:Provider` flippas. Armen är i dag
   *    dark i non-dev (`AddEmailSender` → `NullEmailSender`), så ett presens-påstående vore den
   *    motsatta osanningen — exakt den ansökningshistoriken-fällan som testet ovan finns för.
   *    Egenskapen har överlevt tre providergenerationer och är därför skriven providerneutralt.
   *    Flippen är grindad av `release-checklist.md` §2.5 punkt 1 (FEM led — uppräkningen bor
   *    där, aldrig här), aldrig av en copy-ändring.
   *
   * **Markören måste bindas till STATUS-MENINGEN, inte till stycket** (code-reviewer Major 2,
   * mätt: den första formen passerade VACUÖST i två av tre leaves i BÅDA språken). Orsaken är att
   * disclosure-meningens egna participform mättar en bred assertion — "Notiserna **planeras** att
   * skickas", "All e-post **planeras** att levereras" / "are **planned** to be sent". Med
   * `/planerat|planerad|planeras/` respektive `/planned/` kunde markörmeningen strykas ur samtyckes-
   * och mottagarstyckena med testet grönt, medan §2.6:s smala grep tyst föll 9+9 → 7+7. Mönstren nedan är därför
   * de RATIFIERADE markörformerna och inget bredare — och de binder hela MENINGEN
   * (`planerat och ännu inte i drift`), **avsiktligt smalare** än ansökningshistorik-tripwirens
   * `planerat`. Systern kan INTE följa med: retentionsposterna bär `(planerat)` utan markörmeningen, så
   * meningsformen hade fällt dem. Bredda aldrig tillbaka. Och "not yet in operation" är den engelska
   * markörens bärande led (`/planned/` är otillräcklig oavsett bredd).
   *
   * Testet ska FALLA vid prod-flippen. Ta då bort markör-halvan i samma ändring som flippar
   * copyn — men BEHÅLL golvet OCH path-pariteten: leverantören måste vara namngiven efter flippen
   * också, och då hårdare än nu.
   */
  it("e-postleverantören Scaleway är namngiven i policyn och varje omnämnande bär status-markören (#186/#1169/#183)", () => {
    // WHOLE catalogue, not just `privacy`: measured 0 mentions outside `privacy` today (3 of 3 leaves
    // per språk ligger i `privacy`), so the widening is free and strictly increases coverage. A future
    // mention in `terms`/`cookies`/`recruiterNotice` would otherwise escape both the floor and the
    // marker requirement.
    //
    // Termen är den PART-BÄRANDE strängen `Scaleway SAS`, inte enbart `Scaleway` (för brett —
    // #1237 mätte att ett leverantörsnamn med en avslutande punkt gav 10/10 grönt medan spärren
    // asserterade ingenting). Den namnger avtalsparten, alltså den juridiska person Art. 13(1)(e)
    // kräver att mottagaravsnittet pekar ut — samma precisionsstandard som `netcup GmbH` på
    // värdraden.
    //
    // **EN TERM PER INVARIANT, och det är inte symmetri för symmetrins skull** (code-reviewer
    // Minor 1 + dess omkontroll, 2026-08-09; formen överlever providerbytet 2026-08-15). Copyn bär
    // TVÅ namnformer: bolaget (`Scaleway SAS`) och tjänsten (`Scaleway Transactional Email`,
    // mottagarsektionen). De två invarianterna vill ha OLIKA mängder, och att driva båda ur en
    // union gör invariant 1 svagare i samma andetag som invariant 2 blir starkare:
    //
    //   Invariant 1 (golv + path-paritet) vill ha den PART-BÄRANDE formen. `count(union) >= 3`
    //   uppfylls av strikt fler dokumenttillstånd än `count(bolaget) >= 3`. Mätt vittne: skriv om
    //   ett mottagarstycke så att avtalsparten försvinner och bara tjänstenamnet står kvar —
    //   unionen ger 3 och passerar, den part-bärande formen ger 2 och fäller. Termen valdes för att
    //   den fångar den part mottagaravsnittet måste namnge; en union hade låtit stycket tappa den
    //   utan att CI sa något.
    //
    //   Invariant 2 (markören) vill ha VARJE omnämnande, alltså unionen. Ett framtida stycke som
    //   namnger leverantören enbart som `Scaleway Transactional Email` (eller `Scaleway TEM`)
    //   itereras inte av en bolagsbunden loop och kan bära ett presens-påstående med testet grönt.
    //
    // **Mitt första kontrafaktum bevisade fel sats** och är värt att minnas: det visade att
    // union-grenen är NÅBAR, inte att den skärper spärren — i just det scenariot *släppte* unionen
    // igenom vad den smalare termen fällde. En probe måste korsa den kontroll den påstår sig testa.
    const svNamed = matchingLeaves(svLegal, /Scaleway SAS/);
    const enNamed = matchingLeaves(enLegal, /Scaleway SAS/);
    const sv = matchingLeaves(svLegal, EMAIL_PROVIDER_ANY);
    const en = matchingLeaves(enLegal, EMAIL_PROVIDER_ANY);

    // Vacuity guard, and simultaneously invariant 1: THREE known sites today — samtyckesavsnittet
    // och TVÅ i "Mottagare av uppgifter". A rename or deletion that drops the disclosure fails here
    // instead of shipping silently.
    // **Golvet var 4 till 2026-08-15 och är 3 sedan dess, HÄRLETT ur uppräkningen ovan** (#183,
    // ADR 0131, senior-cto-advisor bindande): det fjärde stycket låg i "Överföring till tredje
    // land" och är struket MED SIN GRUND — Scaleway SAS är franskt och fransk-ägt, ingen
    // tredjelandsöverföring uppstår, och copyn ska då vara tyst om Kap. V precis som värdraden är.
    // Golvet hölls medvetet inte kvar på 4 genom att skriva in ett fjärde omnämnande. Bunden till
    // den PART-BÄRANDE formen, aldrig till unionen — se resonemanget ovan.
    expect(svNamed.length).toBeGreaterThanOrEqual(3);

    // Parity by LOCATION, not count — see `matchingLeaves`.
    expect(enNamed.map(([path]) => path)).toEqual(svNamed.map(([path]) => path));

    // LIVENESS-GOLV PÅ UNIONSTERMEN (code-reviewer, omkontroll 2026-08-09). Utan det här har
    // `EMAIL_PROVIDER_ANY` **ingen assertion som faller när den slutar matcha**: dess två läsare
    // — markör-loopen nedan och värd-spärrens snittkontroll — passerar BÅDA på tom mängd (noll
    // iterationer respektive tomt snitt), och golvet ovan använder den separata part-bärande
    // formen. Mätt: `EMAIL_PROVIDER_ANY` ersatt med en felstavad variant av leverantörsnamnet gav
    // HELA sviten grön med konstanten död och två spärrar tysta. Extraktionen gjorde den dessutom
    // mer bärande, eftersom den nya läsaren är en tomhets-assertion — den vakuositetsbenägnaste
    // form som finns.
    //
    // Detta bryter INTE "en term per invariant": invariant 1:s golv ligger kvar på `svNamed`
    // (den part-bärande formen). Det här är ett tredje påstående med ren vakuositetsroll, samma
    // funktion som `sv.length >= 1` har i värd-spärren.
    //
    // **Gränsen, utskriven:** unionsmängden är i dag IDENTISK med part-mängden (3 = 3, samma
    // paths), så golvet bevisar att regexet lever — **inte** att union-formen fångar ett stycke
    // som bara bär tjänstenamnet. Ett strikt superset-påstående vore starkare; golvet är husets
    // form och räcker mot den mätta felmoden.
    expect(sv.length).toBeGreaterThanOrEqual(3);
    expect(en.length).toBeGreaterThanOrEqual(3);

    // The RATIFIED SENTENCE, not a token. `/planerat/i` alone accepts a truncated marker ("Detta är
    // planerat.") that drops "ännu inte i drift" — the very clause that says NOT IN OPERATION — while
    // the en pattern accepts no such truncation. That asymmetry let a Swedish-only thinning pass CI.
    // Both sides now bind the sentence, which also closes the "planerat for an unrelated reason" hole.
    for (const [path, paragraph] of sv) expect(paragraph, path).toMatch(SV_STATUS_MARKER);
    for (const [path, paragraph] of en) expect(paragraph, path).toMatch(EN_STATUS_MARKER);
  });

  /**
   * #1199 — VÄRDLEVERANTÖRS-TRIPWIRE (security-auditor, bindande 2026-08-09).
   *
   * Systerspärr till de två ovan, med en avgörande skillnad: **raden bär medvetet INGEN
   * status-markör.** ADR 0050 `Amendment 2026-08-04` bytte värden Hetzner → Netcup, och lådan
   * KÖR sedan 2026-08-05. `release-checklist.md` §2.6 punkt 2 klassade själv värdraden som
   * "deploy-aktiverad: aktiveras av att stacken körs hos dem" — alltså har aktiveringshändelsen
   * redan inträffat, och en markör hade sagt "ännu inte i drift" om en drift som pågår. Det är
   * ADR 0090 D3:s defekt i spegelvänd form. Systrarna ovan får därför INTE läsas som prejudikat
   * för den här raden; de vaktar behandlingar som bevisligen är mörka.
   *
   * Två invarianter, EN term (`netcup GmbH`) — ingen union, av exakt de skäl e-post-spärren
   * skriver ut ovan:
   *
   * 1. **Värden ÄR namngiven** (Art. 13(1)(e) — en mottagare måste framgå). Strukturtestet
   *    överst fäller en ENSIDIG radering via array-längder, men två språk raderade i takt
   *    passerar det. Ett räknat golv är det enda som fäller en tystnad.
   * 2. **Raden bär INTE markörmeningen.** Negativ pin, och inte symmetri för symmetrins skull:
   *    felmoden är en framtida session som "återställer paritet" mot de tre andra
   *    planerat-styckena och därmed återinför påståendet att driften inte är i drift. Pinnen är
   *    icke-vakuös så länge golvet håller — faller golvet itererar loopen noll löv, och golvet
   *    asserteras först.
   *
   * **Termen är den PART-BÄRANDE formen `netcup GmbH`**, inte `Netcup` och inte `netcup`:
   * avtalsparten är den juridiska personen (Impressum, läst 2026-08-09: `netcup GmbH`,
   * HRB 705547 Amtsgericht Mannheim), och det är den formen mottagarsektionen måste bära —
   * samma precisionsstandard som `Scaleway SAS` på e-postraden, och som dess två föregångare bar
   * där före providerbytena 2026-08-09 och 2026-08-15. **Vik den
   * ALDRIG in i e-post-spärrens union** och **återanvänd inte dess markör-halva**; båda
   * fällorna är namngivna i security-auditors Major 3.
   *
   * **ICKE-VAKUOSITETEN ÄR MÄTT I FYRA KÖRNINGAR, EN PER ASSERTION SOM KAN VARA TYST.** Den
   * första räckte inte, och varför den inte räckte är hela poängen (code-reviewer Major 2,
   * 2026-08-09).
   *
   * ⚠ **Alla fyra kördes mot `0ad5587c`**, alltså FÖRE `c56e167b` flyttade värdstycket ur
   * `sections.6.list` till `paragraphs[1]`. Felmeddelandena nedan citeras därför **verbatim** och
   * ska inte skrivas om: de är protokoll över vad specifika körningar faktiskt skrev ut, och en
   * omskrivning till dagens path hade förfalskat bevisningen. Konsekvensen av stämpeln är att
   * `sections.6.list.0` i körning 2 och 3 upplöses till ingenting i dag medan körning 4:s
   * `sections.5.paragraphs.1` fortfarande upplöses — **stickprova inte ett av dem och
   * generalisera.** Stämpeln daterar alla fyra på en gång och överlever nästa strukturflytt utan
   * redigering, till skillnad från en per-rad-parentes som hade krävt en tredje ommätning.
   *
   * 1. **Golvet.** Testet skrevs FÖRST, med `content-legal.json` orörd →
   *    `AssertionError: expected 0 to be greater than or equal to 1`. Hade mätningen gjorts efter
   *    copy-redigeringen hade den inte skilt en fungerande spärr från en som matchar vad som
   *    helst — jfr #1237, där leverantörsnamnet med en avslutande punkt gav 10/10 grönt medan spärren asserterade
   *    ingenting.
   * 2. **Den svenska negativa pinnen.** Golv-kontrafaktumet ovan bevisade den INTE: `expect`
   *    kastar på golvet, så loop-raderna nedan **kördes aldrig** i den röda körningen, och i den
   *    gröna passerar de på ett löv som inte bär markören — alltså oavsett vad mönstret
   *    innehåller. Båda körningarna hade sett identiska ut med ett felstavat mönster. Mätt genom
   *    att korsa spärren i stället: markörmeningen lades tillfälligt på värdraden i `sv` →
   *    `AssertionError: privacy.sections.6.list.0: expected 'netcup GmbH (driftsleverantör,
   *    Tyskla…' not to match /planerat och ännu inte i drift/i`.
   * 3. **Den engelska negativa pinnen, separat.** Samma fälla en nivå ned: när `sv` föll nådde
   *    `en`-loopen aldrig fram. `sv` återställdes, `en` muterades ensam →
   *    `AssertionError: privacy.sections.6.list.0: … not to match /not yet in operation/i`.
   * 4. **Att det delade markör-hemmet är levande.** `SV_STATUS_MARKER` fick sitt ä strippat
   *    (`annu`) → **e-post-spärren** ovan gick RÖD på `privacy.sections.5.paragraphs.1`. Det är
   *    beviset som gör konstanterna värda något: en negerad assertion kan aldrig fälla sitt eget
   *    mönster, så livness måste komma från den positiva systern.
   *
   * ⚠ **En fälla den negativa loopen bygger in** (code-reviewer Minor 5, inte ett fel i dag):
   * loopen går över HELA katalogen. Namnger ett löv någon gång **både** `netcup GmbH` och
   * `Scaleway SAS` blir sviten osatisfierbar — e-post-spärren kräver markören på det
   * lövet, den här förbjuder den — och enda utvägen vore att försvaga en av dem. Skopa i så fall
   * den här loopen till mottagarsektionen; försvaga aldrig någon av spärrarna.
   *
   * Testet ska INTE falla vid någon lansering, till skillnad från systrarna ovan. Raden är
   * formulerad för att aldrig behöva en flip: den påstår drift, inte planer, och äger därför
   * ingen aktiveringshändelse. Faller den är det för att disclosuren försvann eller för att en
   * markör kröp tillbaka.
   */
  it("värdleverantören netcup GmbH är namngiven och raden bär INGEN status-markör (#1199)", () => {
    const sv = matchingLeaves(svLegal, /netcup GmbH/);
    const en = matchingLeaves(enLegal, /netcup GmbH/);

    // Vacuity guard + invariant 1. ETT känt löv i dag: mottagarsektionens värdstycke
    // (`privacy.sections.6.paragraphs.1`). Det låg i sektionens `list` till 2026-08-09;
    // design-reviewer mätte att en enelements-`list` var ensam i hela katalogen och att
    // sektionens egen praxis är ett stycke per mottagare, så behållaren finns inte längre.
    // Spärren bryr sig inte: den binder sv↔en, aldrig sv↔behållare.
    expect(sv.length).toBeGreaterThanOrEqual(1);

    // Parity by LOCATION, not count — see `matchingLeaves`.
    expect(en.map(([path]) => path)).toEqual(sv.map(([path]) => path));

    // FAIL-FAST mot den latenta konflikten ovan, i stället för en varning ingen läser i tid:
    // namnger ett löv BÅDA parterna kräver e-post-spärren markören på det lövet medan den här
    // förbjuder den, och sviten blir osatisfierbar — varvid den "uppenbara" utvägen är att
    // försvaga en av spärrarna. Den här assertionen fäller i stället vid den commit som skriver
    // lövet, och åtgärden är att dela stycket i två: sektionens egen praxis är en mottagare per
    // stycke. Skopa INTE bort täckning för att lösa det.
    const emailSv = new Set(matchingLeaves(svLegal, EMAIL_PROVIDER_ANY).map(([path]) => path));
    const emailEn = new Set(matchingLeaves(enLegal, EMAIL_PROVIDER_ANY).map(([path]) => path));
    expect(sv.map(([path]) => path).filter((path) => emailSv.has(path))).toEqual([]);
    expect(en.map(([path]) => path).filter((path) => emailEn.has(path))).toEqual([]);

    // Invariant 2 — samma RATIFIERADE markörformer som e-post-spärren binder, i negativ polaritet.
    // Delat hem med den positiva assertionen, se konstanternas doc-kommentar överst.
    for (const [path, item] of sv) expect(item, path).not.toMatch(SV_STATUS_MARKER);
    for (const [path, item] of en) expect(item, path).not.toMatch(EN_STATUS_MARKER);
  });

  it("integritetspolicyn har minst tio sektioner med rubrik i båda katalogerna", () => {
    const sv = svLegal.privacy.sections;
    const en = enLegal.privacy.sections;
    expect(sv.length).toBe(en.length);
    expect(sv.length).toBeGreaterThanOrEqual(10);
    for (const section of [...sv, ...en]) {
      expect(typeof section.heading).toBe("string");
      expect(section.heading.length).toBeGreaterThan(0);
    }
  });
});
