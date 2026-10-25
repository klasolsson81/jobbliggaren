import { describe, expect, it } from "vitest";
import { adSourceLink, applicationLinkHref } from "./ad-source-link";

describe("adSourceLink", () => {
  it.each([
    "https://arbetsformedlingen.se/platsbanken/annonser/1",
    "https://www.arbetsformedlingen.se/platsbanken/annonser/1",
    "https://ARBETSFORMEDLINGEN.SE/x",
    "http://arbetsformedlingen.se/x",
  ])("labels %s as Arbetsförmedlingen's", (url) => {
    expect(adSourceLink(url)?.label).toBe("openAdAf");
  });

  it.each([
    "https://example.com/jobb/1",
    "https://arbetsformedlingen.se.evil.test/x",
    "https://notarbetsformedlingen.se/x",
    "https://evil.test/arbetsformedlingen.se",
    "https://evil.test/?u=arbetsformedlingen.se",
  ])("labels %s with the plain label, because its host is not Arbetsförmedlingen's", (url) => {
    expect(adSourceLink(url)?.label).toBe("openAd");
  });

  it.each(["javascript:alert(1)", "data:text/html,x", "vbscript:x", "ftp://arbetsformedlingen.se/x", "mailto:a@example.test", "//example.com", "", "not a url"])(
    "gives %s no link at all",
    (url) => {
      expect(adSourceLink(url)).toBeNull();
    }
  );

  it("returns the normalised href, which is what reaches the page", () => {
    expect(adSourceLink("https://example.com")?.href).toBe("https://example.com/");
  });
});

describe("applicationLinkHref", () => {
  it.each([
    ["https://jobb.example.se/ansok/1", "https://jobb.example.se/ansok/1"],
    ["HTTP://JOBB.EXAMPLE.SE/Ansok", "http://jobb.example.se/Ansok"],
    ["https://example.com", "https://example.com/"],
  ])("gives %s its normalised href", (url, href) => {
    expect(applicationLinkHref(url)).toBe(href);
  });

  it.each([null, undefined, "", "javascript:alert(1)", "data:text/html,x", "ftp://jobb.example.se/x", "mailto:a@example.test", "//example.com", "not a url"])(
    "gives %s no link at all",
    (url) => {
      expect(applicationLinkHref(url)).toBeNull();
    }
  );
});
