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

vi.mock("@/components/theme-provider", () => ({
  useTheme: () => ({
    theme: "light" as const,
    setTheme: vi.fn(),
  }),
}));

vi.mock("@/components/me/delete-account-section", () => ({
  DeleteAccountSection: () => <div data-testid="delete-account-stub" />,
}));

const baseProfile: JobSeekerProfileDto = {
  id: "profile-1",
  displayName: "Klas Olsson",
  language: "sv",
  emailNotifications: true,
  weeklySummary: false,
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
        initialSkillLabels={[]}
      />,
    );
    const headings = screen
      .getAllByRole("heading", { level: 2 })
      .map((h) => h.textContent);
    // F4-12 PR-B (ADR 0076): Matchning-kortet ligger i första kolumnen efter
    // Personuppgifter. `taxonomy={null}` → kortet degraderar men behåller sin
    // h2-rubrik.
    // ADR 0080 Vag 4 PR-6: Matchningsnotiser-kortet ligger efter Aviseringar i
    // andra kolumnen (båda notis-relaterade).
    expect(headings).toEqual([
      "Personuppgifter",
      "Matchning",
      "Visning",
      "Aviseringar",
      "Matchningsnotiser",
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
        initialSkillLabels={[]}
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
        initialSkillLabels={[]}
      />,
    );
    expect(screen.queryByLabelText(/Telefon/i)).not.toBeInTheDocument();
  });

  it("Visning-kortet har Tema-segment + Språk-segment med English aktiverat", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillLabels={[]}
      />,
    );
    const themeGroup = screen.getByRole("radiogroup", { name: "Tema" });
    expect(themeGroup).toBeInTheDocument();
    const langGroup = screen.getByRole("radiogroup", { name: "Språk" });
    expect(langGroup).toBeInTheDocument();
    // English är nu live (next-intl wirad, ADR 0078) — inte längre disabled.
    const english = screen.getByRole("radio", { name: "English" });
    expect(english).toBeEnabled();
  });

  it("Aviseringar-kortet har EXAKT 2 toggles (CTO Val 3B, no-mock)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillLabels={[]}
      />,
    );
    // Aviseringar-kortets två wirede toggles.
    expect(
      screen.getByRole("switch", { name: /E-postnotifikationer/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("switch", { name: /Veckosammanfattning/ }),
    ).toBeInTheDocument();
    // ADR 0080 Vag 4 PR-6: Matchningsnotiser-kortets opt-in-toggle är den tredje
    // switchen på sidan (default OFF). Tre toggles totalt: Aviseringar (2) +
    // Matchningsnotiser (1).
    expect(screen.getAllByRole("switch")).toHaveLength(3);
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
        initialSkillLabels={[]}
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
        initialSkillLabels={[]}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Logga ut/ }),
    ).toBeInTheDocument();
  });
});
