import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import svSettings from "../../../../../messages/sv/settings.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import MinaSidorMatchningPage from "./page";
import Loading from "./loading";

const getServerSession = vi.fn();
const getMyProfile = vi.fn<() => Promise<ApiResult<JobSeekerProfileDto>>>();

// The async server page resolves its copy through next-intl/server, which jsdom cannot run: a real
// translator over the Swedish catalogue stands in for it.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages" | "settings") =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages, settings: svSettings },
      namespace,
    }),
}));
vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/api/taxonomy", () => ({
  getTaxonomyTree: async () => ({ kind: "error" }),
}));
vi.mock("@/lib/api/skills", () => ({
  resolveSkillLabels: async () => ({ kind: "ok", data: [] }),
}));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));
// This file pins the page's branching. The card is pinned in its own tests, so it stands in here as
// a marker that says the page chose to render it, and with what.
vi.mock("@/components/settings/match-preferences-card", () => ({
  MatchPreferencesCard: ({ degraded }: { degraded: boolean }) => (
    <div data-testid="match-preferences-card" data-degraded={String(degraded)} />
  ),
}));

const profile: JobSeekerProfileDto = {
  id: "profile-1",
  language: "sv",
  backgroundMatchNotificationsEnabled: false,
  digestCadence: "Weekly",
  followedCompanyNotificationsEnabled: false,
  createdAt: "2026-05-01T08:00:00Z",
  hasStatedDesiredOccupation: false,
  preferredOccupationGroups: [],
  preferredRegions: [],
  preferredMunicipalities: [],
  preferredRemote: false,
  preferredEmploymentTypes: [],
  preferredSkills: [],
  experienceYears: null,
  preferredOccupationExperience: [],
};

async function renderPage() {
  render(await MinaSidorMatchningPage());
}

/** Whether `a` comes before `b` in document order. */
function precedes(a: Node, b: Node): boolean {
  return (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
}

/**
 * Old notification mails link to bare /mina-sidor, which opens this section: the way to Notiser, where
 * their consent is withdrawn, is the menu (security-auditor, #1891 row 14 (e)).
 */
function expectNotiserInTheMenuBefore(section: Element) {
  const nav = screen.getByRole("navigation", { name: "Mina sidor" });
  expect(within(nav).getByRole("link", { name: "Notiser" })).toHaveAttribute(
    "href",
    "/mina-sidor/notiser",
  );
  expect(precedes(nav, section)).toBe(true);
}

describe("MinaSidorMatchningPage (/mina-sidor)", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getMyProfile.mockReset();
    getServerSession.mockResolvedValue({ email: "klas@example.se", roles: [] });
  });

  it("renders the pagehero band with its title and one static line (#1917)", async () => {
    getMyProfile.mockResolvedValue({ kind: "ok", data: profile });
    await renderPage();

    const title = screen.getByRole("heading", { level: 1, name: "Mina sidor" });
    expect(title).toHaveClass("jp-pagehero__title");
    const band = title.closest("section");
    expect(band).toHaveClass("jp-pagehero");
    expect(band?.querySelectorAll(".jp-pagehero__lede")).toHaveLength(1);
    expect(band?.querySelector(".jp-pagehero__lede")).toHaveTextContent(svPages.minaSidor.lede);
  });

  it("is the Matchning section: the menu marks it current, and the card renders", async () => {
    getMyProfile.mockResolvedValue({ kind: "ok", data: profile });
    await renderPage();

    const nav = screen.getByRole("navigation", { name: "Mina sidor" });
    expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent("Matchning");
    // A failed taxonomy read degrades the card; it does not fail the page.
    expect(screen.getByTestId("match-preferences-card")).toHaveAttribute("data-degraded", "true");
    expectNotiserInTheMenuBefore(screen.getByTestId("match-preferences-card"));
  });

  // `error` is also what getMyProfile makes of the backend's 404, since it reads without
  // `includeNotFound`. The menu and Logga ut read nothing and stay.
  it.each([
    [
      "rateLimited",
      { kind: "rateLimited", retryAfterSeconds: 30 } as const,
      /för många förfrågningar på kort tid/,
    ],
    ["error", { kind: "error" } as const, /kunde inte läsas in just nu/],
  ])("says so in the Matchning card when the profile result is %s", async (_kind, result, message) => {
    getMyProfile.mockResolvedValue(result);
    await renderPage();

    expect(screen.getByRole("heading", { level: 2, name: "Matchning" })).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
    expectNotiserInTheMenuBefore(screen.getByRole("heading", { level: 2, name: "Matchning" }));
    expect(screen.queryByTestId("match-preferences-card")).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Mina sidor" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Logga ut" })).toBeInTheDocument();
  });

  it("keeps the band and the menu while the section loads", () => {
    render(<Loading />);

    expect(screen.getByRole("heading", { level: 1, name: "Mina sidor" })).toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Mina sidor" });
    expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent("Matchning");
    expect(screen.getByRole("status")).toBeInTheDocument();
    const section = document.querySelector(".jp-settings-section");
    expect(section).not.toBeNull();
    expectNotiserInTheMenuBefore(section!);
  });

  it("sends a session the backend no longer knows to the login page", async () => {
    getMyProfile.mockResolvedValue({ kind: "unauthorized" });
    await expect(MinaSidorMatchningPage()).rejects.toThrow("NEXT_REDIRECT:/logga-in");
  });
});
