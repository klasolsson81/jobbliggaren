import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { JobAdMatchSection } from "./job-ad-match-section";
import type {
  JobAdMatchDetail,
  MatchConceptGroup,
  MatchSkillDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

// #2247 — a skill the ad asks for and the profile lacks is one "Lägg till" button. The add goes to
// the backend through the server action; the chip only ever moves because the refreshed match says
// so, never on the client.

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

const addPreferredSkillsAction = vi.fn();
vi.mock("@/lib/actions/match-preferences", () => ({
  addPreferredSkillsAction: (ids: ReadonlyArray<string>) => addPreferredSkillsAction(ids),
}));

function group(display: string, ...members: Array<[string, string]>): MatchConceptGroup {
  return { display, members: members.map(([conceptId, member]) => ({ conceptId, display: member })) };
}

function skill(
  verdict: MatchVerdict,
  matched: MatchConceptGroup[] = [],
  missing: MatchConceptGroup[] = [],
  withIdentity = true,
): MatchSkillDimensionDetail {
  const displays = (groups: MatchConceptGroup[]) => groups.flatMap((g) => g.members.map((m) => m.display));
  return {
    verdict,
    matched: displays(matched),
    missing: displays(missing),
    conceptEvidence: withIdentity ? { matched, missing } : null,
  };
}

function detail(over: Partial<JobAdMatchDetail> = {}): JobAdMatchDetail {
  return {
    grade: "Top",
    ssykOverlap: { verdict: "Match", matched: [{ conceptId: "DJh5_yyF_hEM", label: "Systemutvecklare" }], missing: [], cause: null },
    titleSimilarity: { verdict: "NotAssessed", matched: [], missing: [] },
    regionFit: { verdict: "Match", matched: [{ conceptId: "PVZL_BQT_XtL", label: "Göteborg" }], missing: [], cause: null },
    employmentFit: { verdict: "Match", matchedConceptIds: ["kpPX_CNN_gDU"], missingConceptIds: [], cause: null },
    skillOverlap: skill(
      "Partial",
      [group("C#", ["rPUY_2rX_2yN", "C#"])],
      [group("TypeScript", ["ts_1", "TypeScript"], ["ts_2", "TypeScript, programmeringsspråk"])],
    ),
    mustHaveCoverage: skill("Vacuous"),
    niceToHaveCoverage: skill("Vacuous"),
    ...over,
  };
}

beforeEach(() => {
  refresh.mockReset();
  addPreferredSkillsAction.mockReset();
});

describe("JobAdMatchSection — adding a missing skill to the profile (#2247)", () => {
  it("offers a missing skill as one button named by its visible text, with hover text saying where it goes", () => {
    render(<JobAdMatchSection match={detail()} />);

    const button = screen.getByRole("button", { name: "Lägg till TypeScript" });
    expect(button).toHaveAttribute("title", "Lägg till TypeScript bland kompetenserna i din profil");
  });

  it("offers no button on a skill the profile already has", () => {
    render(<JobAdMatchSection match={detail()} />);

    expect(screen.queryByRole("button", { name: /C#/ })).not.toBeInTheDocument();
    expect(screen.getByText("C#")).toBeInTheDocument();
  });

  it("offers no button when the API sent no concept identity, so nothing could be stored", () => {
    const legacy = skill("Partial", [group("C#", ["rPUY_2rX_2yN", "C#"])], [group("TypeScript", ["ts_1", "TypeScript"])], false);
    render(
      <JobAdMatchSection
        match={detail({ skillOverlap: legacy, mustHaveCoverage: skill("Vacuous", [], [], false), niceToHaveCoverage: skill("Vacuous", [], [], false) })}
      />,
    );

    expect(screen.queryByRole("button", { name: /Lägg till/ })).not.toBeInTheDocument();
    expect(screen.getByText("TypeScript")).toBeInTheDocument();
  });

  it("offers a missing must-have requirement too", () => {
    render(
      <JobAdMatchSection
        match={detail({ mustHaveCoverage: skill("Partial", [group("C#", ["rPUY_2rX_2yN", "C#"])], [group("Kubernetes", ["k8s_1", "Kubernetes"])]) })}
      />,
    );

    // Met and missing share a requirement list, so the button still says which this one is (WCAG 1.3.1).
    expect(screen.getByRole("button", { name: "Lägg till Kubernetes" })).toHaveAccessibleDescription(
      "Saknas: Kubernetes",
    );
  });

  it("sends the chip's whole concept group, refreshes, and says the outcome where focus lands", async () => {
    addPreferredSkillsAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<JobAdMatchSection match={detail()} />);

    await user.click(screen.getByRole("button", { name: "Lägg till TypeScript" }));

    expect(addPreferredSkillsAction).toHaveBeenCalledWith(["ts_1", "ts_2"]);
    const status = await screen.findByRole("status");
    await waitFor(() => expect(status).toHaveTextContent("TypeScript finns nu bland kompetenserna i din profil."));
    expect(refresh).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(status).toHaveFocus());
  });

  it("can be pressed from the keyboard", async () => {
    addPreferredSkillsAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<JobAdMatchSection match={detail()} />);

    screen.getByRole("button", { name: "Lägg till TypeScript" }).focus();
    await user.keyboard("{Enter}");

    expect(addPreferredSkillsAction).toHaveBeenCalledWith(["ts_1", "ts_2"]);
  });

  it("moves nothing on a failure: no refresh, the chip stays, and the error is an alert", async () => {
    addPreferredSkillsAction.mockResolvedValue({ success: false, error: "Det gick inte att lägga till kompetensen. Försök igen." });
    const user = userEvent.setup();
    render(<JobAdMatchSection match={detail()} />);

    const button = screen.getByRole("button", { name: "Lägg till TypeScript" });
    await user.click(button);

    expect(await screen.findByRole("alert")).toHaveTextContent("Det gick inte att lägga till kompetensen. Försök igen.");
    expect(refresh).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Lägg till TypeScript" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("starts no second add while one is running", async () => {
    let resolve: (value: { success: true }) => void = () => {};
    addPreferredSkillsAction.mockReturnValue(new Promise((r) => (resolve = r)));
    const user = userEvent.setup();
    render(
      <JobAdMatchSection
        match={detail({
          skillOverlap: skill("Partial", [group("C#", ["rPUY_2rX_2yN", "C#"])], [
            group("TypeScript", ["ts_1", "TypeScript"]),
            group("Docker", ["dk_1", "Docker"]),
          ]),
        })}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Lägg till TypeScript" }));
    const pending = await screen.findByRole("button", { name: "Lägger till TypeScript" });
    expect(pending).toHaveAttribute("aria-busy", "true");
    const other = screen.getByRole("button", { name: "Lägg till Docker" });
    expect(other).toHaveAttribute("aria-disabled", "true");

    await user.click(other);
    expect(addPreferredSkillsAction).toHaveBeenCalledTimes(1);

    resolve({ success: true });
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  });

  it("keeps the add buttons inside the Kompetenser box", () => {
    render(<JobAdMatchSection match={detail()} />);

    const list = screen.getByRole("list", { name: "Kompetenser: Finns inte i din profil" });
    expect(within(list).getByRole("button", { name: "Lägg till TypeScript" })).toBeInTheDocument();
  });
});
