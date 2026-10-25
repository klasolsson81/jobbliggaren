import { describe, expect, it, vi } from "vitest";
import { useRef } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import type { PanelPlacement } from "@/lib/popover/placement";
import { useCloseOnFocusOut } from "./use-close-on-focus-out";

function Harness({
  open,
  onClose,
  placement = "anchored",
}: {
  open: boolean;
  onClose: () => void;
  placement?: PanelPlacement["placement"] | null;
}) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  useCloseOnFocusOut(open, panelRef, triggerRef, onClose, placement);
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
const pill = () => screen.getByRole("button", { name: "Pill" });

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
    leave(pill());
    leave(screen.getByRole("button", { name: "In a dialog" }));

    expect(onClose).not.toHaveBeenCalled();
  });

  it("stays open when focus moves to the trigger before the panel is placed", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} placement={null} />);

    leave(pill());

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

describe("useCloseOnFocusOut in a sheet, which covers its trigger (#2212)", () => {
  it("closes when keyboard focus moves to the trigger", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} placement="sheet" />);

    leave(pill());

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("leaves a press on the trigger to the trigger's own toggle", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} placement="sheet" />);

    fireEvent.mouseDown(pill());
    leave(pill());

    expect(onClose).not.toHaveBeenCalled();
  });

  it("closes again for the keyboard once a key follows that press", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} placement="sheet" />);

    fireEvent.mouseDown(pill());
    fireEvent.keyDown(document.body, { key: "Tab", shiftKey: true });
    leave(pill());

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("closes for the keyboard after a press that began elsewhere", () => {
    const onClose = vi.fn();
    render(<Harness open onClose={onClose} placement="sheet" />);

    fireEvent.mouseDown(screen.getByRole("button", { name: "First" }));
    leave(pill());

    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
