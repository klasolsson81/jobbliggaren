import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Clock } from "lucide-react";
import { JobbHeroFilters } from "./jobb-hero-filters";
import { HeroChip } from "./hero-chip";
import { ForetagSokSearchbar } from "@/components/company-criteria/foretag-sok-searchbar";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * #2212: every filter popover on /jobb and /foretag/sok is placed inside the viewport.
 *
 * Each trigger rect and client size is the layout engine's, measured 2026-10-24 on 5ad51c9c1
 * (headless Chromium, sv, light, unselected pills, a stated occupation so the Matchning pill
 * renders). Only the trigger's left edge and bottom are read by the placement.
 */

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));

const taxonomy: TaxonomyTree = {
  employmentTypes: [{ conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" }],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "AvNB_uwa_6n6", label: "Stockholm" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [{ conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" }],
    },
  ],
};

const reference: CriterionReference = {
  sniVersion: "SNI2025.v1",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [{ code: "62", name: "Dataprogrammering", leaves: [{ code: "62100", name: "Dataprogrammering" }] }],
    },
  ],
  lan: [{ code: "01", name: "Stockholms län", kommuner: [{ code: "0180", name: "Stockholm" }] }],
};

const rects = new Map<Element, { left: number; bottom: number }>();
const viewport = { clientWidth: 0, clientHeight: 0 };

function stubLayout(clientWidth: number, clientHeight: number) {
  Object.assign(viewport, { clientWidth, clientHeight });
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (
    this: HTMLElement,
  ) {
    const r = rects.get(this) ?? { left: 0, bottom: 0 };
    return { x: r.left, y: 0, left: r.left, top: 0, right: r.left, bottom: r.bottom, width: 0, height: 0, toJSON: () => r };
  });
  vi.spyOn(Element.prototype, "clientWidth", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? viewport.clientWidth : 0;
  });
  vi.spyOn(Element.prototype, "clientHeight", "get").mockImplementation(function (this: Element) {
    return this === document.documentElement ? viewport.clientHeight : 0;
  });
}

afterEach(() => {
  vi.restoreAllMocks();
  rects.clear();
});

function renderJobb() {
  render(
    <JobbHeroFilters
      taxonomy={taxonomy}
      initialOccupationGroup={[]}
      initialRegion={[]}
      initialMunicipality={[]}
      initialRemote={false}
      initialEmploymentType={[]}
      initialWorktimeExtent={[]}
      initialMatchGrades={[]}
      initialMatchningOff={false}
      initialIncludeRelated={false}
      initialHideApplied={false}
      initialOnlyMatched={false}
      hasStatedDesiredOccupation
      hasSeeker={false}
      q=""
      employer={[]}
      sortBy="PublishedAtDesc"
    />,
  );
}

async function openAt(trigger: HTMLElement, rect: { left: number; bottom: number }) {
  rects.set(trigger, rect);
  await userEvent.setup().click(trigger);
  return screen.getByRole("dialog");
}

describe("the /jobb filter popovers stay inside the viewport (#2212)", () => {
  it("Yrke at 1280x720 moves left so its right edge keeps 16px", async () => {
    stubLayout(1280, 720);
    renderJobb();
    const dialog = await openAt(screen.getByRole("button", { name: /^Yrke/ }), { left: 745.078125, bottom: 387.421875 });

    expect(dialog.style.left).toBe("684px");
    expect(dialog.style.width).toBe("580px");
  });

  it("Ort at 1280x720 already fits and stays at its pill", async () => {
    stubLayout(1280, 720);
    renderJobb();
    const dialog = await openAt(screen.getByRole("button", { name: /^Ort/ }), { left: 664, bottom: 387.421875 });

    expect(dialog.style.left).toBe("664px");
  });

  it("Matchning at 1024x768", async () => {
    stubLayout(1024, 768);
    renderJobb();
    const dialog = await openAt(screen.getByRole("button", { name: /^Matchning/ }), { left: 796.46875, bottom: 455.5625 });

    expect(dialog.style.left).toBe("688px");
    expect(dialog.style.width).toBe("320px");
  });

  it("Filter at 800x600", async () => {
    stubLayout(800, 600);
    renderJobb();
    const dialog = await openAt(screen.getByRole("button", { name: /^Filter/ }), { left: 591.921875, bottom: 475.703125 });

    expect(dialog.style.left).toBe("464px");
  });
});

describe("the /jobb hero chips' panels are placed like the filter popovers (#2212)", () => {
  it("Senaste sökningar at 1280x720 opens 8px under its chip, in document coordinates", async () => {
    stubLayout(1280, 720);
    render(
      <HeroChip
        label="Senaste sökningar"
        icon={<Clock size={14} aria-hidden="true" />}
        count={1}
        items={[{ id: "a1", label: "projektledare" }]}
        getKey={(it) => it.id}
        getHref={(it) => `/jobb?q=${it.label}`}
        getLabel={(it) => it.label}
        emptyText="Inga senaste sökningar än."
      />,
    );
    const dialog = await openAt(screen.getByRole("button", { name: /Senaste sökningar/ }), { left: 664, bottom: 195 });

    expect(dialog.style.left).toBe("664px");
    expect(dialog.style.top).toBe("203px");
    expect(dialog.style.width).toBe("320px");
  });
});

describe("the /foretag/sok filter popovers stay inside the viewport (#2212)", () => {
  it("Ort at 768x1024", async () => {
    stubLayout(768, 1024);
    render(<ForetagSokSearchbar reference={reference} referenceOk namn="" sni={[]} kommun={[]} />);
    const dialog = await openAt(screen.getByRole("button", { name: "Välj ort eller län" }), { left: 376, bottom: 669.984375 });

    expect(dialog.style.left).toBe("172px");
  });

  it("Bransch at 1280x720 takes its 560px and stays at its trigger", async () => {
    stubLayout(1280, 720);
    render(<ForetagSokSearchbar reference={reference} referenceOk namn="" sni={[]} kommun={[]} />);
    const dialog = await openAt(screen.getByRole("button", { name: /Välj bransch/ }), { left: 72, bottom: 669.984375 });

    expect(dialog.style.width).toBe("560px");
    expect(dialog.style.left).toBe("72px");
    expect(within(dialog).getByText("Välj bransch")).toBeInTheDocument();
  });
});
