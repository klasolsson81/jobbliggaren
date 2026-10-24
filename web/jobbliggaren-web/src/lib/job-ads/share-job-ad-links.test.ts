import { describe, expect, it } from "vitest";
import { publicJobAdTitle } from "./public-job-ad-title";
import { publicJobAdUrl } from "./public-job-ad-url";
import { shareTargetLinks } from "./share-job-ad-links";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const URL_ = publicJobAdUrl(ID);
const DATA = { title: publicJobAdTitle({ title: "Sjuksköterska", companyName: "Region Exempel" }), url: URL_ };

/** A mailto URL's query, parsed as RFC 6068 values: `+` is a literal character, never a space. */
function mailtoParams(href: string): Map<string, string> {
  expect(href.startsWith("mailto:?")).toBe(true);
  const query = href.slice("mailto:?".length);
  return new Map(
    query.split("&").map((pair) => {
      const [key, value = ""] = pair.split("=");
      return [decodeURIComponent(key ?? ""), decodeURIComponent(value)];
    })
  );
}

describe("shareTargetLinks", () => {
  it("lists Facebook, LinkedIn and WhatsApp alphabetically, then e-mail, and nothing else (ADR 0183)", () => {
    expect(shareTargetLinks(DATA).map((link) => link.target)).toEqual(["facebook", "linkedin", "whatsapp", "email"]);
  });

  it("hands each platform the canonical link as its one parameter, and nothing of the title or the employer", () => {
    const [facebook, linkedin, whatsapp] = shareTargetLinks(DATA).map((link) => new URL(link.href));

    expect(`${facebook?.origin}${facebook?.pathname}`).toBe("https://www.facebook.com/sharer/sharer.php");
    expect([...(facebook?.searchParams ?? [])]).toEqual([["u", URL_]]);
    expect(`${linkedin?.origin}${linkedin?.pathname}`).toBe("https://www.linkedin.com/sharing/share-offsite/");
    expect([...(linkedin?.searchParams ?? [])]).toEqual([["url", URL_]]);
    expect(`${whatsapp?.origin}${whatsapp?.pathname}`).toBe("https://wa.me/");
    expect([...(whatsapp?.searchParams ?? [])]).toEqual([["text", URL_]]);
    for (const link of shareTargetLinks(DATA)) {
      expect(link.href).not.toMatch(/utm_|app_id|via=|hashtags/);
    }
    for (const link of shareTargetLinks(DATA).filter((l) => l.target !== "email")) {
      expect(link.href).not.toContain("Region");
    }
  });

  it("writes the mail with the page title as its subject and the link as its body, and no recipient", () => {
    const email = shareTargetLinks(DATA).find((link) => link.target === "email");
    const params = mailtoParams(email?.href ?? "");

    expect([...params.keys()]).toEqual(["subject", "body"]);
    expect(params.get("subject")).toBe("Sjuksköterska – Region Exempel");
    expect(params.get("body")).toBe(URL_);
  });

  it("keeps a hostile title as plain data: no parameter is added, and line breaks and tabs become one space", () => {
    const hostile = {
      title: "Säljare & co #1 + 100% ? &bcc=x@example.com&body=evil\r\n\r\nTo: x@example.com\tslut – Firma",
      url: URL_,
    };
    const links = shareTargetLinks(hostile);
    const params = mailtoParams(links.find((link) => link.target === "email")?.href ?? "");

    expect([...params.keys()]).toEqual(["subject", "body"]);
    expect(params.get("subject")).toBe("Säljare & co #1 + 100% ? &bcc=x@example.com&body=evil To: x@example.com slut – Firma");
    expect(params.get("body")).toBe(URL_);
    for (const link of links.filter((l) => l.target !== "email")) {
      expect([...new URL(link.href).searchParams].length).toBe(1);
    }
  });

  it("encodes a space as %20, never as +", () => {
    const email = shareTargetLinks(DATA).find((link) => link.target === "email");

    expect(email?.href).toContain("Sjuksk%C3%B6terska%20%E2%80%93%20Region%20Exempel");
    expect(email?.href).not.toContain("+");
  });
});
