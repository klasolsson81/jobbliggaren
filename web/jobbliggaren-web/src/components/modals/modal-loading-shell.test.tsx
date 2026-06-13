import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { ModalLoadingShell } from "./modal-loading-shell";

describe("ModalLoadingShell", () => {
  it("renderar en dialog med aria-busy men UTAN aria-modal (transient, icke-inert fallback)", () => {
    const { getByRole } = render(<ModalLoadingShell statusText="Laddar annons" />);
    const dialog = getByRole("dialog");
    expect(dialog).not.toBeNull();
    expect(dialog.getAttribute("aria-busy")).toBe("true");
    // Ingen focus-trap/inert appliceras under den korta fallbacken → aria-modal
    // vore osant (design-reviewer Fas 2). Den riktiga shell:en bär aria-modal.
    expect(dialog.getAttribute("aria-modal")).toBeNull();
  });

  it("namnger dialogen via status-texten (aria-label === statusText)", () => {
    const { getByRole } = render(<ModalLoadingShell statusText="Hämtar ansökan" />);
    expect(getByRole("dialog").getAttribute("aria-label")).toBe("Hämtar ansökan");
  });

  it("visar status-texten som synlig rad, aria-hidden (sighted-only, ej dubbel-uppläst)", () => {
    const { container } = render(<ModalLoadingShell statusText="Laddar annons" />);
    const text = container.querySelector(".jp-modal-loading__text")!;
    expect(text).not.toBeNull();
    expect(text.textContent).toBe("Laddar annons");
    expect(text.getAttribute("aria-hidden")).toBe("true");
  });

  it("kopplar in laddnings-annonsering via BrandSpinners role=status live-region", () => {
    const { getByRole } = render(<ModalLoadingShell statusText="Laddar annons" />);
    expect(getByRole("status")).not.toBeNull();
  });

  it("återanvänder befintlig modal-yta (jp-modal-scrim + jp-modal jp-modal--loading)", () => {
    const { container } = render(<ModalLoadingShell statusText="Laddar annons" />);
    const scrim = container.querySelector(".jp-modal-scrim")!;
    expect(scrim).not.toBeNull();
    const panel = container.querySelector(".jp-modal.jp-modal--loading")!;
    expect(panel).not.toBeNull();
  });

  it("är icke-interaktiv (Variant A): scrim har role=presentation och inga knappar", () => {
    const { container } = render(<ModalLoadingShell statusText="Laddar annons" />);
    expect(container.querySelector(".jp-modal-scrim")!.getAttribute("role")).toBe("presentation");
    expect(container.querySelectorAll("button").length).toBe(0);
  });
});
