import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import MinaSidorPage from "./page";

const getServerSession = vi.fn();
const getMyProfile = vi.fn<() => Promise<ApiResult<JobSeekerProfileDto>>>();

// The async server page resolves its copy through next-intl/server, which jsdom cannot run: a real
// translator over the Swedish catalogue stands in for it.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({ locale: "sv", messages: { pages: svPages }, namespace }),
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
// This file pins the page's branching. The cards are pinned in their own tests, so they stand in
// here as markers that say which of them the page chose to render.
vi.mock("@/components/settings/settings-form", () => ({
  SettingsForm: () => <div data-testid="settings-form" />,
}));
vi.mock("@/components/settings/account-cards", () => ({
  AccountCards: ({ email }: { email: string }) => (
    <div data-testid="account-cards">{email}</div>
  ),
}));

const EMAIL = "klas@example.se";

const profile: JobSeekerProfileDto = {
  id: "profile-1",
  displayName: null,
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
  render(await MinaSidorPage());
}

describe("MinaSidorPage", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getMyProfile.mockReset();
    getServerSession.mockResolvedValue({ email: EMAIL, roles: [] });
  });

  it("renders the pagehero band with the page's title and lede", async () => {
    getMyProfile.mockResolvedValue({ kind: "ok", data: profile });
    await renderPage();

    const title = screen.getByRole("heading", { level: 1, name: "Mina sidor" });
    expect(title).toHaveClass("jp-pagehero__title");
    expect(title.closest("section")).toHaveClass("jp-pagehero");
    expect(
      screen.getByText("Här hanterar du ditt konto, dina notiser och vilka jobb du söker."),
    ).toBeInTheDocument();
  });

  it("hands a readable profile to the form, which renders the account cards itself", async () => {
    getMyProfile.mockResolvedValue({ kind: "ok", data: profile });
    await renderPage();

    expect(screen.getByTestId("settings-form")).toBeInTheDocument();
    // Rendered once, inside the form: a second set here would print every account card twice.
    expect(screen.queryByTestId("account-cards")).not.toBeInTheDocument();
  });

  // The account cards read only the session's address, so no profile result may take them away:
  // they are where the user changes the address, deletes the account and logs out. `error` is also
  // what getMyProfile makes of the backend's 404, since it reads without `includeNotFound`.
  it.each([
    [
      "rateLimited",
      { kind: "rateLimited", retryAfterSeconds: 30 } as const,
      /För många förfrågningar/,
    ],
    ["error", { kind: "error" } as const, /Profilen kunde inte hämtas just nu/],
  ])(
    "keeps the account cards when the profile result is %s",
    async (_kind, result, message) => {
      getMyProfile.mockResolvedValue(result);
      await renderPage();

      expect(screen.getByTestId("account-cards")).toHaveTextContent(EMAIL);
      expect(screen.queryByTestId("settings-form")).not.toBeInTheDocument();
      expect(screen.getByText(message)).toBeInTheDocument();
    },
  );
});
