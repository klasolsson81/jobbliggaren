import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationModalShell } from "./application-modal-shell";

const back = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ back }),
}));

describe("ApplicationModalShell", () => {
  beforeEach(() => {
    back.mockReset();
  });

  it("renderar role=dialog, aria-modal och aria-labelledby kopplat till titeln", () => {
    render(
      <ApplicationModalShell title="Backend-utvecklare" subtitle="Volvo · #abc">
        <div className="jp-modal__body">innehåll</div>
      </ApplicationModalShell>
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    const labelledby = dialog.getAttribute("aria-labelledby");
    expect(labelledby).toBeTruthy();
    const heading = document.getElementById(labelledby!);
    expect(heading).toHaveTextContent("Backend-utvecklare");
  });

  it("har aria-describedby=jp-modal-desc och referens-id:t finns i DOM (F5 M1, F3-paritet)", () => {
    render(
      <ApplicationModalShell title="Backend-utvecklare" subtitle="Volvo · #abc">
        <div className="jp-modal__body">
          {/* ApplicationDetail status-blocket renderar id="jp-modal-desc"
              OVILLKORLIGT — speglat här minimalt så referensen aldrig dinglar. */}
          <div id="jp-modal-desc">Status</div>
        </div>
      </ApplicationModalShell>
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-describedby", "jp-modal-desc");
    expect(document.getElementById("jp-modal-desc")).not.toBeNull();
  });

  it("ESC stänger modalen (router.back)", async () => {
    const user = userEvent.setup();
    render(
      <ApplicationModalShell title="T" subtitle="S">
        <div className="jp-modal__body">x</div>
      </ApplicationModalShell>
    );
    await user.keyboard("{Escape}");
    expect(back).toHaveBeenCalledTimes(1);
  });

  it("klick på scrim stänger; klick i panelen gör det inte", async () => {
    const user = userEvent.setup();
    render(
      <ApplicationModalShell title="T" subtitle="S">
        <div className="jp-modal__body">panelinnehåll</div>
      </ApplicationModalShell>
    );
    await user.click(screen.getByText("panelinnehåll"));
    expect(back).not.toHaveBeenCalled();

    const scrim = screen.getByRole("presentation");
    await user.click(scrim);
    expect(back).toHaveBeenCalledTimes(1);
  });

  it("Stäng-knappen och fokus hamnar på stäng vid öppning", async () => {
    const user = userEvent.setup();
    render(
      <ApplicationModalShell title="T" subtitle="S">
        <div className="jp-modal__body">x</div>
      </ApplicationModalShell>
    );
    const closeIcon = screen.getByRole("button", {
      name: "Stäng dialogrutan",
    });
    expect(closeIcon).toHaveFocus();
    await user.click(closeIcon);
    expect(back).toHaveBeenCalledTimes(1);

    // Footer-knappen "Stäng" är en separat sekundär action (v3 jp-modal__foot).
    expect(
      screen.getByRole("button", { name: "Stäng" })
    ).toBeInTheDocument();
  });

  it("renderar valfri footer-action (Återta ansökan-slot)", () => {
    render(
      <ApplicationModalShell
        title="T"
        subtitle="S"
        footer={<button type="button">Återta ansökan</button>}
      >
        <div className="jp-modal__body">x</div>
      </ApplicationModalShell>
    );
    expect(
      screen.getByRole("button", { name: "Återta ansökan" })
    ).toBeInTheDocument();
  });
});
