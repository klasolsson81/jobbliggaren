import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MeProfileForm } from "./me-profile-form";
import type { JobSeekerProfileDto } from "@/lib/types/me";
import type { ActionResult } from "@/lib/actions/me";
import type { UpdateMyProfileInput } from "@/lib/actions/me-schemas";

const updateMyProfileActionMock =
  vi.fn<(input: UpdateMyProfileInput) => Promise<ActionResult>>();

vi.mock("@/lib/actions/me", () => ({
  updateMyProfileAction: (input: UpdateMyProfileInput) =>
    updateMyProfileActionMock(input),
}));

function makeProfile(
  overrides: Partial<JobSeekerProfileDto> = {}
): JobSeekerProfileDto {
  return {
    id: "550e8400-e29b-41d4-a716-446655440000",
    displayName: "Anna Andersson",
    language: "sv",
    emailNotifications: true,
    weeklySummary: false,
    createdAt: "2026-01-01T00:00:00Z",
    hasStatedDesiredOccupation: false,
    preferredOccupationGroups: [],
    preferredRegions: [],
    preferredEmploymentTypes: [],
    ...overrides,
  };
}

describe("MeProfileForm", () => {
  beforeEach(() => {
    updateMyProfileActionMock.mockReset();
    updateMyProfileActionMock.mockResolvedValue({ success: true });
  });

  it("renders all fields with initial values", () => {
    render(<MeProfileForm initialProfile={makeProfile()} />);

    expect(screen.getByLabelText("Visningsnamn")).toHaveValue("Anna Andersson");
    // shadcn Select (Radix) renderar trigger som combobox-knapp med SelectValue
    // som text-nod, inte native <select>. Verifierar därför text-innehåll.
    expect(screen.getByLabelText("Språk")).toHaveTextContent("Svenska");
    expect(screen.getByLabelText("E-postnotifieringar")).toBeChecked();
    expect(screen.getByLabelText("Veckosammanfattning")).not.toBeChecked();
  });

  it("submits sanitized values and shows 'Sparat' on success", async () => {
    const user = userEvent.setup();
    render(<MeProfileForm initialProfile={makeProfile()} />);

    const name = screen.getByLabelText("Visningsnamn");
    await user.clear(name);
    await user.type(name, "Anna Ny");
    await user.click(screen.getByLabelText("Veckosammanfattning"));
    await user.click(screen.getByRole("button", { name: "Spara profil" }));

    await waitFor(() => {
      expect(updateMyProfileActionMock).toHaveBeenCalledTimes(1);
    });
    expect(updateMyProfileActionMock).toHaveBeenCalledWith({
      displayName: "Anna Ny",
      language: "sv",
      emailNotifications: true,
      weeklySummary: true,
    });

    // Lock sv-SE 24h locale-format: "Sparat HH:MM."
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(/Sparat \d{2}:\d{2}\./);
  });

  it("shows server error when action returns { success:false, error }", async () => {
    updateMyProfileActionMock.mockResolvedValueOnce({
      success: false,
      error: "Kunde inte uppdatera profilen.",
    });

    const user = userEvent.setup();
    render(<MeProfileForm initialProfile={makeProfile()} />);

    await user.click(screen.getByRole("button", { name: "Spara profil" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte uppdatera profilen.");
  });

  it("TD-15: client-side validation fail sets aria-invalid and focuses field", async () => {
    const user = userEvent.setup();
    render(<MeProfileForm initialProfile={makeProfile()} />);

    const name = screen.getByLabelText("Visningsnamn");
    // Bypass HTML required-constraint by removing it; schema-level validation
    // is what we want to exercise (trim → min(1) fails on whitespace).
    name.removeAttribute("required");
    await user.clear(name);
    await user.type(name, "   ");

    await user.click(screen.getByRole("button", { name: "Spara profil" }));

    // Verify the alert element actually carries the id that aria-describedby
    // points to — guards against dangling-reference regression.
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveAttribute("id", "me-profile-form-error");

    await waitFor(() => {
      expect(name).toHaveAttribute("aria-invalid", "true");
    });
    expect(name).toHaveAttribute("aria-describedby", "me-profile-form-error");
    expect(name).toHaveFocus();
    expect(updateMyProfileActionMock).not.toHaveBeenCalled();
  });

  // TD-15 path-routing för `language`-fältet är inte längre möjlig att trigga
  // via UI efter Batch B (TD-41 — shadcn Select). Radix Select renderar inte
  // native <select>, så `appendChild fake option`-bypass fungerar inte och
  // trigger:n exponerar endast giltiga z.enum-värden ("sv" | "en") som
  // valbara items. pathToElementId("language") → "me-language" testas direkt
  // i `lib/forms/me-path-routing.test.ts`; UI-integrationen av displayName-
  // path:en (test ovan) bevisar att effekten kör korrekt.
});
