import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createFormatter, createTranslator } from "next-intl";
import sv from "../../../messages/sv";
import type { PublicJobAdCatalogueResult } from "@/lib/api/public-job-ad-catalogue";

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

const getPublicJobAdCatalogue = vi.fn<(page: number) => Promise<PublicJobAdCatalogueResult>>();
vi.mock("@/lib/api/public-job-ad-catalogue", () => ({
  getPublicJobAdCatalogue: (page: number) => getPublicJobAdCatalogue(page),
}));

import PublicCataloguePage, { generateMetadata } from "./lediga-jobb/page";
import PublicCatalogueNotFound from "./lediga-jobb/not-found";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";
import { absoluteUrl } from "@/lib/site-url";

const LABEL = sv.landing.common.catalogueLink;
const SENTINEL_COMPANY = "Sentinel Namnsson Enskild Firma";

const row = (n: number, overrides: Partial<Record<string, string | null>> = {}) => ({
  id: `0b0b0b0b-1111-4222-8333-${String(n).padStart(12, "0")}`,
  title: `Sjuksköterska ${n}`,
  companyName: n === 1 ? SENTINEL_COMPANY : "Region Exempel",
  municipalityName: "Umeå",
  regionName: "Västerbottens län",
  publishedAt: "2026-10-01T08:00:00Z",
  ...overrides,
});

const pageOf = (items: ReturnType<typeof row>[], totalCount: number, page = 1) => ({
  kind: "ok" as const,
  page: { items, totalCount, page, pageSize: 20, totalPages: Math.ceil(totalCount / 20) },
});

const props = (sida?: string | string[]) => ({ searchParams: Promise.resolve(sida === undefined ? {} : { sida }) });

beforeEach(() => {
  getPublicJobAdCatalogue.mockReset();
});

describe("the public catalogue page", () => {
  it("names itself with the one label the header and footer use, and leads with source and order", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([row(1), row(2)], 41));
    render(await PublicCataloguePage(props()));

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(LABEL);
    expect(screen.getByText(sv.publicjob.catalogue.lede)).toBeInTheDocument();
  });

  it("renders a row per ad as an h2 with one plain anchor to the job page, the company and the place", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(
      pageOf([row(1), row(2, { municipalityName: null }), row(3, { municipalityName: null, regionName: null })], 41)
    );
    render(await PublicCataloguePage(props()));

    const rows = screen.getAllByRole("listitem").filter((li) => li.closest("ol[aria-labelledby]"));
    expect(rows).toHaveLength(3);
    for (const [i, li] of rows.entries()) {
      const heading = within(li).getByRole("heading", { level: 2 });
      const links = within(heading).getAllByRole("link");
      expect(links).toHaveLength(1);
      expect(links[0]).toHaveAttribute("href", `/jobb/${row(i + 1).id}`);
      expect(links[0]).not.toHaveAttribute("data-next-link");
      expect(links[0]!.textContent).toBe(row(i + 1).title);
      expect(within(li).getByText(row(i + 1).companyName)).toBeInTheDocument();
      expect(li.querySelector("time")).toHaveAttribute("datetime", row(i + 1).publishedAt);
    }
    expect(within(rows[0]!).getByText("Umeå")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("Västerbottens län")).toBeInTheDocument();
    expect(within(rows[2]!).queryByText(/län|Umeå/)).toBeNull();
  });

  it("keeps the count line, the list and the pager in one data-nosnippet block, and no number outside it", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([row(1)], 2112 * 20, 5));
    const { container } = render(await PublicCataloguePage(props("5")));

    const blocks = container.querySelectorAll("[data-nosnippet]");
    expect(blocks).toHaveLength(1);
    const block = blocks[0] as HTMLElement;
    expect(block.tagName).toBe("DIV");
    expect(block.querySelector(".jp-results-count")).not.toBeNull();
    expect(block.querySelector("ol")).not.toBeNull();
    expect(within(block).getByRole("navigation")).toBeInTheDocument();

    const main = container.querySelector("main")!.cloneNode(true) as HTMLElement;
    main.querySelector("[data-nosnippet]")!.remove();
    expect(main.textContent).not.toMatch(/\d/);
  });

  it("writes the count in the locale's grouping, with the singular for one", async () => {
    getPublicJobAdCatalogue.mockResolvedValueOnce(pageOf([row(1)], 42270));
    const { container, unmount } = render(await PublicCataloguePage(props()));
    expect(container.querySelector(".jp-results-count")?.textContent).toBe("42 270 lediga jobb");
    unmount();

    getPublicJobAdCatalogue.mockResolvedValueOnce(pageOf([row(1)], 1));
    const one = render(await PublicCataloguePage(props()));
    expect(one.container.querySelector(".jp-results-count")?.textContent).toBe("1 ledigt jobb");
  });

  it("links the pager without prefetch and states pages only, never a total of hits (ADR 0120)", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([row(1)], 100, 2));
    render(await PublicCataloguePage(props("2")));

    const pager = screen.getByRole("navigation");
    for (const link of within(pager).getAllByRole("link")) {
      expect(link).toHaveAttribute("data-prefetch", "false");
    }
    expect(within(pager).getByRole("link", { name: /Nästa/ })).toHaveAttribute("href", "/lediga-jobb?sida=3");
    expect(within(pager).getByRole("link", { name: /Föregående/ })).toHaveAttribute("href", "/lediga-jobb");
    expect(pager.textContent).not.toMatch(/träffar/);
  });

  it("moves focus to the page's main when the catalogue shows another page (design-reviewer Blocker 1)", async () => {
    getPublicJobAdCatalogue.mockResolvedValueOnce(pageOf([row(1)], 200, 5));
    const { rerender } = render(await PublicCataloguePage(props("5")));
    expect(document.activeElement).not.toBe(document.getElementById("main"));

    getPublicJobAdCatalogue.mockResolvedValueOnce(pageOf([row(2)], 200, 6));
    rerender(await PublicCataloguePage(props("6")));

    expect(document.activeElement).toBe(document.getElementById("main"));
  });

  it("renders the empty state on a count of 0: no count line, no list, no pager", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([], 0));
    const { container } = render(await PublicCataloguePage(props()));

    expect(screen.getByRole("heading", { level: 2, name: sv.publicjob.catalogue.empty.title })).toBeInTheDocument();
    expect(container.querySelector(".jp-results-count")).toBeNull();
    expect(container.querySelector("ol")).toBeNull();
    expect(screen.queryByRole("navigation")).toBeNull();
  });

  it("sends sida=1 to the bare path with a permanent redirect, before any read", async () => {
    await expect(PublicCataloguePage(props("1"))).rejects.toMatchObject({
      digest: expect.stringMatching(/^NEXT_REDIRECT;replace;\/lediga-jobb;308;/),
    });
    expect(getPublicJobAdCatalogue).not.toHaveBeenCalled();
  });

  it.each([["abc"], ["0"], ["02"], [["2", "3"]]])(
    "answers sida=%j with a real 404 and never asks the API",
    async (sida) => {
      await expect(PublicCataloguePage(props(sida))).rejects.toMatchObject({
        digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404"),
      });
      expect(getPublicJobAdCatalogue).not.toHaveBeenCalled();
    }
  );

  it("answers a page the API does not have with a real 404", async () => {
    getPublicJobAdCatalogue.mockResolvedValue({ kind: "notFound" });
    await expect(PublicCataloguePage(props("9999"))).rejects.toMatchObject({
      digest: expect.stringContaining("NEXT_HTTP_ERROR_FALLBACK;404"),
    });
  });

  it("throws on an unavailable read, which is a 5xx and never a 404", async () => {
    getPublicJobAdCatalogue.mockResolvedValue({ kind: "unavailable" });
    const failure = await PublicCataloguePage(props()).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(Error);
    expect((failure as { digest?: string }).digest ?? "").not.toContain("NEXT_HTTP_ERROR_FALLBACK");
  });
});

describe("the public catalogue's metadata", () => {
  it("titles page 1 with the label alone and later pages with their number, each with its own canonical", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([row(1)], 100));
    const first = await generateMetadata(props());
    expect(first.title).toBe(LABEL);
    expect(first.alternates?.canonical).toBe(absoluteUrl("/lediga-jobb"));
    expect(first.robots).toEqual({ index: true, follow: true });

    const third = await generateMetadata(props("3"));
    expect(third.title).toBe(`${LABEL} – sida 3`);
    expect(third.alternates?.canonical).toBe(absoluteUrl("/lediga-jobb?sida=3"));
  });

  it("puts no row value and no number in the title, the description or the preview", async () => {
    getPublicJobAdCatalogue.mockResolvedValue(pageOf([row(1)], 42270));
    const metadata = JSON.stringify(await generateMetadata(props()));

    expect(metadata).not.toContain(SENTINEL_COMPANY);
    expect(metadata).not.toContain("Sjuksköterska");
    expect(metadata).not.toMatch(/42[\s ]?270/);
    const { title, description, openGraph } = await generateMetadata(props());
    expect(`${title} ${description} ${JSON.stringify(openGraph?.title)}`).not.toMatch(/\d/);
  });

  it("titles an unavailable read as the error page it renders, never as a page that does not exist", async () => {
    getPublicJobAdCatalogue.mockResolvedValue({ kind: "unavailable" });
    const metadata = await generateMetadata(props());

    expect(metadata.title).toBe(sv.fallback.errorTitle);
    expect(metadata).not.toEqual(await notFoundMetadata());
  });

  it("is the not-found metadata for a page that is not one, and for a page the API does not have", async () => {
    expect(await generateMetadata(props("abc"))).toEqual(await notFoundMetadata());
    getPublicJobAdCatalogue.mockResolvedValue({ kind: "notFound" });
    expect(await generateMetadata(props("9999"))).toEqual(await notFoundMetadata());
  });
});

describe("the catalogue's 404", () => {
  it("leads back to the catalogue's first page and reads nothing", async () => {
    render(await PublicCatalogueNotFound());

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(sv.publicjob.catalogue.notFound.title);
    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAccessibleName(sv.publicjob.catalogue.notFound.toCatalogue);
    expect(links[0]).toHaveAttribute("href", "/lediga-jobb");
    expect(links[0]).not.toHaveAttribute("data-next-link");
    expect(getPublicJobAdCatalogue).not.toHaveBeenCalled();
  });
});
