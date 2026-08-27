import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";
import type { UploadOutcome } from "@/components/resumes/cv-upload-form";

/**
 * #1060 — bekräftelse-plattan i välkomstmodulen säger vad som FAKTISKT hände.
 *
 * Egen fil, inte ett block i `match-setup-rail-modal.test.tsx`, av ett konkret skäl:
 * `vi.mock` hissas per fil, och den här sviten behöver stubba `CvUploadForm` för att kunna
 * driva `onUploaded`. Att göra det i den befintliga filen skulle byta ut den riktiga formen
 * under fyra beskrivningar som inte handlar om uppladdning alls.
 *
 * Stubben är seamen, och den är ärlig: testets subjekt är MODALENS gren, inte formens
 * uppladdning. `UploadOutcome` är produktionens egen typ och stubben emitterar exakt de två
 * former `handleCvUploaded` tar emot i produktion — den hittar inte på en tredje.
 */

const {
  updateMock,
  countMock,
  cvSuggestMock,
  parsedSuggestMock,
  skillSearchMock,
  skillSuggestMock,
} = vi.hoisted(() => ({
  updateMock: vi.fn(),
  countMock: vi.fn(),
  cvSuggestMock: vi.fn(),
  parsedSuggestMock: vi.fn(),
  skillSearchMock: vi.fn(),
  skillSuggestMock: vi.fn(),
}));
vi.mock("@/lib/actions/match-preferences", () => ({
  updateMatchPreferencesAction: updateMock,
  suggestOccupationsFromCvAction: cvSuggestMock,
  suggestOccupationsFromParsedResumeAction: parsedSuggestMock,
  searchSkillsAction: skillSearchMock,
  suggestSkillsFromParsedResumeAction: skillSuggestMock,
}));
vi.mock("@/lib/hooks/use-draft-match-count", () => ({
  useDraftMatchCount: () => countMock(),
}));
vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    refresh: vi.fn(),
    prefetch: vi.fn(),
    back: vi.fn(),
  }),
  useSearchParams: () => new URLSearchParams(),
  usePathname: () => "/",
}));

// Stubbad uppladdningsform: två knappar, ett utfall var.
//
// `UploadOutcome` importeras som TYP från produktionsmodulen i stället för att skrivas av
// strukturellt. `vi.mock` typkollas inte, så en handskriven kopia hade lämnat testet grönt
// om produktionsformen drev — och då hade stubben drivit modalen med ett utfall den aldrig
// får, vilket är precis "testa mocken" i stället för koden. Med `import type` bryter en drift
// i `tsc` i stället.
vi.mock("@/components/resumes/cv-upload-form", () => ({
  CvUploadForm: ({
    onUploaded,
  }: {
    onUploaded: (outcome: UploadOutcome, fileName?: string) => void;
  }) => (
    <div>
      <button
        type="button"
        onClick={() =>
          onUploaded(
            { kind: "promoted", resumeId: "r-1", parsedResumeId: "p-1" },
            "anna-cv.pdf",
          )
        }
      >
        stub-promoted
      </button>
      <button
        type="button"
        onClick={() =>
          onUploaded(
            {
              kind: "pending",
              parsedResumeId: "p-2",
              blockReason: "PersonnummerPresent",
              personnummerCount: 1,
            },
            "anna-cv.pdf",
          )
        }
      >
        stub-pending
      </button>
    </div>
  ),
}));

import { MatchSetupRailModal } from "./match-setup-rail-modal";

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

function renderModal() {
  render(
    <MatchSetupRailModal
      open
      onOpenChange={vi.fn()}
      occupationFields={occupationFields}
      regions={regions}
      employmentTypes={employmentTypes}
      persistedOccupationGroups={[]}
      persistedRegions={[]}
      persistedMunicipalities={[]}
      persistedRemote={false}
      persistedEmploymentTypes={[]}
      persistedSkills={[]}
      persistedOccupationExperience={[]}
      importCvHref="/cv/importera"
    />,
  );
}

beforeEach(() => {
  updateMock.mockReset();
  countMock.mockReset();
  countMock.mockReturnValue({ count: 42, loading: false });
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("MatchSetupRailModal — bekräftelse-plattan efter uppladdning (#1060)", () => {
  it("säger SPARAT när uppladdningen befordrades", async () => {
    // Efter att förords-grinden retirerats sparas den vanliga uppladdningen direkt.
    // "CV inläst" underdriver då det som hänt, på just den skärm vars hela uppgift är
    // att berätta vad som hände — och #1060 filades för att produkten sa mindre än den
    // gjort.
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "stub-promoted" }));

    expect(screen.getByText("CV sparat: anna-cv.pdf")).toBeInTheDocument();
    expect(screen.queryByText("CV inläst: anna-cv.pdf")).not.toBeInTheDocument();
  });

  it("säger INLÄST när uppladdningen blockerades — fortfarande sant om den", async () => {
    // Motbevisningen som gör raden ovan meningsfull: en grön "sparat"-assertion ensam
    // skulle passera lika bra på en platta som alltid säger sparat. VARFÖR den
    // blockerades ägs av PR C (D6), inte av den här grenen.
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "stub-pending" }));

    expect(screen.getByText("CV inläst: anna-cv.pdf")).toBeInTheDocument();
    expect(screen.queryByText("CV sparat: anna-cv.pdf")).not.toBeInTheDocument();
  });
});

describe("MatchSetupRailModal — förslagskällan efter uppladdning", () => {
  // Regressionsvakten för den halva av fixen som lever i FE. Går parse-id:t tillbaka till
  // null på den promotade armen faller yrkena tyst till latestRole-vägen och kompetenserna
  // till ingen väg alls — inget kastar, inget syns, sektionerna blir bara tomma. Det är
  // precis den formen defekten hade i produktion.
  it("matar parse-id:t till båda sektionerna när uppladdningen befordrades", async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "stub-promoted" }));
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    expect(parsedSuggestMock).toHaveBeenCalledWith("p-1");
    expect(cvSuggestMock).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Nästa" }));

    expect(skillSuggestMock).toHaveBeenCalledWith("p-1");
  });

  it("matar parse-id:t till båda sektionerna när uppladdningen stannade i granskning", async () => {
    // Kontrollarmen: den här vägen fungerade före fixen, och måste fortsätta göra det.
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByRole("button", { name: "stub-pending" }));
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    expect(parsedSuggestMock).toHaveBeenCalledWith("p-2");
    expect(cvSuggestMock).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Nästa" }));

    expect(skillSuggestMock).toHaveBeenCalledWith("p-2");
  });
});
