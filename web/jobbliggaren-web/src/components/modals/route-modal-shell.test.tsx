import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouteModalShell } from "./route-modal-shell";

const back = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ back }),
}));

describe("RouteModalShell", () => {
  beforeEach(() => {
    back.mockReset();
  });

  it("renderar role=dialog, aria-modal och aria-labelledby kopplat till titeln", () => {
    render(
      <RouteModalShell title="Importera CV">
        <div className="jp-modal__body">innehåll</div>
      </RouteModalShell>
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    const labelledby = dialog.getAttribute("aria-labelledby");
    expect(labelledby).toBeTruthy();
    expect(document.getElementById(labelledby!)).toHaveTextContent(
      "Importera CV"
    );
  });

  it("kopplar aria-describedby till en renderad beskrivning när description ges", () => {
    render(
      <RouteModalShell title="Importera CV" description="Ladda upp ditt CV.">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    const dialog = screen.getByRole("dialog");
    const describedby = dialog.getAttribute("aria-describedby");
    expect(describedby).toBeTruthy();
    // Referensen får aldrig dangla — elementet måste finnas i DOM.
    expect(document.getElementById(describedby!)).toHaveTextContent(
      "Ladda upp ditt CV."
    );
  });

  it("sätter INGEN aria-describedby när description saknas (ingen danglande referens)", () => {
    render(
      <RouteModalShell title="Nytt CV">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    expect(screen.getByRole("dialog")).not.toHaveAttribute(
      "aria-describedby"
    );
  });

  it("renderar valfri subtitle under titeln", () => {
    render(
      <RouteModalShell title="Nytt CV" subtitle="Steg 1 av 1">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    expect(screen.getByText("Steg 1 av 1")).toBeInTheDocument();
  });

  it("ESC stänger modalen (router.back)", async () => {
    const user = userEvent.setup();
    render(
      <RouteModalShell title="T">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    await user.keyboard("{Escape}");
    expect(back).toHaveBeenCalledTimes(1);
  });

  it("klick på scrim stänger; klick i panelen gör det inte", async () => {
    const user = userEvent.setup();
    render(
      <RouteModalShell title="T">
        <div className="jp-modal__body">panelinnehåll</div>
      </RouteModalShell>
    );
    await user.click(screen.getByText("panelinnehåll"));
    expect(back).not.toHaveBeenCalled();

    await user.click(screen.getByRole("presentation"));
    expect(back).toHaveBeenCalledTimes(1);
  });

  it("Stäng-knappen finns, har fokus vid öppning och stänger", async () => {
    const user = userEvent.setup();
    render(
      <RouteModalShell title="T">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    const closeBtn = screen.getByRole("button", { name: "Stäng dialogrutan" });
    expect(closeBtn).toHaveFocus();
    await user.click(closeBtn);
    expect(back).toHaveBeenCalledTimes(1);
  });

  it("låser body-scroll medan modalen är öppen och återställer vid unmount", () => {
    const { unmount } = render(
      <RouteModalShell title="T">
        <div className="jp-modal__body">x</div>
      </RouteModalShell>
    );
    expect(document.body.style.overflow).toBe("hidden");
    unmount();
    expect(document.body.style.overflow).not.toBe("hidden");
  });
});
