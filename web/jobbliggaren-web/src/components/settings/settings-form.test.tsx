import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SettingsForm } from "./settings-form";
import type { JobSeekerProfileDto } from "@/lib/types/me";

const { updateMyProfileActionMock } = vi.hoisted(() => ({
  updateMyProfileActionMock: vi.fn(),
}));

vi.mock("@/lib/actions/me", () => ({
  updateMyProfileAction: updateMyProfileActionMock,
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

describe("SettingsForm — F6 Prompt 2 smoke", () => {
  it("renderar alla kort i rätt ordning", () => {
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
    // F4-12 PR-B (ADR 0076): Matchning-kortet ligger i första kolumnen.
    // `taxonomy={null}` → kortet degraderar men behåller sin h2-rubrik.
    // #1740 (design-reviewer D4): the two notice cards follow Matchning in column 1, because
    // background matching reads that profile; column 2 is the account, ending in the
    // destructive card and Logga ut.
    // TD-115 (2026-06-25): det gamla "Aviseringar"-kortet (EmailNotifications +
    // WeeklySummary) togs bort — de styrde ingen e-postväg.
    // Bevakning F4 (#803): "Notiser om företag du följer" ligger DIREKT efter
    // Matchningsnotiser. Adjacensen är funktionell, inte estetisk: de två delar
    // digest-kadens (ADR 0087 D2), vars kontroll bara finns i det förra kortet —
    // och DOM-ordningen håller även när gridden kollapsar till en kolumn.
    expect(headings).toEqual([
      "Matchning",
      "Matchningsnotiser",
      "Notiser om företag du följer",
      "Visning",
      "Byt e-postadress",
      "Sekretess och data",
      "Logga ut",
    ]);
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

// The language segment is direct-apply: a change goes straight to the action, carrying the
// language and nothing else.
describe("SettingsForm — the language write", () => {
  beforeEach(() => {
    updateMyProfileActionMock.mockReset();
    updateMyProfileActionMock.mockResolvedValue({ success: true });
  });

  function renderForm() {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
  }

  it("sends ONLY the language when the language changes", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole("radio", { name: "English" }));

    await waitFor(() => expect(updateMyProfileActionMock).toHaveBeenCalledTimes(1));
    expect(updateMyProfileActionMock.mock.calls[0]![0]).toEqual({ language: "en" });
  });
});

// #1391 — a direct-apply control reports its own OUTCOME where the user is looking. The
// language save's message used to render in the name card, in the other grid column, while
// the segment reverted silently. Pinned on the CARD, not on the copy: the defect is which
// card the text lands in.
describe("SettingsForm — the direct-apply outcome lands on the control that started it (#1391)", () => {
  beforeEach(() => {
    updateMyProfileActionMock.mockReset();
    updateMyProfileActionMock.mockResolvedValue({ success: true });
  });

  function renderForm() {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
        taxonomy={null}
        initialSkillGroups={[]}
      />,
    );
  }

  /** The card a node lives in — the unit this issue is about, one per grid column. */
  function cardOf(node: Element): HTMLElement | null {
    return node.closest<HTMLElement>("section.jp-card");
  }

  it("renders a refused language change in the card that owns the segment", async () => {
    updateMyProfileActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte na servern.",
    });
    const user = userEvent.setup();
    renderForm();
    const languageGroup = screen.getByRole("radiogroup", { name: "Språk" });

    await user.click(screen.getByRole("radio", { name: "English" }));

    const alert = await screen.findByRole("alert");
    expect(cardOf(alert)).toBe(cardOf(languageGroup));
    expect(screen.getAllByRole("alert")).toHaveLength(1);
  });

  it("associates the refusal with the language group", async () => {
    updateMyProfileActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte na servern.",
    });
    const user = userEvent.setup();
    renderForm();
    const languageGroup = screen.getByRole("radiogroup", { name: "Språk" });
    const hint = screen.getByText("Sparas på ditt konto.");

    await user.click(screen.getByRole("radio", { name: "English" }));

    const alert = await screen.findByRole("alert");
    // Both ids, in order: the a11y skill's §5 rule is that describedby carries the help text
    // AND the error, not one replacing the other.
    expect(languageGroup.getAttribute("aria-describedby")).toBe(
      `${hint.id} ${alert.id}`,
    );
  });

  it("moves focus back to the language group when the save is refused", async () => {
    // The segment is disabled while the save is pending, which drops focus to <body> in a real
    // browser, and Segment's own restore effect is gated on the group already holding focus.
    // Without this the message is announced but the control it names is unreachable.
    //
    // The blur is what makes this pin discriminate. jsdom does not blur on `disabled`, and
    // `user.click` leaves focus inside the group, so without it Segment's OWN [value] restore
    // effect re-focuses the checked button on the revert and the assertion holds with this
    // card's effect deleted. Blurring reproduces the browser's disabled-blur, and Segment's
    // restore is gated on the group already holding focus, so only this card's effect can
    // bring it back. The real timing is measured in Chromium, in
    // docs/reviews/2026-08-23-1391-rendered-measurement.md.
    updateMyProfileActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte na servern.",
    });
    const user = userEvent.setup();
    renderForm();
    const languageGroup = screen.getByRole("radiogroup", { name: "Språk" });

    await user.click(screen.getByRole("radio", { name: "English" }));
    (document.activeElement as HTMLElement | null)?.blur();
    expect(languageGroup.contains(document.activeElement)).toBe(false);

    await screen.findByRole("alert");
    await waitFor(() =>
      expect(languageGroup.contains(document.activeElement)).toBe(true),
    );
    expect(document.activeElement).toHaveAttribute("aria-checked", "true");
  });

  it("renders the receipt for a saved language in the card that owns the segment", async () => {
    const user = userEvent.setup();
    renderForm();
    const languageGroup = screen.getByRole("radiogroup", { name: "Språk" });

    await user.click(screen.getByRole("radio", { name: "English" }));

    const receipt = await screen.findByText(/^Sparat \d{2}:\d{2}$/);
    expect(cardOf(receipt)).toBe(cardOf(languageGroup));
  });
});

