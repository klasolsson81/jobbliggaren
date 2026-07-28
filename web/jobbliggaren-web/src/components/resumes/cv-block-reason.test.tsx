import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CvBlockReason } from "./cv-block-reason";

// CvBlockReason (#1060) — the answer to "why is my CV not saved?", on the page the pending
// card already routes to. Before this component the reason could not be learned at all without
// uploading the file again, which is the issue's third sub-requirement.
//
// The load-bearing properties: every reason gets its OWN copy (a generic block would be the
// status quo the issue filed), the copy names a CONCRETE next action (design-copy skill:
// konstatering + konkret nästa steg), and null is a distinct message rather than silence.
describe("CvBlockReason", () => {
  it("names the personnummer as the cause and gives the action", () => {
    render(<CvBlockReason reason="PersonnummerPresent" />);

    expect(
      screen.getByRole("heading", { name: "Därför är filen inte sparad som CV" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/innehåller ett personnummer/i)).toBeInTheDocument();
    expect(screen.getByText(/ladda upp den igen/i)).toBeInTheDocument();
  });

  it("explains a failed extraction in terms of the FILE, not of an internal verdict", () => {
    // "ParseNotConfident" means nothing to a job seeker. The copy has to say what she can see
    // (an image or a scan) and what to do about it.
    render(<CvBlockReason reason="ParseNotConfident" />);

    expect(screen.getByText(/kunde inte läsa ut någon text/i)).toBeInTheDocument();
    expect(screen.getByText(/skannat dokument/i)).toBeInTheDocument();
    expect(screen.getByText(/markerbar text/i)).toBeInTheDocument();
  });

  it("names which fields are missing for incomplete content", () => {
    render(<CvBlockReason reason="IncompleteContent" />);

    expect(screen.getByText(/saknar arbetsgivare eller titel/i)).toBeInTheDocument();
  });

  it("gives the three reasons three DIFFERENT bodies", () => {
    // The regression this guards is the cheap one: wiring every reason to the same string and
    // calling the issue done. Reading the rendered text back is the only way to catch it.
    const bodies = (
      ["PersonnummerPresent", "ParseNotConfident", "IncompleteContent"] as const
    ).map((reason) => {
      const { container, unmount } = render(<CvBlockReason reason={reason} />);
      const text = container.textContent ?? "";
      unmount();
      return text;
    });

    expect(new Set(bodies).size).toBe(3);
  });

  it("reports 'nothing is blocking this any more' rather than falling silent, when the reason is null", () => {
    // A real population, not a hypothetical: PR B retired one gate and narrowed another, so
    // every artifact left pending under the old gates now evaluates to null and is still
    // sitting in the hub. Rendering nothing would leave exactly those users with a card that
    // says "needs action" and a review page that never says what.
    render(<CvBlockReason reason={null} />);

    expect(
      screen.getByRole("heading", { name: "Inget hindrar filen längre" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/ladda upp den igen så sparas den/i)).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Därför är filen inte sparad som CV" }),
    ).not.toBeInTheDocument();
  });

  it("is a landmark named by its own heading, in both states", () => {
    const { unmount } = render(<CvBlockReason reason="IncompleteContent" />);
    expect(
      screen.getByRole("region", { name: "Därför är filen inte sparad som CV" }),
    ).toBeInTheDocument();
    unmount();

    render(<CvBlockReason reason={null} />);
    expect(
      screen.getByRole("region", { name: "Inget hindrar filen längre" }),
    ).toBeInTheDocument();
  });

  it("never echoes evidence — the token names the gate, and the copy never quotes the file", () => {
    // The DTO's egress contract, asserted at the surface that renders it: nothing here may
    // carry a personnummer, a file name or parsed content (CLAUDE.md §5, highest priority).
    const { container } = render(<CvBlockReason reason="PersonnummerPresent" />);

    expect(container.textContent).not.toMatch(/\d{6}[-\s]?\d{4}/);
    // And it does not leak the internal token into user-facing copy either.
    expect(container.textContent).not.toContain("PersonnummerPresent");
  });
});
