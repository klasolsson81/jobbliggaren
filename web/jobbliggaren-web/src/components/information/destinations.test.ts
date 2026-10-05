import { describe, expect, it } from "vitest";
import { destinationForPath, destinationHref, INFORMATION_PATHS, validInformationHref, type ReturnDestination } from "./destinations";
import { LEGAL_SECTION_IDS, legalSectionId } from "./section-ids";
import sv from "../../../messages/sv/content-legal.json";
import en from "../../../messages/en/content-legal.json";
import { GUEST_MOCK } from "@/lib/guest/mock-data";

describe("canonical information destinations", () => {
  it.each(["https://evil.test", "//evil.test", "javascript:alert(1)", "/logga-in?next=/jobb", "/jobb/not-an-id", "/jobb/%2f%2fevil.test", "/jobb/../integritet", "/integritet", "/villkor#provider", "/logga-in/", "/admin", "/logga-in\\evil"])("rejects %s as a return destination", path => {
    expect(destinationForPath(path)).toBeNull();
  });
  it("constructs a canonical route from a validated identifier", () => {
    const path = "/jobb/19860000-0000-4000-8000-000000000001";
    expect(destinationHref(destinationForPath(path)!)).toBe(path);
    expect(destinationHref({ kind: "job", id: "//evil.test" })).toBeNull();
    // An impossible typed caller may only prove safe degradation.
    expect(destinationHref({ kind: "unknown" } as unknown as ReturnDestination)).toBeNull();
    expect(destinationHref({ kind: "__proto__" } as unknown as ReturnDestination)).toBeNull();
  });
  it("admits existing demo destinations and their producer's identifier shapes", () => {
    const paths = ["/gast/jobb", "/gast/ansokningar", "/gast/cv", "/gast/oversikt",
      ...GUEST_MOCK.jobAds.map(ad => `/gast/jobb/${ad.id}`),
      ...GUEST_MOCK.applications.map(application => `/gast/ansokningar/${application.id}`)];
    paths.forEach(path => expect(destinationHref(destinationForPath(path)!)).toBe(path));
  });
  it.each(["/gast/jobb/gj-0", "/gast/jobb/gj-01", "/gast/jobb/gj-1000", "/gast/jobb/ga-1", "/gast/ansokningar/gj-1", "/gast/ansokningar/ga-1?next=/", "/gast/jobb/%2f%2fevil.test"])("rejects an invalid demo identifier in %s", path => {
    expect(destinationForPath(path)).toBeNull();
  });
  it.each(["/integritet?returnTo=/jobb", "//evil.test/integritet", "https://evil.test/integritet", "/cookies#", "/cookies#about-cookies#extra", "/cookies#%22", "/cookies#<script>"])("rejects an untrusted information href %s", href => {
    expect(validInformationHref(href)).toBe(false);
  });
  it("admits only the thirteen canonical pages and optional stable fragments", () => {
    expect(INFORMATION_PATHS).toHaveLength(13);
    for (const path of INFORMATION_PATHS) {
      expect(validInformationHref(path)).toBe(true);
      expect(validInformationHref(`${path}#stable-section`)).toBe(true);
    }
  });
});

describe("legal section anchors", () => {
  it("maps every section in both unchanged legal catalogs to one stable identifier", () => {
    for (const page of Object.keys(LEGAL_SECTION_IDS) as (keyof typeof LEGAL_SECTION_IDS)[]) {
      const ids = LEGAL_SECTION_IDS[page];
      const key = ({ integritet: "privacy", villkor: "terms", cookies: "cookies", tillganglighet: "accessibility" } as const)[page];
      expect(ids.length).toBe(sv[key].sections.length);
      expect(ids.length).toBe(en[key].sections.length);
      expect(new Set(ids).size).toBe(ids.length);
      ids.forEach((id, index) => expect(legalSectionId(page, index)).toBe(id));
      expect(() => legalSectionId(page, ids.length)).toThrow();
    }
  });
});
