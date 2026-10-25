import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createFormatter, createTranslator } from "next-intl";
import sv from "../../../messages/sv";
import type { PublicJobAdResult } from "@/lib/api/public-job-ad";
import type { PublicJobAdRelatedResult } from "@/lib/api/public-job-ad-related";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({ locale: "sv", messages: sv as never, namespace: namespace as never }),
  getFormatter: async () => createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
  getLocale: async () => "sv",
}));

const getPublicJobAd = vi.fn<(id: string) => Promise<PublicJobAdResult>>();
vi.mock("@/lib/api/public-job-ad", () => ({ getPublicJobAd: (id: string) => getPublicJobAd(id) }));
const getPublicJobAdRelated = vi.fn<(id: string) => Promise<PublicJobAdRelatedResult>>();
vi.mock("@/lib/api/public-job-ad-related", () => ({ getPublicJobAdRelated: (id: string) => getPublicJobAdRelated(id) }));

import PublicJobPage, { generateMetadata } from "./publik/jobb/[id]/page";
import PublicJobGonePage, { generateMetadata as goneMetadata } from "./publik/borttagen/page";
import PublicJobBusyPage, { generateMetadata as busyMetadata } from "./publik/overbelastad/page";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const SENTINEL_NAME = "Sentinel Namnsson";
const GROUP = "Grundutbildade sjuksköterskor";

const AD = {
  id: ID,
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  description: `Vi söker dig. Kontakta ${SENTINEL_NAME} på avdelningen.`,
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00Z",
  expiresAt: "2026-11-01T08:00:00Z",
  municipalityName: "Stockholm",
  regionName: "Stockholms län",
  occupationGroupName: GROUP,
  employmentTypeConceptId: "gro4_cWF_6D7",
  worktimeExtentConceptId: "6YE1_gAC_R2G",
};

const params = (id = ID) => ({ params: Promise.resolve({ id }) });
const APPLICATION_PAGE = "https://jobb.example.se/ansok/1";

const RELATED_SENTINEL = "Relaterad Sentinelsson Enskild Firma";
const RELATED_ROW = {
  id: "0b0b0b0b-1111-4222-8333-555555555555",
  title: "Undersköterska sökes",
  companyName: RELATED_SENTINEL,
  municipalityName: "Solna",
  regionName: "Stockholms län",
  publishedAt: "2026-10-02T08:00:00Z",
};

beforeEach(() => {
  getPublicJobAd.mockReset();
  getPublicJobAdRelated.mockReset();
  getPublicJobAdRelated.mockResolvedValue({ kind: "unavailable" });
});

describe("the public job page", () => {
  it("renders the ad for a listed read", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    render(await PublicJobPage(params()));

    expect(screen.getByRole("heading", { level: 1, name: "Sjuksköterska" })).toBeInTheDocument();
  });

  it("shows the occupation group in the facts row, keeps one primary action and puts the explainer links after the article", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const { container } = render(await PublicJobPage(params()));

    expect(container.querySelector('.jp-modal__facts [lang="sv"]')?.textContent).toBe(GROUP);
    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
    const heading = screen.getByRole("heading", { level: 2, name: "Relaterade sidor" });
    expect(heading.closest("article")).toBeNull();
    expect(heading.closest("main")).not.toBeNull();
  });

  it("keeps one primary action when the ad carries the employer's application page, which is then Ansök (#2214 PR 1b)", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, applicationUrl: APPLICATION_PAGE } });
    const { container } = render(await PublicJobPage(params()));

    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
    expect(screen.getByRole("link", { name: "Ansök" })).toHaveAttribute("href", APPLICATION_PAGE);
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

});

describe("the public job page's related section (ADR 0182)", () => {
  it("reads the related ads once, from the page body, in parallel with the ad's own read", async () => {
    let releaseAd: (value: PublicJobAdResult) => void = () => undefined;
    getPublicJobAd.mockReturnValue(new Promise((resolve) => (releaseAd = resolve)));
    getPublicJobAdRelated.mockResolvedValue({ kind: "ok", rows: [RELATED_ROW] });

    const page = PublicJobPage(params());
    await Promise.resolve();
    await Promise.resolve();

    expect(getPublicJobAdRelated).toHaveBeenCalledTimes(1);
    expect(getPublicJobAdRelated).toHaveBeenCalledWith(ID);
    expect(getPublicJobAd).toHaveBeenCalledTimes(1);
    releaseAd({ kind: "ok", jobAd: AD });
    render(await page);

    expect(screen.getByRole("heading", { level: 2, name: "Andra jobb i samma yrkesgrupp" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: RELATED_ROW.title })).toBeInTheDocument();
  });

  it("renders the ad without the section when the related read is unavailable", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const { container } = render(await PublicJobPage(params()));

    expect(screen.getByRole("heading", { level: 1, name: "Sjuksköterska" })).toBeInTheDocument();
    expect(container.querySelector(`section[aria-labelledby="jp-related-title"]`)).toBeNull();
    expect(screen.queryByRole("heading", { name: "Andra jobb i samma yrkesgrupp" })).toBeNull();
  });

  it("never reads the related ads for the metadata, and no related value reaches a title, description or OpenGraph tag", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    getPublicJobAdRelated.mockResolvedValue({ kind: "ok", rows: [RELATED_ROW] });

    const metadata = await generateMetadata(params());

    expect(getPublicJobAdRelated).not.toHaveBeenCalled();
    expect(JSON.stringify(metadata)).not.toContain(RELATED_SENTINEL);
    expect(JSON.stringify(metadata)).not.toContain(RELATED_ROW.title);
  });

  it("lets the ad's own read decide the status: a notFound read is a real 404 even when the related read has rows", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "notFound" });
    getPublicJobAdRelated.mockResolvedValue({ kind: "ok", rows: [RELATED_ROW] });

    await expect(PublicJobPage(params())).rejects.toMatchObject({
      digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404"),
    });
  });

  it("lets the ad's own read decide the status: an unavailable read is a 5xx, never a 404, even when the related read has rows", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "unavailable" });
    getPublicJobAdRelated.mockResolvedValue({ kind: "ok", rows: [RELATED_ROW] });

    const error = await PublicJobPage(params()).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(Error);
    expect(String((error as { digest?: unknown }).digest ?? "")).not.toContain("404");
  });
});

describe("the public job page's metadata", () => {
  it("is built from the structured fields: the title, the company, the dates and the clean canonical", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params());

    expect(metadata.title).toBe("Sjuksköterska – Region Exempel");
    expect(metadata.description).toBe(
      "Jobbannons från Region Exempel: Sjuksköterska. Publicerad 1 okt. 2026. Sista ansökningsdag 1 nov. 2026."
    );
    expect(metadata.alternates?.canonical).toBe(`https://dev.jobbliggaren.se/jobb/${ID}`);
    expect(metadata.robots).toEqual({ index: true, follow: true });
    expect(metadata.openGraph).toMatchObject({
      type: "website",
      locale: "sv_SE",
      siteName: "Jobbliggaren",
      title: "Sjuksköterska – Region Exempel",
      url: `https://dev.jobbliggaren.se/jobb/${ID}`,
      images: [{ url: "/delningsbild", width: 400, height: 400, alt: "Jobbliggaren" }],
    });
  });

  // ADR 0165 Amendment 2026-10-25: the compact card. Exactly these four members: a handle or an app id of ours is an
  // ADR 0166 D10 trigger-5 question (ADR 0183 D7), and the strings are the same ones OpenGraph carries.
  it("names the summary card with the same title, description and square image as OpenGraph, and nothing of ours", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params());

    expect(metadata.twitter).toStrictEqual({
      card: "summary",
      title: "Sjuksköterska – Region Exempel",
      description: metadata.description,
      images: [{ url: "/delningsbild", width: 400, height: 400, alt: "Jobbliggaren" }],
    });
  });

  it("names the same image, with no query and no id, for every ad: nothing of an ad reaches the preview image", async () => {
    const OTHER_ID = "0c0c0c0c-2222-4333-8444-555555555555";
    getPublicJobAd.mockImplementation(async (id) => ({ kind: "ok", jobAd: { ...AD, id, title: `Annons ${id}` } }));
    const [first, second] = await Promise.all([generateMetadata(params()), generateMetadata(params(OTHER_ID))]);

    const images = [first, second].flatMap((metadata) => [metadata.openGraph?.images, metadata.twitter?.images]);
    expect(new Set(images.map((value) => JSON.stringify(value))).size).toBe(1);
    expect(JSON.stringify(images[0])).not.toMatch(/\?|0b0b0b0b|0c0c0c0c/);
  });

  // The JobPosting data block on the page DOES carry the description, by design and with Klas's and
  // security-auditor's reading of 2026-10-17 (ADR 0171; ADR 0166 Amendment): its consumers are the
  // search engines that already index the visible text. The metadata rule below is unchanged by that.
  it("never carries the free text: a name in the description is in no tag a link preview or a snippet could copy", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params());

    expect(JSON.stringify(metadata)).not.toContain(SENTINEL_NAME);
    expect(JSON.stringify(metadata)).not.toContain("Vi söker dig");
  });

  it("never carries the application page: it is the page's own link only, in no title, description, OpenGraph or Twitter tag (#2214 PR 1b)", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, applicationUrl: APPLICATION_PAGE } });
    const metadata = await generateMetadata(params());

    expect(JSON.stringify(metadata)).not.toContain("jobb.example.se");
  });

  it("never names the occupation group: it is in the facts row only, in no title, description or OpenGraph tag (#2203)", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: AD });
    const metadata = await generateMetadata(params());

    expect(JSON.stringify(metadata)).not.toContain(GROUP);
    expect(JSON.stringify(metadata)).not.toMatch(/sjuksköterskor/i);
  });

  it("leaves out the deadline sentence for an ad without a deadline", async () => {
    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, expiresAt: null } });
    const metadata = await generateMetadata(params());

    expect(metadata.description).toBe("Jobbannons från Region Exempel: Sjuksköterska. Publicerad 1 okt. 2026.");
  });

  // #2178: the template's own full stop after a title that already ends its sentence rendered "…!. Publicerad".
  it.each(["!", "?", ".", "…"])("puts no second stop after a title ending in %s, with and without a deadline", async (mark) => {
    const title = `Sjuksköterska sökes${mark}`;

    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, title } });
    expect((await generateMetadata(params())).description).toBe(
      `Jobbannons från Region Exempel: ${title} Publicerad 1 okt. 2026. Sista ansökningsdag 1 nov. 2026.`
    );

    getPublicJobAd.mockResolvedValue({ kind: "ok", jobAd: { ...AD, title, expiresAt: null } });
    expect((await generateMetadata(params())).description).toBe(
      `Jobbannons från Region Exempel: ${title} Publicerad 1 okt. 2026.`
    );
  });

  it("closes the title the same way in the English catalogue", async () => {
    const en = (await import("../../../messages/en")).default;
    const t = createTranslator({ locale: "en", messages: { publicjob: en.publicjob }, namespace: "publicjob" });
    const values = { company: "Region Example", published: "1 Oct 2026", deadline: "1 Nov 2026" };

    expect(t("meta.description", { ...values, title: "Nurse", titleEnd: "open" })).toBe(
      "Job ad from Region Example: Nurse. Published 1 Oct 2026."
    );
    expect(t("meta.descriptionWithDeadline", { ...values, title: "Nurse wanted!", titleEnd: "punctuated" })).toBe(
      "Job ad from Region Example: Nurse wanted! Published 1 Oct 2026. Application deadline 1 Nov 2026."
    );
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
  it("has no exclamation mark, no em dash and no emoji, in either catalogue", async () => {
    const en = (await import("../../../messages/en")).default;
    for (const catalogue of [sv, en]) {
      const text = JSON.stringify(catalogue.publicjob);
      expect(text).not.toContain("!");
      expect(text).not.toContain("—");
      expect(text).not.toMatch(/\p{Extended_Pictographic}/u);
    }
  });
});
