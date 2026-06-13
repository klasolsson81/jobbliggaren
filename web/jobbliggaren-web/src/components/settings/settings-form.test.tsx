import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SettingsForm } from "./settings-form";
import type { JobSeekerProfileDto } from "@/lib/types/me";

vi.mock("@/lib/actions/me", () => ({
  updateMyProfileAction: vi.fn().mockResolvedValue({ success: true }),
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
  createdAt: "2026-05-01T08:00:00Z",
};

describe("SettingsForm — F6 Prompt 2 smoke", () => {
  it("renderar alla 5 kort i rätt ordning", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
      />,
    );
    const headings = screen
      .getAllByRole("heading", { level: 2 })
      .map((h) => h.textContent);
    expect(headings).toEqual([
      "Personuppgifter",
      "Visning",
      "Aviseringar",
      "Sekretess och data",
      "Logga ut",
    ]);
  });

  it("Personuppgifter-kortet visar Namn (write) + E-post (read-only)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
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
      />,
    );
    expect(screen.queryByLabelText(/Telefon/i)).not.toBeInTheDocument();
  });

  it("Visning-kortet har Tema-segment + Språk-segment med English disabled", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
      />,
    );
    const themeGroup = screen.getByRole("radiogroup", { name: "Tema" });
    expect(themeGroup).toBeInTheDocument();
    const langGroup = screen.getByRole("radiogroup", { name: "Språk" });
    expect(langGroup).toBeInTheDocument();
    const english = screen.getByRole("radio", { name: "English" });
    expect(english).toBeDisabled();
  });

  it("Aviseringar-kortet har EXAKT 2 toggles (CTO Val 3B, no-mock)", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
      />,
    );
    const switches = screen.getAllByRole("switch");
    expect(switches).toHaveLength(2);
    expect(
      screen.getByRole("switch", { name: /E-postnotifikationer/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("switch", { name: /Veckosammanfattning/ }),
    ).toBeInTheDocument();
  });

  it("Sekretess och data-kortet använder DeleteAccountSection-stub", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
      />,
    );
    expect(screen.getByTestId("delete-account-stub")).toBeInTheDocument();
  });

  it("Logga ut-kortet renderar submit-knapp", () => {
    render(
      <SettingsForm
        initialProfile={baseProfile}
        userEmail="klas@example.se"
      />,
    );
    expect(
      screen.getByRole("button", { name: /Logga ut/ }),
    ).toBeInTheDocument();
  });
});
