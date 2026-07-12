import { describe, it, expect } from "vitest";
import { buildPageHref } from "./jobb-results";

/**
 * #823 — pagineringslänken måste klampa ett för kort q, precis som page.tsx gör vid entry.
 * Utan klampen blev /jobb?q=a → "Nästa sida" = /jobb?page=2&q=a: en länk VI genererar som
 * påstår ett sök sidan självt inte kör, medan sökfältet står tomt.
 *
 * Den här vakten finns för att e2e:n aldrig når hit — med tom annons-DB renderas ingen
 * paginering alls, så raden hade kunnat tas bort utan att något blev rött.
 */
describe("buildPageHref — q-klampen (#823)", () => {
  // Annotering, inte `as`: en assertion hade stängt av kontrollen permanent — läggs ett
  // required-fält till fortsätter filen kompilera medan buildern tar en annan gren.
  const params: Parameters<typeof buildPageHref>[0] = {};

  it("droppar ett q under backendens minimum ur sidlänken", () => {
    const href = buildPageHref({ ...params, q: "a" }, 2, 20);
    expect(href).not.toContain("q=");
    expect(href).toContain("page=2");
  });

  it("behåller ett giltigt q — och normaliserar det som page.tsx gör", () => {
    expect(buildPageHref({ ...params, q: "backend" }, 2, 20)).toContain(
      "q=backend"
    );
    // Trimmad paritet: annars kör sidan "ab" medan länken bär "+ab+".
    expect(buildPageHref({ ...params, q: " ab " }, 2, 20)).toContain("q=ab");
  });
});
