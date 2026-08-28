import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
// `/pure` is the un-aliased real implementation — `vitest.config.ts` anchors the
// alias with `$`, so this import bypasses the sv-only provider shim. Needed only
// by the locale-discrimination block at the bottom of this file.
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import userEvent from "@testing-library/user-event";
import enMessages from "../../../messages/en";
import type {
  TaxonomyOccupationField,
  TaxonomyOption,
  TaxonomyRegion,
} from "@/lib/dto/taxonomy";

// Section server-actions mockade (no-ops) — vi driver rail-modalens EGNA beteenden
// (steg-navigering, save→Klart, skip, räknaren), inte sektionernas interna sök.
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
// CvUploadForm (Start-steget) anropar useRouter → mocka next/navigation.
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
// Det RIKTIGA conceptId:t och den riktiga källetiketten ur klass2-taxonomy.json —
// ett påhittat id hade bara motionerat fallback-grenen, aldrig översättningen (#1537).
const employmentTypes: ReadonlyArray<TaxonomyOption> = [
  {
    conceptId: "kpPX_CNN_gDU",
    label: "Tillsvidareanställning (inkl. eventuell provanställning)",
  },
];

// Shared required props. Every field is mandatory in the component's signature,
// so a new *required* prop is caught by `tsc --noEmit` in pre-commit rather than
// drifting between the two render helpers. A new optional one passes silently —
// `satisfies` fails on assignability, and an optional prop stays assignable.
const modalProps = {
  open: true,
  onOpenChange: vi.fn(),
  occupationFields,
  regions,
  employmentTypes,
  persistedOccupationGroups: [],
  persistedRegions: [],
  persistedMunicipalities: [],
  persistedRemote: false,
  persistedEmploymentTypes: [],
  persistedSkills: [],
  persistedOccupationExperience: [],
  importCvHref: "/cv/importera",
} satisfies React.ComponentProps<typeof MatchSetupRailModal>;

function renderModal(
  overrides?: Partial<React.ComponentProps<typeof MatchSetupRailModal>>,
) {
  const onOpenChange = vi.fn();
  render(
    <MatchSetupRailModal
      {...modalProps}
      onOpenChange={onOpenChange}
      {...overrides}
    />,
  );
  return { onOpenChange };
}

beforeEach(() => {
  updateMock.mockReset();
  countMock.mockReset();
  countMock.mockReturnValue({ count: 42, loading: false });
  // Section auto-suggest (autoSuggestFromCv) — no-CV shapes så sektionerna inte
  // kraschar på undefined (samma som cv-match-setup-testet).
  cvSuggestMock.mockResolvedValue({ kind: "noCv" });
  parsedSuggestMock.mockResolvedValue({ kind: "noCv" });
  skillSearchMock.mockResolvedValue({ success: true, options: [] });
  skillSuggestMock.mockResolvedValue({ kind: "noCv" });
});

describe("MatchSetupRailModal — steg-navigering", () => {
  it("öppnar på Start-steget (välkomst-pitch + Fortsätt)", () => {
    renderModal();
    expect(
      screen.getByRole("heading", { name: "Välkommen till Jobbliggaren" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Vi matchar Platsbankens annonser mot din profil"),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Fortsätt" })).toBeInTheDocument();
  });

  it("Nästa/Fortsätt stegar Start → Yrken", async () => {
    const user = userEvent.setup();
    renderModal();
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    expect(screen.getByRole("heading", { name: "Yrken" })).toBeInTheDocument();
  });

  it("rail-raden hoppar direkt till valt steg (fri navigering)", async () => {
    const user = userEvent.setup();
    renderModal();
    // Rail-knappen "Granska" (label + meta i samma knapp).
    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    expect(granskaRail).toBeDefined();
    await user.click(granskaRail!);
    expect(
      screen.getByRole("heading", { name: "Granska och spara" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Spara matchning" }),
    ).toBeInTheDocument();
  });
});

describe("MatchSetupRailModal — skip och stäng", () => {
  it("'Gör det senare' på Start stänger utan att spara", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderModal();
    await user.click(screen.getByRole("button", { name: "Gör det senare" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(updateMock).not.toHaveBeenCalled();
  });
});

describe("MatchSetupRailModal — ett save på slutet → Klart-läget", () => {
  it("Spara matchning kallar full-replace-PUT och visar Klart", async () => {
    updateMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    renderModal({ persistedOccupationGroups: ["grp_backend"] });

    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    await user.click(granskaRail!);
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    // Full-replace-payloaden bär alla dimensioner (draften seedad från persisted).
    expect(updateMock.mock.calls[0]![0]).toMatchObject({
      preferredOccupationGroups: ["grp_backend"],
    });
    // Klart-läget efter lyckad save.
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Matchningen är sparad" }),
      ).toBeInTheDocument(),
    );
    // Footer-"Stäng" finns (Radix-kryssets aria-label är också "Stäng" → getAll).
    expect(
      screen.getAllByRole("button", { name: "Stäng" }).length,
    ).toBeGreaterThan(0);
  });
});

describe("MatchSetupRailModal — live räknare", () => {
  it("visar talet när räknaren har ett värde", () => {
    countMock.mockReturnValue({ count: 42, loading: false });
    renderModal();
    expect(screen.getByText("MATCHAR NU")).toBeInTheDocument();
    // Två live-regioner (rail-kort + mobil-remsa; CSS döljer en per layout,
    // jsdom applicerar ingen media-query) → någon av dem bär talet.
    const statuses = screen.getAllByRole("status");
    expect(statuses.some((s) => s.textContent?.includes("42"))).toBe(true);
  });

  it("visar en neutral platshållare (aldrig 0) när räknaren är null", () => {
    countMock.mockReturnValue({ count: null, loading: true });
    renderModal();
    expect(screen.getByText("MATCHAR NU")).toBeInTheDocument();
    const statuses = screen.getAllByRole("status");
    expect(statuses.every((s) => !s.textContent?.includes("0"))).toBe(true);
  });
});

// The counter moved from a module-level `new Intl.NumberFormat("sv-SE")` to
// `formatNumber(useFormatter(), …)`. The two are INDISTINGUISHABLE in sv — both
// emit U+00A0 as the group separator — so a Swedish test alone would have been
// green before and after and proven nothing. The locale axis is the only thing
// that discriminates, and it needs its own provider: `vitest.config.ts` aliases
// `@testing-library/react` to a shim hardcoding `locale="sv"`, so the English
// case renders through `/pure`, which the alias's `$` anchor deliberately leaves
// un-rewritten.
//
// The sv case is kept even though it cannot discriminate the two implementations,
// because it guards a different axis: CLAUDE.md §10's NBSP requirement. If CLDR
// ever moves Swedish grouping the way it moved fr-FR (U+00A0 → U+202F in CLDR 42),
// Swedish grouping would break silently on an ICU bump, and this is the assertion
// that would catch it.
describe("MatchSetupRailModal — räknarens tal följer aktiv locale", () => {
  function renderWithEnglishLocale(
    extra: Partial<React.ComponentProps<typeof MatchSetupRailModal>> = {},
  ) {
    rawRender(
      <NextIntlClientProvider
        locale="en"
        messages={enMessages}
        timeZone="Europe/Stockholm"
      >
        {/* Own `onOpenChange`, like `renderModal` — so `modalProps`' default
            mock is never actually called and cannot accumulate calls across
            tests (`beforeEach` resets the named mocks, not every mock). */}
        <MatchSetupRailModal {...modalProps} {...extra} onOpenChange={vi.fn()} />
      </NextIntlClientProvider>,
    );
  }

  it("visar anställningsformen på engelska under locale en (#1537)", () => {
    // Steg 4 är Anställningsform. Etiketten kommer INTE ur props längre: backend skickar
    // den ärliga svenska källetiketten, och ordet hämtas ur katalogen på conceptId.
    renderWithEnglishLocale({ initialStep: 4 });

    expect(
      screen.getByText("Permanent employment (including any trial employment)"),
    ).toBeInTheDocument();
    // Negativt, och det är den halvan som fäller en regression: källetiketten som
    // fortfarande ligger i props får inte nå skärmen under en.
    expect(
      screen.queryByText("Tillsvidareanställning (inkl. eventuell provanställning)"),
    ).toBeNull();
  });

  it("grupperar tusental med hårt mellanslag i sv (CLAUDE.md §10)", () => {
    countMock.mockReturnValue({ count: 1234, loading: false });
    renderModal();
    const statuses = screen.getAllByRole("status");
    expect(
      statuses.some((s) => s.textContent?.includes("1\u00A0234")),
    ).toBe(true);
  });

  it("grupperar enligt en-konventionen när locale är en", () => {
    countMock.mockReturnValue({ count: 1234, loading: false });
    renderWithEnglishLocale();
    const statuses = screen.getAllByRole("status");
    // Positive: the en grouping renders.
    expect(statuses.some((s) => s.textContent?.includes("1,234"))).toBe(true);
    // Negative, and this is the half that fails a reversion: a hardcoded
    // `sv-SE` instance would have rendered U+00A0 here too.
    expect(
      statuses.every((s) => !s.textContent?.includes("1\u00A0234")),
    ).toBe(true);
  });
});

describe("#551 punkt 4 — Distans-valet överlever till MatchPreferences.PreferredRemote", () => {
  // Acceptanskriteriet för wizard-halvan: det användaren kryssar i Orter-steget
  // måste nå den persisterade axeln. Utan den här pinnen kan hela kedjan
  // (kaskad → draft → full-replace-PUT) gå sönder i vilken led som helst utan
  // att något test märker det — en av dem tappades faktiskt tyst under bygget.
  it("en PÅ-slagen Distans-ruta skickas som preferredRemote: true i full-replace-PUT:en", async () => {
    updateMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    // STEP_ORTER = 3 (wizardens Orter-steg; modalen öppnar annars på START).
    renderModal({
      persistedOccupationGroups: ["grp_backend"],
      persistedRemote: false,
      initialStep: 3,
    });

    await user.click(screen.getByRole("button", { name: "Lägg till orter" }));
    await user.click(screen.getByRole("checkbox", { name: "Distans" }));

    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    await user.click(granskaRail!);
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock.mock.calls[0]![0]).toMatchObject({
      preferredRemote: true,
    });
  });

  it("en orörd wizard bär den PERSISTERADE axeln vidare, aldrig en nolla", async () => {
    // Page-wipe-vakten för den nya axeln: att spara någon ANNAN dimension får
    // aldrig tyst släcka ett distans-val användaren gjort tidigare.
    updateMock.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    renderModal({
      persistedOccupationGroups: ["grp_backend"],
      persistedRemote: true,
    });

    const granskaRail = screen
      .getAllByRole("button")
      .find((b) => b.textContent?.includes("Granska"));
    await user.click(granskaRail!);
    await user.click(screen.getByRole("button", { name: "Spara matchning" }));

    await waitFor(() => expect(updateMock).toHaveBeenCalledTimes(1));
    expect(updateMock.mock.calls[0]![0]).toMatchObject({
      preferredRemote: true,
    });
  });
});
