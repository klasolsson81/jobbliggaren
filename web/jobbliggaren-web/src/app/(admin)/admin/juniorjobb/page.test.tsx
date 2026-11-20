import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JuniorJobAdQueueDto } from "@/lib/dto/admin-junior-job-ads";
import type { JuniorJobsSearchParams } from "@/lib/admin/junior-jobs";
import AdminJuniorJobsPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

const api = vi.hoisted(() => ({
  listJuniorJobAds: vi.fn<(tab: string, page: number) => Promise<ApiResult<JuniorJobAdQueueDto>>>(),
}));
vi.mock("@/lib/api/admin-junior-job-ads", () => api);

const actions = vi.hoisted(() => ({ decideJuniorJobAdAction: vi.fn(async () => ({ success: true })) }));
vi.mock("@/lib/actions/admin-junior-job-ads", () => actions);

const QUEUE: JuniorJobAdQueueDto = {
  items: {
    items: [
      {
        id: "00000000-0000-4000-8000-000000000911",
        title: "Junior systemutvecklare .NET",
        companyName: "Arbetsgivare H",
        municipalityName: "Göteborg",
        publishedAt: "2026-11-19T08:15:00+00:00",
        expiresAt: null,
        verdict: "Junior",
        ruleId: "title-junior",
        decision: null,
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 25,
    totalPages: 1,
  },
  counts: { unclear: 7, shown: 1, decided: 0 },
};

async function renderPage(params: JuniorJobsSearchParams = {}) {
  render(await AdminJuniorJobsPage({ searchParams: Promise.resolve(params) }));
}

beforeEach(() => {
  vi.clearAllMocks();
  api.listJuniorJobAds.mockResolvedValue({ kind: "ok", data: QUEUE });
});

describe("/admin/juniorjobb (#2264)", () => {
  it("reads the tab and the page the URL asks for, and shows that tab's ads", async () => {
    await renderPage({ flik: "visas", sida: "1" });

    expect(api.listJuniorJobAds).toHaveBeenCalledWith("shown", 1);
    expect(screen.getByRole("link", { name: "Junior systemutvecklare .NET" })).toHaveAttribute(
      "href",
      "/jobb/00000000-0000-4000-8000-000000000911",
    );
    expect(within(screen.getByRole("navigation", { name: "Välj flik" })).getByRole("link", { current: true })).toHaveTextContent(
      "Visas på /junior (1)",
    );
  });

  it("reads an unknown tab and page as the first page of Att avgöra", async () => {
    await renderPage({ flik: "alla", sida: "noll" });

    expect(api.listJuniorJobAds).toHaveBeenCalledWith("unclear", 1);
  });

  it.each([
    [{ kind: "rateLimited", retryAfterSeconds: 4 } as const, "För många förfrågningar. Försök igen om 4 sekunder."],
    [{ kind: "forbidden" } as const, "Din session saknar Admin-rollen."],
    [{ kind: "unauthorized" } as const, "Du är inte inloggad längre. Logga in och försök igen."],
    [{ kind: "error" } as const, "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
  ])("says why a read failed: %j", async (result, line) => {
    api.listJuniorJobAds.mockResolvedValue(result);
    await renderPage();

    expect(screen.getByRole("alert")).toHaveTextContent(line);
  });
});
