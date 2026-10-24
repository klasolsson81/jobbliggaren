import { describe, expect, it, vi } from "vitest";
import { useRef } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { useCloseOnFocusOut } from "./use-close-on-focus-out";

function Harness({ open, onClose }: { open: boolean; onClose: () => void }) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  useCloseOnFocusOut(open, panelRef, triggerRef, onClose);
  return (
    <>
      <button ref={triggerRef}>Pill</button>
      <div ref={panelRef} role="dialog">
        <button>First</button>
        <button>Last</button>
      </div>
      <button>Outside</button>
      {/* Where the ui/dialog primitive portals its content: outside the panel's DOM. */}
      <div data-slot="dialog-content">
        <button>In a dialog</button>
      </div>
    </>
  );
}

const leave = (to: HTMLElement | null) =>
  fireEvent.focusOut(screen.getByRole("button", { name: "Last" }), { relatedTarget: to });

describe("useCloseOnFocusOut (#2212)", () => {
  it("closes once when focus moves to an element outside the panel, and moves no focus", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} />);
    const outside = screen.getByRole("button", { name: "Outside" });
    outside.focus();

    leave(outside);

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(document.activeElement).toBe(outside);
  });

  it("stays open when focus moves inside the panel, back to the trigger, or into a portaled dialog", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} />);

    leave(screen.getByRole("button", { name: "First" }));
    leave(screen.getByRole("button", { name: "Pill" }));
    leave(screen.getByRole("button", { name: "In a dialog" }));

    expect(onClose).not.toHaveBeenCalled();
  });

  it("stays open when focus leaves for no element", () => {
    // What the browser reports when the focused node re-renders away or the window loses focus.
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} />);

    leave(null);

    expect(onClose).not.toHaveBeenCalled();
  });

  it("does not listen while closed", () => {
    const onClose = vi.fn();
    render(<Harness open={false} onClose={onClose} />);

    leave(screen.getByRole("button", { name: "Outside" }));

    expect(onClose).not.toHaveBeenCalled();
  });
});
