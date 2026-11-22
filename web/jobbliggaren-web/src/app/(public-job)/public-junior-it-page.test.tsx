import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createFormatter, createTranslator } from "next-intl";
import sv from "../../../messages/sv";
import type { PublicJuniorItJobAdsResult } from "@/lib/api/public-junior-it-job-ads";
import { publicJuniorUrl, type JuniorItOrder } from "@/lib/job-ads/junior-it-params";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({ locale: "sv", messages: sv as never, namespace: namespace as never }),
  getFormatter: async () => createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
  getLocale: async () => "sv",
}));

// The pager is next/link; the stub marks every anchor it renders, so a row anchor without the mark is a plain <a>.
vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-next-link="" data-prefetch={String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

const getPublicJuniorItJobAds =
  vi.fn<(municipality: string | null, sortBy: JuniorItOrder, page: number) => Promise<PublicJuniorItJobAdsResult>>();
vi.mock("@/lib/api/public-junior-it-job-ads", () => ({
  getPublicJuniorItJobAds: (municipality: string | null, sortBy: JuniorItOrder, page: number) =>
    getPublicJuniorItJobAds(municipality, sortBy, page),
}));

import PublicJuniorItPage, { generateMetadata } from "./junior/page";
import PublicJuniorItNotFound from "./junior/not-found";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

const SENTINEL_COMPANY = "Sentinel Namnsson Enskild Firma";
const SENTINEL_TITLE = "Sentinel juniorutvecklare";
const STHLM = "AvNB_uwa_6n6";
const GBG = "PVZL_BQT_XtL";

const row = (n: number, overrides: Partial<Record<string, string | null>> = {}) => ({
  id: `0c0c0c0c-1111-4222-8333-${String(n).padStart(12, "0")}`,
  title: n === 1 ? SENTINEL_TITLE : `Junior utvecklare ${n}`,
  companyName: n === 1 ? SENTINEL_COMPANY : "Exempel AB",
  municipalityName: "Göteborg",
  regionName: "Västra Götalands län",
  publishedAt: "2026-11-10T08:00:00Z",
  expiresAt: "2026-12-10T22:59:59Z",
  ...overrides,
});

const places = [
  { municipalityConceptId: GBG, name: "Göteborg", count: 20 },
  { municipalityConceptId: STHLM, name: "Stockholm", count: 39 },
];

// A floor that holds needs a whole list of at least 100 ads, so the answers that carry it carry these places.
const placesAtFloor = [
  { municipalityConceptId: GBG, name: "Göteborg", count: 41 },
  { municipalityConceptId: STHLM, name: "Stockholm", count: 60 },
];

const pageOf = (
  items: ReturnType<typeof row>[],
  totalCount: number,
  page = 1,
  placeList = places,
  floorHolds = false
) => ({
  kind: "ok" as const,
  page: {
    rows: { items, totalCount, page, pageSize: 20, totalPages: Math.ceil(totalCount / 20) },
    places: placeList,
    floorHolds,
  },
});

const props = (params: Record<string, string | string[]> = {}) => ({ searchParams: Promise.resolve(params) });

beforeEach(() => {
  getPublicJuniorItJobAds.mockReset();
});

describe("the junior IT list page", () => {
  it("names itself and asks the API for every kommun, newest first, page 1, by default", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1), row(2)], 59));
    render(await PublicJuniorItPage(props()));

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(sv.publicjob.junior.title);
    expect(getPublicJuniorItJobAds).toHaveBeenCalledWith(null, "PublishedAtDesc", 1);
  });

  it("renders each row as an h2 with one plain anchor to the job page, the company, the place, and both dates", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1), row(2, { expiresAt: null })], 2));
    render(await PublicJuniorItPage(props()));

    const rows = screen.getAllByRole("listitem").filter((li) => li.closest("ol[aria-labelledby]"));
    expect(rows).toHaveLength(2);
    const link = within(within(rows[0]!).getByRole("heading", { level: 2 })).getByRole("link");
    expect(link).toHaveAttribute("href", `/jobb/${row(1).id}`);
    expect(link).not.toHaveAttribute("data-next-link");
    expect(within(rows[0]!).getByText(SENTINEL_COMPANY)).toBeInTheDocument();
    expect(rows[0]!.querySelectorAll("time")).toHaveLength(2);
    expect(rows[0]!.textContent).toContain(sv.publicjob.junior.lastApplication);
    expect(rows[1]!.querySelectorAll("time")).toHaveLength(1);
  });

  it("offers every kommun in the API's order with its count, and the two orders /jobb offers, in a plain GET form", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 59));
    const { container } = render(await PublicJuniorItPage(props()));

    const form = container.querySelector("form")!;
    expect(form.getAttribute("method")).toBe("get");
    expect(form.getAttribute("action")).toBe("/junior");
    const place = screen.getByLabelText("Ort") as HTMLSelectElement;
    expect(place.name).toBe("municipality");
    expect([...place.options].map((o) => [o.value, o.textContent])).toEqual([
      ["", "Alla orter"],
      [GBG, "Göteborg (20)"],
      [STHLM, "Stockholm (39)"],
    ]);
    const sort = screen.getByLabelText(sv.jobads.ui.toolbar.sortLabel) as HTMLSelectElement;
    expect(sort.name).toBe("sortBy");
    expect([...sort.options].map((o) => o.value)).toEqual(["PublishedAtDesc", "ExpiresAtAsc"]);
  });

  it("passes the chosen kommun and order to the API and names the kommun in the count line", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 20));
    const { container } = render(await PublicJuniorItPage(props({ municipality: GBG, sortBy: "ExpiresAtAsc" })));

    expect(getPublicJuniorItJobAds).toHaveBeenCalledWith(GBG, "ExpiresAtAsc", 1);
    expect(container.querySelector(".jp-results-count")?.textContent).toBe("20 lediga jobb i Göteborg");
    expect((screen.getByLabelText("Ort") as HTMLSelectElement).value).toBe(GBG);
    expect((screen.getByLabelText(sv.jobads.ui.toolbar.sortLabel) as HTMLSelectElement).value).toBe("ExpiresAtAsc");
  });

  it("keeps the form, the count line, the list and the pager in one data-nosnippet block", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 59, 2));
    const { container } = render(await PublicJuniorItPage(props({ page: "2" })));

    const blocks = container.querySelectorAll("[data-nosnippet]");
    expect(blocks).toHaveLength(1);
    expect(blocks[0]!.querySelector("form")).not.toBeNull();
    expect(blocks[0]!.querySelector(".jp-results-count")).not.toBeNull();
    expect(blocks[0]!.querySelector("ol")).not.toBeNull();
    expect(within(blocks[0] as HTMLElement).getByRole("navigation")).toBeInTheDocument();
  });

  it("links the pager without prefetch and keeps the kommun and order on every page link", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 59, 2));
    render(await PublicJuniorItPage(props({ municipality: STHLM, sortBy: "ExpiresAtAsc", page: "2" })));

    const pager = screen.getByRole("navigation");
    for (const link of within(pager).getAllByRole("link")) {
      expect(link).toHaveAttribute("data-prefetch", "false");
    }
    expect(within(pager).getByRole("link", { name: /Nästa/ })).toHaveAttribute(
      "href",
      `/junior?municipality=${STHLM}&sortBy=ExpiresAtAsc&page=3`
    );
    expect(within(pager).getByRole("link", { name: /Föregående/ })).toHaveAttribute(
      "href",
      `/junior?municipality=${STHLM}&sortBy=ExpiresAtAsc`
    );
  });

  it("names no total in the all-places option, which the ads without a named kommun would contradict", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 64));
    const { container } = render(await PublicJuniorItPage(props()));

    expect((screen.getByLabelText("Ort") as HTMLSelectElement).options[0]!.textContent).toBe("Alla orter");
    expect(container.querySelector(".jp-results-count")?.textContent).toBe("64 lediga jobb");
  });

  it("keeps the form, the order included, when no listed ad has a named kommun", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1, { municipalityName: null })], 1, 1, []));
    render(await PublicJuniorItPage(props()));

    expect([...(screen.getByLabelText("Ort") as HTMLSelectElement).options].map((o) => o.value)).toEqual([""]);
    expect(screen.getByLabelText(sv.jobads.ui.toolbar.sortLabel)).toBeInTheDocument();
  });

  it("names the län where an ad names no kommun, and no place where it names neither", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(
      pageOf([row(1, { municipalityName: null }), row(2, { municipalityName: null, regionName: null })], 2)
    );
    render(await PublicJuniorItPage(props()));

    const rows = screen.getAllByRole("listitem").filter((li) => li.closest("ol[aria-labelledby]"));
    expect(within(rows[0]!).getByText("Västra Götalands län")).toBeInTheDocument();
    expect(within(rows[1]!).queryByText(/län|Göteborg/)).toBeNull();
  });

  it("renders the empty state when the whole list is empty", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([], 0, 1, []));
    const { container } = render(await PublicJuniorItPage(props()));

    expect(screen.getByRole("heading", { level: 2, name: sv.publicjob.junior.empty.title })).toBeInTheDocument();
    expect(container.querySelector(".jp-results-count")).toBeNull();
    expect(container.querySelector("ol")).toBeNull();
    expect(container.querySelector("form")).toBeNull();
  });

  it("offers every kommun again when a kept link names a kommun with no junior job left", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([], 0));
    render(await PublicJuniorItPage(props({ municipality: "zzzz_zzz_zzz", sortBy: "ExpiresAtAsc" })));

    expect(screen.getByRole("heading", { level: 2, name: sv.publicjob.junior.emptyPlace.title })).toBeInTheDocument();
    expect(document.querySelector("form")).toBeNull();
    expect(screen.getByRole("link", { name: sv.publicjob.junior.emptyPlace.allPlaces })).toHaveAttribute(
      "href",
      "/junior?sortBy=ExpiresAtAsc"
    );
  });

  it.each([[{ page: "0" }], [{ sortBy: "Relevance" }], [{ municipality: "Göteborg" }], [{ page: ["1", "2"] }]])(
    "answers %j with a real 404 and never asks the API",
    async (params) => {
      await expect(PublicJuniorItPage(props(params))).rejects.toMatchObject({
        digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404"),
      });
      expect(getPublicJuniorItJobAds).not.toHaveBeenCalled();
    }
  );

  it("answers a page the API does not have with a real 404", async () => {
    getPublicJuniorItJobAds.mockResolvedValue({ kind: "notFound" });
    await expect(PublicJuniorItPage(props({ page: "99" }))).rejects.toMatchObject({
      digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404"),
    });
  });

  it("throws on an unavailable read, which is a 5xx and never a 404", async () => {
    getPublicJuniorItJobAds.mockResolvedValue({ kind: "unavailable" });
    const failure = await PublicJuniorItPage(props()).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(Error);
    expect((failure as { digest?: string }).digest ?? "").not.toContain("NEXT_HTTP_ERROR_FALLBACK");
  });

  it("renders its 404 with a way back to the list's first page and no read", async () => {
    render(await PublicJuniorItNotFound());

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(sv.publicjob.junior.notFound.title);
    expect(screen.getByRole("link", { name: sv.publicjob.junior.notFound.toList })).toHaveAttribute("href", "/junior");
    expect(getPublicJuniorItJobAds).not.toHaveBeenCalled();
  });
});

describe("the junior IT list's metadata", () => {
  it("is noindex, follow on every answer while the floor fails, with no canonical", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 59));
    const views: Record<string, string>[] = [{}, { municipality: GBG }, { sortBy: "ExpiresAtAsc", page: "2" }];
    for (const params of views) {
      const metadata = await generateMetadata(props(params));
      expect(metadata.robots).toEqual({ index: false, follow: true });
      expect(metadata.alternates).toBeUndefined();
      expect(metadata.openGraph).not.toHaveProperty("url");
    }
  });

  it("is index, follow with its own canonical and preview address on the whole list's first page while the floor holds", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 101, 1, placesAtFloor, true));
    const spellings: Record<string, string>[] = [
      {},
      { page: "1" },
      { sortBy: "PublishedAtDesc", municipality: "" },
      { utm_source: "linkedin", vidare: "1" },
    ];
    for (const params of spellings) {
      const metadata = await generateMetadata(props(params));
      expect(metadata.robots).toEqual({ index: true, follow: true });
      expect(metadata.alternates).toEqual({ canonical: publicJuniorUrl() });
      expect(metadata.openGraph).toHaveProperty("url", publicJuniorUrl());
    }
  });

  it("keeps a kommun, the deadline order and a later page noindex, follow with no canonical while the floor holds", async () => {
    const views: [Record<string, string>, number][] = [
      [{ municipality: GBG }, 41],
      [{ sortBy: "ExpiresAtAsc" }, 101],
      [{ page: "2" }, 101],
    ];
    for (const [params, total] of views) {
      getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], total, Number(params.page ?? "1"), placesAtFloor, true));
      const metadata = await generateMetadata(props(params));
      expect(metadata.robots).toEqual({ index: false, follow: true });
      expect(metadata.alternates).toBeUndefined();
      expect(metadata.openGraph).not.toHaveProperty("url");
    }
  });

  it("puts no row value and no number in the title, the description or the preview", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 59));
    const metadata = JSON.stringify(await generateMetadata(props({ municipality: GBG })));
    expect(metadata).not.toContain(SENTINEL_COMPANY);
    expect(metadata).not.toContain(SENTINEL_TITLE);
    expect(metadata).not.toContain("Göteborg");
    expect(metadata.replace(/"url":"[^"]*"|"width":\d+|"height":\d+/g, "")).not.toMatch(/\d/);
  });

  it("titles the page and its preview with the search title, not the heading", async () => {
    getPublicJuniorItJobAds.mockResolvedValue(pageOf([row(1)], 101, 1, placesAtFloor, true));
    const metadata = await generateMetadata(props({}));
    expect(metadata.title).toBe(sv.publicjob.junior.metaTitle);
    expect(metadata.description).toBe(sv.publicjob.junior.metaDescription);
    expect(metadata.openGraph).toMatchObject({
      title: sv.publicjob.junior.metaTitle,
      description: sv.publicjob.junior.metaDescription,
    });
    expect(sv.publicjob.junior.metaTitle).not.toBe(sv.publicjob.junior.title);
  });

  it("titles a refused parameter and a missing page as not found", async () => {
    expect(await generateMetadata(props({ page: "0" }))).toEqual(await notFoundMetadata());
    getPublicJuniorItJobAds.mockResolvedValue({ kind: "notFound" });
    expect(await generateMetadata(props({ page: "99" }))).toEqual(await notFoundMetadata());
  });
});
