import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import svSettings from "../../../../../messages/sv/settings.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import MinaSidorNotiserPage from "./page";

const getServerSession = vi.fn();
const getMyProfile = vi.fn<() => Promise<ApiResult<JobSeekerProfileDto>>>();

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
vi.mock("@/lib/auth/actions", () => ({ logoutAction: vi.fn() }));
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationConsentAction: vi.fn(),
  updateFollowedCompanyNotificationConsentAction: vi.fn(),
  updateDigestCadenceAction: vi.fn(),
}));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

function profile(matchEnabled: boolean, followEnabled: boolean): JobSeekerProfileDto {
  return {
    id: "profile-1",
    language: "sv",
    backgroundMatchNotificationsEnabled: matchEnabled,
    digestCadence: "Weekly",
    followedCompanyNotificationsEnabled: followEnabled,
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
}

async function renderPage() {
  render(await MinaSidorNotiserPage());
}

function expectNotiserCurrent() {
  const nav = screen.getByRole("navigation", { name: "Mina sidor" });
  expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent("Notiser");
}

// The notification mails' withdrawal link lands on this page (ADR 0144 row 14).
describe("MinaSidorNotiserPage (/mina-sidor/notiser)", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getMyProfile.mockReset();
    getServerSession.mockResolvedValue({ email: "klas@example.se", roles: [] });
  });

  it.each([
    [true, false],
    [false, true],
  ])(
    "shows the saved match consent (%s) and followed-company consent (%s) on their own switches",
    async (matchEnabled, followEnabled) => {
      getMyProfile.mockResolvedValue({ kind: "ok", data: profile(matchEnabled, followEnabled) });
      await renderPage();

      expectNotiserCurrent();
      expect(screen.getByRole("switch", { name: "Matcha nya annonser åt mig" })).toHaveAttribute(
        "aria-checked",
        String(matchEnabled),
      );
      expect(
        screen.getByRole("switch", { name: "Mejla mig nya annonser från företag jag följer" }),
      ).toHaveAttribute("aria-checked", String(followEnabled));
    },
  );

  it.each([
    [
      "rateLimited",
      { kind: "rateLimited", retryAfterSeconds: 30 } as const,
      /för många förfrågningar på kort tid/,
    ],
    ["error", { kind: "error" } as const, /kunde inte läsas in just nu/],
  ])("says so in the Notiser card when the profile result is %s", async (_kind, result, message) => {
    getMyProfile.mockResolvedValue(result);
    await renderPage();

    expect(screen.getByRole("heading", { level: 2, name: "Notiser" })).toBeInTheDocument();
    expect(screen.getByText(message)).toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expectNotiserCurrent();
    expect(screen.getByRole("button", { name: "Logga ut" })).toBeInTheDocument();
  });

  it("sends a session the backend no longer knows to the login page", async () => {
    getMyProfile.mockResolvedValue({ kind: "unauthorized" });
    await expect(MinaSidorNotiserPage()).rejects.toThrow("NEXT_REDIRECT:/logga-in");
  });
});
