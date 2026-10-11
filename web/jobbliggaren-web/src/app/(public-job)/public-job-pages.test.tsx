import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createFormatter, createTranslator } from "next-intl";
import sv from "../../../messages/sv";
import type { PublicJobAdResult } from "@/lib/api/public-job-ad";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({ locale: "sv", messages: sv as never, namespace: namespace as never }),
  getFormatter: async () => createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
}));

const getPublicJobAd = vi.fn<(id: string) => Promise<PublicJobAdResult>>();
vi.mock("@/lib/api/public-job-ad", () => ({ getPublicJobAd: (id: string) => getPublicJobAd(id) }));

import PublicJobPage, { generateMetadata } from "./publik/jobb/[id]/page";
import PublicJobGonePage, { generateMetadata as goneMetadata } from "./publik/borttagen/page";
import PublicJobBusyPage, { generateMetadata as busyMetadata } from "./publik/overbelastad/page";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const SENTINEL_NAME = "Sentinel Namnsson";

const AD = {
  id: ID,
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  description: `Vi söker dig. Kontakta ${SENTINEL_NAME} på avdelningen.`,
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00Z",
  expiresAt: "2026-11-01T08:00:00Z",
};

const params = (id = ID) => ({ params: Promise.resolve({ id }) });

beforeEach(() => {
  getPublicJobAd.mockReset();
});

describe("the public job page", () => {
  it("renders the ad for a listed read", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    render(await PublicJobPage(params()));

    expect(screen.getByRole("heading", { level: 1, name: "Sjuksköterska" })).toBeInTheDocument();
  });

  it("answers a 404 or 410 read (the ad ended after the edge's probe) with notFound(), a real 404", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "notFound" });
    await expect(PublicJobPage(params())).rejects.toMatchObject({ digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404") });
  });

  it("throws on an unavailable read, which is a 5xx; it is never a notFound() (ADR 0160 D9.5)", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "unavailable" });

    const error = await PublicJobPage(params()).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(Error);
    expect(String((error as { digest?: unknown }).digest ?? "")).not.toContain("NOT_FOUND");
    expect(String((error as { digest?: unknown }).digest ?? "")).not.toContain("404");
  });

  it("reads the ad once for the page and once for the metadata, which React's cache makes one call per render", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    await generateMetadata(params());
    render(await PublicJobPage(params()));

    expect(getPublicJobAd.mock.calls.every(([id]) => id === ID)).toBe(true);
  });
});

describe("the public job page's metadata", () => {
  it("is built from the structured fields: the title, the company, the dates and the clean canonical", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params(ID.toUpperCase()));

    expect(metadata.title).toBe("Sjuksköterska – Region Exempel");
    expect(metadata.description).toBe(
      "Jobbannons från Region Exempel: Sjuksköterska. Publicerad 1 okt. 2026. Sista ansökningsdag 1 nov. 2026."
    );
    expect(metadata.alternates?.canonical).toBe(`https://dev.jobbliggaren.se/jobb/${ID}`);
    expect(metadata.robots).toEqual({ index: true, follow: true });
    expect(metadata.openGraph).toMatchObject({
      type: "website",
      siteName: "Jobbliggaren",
      title: "Sjuksköterska – Region Exempel",
      url: `https://dev.jobbliggaren.se/jobb/${ID}`,
      images: [{ url: "/opengraph-image", width: 1200, height: 630 }],
    });
  });

  it("never carries the free text: a name in the description is in no tag a link preview or a snippet could copy", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params());

    expect(JSON.stringify(metadata)).not.toContain(SENTINEL_NAME);
    expect(JSON.stringify(metadata)).not.toContain("Vi söker dig");
  });

  it("leaves out the deadline sentence for an ad without a deadline", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, expiresAt: null } });
    const metadata = await generateMetadata(params());

    expect(metadata.description).toBe("Jobbannons från Region Exempel: Sjuksköterska. Publicerad 1 okt. 2026.");
  });

  it.each([{ kind: "notFound" }, { kind: "unavailable" }] as const)(
    "is the shared not-found title for a read of kind %j: no ad title, no company",
    async (result) => {
      getPublicJobAd.mockResolvedValue(result);
      expect(await generateMetadata(params())).toEqual(await notFoundMetadata());
    }
  );
});

describe("the 410 page", () => {
  it("says the same four words about every ad: no title, no company, no date, no id, nothing read", async () => {
    render(await PublicJobGonePage());

    expect(screen.getByRole("heading", { level: 1, name: "Annonsen är inte längre tillgänglig" })).toBeInTheDocument();
    expect(screen.getByText("Sista ansökningsdagen kan ha passerat eller så har annonsen tagits bort.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Till startsidan" })).toHaveAttribute("href", "/");
    expect(getPublicJobAd).not.toHaveBeenCalled();
  });

  it("is never indexed and its title is its own", async () => {
    expect(await goneMetadata()).toEqual({
      title: "Annonsen är inte längre tillgänglig",
      robots: { index: false, follow: false },
    });
  });

  it("is one document for an ended and an erased ad: two renders are the same markup", async () => {
    const first = render(await PublicJobGonePage()).container.innerHTML;
    const second = render(await PublicJobGonePage()).container.innerHTML;
    expect(first).toBe(second);
  });
});

describe("the 503 page", () => {
  it("states no cause: the budget and an outage read alike", async () => {
    render(await PublicJobBusyPage());

    expect(screen.getByRole("heading", { level: 1, name: "Annonsen kan inte visas just nu" })).toBeInTheDocument();
    expect(screen.getByText("Försök igen om en stund.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Till startsidan" })).toHaveAttribute("href", "/");
  });

  it("is never indexed and its title differs from the 410's", async () => {
    const busy = await busyMetadata();
    const gone = await goneMetadata();

    expect(busy.robots).toEqual({ index: false, follow: false });
    expect(busy.title).not.toBe(gone.title);
  });
});

describe("the copy", () => {
  it("has no exclamation mark and no emoji, in either catalogue", async () => {
    const en = (await import("../../../messages/en")).default;
    for (const catalogue of [sv, en]) {
      const text = JSON.stringify(catalogue.publicjob);
      expect(text).not.toContain("!");
      expect(text).not.toMatch(/\p{Extended_Pictographic}/u);
    }
  });
});
