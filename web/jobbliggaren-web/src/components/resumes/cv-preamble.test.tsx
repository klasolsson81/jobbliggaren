import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CvPreamble } from "./cv-preamble";

// CvPreamble — the neutral, display-only affordance for the unclassified preamble (#844,
// ADR 0109). The load-bearing property is NEUTRALITY: it shows the text and says what it is
// (text above the first heading, not classified) — never a badge, never a claim it is a profile,
// never a grade. And it is caller-gated: nothing renders when there is no preamble.
describe("CvPreamble", () => {
  it("renders the neutral affordance with the verbatim text when a preamble is present", () => {
    const preamble = "Erfaren undersköterska med tio år i yrket.\nSöker nya utmaningar.";

    render(<CvPreamble preamble={preamble} />);

    // Neutral, positional heading — never "Din profil" / "Hittad sammanfattning".
    expect(
      screen.getByRole("heading", { name: "Text ovanför första rubriken" }),
    ).toBeInTheDocument();

    // One landmark, named by its heading (OccupationProposals pattern). The verbatim text is
    // shown back inside it, line breaks preserved.
    const region = screen.getByRole("region", {
      name: "Text ovanför första rubriken",
    });
    expect(region.textContent).toContain("Erfaren undersköterska med tio år i yrket.");
    expect(region.textContent).toContain("Söker nya utmaningar.");

    // The honest "why + how to adopt" line (points at the re-upload path, not an in-app rewrite).
    expect(screen.getByText(/ladda upp filen igen/i)).toBeInTheDocument();
  });

  it("makes no classification claim — no badge, no 'found in file' framing", () => {
    render(<CvPreamble preamble="Något ovanför rubriken." />);

    // ADR 0109's core: the engine describes, it does not classify. None of these may appear.
    expect(screen.queryByText(/hittad i filen/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/din profiltext/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/din sammanfattning/i)).not.toBeInTheDocument();
  });

  it("renders nothing when there is no preamble (null)", () => {
    const { container } = render(<CvPreamble preamble={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when the preamble is only whitespace", () => {
    // Expression form so the escapes are real whitespace (a JSX string attribute would pass a
    // literal backslash-n). trim() collapses this to "" → the affordance stays absent.
    const { container } = render(<CvPreamble preamble={"   \n\t  "} />);

    expect(container).toBeEmptyDOMElement();
  });
});
