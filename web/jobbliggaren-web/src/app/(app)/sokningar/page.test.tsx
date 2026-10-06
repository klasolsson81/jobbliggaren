import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import SokningarPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: async () => ({ email: "a@b.se", roles: [] }),
}));

const RECENT: RecentJobSearchDto = {
  id: "a1",
  q: "backend",
  occupationGroupList: [],
  municipalityList: [],
  regionList: [],
  employmentTypeList: [],
  worktimeExtentList: [],
  employerList: [],
  remote: false,
  occupationGroupLabels: [],
  municipalityLabels: [],
  regionLabels: [],
  sortBy: "PublishedAtDesc",
  label: queryLabel("backend"),
  currentCount: 0,
  newCount: 0,
  lastViewedAt: "2026-05-20T19:00:00Z",
};

vi.mock("@/lib/api/recent-searches", () => ({
  getRecentSearches: async () => ({ kind: "ok", data: [RECENT] }),
}));

vi.mock("@/lib/actions/recent-searches", () => ({
  deleteRecentSearchAction: async () => ({ success: true }),
}));

vi.mock("@/lib/hooks/use-recent-search-counts", () => ({
  useRecentSearchCounts: () => null,
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  useRouter: () => ({ push: vi.fn() }),
}));

// #2029: the list's last fallback is this page's h1, reached by the id the page hands it.
describe("/sokningar — the h1 a removal falls back to", () => {
  it("takes focus when the last search is removed", async () => {
    render(await SokningarPage());
    const h1 = screen.getByRole("heading", { level: 1 });

    screen.getByRole("button", { name: "Ta bort sökningen: backend" }).focus();
    await userEvent.setup().keyboard("{Enter}");

    expect(h1).toHaveFocus();
  });
});
