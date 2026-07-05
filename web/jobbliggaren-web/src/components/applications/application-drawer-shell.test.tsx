import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ApplicationDrawerShell } from "./application-drawer-shell";
import { setDrawerAnchor, resetDrawerAnchor } from "./drawer-anchor";
import { clampDrawerTop } from "@/lib/applications/drawer-position";

const backMock = vi.fn();
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({
      back: backMock,
      push: vi.fn(),
      replace: vi.fn(),
      forward: vi.fn(),
      prefetch: vi.fn(),
      refresh: vi.fn(),
    }),
  };
});

function renderShell(children = <button type="button">innehåll</button>) {
  return render(
    <ApplicationDrawerShell
      title="Backend-utvecklare"
      subtitle="Volvo · #aaaaaaaa"
    >
      {children}
    </ApplicationDrawerShell>,
  );
}

describe("ApplicationDrawerShell", () => {
  beforeEach(() => {
    backMock.mockClear();
    resetDrawerAnchor();
  });

  it("renders a labelled modal dialog with the aria-describedby contract", () => {
    renderShell();
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    // The body owns id="jp-modal-desc"; the shell must reference it (or it dangles).
    expect(dialog).toHaveAttribute("aria-describedby", "jp-modal-desc");
    // aria-labelledby → the <h2> title = the dialog's accessible name.
    expect(
      screen.getByRole("dialog", { name: "Backend-utvecklare" }),
    ).toBeInTheDocument();
  });

  it("focuses the close button on open", () => {
    renderShell();
    const closeBtn = screen.getByRole("button", { name: /stäng/i });
    expect(document.activeElement).toBe(closeBtn);
  });

  it("closes (router.back) on scrim click but NOT on panel click", () => {
    const { container } = renderShell();
    // Click inside the panel → stopPropagation → does not close.
    fireEvent.click(screen.getByRole("dialog"));
    expect(backMock).not.toHaveBeenCalled();
    // Click the scrim → close.
    fireEvent.click(container.querySelector(".jp-appdrawer-scrim")!);
    expect(backMock).toHaveBeenCalledTimes(1);
  });

  it("positions the panel near the click anchor (clampDrawerTop)", () => {
    setDrawerAnchor(500, null);
    const { container } = renderShell();
    const panel = container.querySelector<HTMLElement>(".jp-appdrawer")!;
    const expected = clampDrawerTop(500, window.innerHeight, { gutter: 16 });
    expect(panel.style.top).toBe(`${expected}px`);
  });

  it("returns focus to the triggering element on close (WCAG 2.4.3)", () => {
    const trigger = document.createElement("button");
    trigger.textContent = "row";
    document.body.appendChild(trigger);
    trigger.focus();
    setDrawerAnchor(300, trigger);

    const { unmount } = renderShell();
    // On open the shell moved focus off the trigger (to its close button).
    expect(document.activeElement).not.toBe(trigger);

    unmount();
    // On close/unmount, focus returns to the trigger (router.back does not
    // restore it for a drawer — the shell does it manually).
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });
});
