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
   * 2026-07-26). **TERMEN RIKTADES OM 2026-08-09 (#1169)**: ADR 0124 bytte providern från
   * Resend, Inc. (USA) till Amazon Web Services EMEA SARL (Luxemburg), så `/Resend/` hade blivit
   * en spärr som vaktar en part vi inte längre har. Vad som INTE ändrades: golvet, path-pariteten
   * och markör-halvan. Detta är inte prod-flippen — armen förblir mörk.
   *
   * **Omriktningen är mätt icke-vakuös, i den ordning som är det enda beviset:** termen byttes
   * FÖRST, med `content-legal.json` orörd, och testet föll på golvet
   * (`AssertionError: expected 0 to be greater than or equal to 4`). Hade den mätningen gjorts
   * efter copy-redigeringen hade den inte skilt en fungerande spärr från en som matchar vad som
   * helst — jfr #1237, där `"Amazon"` → `"Amazon."` gav 10/10 grönt medan spärren asserterade
   * ingenting.
   *
   * Två invarianter i ett test, båda riktningarna av samma defekt:
   *
   * 1. **Leverantören ÄR namngiven.** Detta är hela #186:s leverans (Art. 13(1)(e)/28 — en
   *    mottagare av personuppgifter måste framgå). Före den ändringen bar policyn ett
   *    e-poststycke som var *sant* men aldrig nämnde en leverantör, vilket gjorde frånvaron
   *    OSYNLIG för varje token-grep: leverantörstoken hade noll träffar i hela katalogen, och tre
   *    nollträffs-scopingar i rad missade därför att stycket alls fanns. Ett räknat golv är
   *    det enda som fäller en tystnad.
   * 2. **Varje omnämnande bär status-markören** tills `Email:Provider` flippas. SES är i dag
   *    dark i non-dev (`AddEmailSender` → `NullEmailSender`), så ett presens-påstående vore den
   *    motsatta osanningen — exakt den ansökningshistoriken-fällan som testet ovan finns för.
   *    Flippen är grindad av `release-checklist.md` §2.5 punkt 1 (FEM led — uppräkningen bor
   *    där, aldrig här), aldrig av en copy-ändring.
   *
   * **Markören måste bindas till STATUS-MENINGEN, inte till stycket** (code-reviewer Major 2,
   * mätt: den första formen passerade VACUÖST i två av tre leaves i BÅDA språken). Orsaken är att
   * disclosure-meningens egna participform mättar en bred assertion — "Notiserna **planeras** att
   * skickas", "All e-post **planeras** att levereras" / "are **planned** to be sent". Med
   * `/planerat|planerad|planeras/` respektive `/planned/` kunde markörmeningen strykas ur rad 63
   * och 73 med testet grönt, medan §2.6:s smala grep tyst föll 9+9 → 7+7. Mönstren nedan är därför
   * de RATIFIERADE markörformerna och inget bredare — och de binder hela MENINGEN
   * (`planerat och ännu inte i drift`), **avsiktligt smalare** än ansökningshistorik-tripwirens
   * `planerat`. Systern kan INTE följa med: rad 99/100 bär `(planerat)` utan markörmeningen, så
   * meningsformen hade fällt dem. Bredda aldrig tillbaka. Och "not yet in operation" är den engelska
   * markörens bärande led (`/planned/` är otillräcklig oavsett bredd).
   *
   * Testet ska FALLA vid prod-flippen. Ta då bort markör-halvan i samma ändring som flippar
   * copyn — men BEHÅLL golvet OCH path-pariteten: leverantören måste vara namngiven efter flippen
   * också, och då hårdare än nu.
   */
  it("e-postleverantören AWS är namngiven i policyn och varje omnämnande bär status-markören (#186/#1169)", () => {
    // WHOLE catalogue, not just `privacy`: measured 0 mentions outside `privacy` today (4 of 4 leaves
    // per språk ligger i `privacy`), so the widening is free and strictly increases coverage. A future
    // mention in `terms`/`cookies`/`recruiterNotice` would otherwise escape both the floor and the
    // marker requirement.
    //
    // Termen är den PROCESSOR-BÄRANDE strängen, inte "SES" och inte "Amazon": `Amazon Web Services`
    // matchar både avtalsparten (`... EMEA SARL`) och koncernmodern (`..., Inc.`), vilket är precis
    // de två parter Kap. V-stycket måste namnge. "SES" hade missat mottagar-stycket, som namnger
    // bolaget och inte produkten.
    const sv = matchingLeaves(svLegal, /Amazon Web Services/);
    const en = matchingLeaves(enLegal, /Amazon Web Services/);

    // Vacuity guard, and simultaneously invariant 1: FOUR known sites today (consent section, TWO in
    // "Mottagare av uppgifter" and one in "Överföring till tredje land"). A rename or deletion that
    // drops the disclosure fails here instead of shipping silently. Golvet är OFÖRÄNDRAT 4 över
    // providerbytet: samma fyra stycken bär namnet före och efter (#1169).
    expect(sv.length).toBeGreaterThanOrEqual(4);

    // Parity by LOCATION, not count — see `matchingLeaves`.
    expect(en.map(([path]) => path)).toEqual(sv.map(([path]) => path));

    // The RATIFIED SENTENCE, not a token. `/planerat/i` alone accepts a truncated marker ("Detta är
    // planerat.") that drops "ännu inte i drift" — the very clause that says NOT IN OPERATION — while
    // the en pattern accepts no such truncation. That asymmetry let a Swedish-only thinning pass CI.
    // Both sides now bind the sentence, which also closes the "planerat for an unrelated reason" hole.
    for (const [path, paragraph] of sv) expect(paragraph, path).toMatch(/planerat och ännu inte i drift/i);
    for (const [path, paragraph] of en) expect(paragraph, path).toMatch(/not yet in operation/i);
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
