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
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/actions/me", () => ({
  updateNotificationMailAction: vi.fn(),
  updateNotificationConsentAction: vi.fn(),
  updateTopMatchMailAction: vi.fn(),
  updateMatchSummaryMailAction: vi.fn(),
  updateFollowedCompanyNotificationConsentAction: vi.fn(),
  updateDigestCadenceAction: vi.fn(),
}));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

const MAIL = "E-postnotiser";
const MATCH = "Matcha nya annonser åt mig";
const TOP = "Toppmatchningar via e-post";
const SUMMARY = "Sammanfattning via e-post";
const FOLLOW = "Nya annonser via e-post";

interface Saved {
  mail?: boolean;
  match?: boolean;
  top?: boolean;
  summary?: boolean;
  follow?: boolean;
}

function profile({
  mail = true,
  match = false,
  top = true,
  summary = true,
  follow = false,
}: Saved = {}): JobSeekerProfileDto {
  return {
    id: "profile-1",
    language: "sv",
    backgroundMatchNotificationsEnabled: match,
    digestCadence: "Weekly",
    followedCompanyNotificationsEnabled: follow,
    notificationMailEnabled: mail,
    topMatchMailEnabled: top,
    matchSummaryMailEnabled: summary,
    notificationFeedMatchesEnabled: true,
    notificationFeedFollowedAdsEnabled: true,
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
    async (match, follow) => {
      getMyProfile.mockResolvedValue({ kind: "ok", data: profile({ match, follow }) });
      await renderPage();

      expectNotiserCurrent();
      expect(screen.getByRole("switch", { name: MATCH })).toHaveAttribute(
        "aria-checked",
        String(match),
      );
      expect(screen.getByRole("switch", { name: FOLLOW })).toHaveAttribute(
        "aria-checked",
        String(follow),
      );
    },
  );

  // Each switch shows its own saved value: a value read into the wrong switch would show a mail
  // as off while it is sent, or on while it is not.
  it.each([
    [{ mail: true, match: true, top: true, summary: false }],
    [{ mail: true, match: true, top: false, summary: true }],
  ])("shows the saved e-mail, Toppmatch and summary values on their own switches (%o)", async (saved) => {
    getMyProfile.mockResolvedValue({ kind: "ok", data: profile(saved) });
    await renderPage();

    expect(screen.getByRole("switch", { name: MAIL })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("switch", { name: TOP })).toHaveAttribute(
      "aria-checked",
      String(saved.top),
    );
    expect(screen.getByRole("switch", { name: SUMMARY })).toHaveAttribute(
      "aria-checked",
      String(saved.summary),
    );
  });

  it("shows the saved pause: the e-mail switch off, the mail switches hidden, the consents on show", async () => {
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: profile({ mail: false, match: true, follow: true }),
    });
    await renderPage();

    expect(screen.getByRole("switch", { name: MAIL })).toHaveAttribute("aria-checked", "false");
    expect(screen.queryByRole("switch", { name: TOP })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch", { name: SUMMARY })).not.toBeInTheDocument();
    expect(screen.getByRole("switch", { name: MATCH })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("switch", { name: FOLLOW })).toHaveAttribute("aria-checked", "true");
  });

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
