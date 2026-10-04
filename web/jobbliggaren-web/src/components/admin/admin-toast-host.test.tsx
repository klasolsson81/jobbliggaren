import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import {
  dismissAdminToast,
  getAdminToastSnapshot,
  holdAdminToasts,
  showAdminToast,
} from "@/lib/admin/toast-store";
import { AdminToastHost } from "./admin-toast-host";

function show(message: string) {
  act(() => {
    showAdminToast(message);
  });
}

function advance(ms: number) {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
  vi.useRealTimers();
});

describe("AdminToastHost (ADR 0150, handoff 12)", () => {
  it("keeps an empty live region mounted, so the first receipt is announced", () => {
    render(<AdminToastHost />);

    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    show("konto.a@example.test är suspenderat.");
    expect(screen.getByRole("status")).toHaveTextContent("konto.a@example.test är suspenderat.");
  });

  it("closes after 8 seconds", () => {
    render(<AdminToastHost />);
    show("Kvitto");

    advance(7_999);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");
    advance(1);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("stops the clock while hovered and gives a full 8 seconds after", () => {
    render(<AdminToastHost />);
    show("Kvitto");
    const card = screen.getByText("Kvitto").parentElement!;

    advance(5_000);
    fireEvent.mouseEnter(card);
    advance(30_000);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");

    fireEvent.mouseLeave(card);
    advance(7_999);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");
    advance(1);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("stops the clock while its close button has focus (WCAG 2.2.1)", () => {
    render(<AdminToastHost />);
    show("Kvitto");
    const close = screen.getByRole("button", { name: "Stäng meddelandet" });

    act(() => close.focus());
    advance(30_000);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");

    act(() => close.blur());
    advance(8_000);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("closes from its button", () => {
    render(<AdminToastHost />);
    show("Kvitto");

    fireEvent.click(screen.getByRole("button", { name: "Stäng meddelandet" }));
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("gives a replacing receipt its own 8 seconds", () => {
    render(<AdminToastHost />);
    show("Första");
    advance(7_000);
    show("Andra");

    advance(2_000);
    expect(screen.getByRole("status")).toHaveTextContent("Andra");
    advance(6_000);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("keeps the receipt while a dialog holds it, and gives it a full 8 seconds once released", () => {
    render(<AdminToastHost />);
    let release = () => {};
    act(() => {
      release = holdAdminToasts();
    });
    show("Kvitto");

    advance(30_000);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");
    act(() => release());
    advance(7_999);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");
    advance(1);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("stays paused while the pointer has left but focus has not", () => {
    render(<AdminToastHost />);
    show("Kvitto");
    const card = screen.getByText("Kvitto").parentElement!;
    const close = screen.getByRole("button", { name: "Stäng meddelandet" });

    fireEvent.mouseEnter(card);
    act(() => close.focus());
    fireEvent.mouseLeave(card);
    advance(30_000);
    expect(screen.getByRole("status")).toHaveTextContent("Kvitto");

    act(() => close.blur());
    advance(8_000);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });
});
