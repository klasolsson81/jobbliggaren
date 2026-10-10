import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvPreamble } from "./cv-preamble";

// CvPreamble — the neutral, display-only notice for the unclassified preamble (#844, ADR 0109),
// folded away behind "Visa texten" since #2083. The load-bearing property is NEUTRALITY: it says
// what the text is (text above the first heading, not classified) and shows it back verbatim —
// never a badge, never a claim it is a profile, never a grade. And it is caller-gated: nothing
// renders when there is no preamble.
describe("CvPreamble", () => {
  const preamble = "Erfaren undersköterska med tio år i yrket.\nSöker nya utmaningar.";

  it("says what the text is and how to use it, in one notice", () => {
    render(<CvPreamble preamble={preamble} />);

    expect(screen.getByText("Text ovanför första rubriken.")).toBeInTheDocument();
    expect(screen.getByText(/Vi tolkar den inte som ett avsnitt\./)).toBeInTheDocument();
    // The honest path to adopt it is the re-upload, not an in-app rewrite.
    expect(screen.getByText(/ladda upp filen igen/i)).toBeInTheDocument();
  });

  it("folds the text away until the user asks for it, and folds it back", async () => {
    const user = userEvent.setup();
    render(<CvPreamble preamble={preamble} />);

    const toggle = screen.getByRole("button", { name: "Visa texten" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const panel = document.getElementById(toggle.getAttribute("aria-controls") ?? "");
    expect(panel).not.toBeNull();
    expect(panel).not.toBeVisible();

    await user.click(toggle);
    expect(screen.getByRole("button", { name: "Dölj texten" })).toHaveAttribute(
      "aria-expanded",
      "true",
    );
    expect(panel).toBeVisible();
    // Verbatim, line break included (the blockquote keeps it with pre-wrap).
    expect(panel?.querySelector("blockquote")?.textContent).toBe(preamble);

    await user.click(screen.getByRole("button", { name: "Dölj texten" }));
    expect(panel).not.toBeVisible();
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
    // literal backslash-n). trim() collapses this to "" → the notice stays absent.
    const { container } = render(<CvPreamble preamble={"   \n\t  "} />);

    expect(container).toBeEmptyDOMElement();
  });
});
