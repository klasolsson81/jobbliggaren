import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CvBlockReason } from "./cv-block-reason";
import { autoPromoteBlockReasonSchema } from "@/lib/dto/parsed-resume";

// CvBlockReason (#1060) — the answer to "why is my CV not saved?", on the page the pending
// card already routes to. Before this component the reason could not be learned at all without
// uploading the file again, which is the issue's third sub-requirement.
//
// The load-bearing properties: every reason gets its OWN copy, the copy names a CONCRETE next
// action (design-copy skill: konstatering + konkret nästa steg), the copy is TRUE of the gate
// it renders, and null is a scoped message rather than either silence or a certification.
describe("CvBlockReason", () => {
  it("names the personnummer as the cause and gives the action", () => {
    render(<CvBlockReason reason="PersonnummerPresent" />);

    expect(
      screen.getByRole("heading", { name: "Därför är filen inte sparad som CV" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/innehåller ett personnummer/i)).toBeInTheDocument();
    expect(screen.getByText(/ladda upp den igen/i)).toBeInTheDocument();
  });

  it("sends the ACCOUNT-NAME case to Inställningar, and says the file is clean", () => {
    // The design/security Blocker. The file has nothing in it on this path, so
    // PersonnummerWarning renders nothing and ParseSummary shows zero findings — telling the
    // user to remove a number from her file is advice that cannot work, and a loop with no
    // exit. The token is separate for exactly this reason (CTO-bind D2), and the control has
    // to be next to the instruction (ADR 0047), not three screens away.
    render(<CvBlockReason reason="PersonnummerInAccountName" />);

    expect(screen.getByText(/Namnet på ditt konto innehåller ett personnummer/i)).toBeInTheDocument();
    expect(screen.getByText(/Filen är däremot ren/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Inställningar/ })).toHaveAttribute(
      "href",
      "/installningar",
    );
    // It must NOT tell her to edit the file.
    expect(screen.queryByText(/Ta bort det ur filen/i)).not.toBeInTheDocument();
  });

  it("explains a failed extraction as an ACTION, leaving the statement to ParseSummary", () => {
    // ParseSummary renders `parse.overallFailed` on this same page, and the two must reconcile
    // rather than contradict (ADR 0047). Until #1373 both ended on "fylla i uppgifterna för
    // hand" — an instruction that pointed at /cv/ny, which has 404'd since #1061. Both now end
    // on the one path the MVP actually has: correct the file and upload it again.
    render(<CvBlockReason reason="ParseNotConfident" />);

    expect(screen.getByText(/text som går att markera/i)).toBeInTheDocument();
    // Covers the password-protected case, which "save it as a PDF" alone does not.
    expect(screen.getByText(/lösenordsskyddat/i)).toBeInTheDocument();
    expect(screen.getByText(/laddar upp igen/i)).toBeInTheDocument();
    // The dead promise stays dead: this route is paused, so no surface may offer it.
    expect(
      screen.queryByText(/fylla i uppgifterna för hand/i),
    ).not.toBeInTheDocument();
  });

  it("describes incomplete content across the whole validation set, not two branches of it", () => {
    // The buildability check spans the whole content, not the experience labels alone.
    // "arbetsgivare eller titel" sent a user whose EDUCATION entry lacked an institution to look
    // in the wrong place. (No branch count here: it was "20+", which #1060 D3(β-2) made a claim
    // about the wrong subject when thirteen arms moved to ResumeEntryBuildability — and the
    // sentence never needed a quantifier to carry its point.)
    render(<CvBlockReason reason="IncompleteContent" />);

    expect(screen.getByText(/anställning har arbetsgivare och titel/i)).toBeInTheDocument();
    expect(screen.getByText(/utbildning har lärosäte och examen/i)).toBeInTheDocument();
  });

  it("gives every reason its own body, and covers the whole locked set", () => {
    // The regression this guards is the cheap one: wiring every reason to the same string and
    // calling the issue done. Driven off the schema's own option list, so a member added to the
    // backend without copy fails here rather than rendering a missing-translation error.
    const bodies = autoPromoteBlockReasonSchema.options.map((reason) => {
      const { container, unmount } = render(<CvBlockReason reason={reason} />);
      const text = container.textContent ?? "";
      unmount();
      return text;
    });

    expect(bodies).toHaveLength(4);
    expect(new Set(bodies).size).toBe(4);
  });

  it("scopes the null verdict to the FILE and never certifies a save", () => {
    // CTO-bind D1. The gate's label channel reads the upload form's CV-name field, which the
    // read path does not have, so it evaluates the generated default: a personnummer typed
    // there is NOT ASSESSED here. The earlier copy said "Klar att sparas" and "uppfyller kraven
    // för att sparas som CV" — a claim about a submission that has not happened, and false for
    // exactly the user whose name carried the number (CLAUDE.md §5).
    render(<CvBlockReason reason={null} />);

    expect(
      screen.getByRole("heading", { name: "Inget i filen hindrar den" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Vi hittar inget i filen som stoppar den/i)).toBeInTheDocument();
    // The unassessed channel is disclosed, not silently omitted.
    expect(
      screen.getByText(/namn du skriver själv kontrolleras först vid uppladdningen/i),
    ).toBeInTheDocument();
    // And the retired certifications must not come back.
    expect(screen.queryByText(/Klar att sparas/)).not.toBeInTheDocument();
    // "Inget hittat i filen" was my first kicker and it was also wrong: it is
    // parse.overallFailed's own failure phrasing ("Vi kunde inte läsa någon användbar text
    // ur filen"), rendered on the same page, inside a GREEN pill (design-reviewer round 2).
    expect(screen.queryByText(/Inget hittat i filen/)).not.toBeInTheDocument();
    expect(screen.getByText("Inga hinder i filen")).toBeInTheDocument();
    expect(screen.queryByText(/uppfyller kraven/)).not.toBeInTheDocument();
    expect(screen.queryByText(/så sparas den direkt/)).not.toBeInTheDocument();
  });

  it("gives the cleared state a control, and tones the card to match its own verdict", () => {
    // A green pill inside a card whose left accent is hardcoded to --jp-warning is the
    // container contradicting the verdict, on a page whose whole purpose in this PR is to stop
    // giving two conflicting answers. And an instruction whose control is five sections down
    // the page is the ADR 0047 class the Blocker was.
    const { container } = render(<CvBlockReason reason={null} />);

    expect(container.querySelector(".jp-cvaction--ok")).not.toBeNull();
    expect(container.querySelector(".jp-pill--success")).not.toBeNull();
    expect(screen.getByRole("link", { name: /Ladda upp på nytt/ })).toHaveAttribute(
      "href",
      "/cv/importera",
    );
  });

  it("keeps the warning tone and the action kicker while something IS blocking", () => {
    const { container } = render(<CvBlockReason reason="IncompleteContent" />);

    expect(container.querySelector(".jp-cvaction--ok")).toBeNull();
    expect(container.querySelector(".jp-pill--warning")).not.toBeNull();
    expect(screen.getByText("Kräver åtgärd")).toBeInTheDocument();
  });

  it("is a landmark named by its own heading, in both states", () => {
    const { unmount } = render(<CvBlockReason reason="IncompleteContent" />);
    expect(
      screen.getByRole("region", { name: "Därför är filen inte sparad som CV" }),
    ).toBeInTheDocument();
    unmount();

    render(<CvBlockReason reason={null} />);
    expect(
      screen.getByRole("region", { name: "Inget i filen hindrar den" }),
    ).toBeInTheDocument();
  });

  it("never echoes evidence — the token names the gate, and the copy never quotes the file", () => {
    // The DTO's egress contract, asserted at the surface that renders it: nothing here may
    // carry a personnummer, a file name or parsed content (CLAUDE.md §5, highest priority).
    for (const reason of autoPromoteBlockReasonSchema.options) {
      const { container, unmount } = render(<CvBlockReason reason={reason} />);
      expect(container.textContent).not.toMatch(/\d{6}[-\s]?\d{4}/);
      expect(container.textContent).not.toContain(reason);
      unmount();
    }
  });
});
