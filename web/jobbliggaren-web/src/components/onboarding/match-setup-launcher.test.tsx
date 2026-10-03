import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ComponentProps } from "react";
import type { MatchSetupRailModal } from "@/components/settings/match-setup-rail-modal";
import { MatchSetupLauncher, type MatchSetupData } from "./match-setup-launcher";

type RailProps = ComponentProps<typeof MatchSetupRailModal>;
const railProps = vi.fn<(props: RailProps) => void>();
const replace = vi.fn();
const refresh = vi.fn();
const markSeen = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace, refresh }) }));
vi.mock("@/lib/onboarding/setup-welcome-actions", () => ({ markSetupWelcomeSeen: () => markSeen() }));
// Real save/Done behavior is pinned in match-setup-rail-modal.test.tsx. This stateful
// seam exposes an unmount: the receipt must survive a server revalidation of eligibility.
vi.mock("@/components/settings/match-setup-rail-modal", async () => {
  const { useState } = await import("react");
  return { MatchSetupRailModal: function Rail(props: RailProps) {
    railProps(props);
    const [saved, setSaved] = useState(false);
    return props.open ? <div>
      <p>{saved ? "Done receipt" : "Open rail"}</p>
      <button onClick={() => setSaved(true)}>Save</button>
      <button onClick={() => props.onOpenChange(false)}>Close</button>
    </div> : null;
  } };
});

const twinPair = { conceptId: "Sk1l_CsH_arp", label: "C#", memberConceptIds: ["Sk1l_CsH_arp", "Sk1l_CsH_af"] };
const data: MatchSetupData = {
  occupationFields: [], regions: [], employmentTypes: [],
  persistedOccupationGroups: [], persistedRegions: ["region_AB"],
  persistedMunicipalities: ["municipality_0180"], persistedRemote: true,
  persistedEmploymentTypes: ["permanent"], persistedSkills: twinPair.memberConceptIds,
  persistedSkillGroups: [twinPair], persistedOccupationExperience: [],
  importCvHref: "/cv/importera", resumeStep: 1,
};
function latest(): RailProps { return railProps.mock.lastCall![0]; }

beforeEach(() => { vi.clearAllMocks(); markSeen.mockResolvedValue(undefined); });

describe("MatchSetupLauncher", () => {
  it("resumes all saved selections at occupations, including resolved skill names", () => {
    render(<MatchSetupLauncher request="resume" data={data} />);
    expect(latest()).toMatchObject({ ...data, open: true, initialStep: 1 });
  });
  it.each([0, 1] as const)("automatic welcome always starts at Start with resumeStep %s", (resumeStep) => {
    render(<MatchSetupLauncher request="welcome" data={{ ...data, resumeStep }} />);
    expect(latest().initialStep).toBe(0);
  });
  it("an explicit empty-profile resume starts at Start", () => {
    render(<MatchSetupLauncher request="resume" data={{ ...data, resumeStep: 0 }} />);
    expect(latest().initialStep).toBe(0);
  });
  it("a later opening request works and dismissal clears query through the existing action", async () => {
    const user = userEvent.setup();
    const view = render(<MatchSetupLauncher request={null} data={null} />);
    expect(screen.queryByText("Open rail")).toBeNull();
    view.rerender(<MatchSetupLauncher request="resume" data={data} />);
    expect(screen.getByText("Open rail")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(markSeen).toHaveBeenCalledOnce();
    expect(replace).toHaveBeenCalledWith("/oversikt", { scroll: false });
    expect(refresh).toHaveBeenCalledOnce();
    view.rerender(<MatchSetupLauncher request={null} data={null} />);
    const newer = { ...data, persistedRemote: false, persistedSkills: ["new-saved-skill"] };
    view.rerender(<MatchSetupLauncher request="resume" data={newer} />);
    expect(latest()).toMatchObject({ open: true, persistedRemote: false, persistedSkills: ["new-saved-skill"] });
  });
  it("preserves the opening's data and mounted receipt when profile revalidation removes eligibility", async () => {
    const user = userEvent.setup();
    const view = render(<MatchSetupLauncher request="resume" data={data} />);
    await user.click(screen.getByRole("button", { name: "Save" }));
    view.rerender(<MatchSetupLauncher request={null} data={null} />);
    expect(screen.getByText("Done receipt")).toBeInTheDocument();
    expect(latest()).toMatchObject({ open: true, ...data });
    await user.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.queryByText("Done receipt")).toBeNull();
  });
  it("waits for taxonomy data and opens after a successful retry of the same request", () => {
    const view = render(<MatchSetupLauncher request="resume" data={null} />);
    expect(railProps).not.toHaveBeenCalled();
    view.rerender(<MatchSetupLauncher request="resume" data={data} />);
    expect(latest().open).toBe(true);
  });
  it.each(["opener", "continuation", "matching"] as const)("restores focus to %s using the current page", (target) => {
    const opener = document.createElement("button");
    const continuation = document.createElement("a");
    continuation.id = "oversikt-continue-setup";
    continuation.href = "/oversikt?matchsetup=1";
    const matching = document.createElement("h2");
    matching.id = "oversikt-card-matching";
    matching.tabIndex = -1;
    document.body.append(opener, continuation, matching);
    const view = render(<MatchSetupLauncher request="resume" data={data} />);
    opener.focus();
    act(() => latest().onOpenAutoFocus?.(new Event("open")));
    if (target !== "opener") opener.remove();
    if (target === "matching") document.getElementById("oversikt-continue-setup")!.remove();
    const closeEvent = new Event("close", { cancelable: true });
    act(() => latest().onCloseAutoFocus?.(closeEvent));
    expect(closeEvent.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(target === "opener" ? opener : document.getElementById(target === "continuation" ? "oversikt-continue-setup" : "oversikt-card-matching"));
    view.unmount();
    opener.remove(); continuation.remove(); matching.remove();
  });
});
