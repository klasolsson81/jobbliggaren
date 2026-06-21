import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// Server Action — jsdom har ingen riktig server, mocka set-cookien.
const { markSeenMock } = vi.hoisted(() => ({ markSeenMock: vi.fn(async () => {}) }));
vi.mock("@/lib/onboarding/setup-welcome-actions", () => ({
  markSetupWelcomeSeen: markSeenMock,
}));

// useRouter().refresh() — undvik next/navigation-routing-fel i jsdom.
const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }));
vi.mock("next/navigation", async () => {
  const actual =
    await vi.importActual<typeof import("next/navigation")>("next/navigation");
  return {
    ...actual,
    useRouter: () => ({ refresh: refreshMock, push: vi.fn(), replace: vi.fn() }),
  };
});

// Wizardens preferens-actions (monteras i komponenten via MatchSetupWizard).
const { updateMock, deriveMock, cvSuggestMock } = vi.hoisted(() => ({
  updateMock: vi.fn(),
  deriveMock: vi.fn(),
  cvSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  deriveOccupationsAction: deriveMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
}));

// CvUploadForm mockad: exponera dess onUploaded-callback via en knapp så
// step-flödet (upload → confirm) kan drivas utan en riktig fetch/filväljare.
vi.mock("@/components/resumes/cv-upload-form", () => ({
  CvUploadForm: ({ onUploaded }: { onUploaded?: (id: string) => void }) => (
    <button type="button" onClick={() => onUploaded?.("parsed-1")}>
      MOCK_LADDA_UPP
    </button>
  ),
}));

import { WelcomeSetupModal } from "./welcome-setup-modal";

const occupationFields: ReadonlyArray<TaxonomyOccupationField> = [
  {
    conceptId: "field_data",
    label: "Data/IT",
    occupationGroups: [{ conceptId: "grp_backend", label: "Backendutvecklare" }],
  },
];
const regions: ReadonlyArray<TaxonomyRegion> = [
  { conceptId: "region_sthlm", label: "Stockholms län", municipalities: [] },
];
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  { conceptId: "et_fast", label: "Tillsvidareanställning" },
];

function renderModal(
  overrides?: Partial<React.ComponentProps<typeof WelcomeSetupModal>>
) {
  render(
    <WelcomeSetupModal
      showWelcome
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      persistedOccupationGroups={[]}
      persistedRegions={[]}
      persistedEmploymentTypes={[]}
      importCvHref="/cv/importera"
      {...overrides}
    />
  );
}

beforeEach(() => {
  markSeenMock.mockClear();
  refreshMock.mockClear();
  updateMock.mockReset();
  deriveMock.mockReset();
  cvSuggestMock.mockReset();
  updateMock.mockResolvedValue({ success: true });
  deriveMock.mockResolvedValue({ success: true, candidates: [] });
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("WelcomeSetupModal — gating", () => {
  it("renderar inte när showWelcome=false", () => {
    renderModal({ showWelcome: false });
    expect(
      screen.queryByRole("heading", { name: "Kom igång med matchning" })
    ).toBeNull();
  });

  it("öppnar på upload-steget när showWelcome=true", () => {
    renderModal();
    expect(
      screen.getByRole("heading", { name: "Kom igång med matchning" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "MOCK_LADDA_UPP" })
    ).toBeInTheDocument();
  });
});

describe("WelcomeSetupModal — upload → done (bekräftelse + val i EN slide)", () => {
  it("upload → done: grön bekräftelse (CV uppladdat) + matchnings-valet i samma slide, inte 'match klar'", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "MOCK_LADDA_UPP" }));

    // Bekräftelse OCH val i SAMMA slide (ingen separat "Fortsätt"-mellansida).
    expect(
      await screen.findByRole("heading", { name: "CV uppladdat" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Vi har läst in och tolkat ditt CV/)
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ja, ställ in matchning" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Hoppa över" })
    ).toBeInTheDocument();
    // Får aldrig påstå att en matchning är gjord (FAS-DEFERRAL / honest copy).
    expect(screen.queryByText(/matchningar hittade/i)).toBeNull();
    expect(screen.queryByText(/Vi hittade \d+ matchningar/i)).toBeNull();
  });

  it("'Fortsätt utan CV' går till done UTAN grön bekräftelse, rakt på matchnings-valet", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "Fortsätt utan CV" }));

    expect(
      screen.getByRole("heading", { name: "Ställ in din matchning" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ja, ställ in matchning" })
    ).toBeInTheDocument();
    // Ingen "CV uppladdat"-bekräftelse när inget CV laddades upp.
    expect(screen.queryByRole("heading", { name: "CV uppladdat" })).toBeNull();
  });
});

describe("WelcomeSetupModal — 'Ja' öppnar wizarden", () => {
  it("primär 'Ja, ställ in matchning' öppnar MatchSetupWizard (steg 1 av 4)", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "MOCK_LADDA_UPP" }));
    await user.click(
      screen.getByRole("button", { name: "Ja, ställ in matchning" })
    );

    // Wizarden öppnas (sekventiellt efter att välkomsten stängts). Cookien sätts
    // INTE här utan när wizarden stängs (annars re-rendrar server-actionen RSC:n
    // och avmonterar modalen innan wizarden hinner öppnas).
    expect(await screen.findByText("Steg 1 av 4")).toBeInTheDocument();
    expect(markSeenMock).not.toHaveBeenCalled();
  });
});

describe("WelcomeSetupModal — skip markerar cookien sedd", () => {
  it("'Hoppa över' i done-steget anropar markSetupWelcomeSeen + refresh", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "Fortsätt utan CV" }));
    await user.click(screen.getByRole("button", { name: "Hoppa över" }));

    await waitFor(() => expect(markSeenMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    // Välkomsten är stängd efter skip.
    expect(
      screen.queryByRole("heading", { name: "Kom igång med matchning" })
    ).toBeNull();
  });
});

describe("WelcomeSetupModal — civic-utility", () => {
  it("innehåller inga emoji eller utropstecken", () => {
    const { container } = render(
      <WelcomeSetupModal
        showWelcome
        occupationFields={occupationFields}
        regions={regions}
        employmentTypes={employmentTypes}
        persistedOccupationGroups={[]}
        persistedRegions={[]}
        persistedEmploymentTypes={[]}
        importCvHref="/cv/importera"
      />
    );
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/!/);
    expect(text).not.toMatch(/[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/u);
  });
});

describe("WelcomeSetupModal — a11y (Radix description-wiring)", () => {
  // Regression: tidigare sattes BÅDE aria-describedby på DialogContent OCH ett
  // explicit id på DialogDescription, vilket besegrade Radix auto-wiring och gav
  // konsolvarningen "Missing `Description` or `aria-describedby={undefined}`".
  // Nu låter vi Radix koppla id/aria-describedby själv, så varningen inte fyrar.
  it("renderar utan Radix missing-description-varning", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderModal();
    const logged = [...warnSpy.mock.calls, ...errorSpy.mock.calls]
      .flat()
      .join(" ");
    expect(logged).not.toMatch(/Missing .?Description|aria-describedby/i);
    warnSpy.mockRestore();
    errorSpy.mockRestore();
  });
});
