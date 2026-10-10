import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { SITE_URL } from "@/lib/site-url";
import svMessages from "../../../messages/sv";
import { SiteIdentityJsonLd } from "./site-identity-json-ld";

// apple-icon.tsx renders through next/og, which needs the edge runtime; only its
// declared size and type are read here.
vi.mock("next/og", () => ({ ImageResponse: vi.fn() }));

function renderBlocks(): unknown[] {
  const { container } = render(
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <SiteIdentityJsonLd />
    </NextIntlClientProvider>
  );
  return [...container.querySelectorAll('script[type="application/ld+json"]')].map(
    (script) => JSON.parse(script.innerHTML) as unknown
  );
}

describe("SiteIdentityJsonLd (#2092)", () => {
  it("is exactly a WebSite and an Organization on the site's own origin, with nothing invented", () => {
    expect(renderBlocks()).toEqual([
      {
        "@context": "https://schema.org",
        "@type": "WebSite",
        name: "Jobbliggaren",
        alternateName: new URL(SITE_URL).hostname,
        url: SITE_URL,
      },
      {
        "@context": "https://schema.org",
        "@type": "Organization",
        name: "Jobbliggaren",
        url: SITE_URL,
        logo: `${SITE_URL}/apple-icon`,
      },
    ]);
  });

  it("names the site as og:site_name does", () => {
    const [website] = renderBlocks() as [{ name: string }];
    expect(website.name).toBe(svMessages.metadata.applicationName);
  });

  it("points the logo at a route that serves a PNG of at least Google's 112 px", async () => {
    const { size, contentType } = await import("@/app/apple-icon");

    expect(contentType).toBe("image/png");
    expect(size.width).toBeGreaterThanOrEqual(112);
    expect(size.height).toBeGreaterThanOrEqual(112);
  });
});
