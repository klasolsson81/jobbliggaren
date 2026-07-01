import { describe, it, expect, vi } from "vitest";
import { render, fireEvent } from "@testing-library/react";
import { useRef } from "react";
import { useDismissable } from "./use-dismissable";

// #419 pt7 — pinnar useDismissable-selektorn: en Radix MODAL Dialog (InfoDialog "?")
// öppnad från INUTI en dismissable popover får INTE stänga popovern. Dialog-ytorna
// bär `data-slot="dialog-content"`/`"dialog-overlay"` (satta av ui/dialog.tsx) och ska
// ignoreras av utanför-klick-handlern, precis som Popper-ytor. Ett äkta utanför-klick
// stänger fortfarande.

function Harness({
  onClose,
  withDialog = true,
}: {
  onClose: () => void;
  withDialog?: boolean;
}) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const ref = useDismissable<HTMLDivElement>(true, onClose, triggerRef);
  return (
    <div>
      <button ref={triggerRef}>trigger</button>
      <div ref={ref} data-testid="panel">
        <span data-testid="inside">inside</span>
      </div>
      <div data-testid="outside">outside</div>
      {/* Efterliknar en portalerad Radix Dialog (content + overlay) i document.body. */}
      {withDialog ? (
        <>
          <div data-slot="dialog-content" data-testid="dialog-content">
            <span data-testid="dialog-child">help text</span>
            {/* Fokuserbart element inuti dialogen — en modal Radix Dialog trap:ar
                fokus hit, vilket är signalen Escape-guarden scopar mot. */}
            <button data-testid="dialog-button">stäng</button>
          </div>
          <div data-slot="dialog-overlay" data-testid="dialog-overlay">
            overlay
          </div>
        </>
      ) : null}
    </div>
  );
}

describe("useDismissable — modal-in-popover ignore (#419 pt7)", () => {
  it("mousedown utanför panelen stänger (oförändrad grundfunktion)", () => {
    const onClose = vi.fn();
    const { getByTestId } = render(<Harness onClose={onClose} />);
    fireEvent.mouseDown(getByTestId("outside"));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("mousedown inuti en Radix dialog-content stänger INTE popovern", () => {
    const onClose = vi.fn();
    const { getByTestId } = render(<Harness onClose={onClose} />);
    fireEvent.mouseDown(getByTestId("dialog-child"));
    expect(onClose).not.toHaveBeenCalled();
  });

  it("mousedown på dialog-overlay stänger INTE popovern", () => {
    const onClose = vi.fn();
    const { getByTestId } = render(<Harness onClose={onClose} />);
    fireEvent.mouseDown(getByTestId("dialog-overlay"));
    expect(onClose).not.toHaveBeenCalled();
  });

  it("mousedown inuti panelen stänger inte", () => {
    const onClose = vi.fn();
    const { getByTestId } = render(<Harness onClose={onClose} />);
    fireEvent.mouseDown(getByTestId("inside"));
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Escape stänger popovern (ingen dialog öppen)", () => {
    const onClose = vi.fn();
    render(<Harness onClose={onClose} withDialog={false} />);
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("Escape stänger INTE popovern när FOKUS ligger inuti dialogen (dialogen äger Escape)", () => {
    // #419 pt7 — annars stänger ett enda Escape BÅDE dialogen (Radix) OCH popovern
    // (useDismissable) samtidigt. Guarden scopar mot document.activeElement: en modal
    // dialog trap:ar fokus hit, så useDismissable avstår.
    const onClose = vi.fn();
    const { getByTestId } = render(<Harness onClose={onClose} withDialog />);
    getByTestId("dialog-button").focus();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).not.toHaveBeenCalled();
  });

  it("Escape stänger popovern ÄVEN om en orelaterad dialog finns men fokus är UTANFÖR den", () => {
    // code-reviewer Major 1 — guarden är scopad till FOKUS-läget, inte en dokument-bred
    // "finns någon dialog"-fråga: en orelaterad öppen dialog (fokus ej i den) får inte
    // svälja popoverns Escape (WCAG 2.1.2). Fokus utanför dialogen ⇒ Escape stänger popovern.
    const onClose = vi.fn();
    const { getByText } = render(<Harness onClose={onClose} withDialog />);
    // Fokus på triggern (fokuserbar knapp, definitivt utanför dialogen).
    getByText("trigger").focus();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
