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
  followedCompanyNotificationsEnabled: false,
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
    // WeeklySummary) togs bort — de styrde ingen e-postväg.
    // Bevakning F4 (#803): "Notiser om företag du följer" ligger DIREKT efter
    // Matchningsnotiser. Adjacensen är funktionell, inte estetisk: de två delar
    // digest-kadens (ADR 0087 D2), vars kontroll bara finns i det förra kortet —
    // och DOM-ordningen håller även när gridden kollapsar till en kolumn.
    // #678: the change-password card sits in the second column, before Sekretess
    // och data (privacy/danger zone) and Logga ut.
    // #679: the change-email card sits directly before change-password (identity
    // credential before secret credential).
    expect(headings).toEqual([
      "Personuppgifter",
      "Matchning",
      "Visning",
      "Matchningsnotiser",
      "Notiser om företag du följer",
      "Byt e-postadress",
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

  it("exakt två notis-toggles, en per samtyckesändamål (TD-115 + bevakning F4)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    // TD-115: det gamla Aviseringar-kortets två toggles (EmailNotifications +
    // WeeklySummary) togs bort — de styrde ingen e-postväg. Kvar står EN switch
    // per SAMTYCKESÄNDAMÅL (GDPR Art. 6(1)(a)): matchningsnotiser och notiser om
    // följda företag (bevakning F4 / ADR 0087 D5 — skilda flaggor, skilda Art. 7-
    // tidsstämplar, skilda endpoints). Testet pinnar antalet så en tredje toggle
    // aldrig smyger in utan ett eget ändamål.
    expect(screen.getAllByRole("switch")).toHaveLength(2);
    expect(
      screen.getByRole("switch", { name: "Matcha nya annonser åt mig" }),
    ).toHaveAttribute("aria-checked", "false");
    expect(
      screen.getByRole("switch", {
        name: "Mejla mig nya annonser från företag jag följer",
      }),
    ).toHaveAttribute("aria-checked", "false");
  });

  it("bara ETT kadens-val på sidan — takten är delad (ADR 0087 D2)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
    // Följ-notis-kortet visar takten som TEXT och pekar på matchnings-kortet.
    // Två kontroller för ett värde vore garanterad drift.
    expect(
      screen.getAllByRole("radiogroup", {
        name: "Hur ofta vill du få sammanfattningen",
      }),
    ).toHaveLength(1);
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
