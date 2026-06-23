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

// CvUploadForm mockad: exponera dess onUploaded-callback via en knapp så
// step-flödet (upload → done) kan drivas utan en riktig fetch/filväljare.
vi.mock("@/components/resumes/cv-upload-form", () => ({
  CvUploadForm: ({ onUploaded }: { onUploaded?: (id: string) => void }) => (
    <button type="button" onClick={() => onUploaded?.("parsed-1")}>
      MOCK_LADDA_UPP
    </button>
  ),
}));

// MatchSetupWizard mockad: ren prop-spegel. Vi verifierar att den öppnas (open)
// OCH att den får det uppladdade parsed_resume:ts id (onboarding-frikoppling: CV:t
// befordras inte, staging-artefakten lever → wizarden auto-föreslår live ur den).
// Renderas bara när open=true (matchar Radix-beteendet att en stängd dialog inte
// exponerar innehåll), så testet kan skilja på öppen/stängd wizard.
vi.mock("@/components/settings/match-setup-wizard", () => ({
  MatchSetupWizard: ({
    open,
    parsedResumeId,
  }: {
    open: boolean;
    parsedResumeId?: string;
  }) =>
    open ? (
      <div data-testid="wizard" data-parsed-resume-id={parsedResumeId ?? ""}>
        Steg 1 av 5
      </div>
    ) : null,
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
      persistedMunicipalities={[]}
      persistedEmploymentTypes={[]}
      persistedSkills={[]}
      persistedExperienceYears={null}
      importCvHref="/cv/importera"
      {...overrides}
    />
  );
}

beforeEach(() => {
  markSeenMock.mockClear();
  refreshMock.mockClear();
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

describe("WelcomeSetupModal — upload → done (onboarding-frikoppling: ingen gap-fill)", () => {
  it("upload går RAKT till done med ärlig 'CV inläst'-bekräftelse (CV:t sparas inte)", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "MOCK_LADDA_UPP" }));

    // Direkt efter upload: done-steget (bekräftelse + matchnings-val) i EN slide.
    // Ingen mellanliggande gap-fill ("Komplettera ditt CV" finns inte längre).
    expect(
      await screen.findByRole("heading", { name: "CV inläst" })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Komplettera ditt CV" })
    ).toBeNull();
    // Copyn är ärlig: inläst men inte sparat (CTO-bind, pending-card).
    expect(screen.getByText(/inläst men inte sparat/)).toBeInTheDocument();
    // Får aldrig påstå att CV:t är sparat.
    expect(screen.queryByText(/Ditt CV är sparat/)).toBeNull();
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
    // Ingen "CV inläst"-bekräftelse när inget CV laddades upp.
    expect(screen.queryByRole("heading", { name: "CV inläst" })).toBeNull();
  });
});

describe("WelcomeSetupModal — 'Ja' öppnar wizarden", () => {
  it("primär 'Ja, ställ in matchning' öppnar MatchSetupWizard med det uppladdade parsedResumeId", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "MOCK_LADDA_UPP" }));
    await user.click(
      screen.getByRole("button", { name: "Ja, ställ in matchning" })
    );

    // Wizarden öppnas (sekventiellt efter att välkomsten stängts). Cookien sätts
    // INTE här utan när wizarden stängs (annars re-rendrar server-actionen RSC:n
    // och avmonterar modalen innan wizarden hinner öppnas).
    const wizard = await screen.findByTestId("wizard");
    expect(wizard).toHaveTextContent("Steg 1 av 5");
    // CV:t är INTE befordrat → staging-artefakten lever → wizarden får dess id
    // och auto-föreslår live ur den pending-parsade artefakten.
    expect(wizard).toHaveAttribute("data-parsed-resume-id", "parsed-1");
    expect(markSeenMock).not.toHaveBeenCalled();
  });

  it("utan uppladdat CV öppnar wizarden UTAN parsedResumeId", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "Fortsätt utan CV" }));
    await user.click(
      screen.getByRole("button", { name: "Ja, ställ in matchning" })
    );

    const wizard = await screen.findByTestId("wizard");
    expect(wizard).toHaveAttribute("data-parsed-resume-id", "");
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
        persistedMunicipalities={[]}
        persistedEmploymentTypes={[]}
        persistedSkills={[]}
        persistedExperienceYears={null}
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
