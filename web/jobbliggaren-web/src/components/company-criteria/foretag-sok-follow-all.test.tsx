import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ForetagSokFollowAll } from "./foretag-sok-follow-all";

const createCriterionActionMock = vi.fn();

vi.mock("@/lib/actions/company-criteria", () => ({
  createCriterionAction: (...args: unknown[]) => createCriterionActionMock(...args),
}));

beforeEach(() => {
  createCriterionActionMock.mockReset();
});

const READY = { namn: "", sni: ["62010"], kommun: ["0180"] };

describe("ForetagSokFollowAll", () => {
  it("enables the CTA and shows the ready explainer for a criterion-shaped filter", () => {
    render(<ForetagSokFollowAll {...READY} />);
    const button = screen.getByRole("button", { name: "Bevaka alla träffar" });
    expect(button).not.toHaveAttribute("aria-disabled");
    expect(
      screen.getByText(/Spara branscherna och kommunerna som en bevakning/i),
    ).toBeInTheDocument();
  });

  it("disables the CTA with the empty-filter explainer on a browse-all filter", () => {
    render(<ForetagSokFollowAll namn="" sni={[]} kommun={[]} />);
    expect(screen.getByRole("button", { name: "Bevaka alla träffar" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(
      screen.getByText(
        "Välj minst en bransch och en kommun för att spara sökningen som bevakning.",
      ),
    ).toBeInTheDocument();
  });

  it("disables the CTA with the name-term explainer when a name is present (silent-drift guard)", () => {
    render(<ForetagSokFollowAll namn="Volvo" sni={["62010"]} kommun={["0180"]} />);
    expect(screen.getByRole("button", { name: "Bevaka alla träffar" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
    expect(
      screen.getByText(/En sökning på företagsnamn går inte att spara som bevakning/i),
    ).toBeInTheDocument();
  });

  it("shows the SNI-missing explainer for a kommun-only filter", () => {
    render(<ForetagSokFollowAll namn="" sni={[]} kommun={["0180"]} />);
    expect(
      screen.getByText("Lägg till minst en bransch för att spara sökningen som bevakning."),
    ).toBeInTheDocument();
  });

  it("shows the kommun-missing explainer for an SNI-only filter", () => {
    render(<ForetagSokFollowAll namn="" sni={["62010"]} kommun={[]} />);
    expect(
      screen.getByText("Lägg till minst en kommun för att spara sökningen som bevakning."),
    ).toBeInTheDocument();
  });

  it("saves the active axes and shows a confirmation linking to the hub on success", async () => {
    createCriterionActionMock.mockResolvedValue({ success: true });
    render(<ForetagSokFollowAll namn="" sni={["62010", "62020"]} kommun={["0180"]} />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Bevaka alla träffar" }));

    expect(createCriterionActionMock).toHaveBeenCalledWith({
      sniCodes: ["62010", "62020"],
      municipalityCodes: ["0180"],
    });
    expect(
      await screen.findByText("Sökningen sparades som bevakning."),
    ).toBeInTheDocument();
    // Focus moved to the (always-mounted) confirmation region — not dropped to <body> when the
    // button unmounts (WCAG 2.4.3, design-reviewer Major).
    expect(document.activeElement).toHaveTextContent("Sökningen sparades som bevakning.");
    // The button is replaced by the confirmation (no accidental duplicate on a second click).
    expect(
      screen.queryByRole("button", { name: "Bevaka alla träffar" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Visa dina bevakningar" })).toHaveAttribute(
      "href",
      "/foretag",
    );
  });

  it("surfaces the action error and keeps the button on failure", async () => {
    createCriterionActionMock.mockResolvedValue({
      success: false,
      error: "Du kan ha högst 20 bevakningar. Ta bort en bevakning för att skapa en ny.",
    });
    render(<ForetagSokFollowAll {...READY} />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Bevaka alla träffar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/högst 20 bevakningar/i);
    expect(screen.getByRole("button", { name: "Bevaka alla träffar" })).toBeInTheDocument();
  });

  it("does not call the action when the CTA is blocked (the guard is the real barrier)", async () => {
    render(<ForetagSokFollowAll namn="Volvo" sni={["62010"]} kommun={["0180"]} />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Bevaka alla träffar" }));

    expect(createCriterionActionMock).not.toHaveBeenCalled();
  });
});
