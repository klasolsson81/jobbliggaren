import type { ReactNode } from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import svMessages from "../../../messages/sv";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import { JobbQuickLinks } from "./jobb-quick-links";

// The quick links are next/link. jsdom never prefetches, so the prop itself is what is pinned: this stub renders an
// <a> and writes the prefetch value it was given onto it.
vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-prefetch={prefetch === undefined ? "default" : String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock("@/lib/actions/quick-links", () => ({
  setJuniorQuickLinkAction: vi.fn(),
  setRecentSearchQuickLinkAction: vi.fn(),
}));

function search(n: number, isQuickLink: boolean): RecentJobSearchDto {
  return {
    id: `3333333${n}-3333-3333-3333-333333333333`,
    q: `sökning ${n}`,
    occupationGroupList: [],
    occupationNameList: [],
    municipalityList: [],
    regionList: [],
    employmentTypeList: [],
    worktimeExtentList: [],
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    occupationNameLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: "PublishedAtDesc",
    label: queryLabel(`sökning ${n}`),
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-11-21T10:00:00Z",
    isQuickLink,
  };
}

describe("JobbQuickLinks prefetch (ADR 0188 A5)", () => {
  it("passes prefetch={false} to every quick link and to the list's Alla sökningar", async () => {
    const user = userEvent.setup();
    render(
      <NextIntlClientProvider locale="sv" messages={svMessages}>
        <JobbQuickLinks
          juniorCount={152}
          juniorEnabled
          searches={[search(0, true), search(1, true), ...[2, 3, 4, 5, 6, 7, 8].map((n) => search(n, false))]}
          headingId="jobb-hero-title"
        />
      </NextIntlClientProvider>,
    );

    const links = within(screen.getByRole("navigation", { name: "Snabblänkar" })).getAllByRole("link");
    expect(links).toHaveLength(3);
    for (const link of links) expect(link).toHaveAttribute("data-prefetch", "false");

    await user.click(screen.getByRole("button", { name: "Anpassa snabblänkar" }));

    expect(screen.getByRole("link", { name: "Alla sökningar" })).toHaveAttribute("data-prefetch", "false");
  });
});
