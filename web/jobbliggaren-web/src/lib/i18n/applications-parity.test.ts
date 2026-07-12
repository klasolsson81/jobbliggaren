import { describe, it, expect } from "vitest";
import svApplications from "../../../messages/sv/applications.json";
import enApplications from "../../../messages/en/applications.json";

/**
 * sv/en-paritet för applications-namespacet. next-intl typar mot SV-katalogen
 * (source of truth); EN är en plain JSON-import som tsc INTE korslänkar, så en
 * saknad EN-nyckel slinker igenom typkollen och ger en tom sträng/fallback i
 * runtime. Detta test pinnar att de två katalogerna har IDENTISK nyckel-struktur
 * (rekursivt) så varje sv-nyckel har en en-motsvarighet (och vice versa) — och
 * täcker explicit den nya `ui.preservedAd`-grenen (#315 / ADR 0086, bevarad
 * annons-snapshot som fallback när live-annonsen är arkiverad).
 */

// Rekursiva, sorterade dot-paths för alla LÖV-nycklar i ett message-objekt.
function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("applications i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enApplications)).toEqual(leafPaths(svApplications));
  });

  it("preservedAd-nycklarna finns i båda katalogerna (#315 / ADR 0086)", () => {
    const required = [
      "ui.preservedAd.panelTitle",
      "ui.preservedAd.savedNotice",
      "ui.preservedAd.company",
      "ui.preservedAd.location",
      "ui.preservedAd.published",
      "ui.preservedAd.applyBy",
      "ui.preservedAd.source",
      "ui.preservedAd.descriptionLabel",
      "ui.preservedAd.minimizedNotice",
    ];
    const sv = new Set(leafPaths(svApplications));
    const en = new Set(leafPaths(enApplications));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });

  // #805-3 (Beslut B): utlänken flyttade FRÅN den bevarade panelen (som bara
  // renderas när annonsen är BORTA — där hade länken varit död) TILL
  // SourceAdSection, som visar den medan annonsen ÄR aktiv. Testet pinnar båda
  // sidorna av flytten så den inte kan backas tyst.
  it("jobInfo-nycklarna bär utlänken; preservedAd har INGEN länk kvar (#805-3)", () => {
    const sv = new Set(leafPaths(svApplications));
    const en = new Set(leafPaths(enApplications));

    const required = [
      "ui.jobInfo.title",
      "ui.jobInfo.viewAd",
      "ui.jobInfo.viewAdAriaLabel",
      // Manuell ansökan: ingen källa att namnge. Utan denna nyckel skulle
      // aria-label:n rendera "Visa annonsen hos Manuellt".
      "ui.jobInfo.viewAdAriaLabelManual",
      // Borta-läget utan bevarad kopia (ansökan skapad före #315).
      "ui.jobInfo.noLongerActive",
    ];
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }

    // Den döda länken får inte smyga tillbaka in i borta-läget.
    for (const path of ["ui.preservedAd.viewAd", "ui.preservedAd.viewAdAriaLabel"]) {
      expect(sv.has(path), `sv har kvar borttagen nyckel ${path}`).toBe(false);
      expect(en.has(path), `en har kvar borttagen nyckel ${path}`).toBe(false);
    }
  });
});
