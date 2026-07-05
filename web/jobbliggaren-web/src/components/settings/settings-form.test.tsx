import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SettingsForm } from "./settings-form";
import type { JobSeekerProfileDto } from "@/lib/types/me";

vi.mock("@/lib/actions/me", () => ({
  updateMyProfileAction: vi.fn().mockResolvedValue({ success: true }),
  // ADR 0080 Vag 4 PR-6: BackgroundMatchCard:s egen action.
  updateNotificationConsentAction: vi.fn().mockResolvedValue({ success: true }),
}));

// The language Segment switches the UI locale via the cookie server action +
// router.refresh(); mock both for the render smoke tests.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), back: vi.fn() }),
}));

vi.mock("@/i18n/set-locale-action", () => ({
  setLocaleAction: vi.fn().mockResolvedValue(undefined),
}));

vi.mock("@/lib/auth/actions", () => ({
  logoutAction: vi.fn(),
  deleteAccountAction: vi.fn(),
}));

// (MVP: theme-provider/useTheme-mock borttagen — settings-form importerar inte
//  längre useTheme; tema-segmentet är "släckt".)

vi.mock("@/components/me/delete-account-section", () => ({
  DeleteAccountSection: () => <div data-testid="delete-account-stub" />,
}));

const baseProfile: JobSeekerProfileDto = {
  id: "profile-1",
  displayName: "Klas Olsson",
  language: "sv",
  backgroundMatchNotificationsEnabled: false,
  digestCadence: "Weekly",
  createdAt: "2026-05-01T08:00:00Z",
  hasStatedDesiredOccupation: false,
  preferredOccupationGroups: [],
  preferredRegions: [],
  preferredMunicipalities: [],
  preferredEmploymentTypes: [],
  preferredSkills: [],
  experienceYears: null,
  preferredOccupationExperience: [],
};

describe("SettingsForm — F6 Prompt 2 smoke", () => {
  it("renderar alla kort i rätt ordning (Matchning efter Personuppgifter)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    const headings = screen
      .getAllByRole("heading", { level: 2 })
      .map((h) => h.textContent);
    // F4-12 PR-B (ADR 0076): Matchning-kortet ligger i första kolumnen efter
    // Personuppgifter. `taxonomy={null}` → kortet degraderar men behåller sin
    // h2-rubrik.
    // TD-115 (2026-06-25): det gamla "Aviseringar"-kortet (EmailNotifications +
    // WeeklySummary) togs bort — Matchningsnotiser är nu den enda notis-ytan.
    // #678: the change-password card sits in the second column, before Sekretess
    // och data (privacy/danger zone) and Logga ut.
    expect(headings).toEqual([
      "Personuppgifter",
      "Matchning",
      "Visning",
      "Matchningsnotiser",
      "Byt lösenord",
      "Sekretess och data",
      "Logga ut",
    ]);
  });

  it("Personuppgifter-kortet visar Namn (write) + E-post (read-only)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    const name = screen.getByLabelText("Namn") as HTMLInputElement;
    expect(name.value).toBe("Klas Olsson");
    expect(name.readOnly).toBe(false);
    const email = screen.getByLabelText("E-postadress") as HTMLInputElement;
    expect(email.value).toBe("klas@example.se");
    expect(email.readOnly).toBe(true);
  });

  it("INNEHÅLLER INGET Telefon-fält (CTO Val 4B, no-mock-doktrin)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    expect(screen.queryByLabelText(/Telefon/i)).not.toBeInTheDocument();
  });

  it("Visning-kortet har Språk-segment (English aktiverat); Tema-segment borttaget (MVP: ett färgläge)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    // MVP (Klas 2026-06-24): dark-mode "släckt" → Tema-segmentet är borttaget.
    expect(
      screen.queryByRole("radiogroup", { name: "Tema" }),
    ).not.toBeInTheDocument();
    const langGroup = screen.getByRole("radiogroup", { name: "Språk" });
    expect(langGroup).toBeInTheDocument();
    // English är nu live (next-intl wirad, ADR 0078) — inte längre disabled.
    const english = screen.getByRole("radio", { name: "English" });
    expect(english).toBeEnabled();
  });

  it("Matchningsnotiser är den enda notis-toggeln (TD-115: Aviseringar-kortet borttaget)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    // TD-115: det gamla Aviseringar-kortets två toggles (EmailNotifications +
    // WeeklySummary) togs bort — de styrde ingen e-postväg. Matchningsnotiser-
    // kortets opt-in-toggle (default OFF) är nu den ENDA switchen på sidan.
    expect(screen.getAllByRole("switch")).toHaveLength(1);
    expect(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" }),
    ).toHaveAttribute("aria-checked", "false");
  });

  it("Sekretess och data-kortet använder DeleteAccountSection-stub", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    expect(screen.getByTestId("delete-account-stub")).toBeInTheDocument();
  });

  it("Logga ut-kortet renderar submit-knapp", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Logga ut/ }),
    ).toBeInTheDocument();
  });
});
