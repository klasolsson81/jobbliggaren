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
    const historyParagraphs = (catalogue: unknown, term: RegExp) =>
      leafPaths(catalogue)
        .map((path) =>
          path
            .split(".")
            .reduce<unknown>(
              (node, key) => (node as Record<string, unknown>)?.[key],
              catalogue
            )
        )
        .filter((leaf): leaf is string => typeof leaf === "string" && term.test(leaf));

    const sv = historyParagraphs(svLegal.privacy, /ansökningshistorik/i);
    const en = historyParagraphs(enLegal.privacy, /application history/i);

    // Guard against a vacuous pass: if the paragraphs are ever renamed away, the filter would match
    // nothing and every assertion below would trivially hold. Three known sites today (Art. 13
    // data-categories list, retention list, "Inga automatiserade beslut").
    expect(sv.length).toBeGreaterThanOrEqual(3);
    expect(en.length).toBe(sv.length);

    for (const paragraph of sv) expect(paragraph).toMatch(/planerat/i);
    for (const paragraph of en) expect(paragraph).toMatch(/planned/i);
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
