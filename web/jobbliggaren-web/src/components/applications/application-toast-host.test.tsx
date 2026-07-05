import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import { ApplicationToastHost } from "./application-toast-host";
import {
  dismissApplicationToast,
  getApplicationToastSnapshot,
  showApplicationToast,
} from "@/lib/applications/toast-store";

const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction,
}));

const APP_ID = "11111111-2222-3333-4444-555555555555";

function showStatusToast() {
  act(() => {
    showApplicationToast({
      kind: "statusChange",
      applicationId: APP_ID,
      company: "Volvo",
      from: "Submitted",
      to: "Acknowledged",
    });
  });
}

beforeEach(() => {
  transitionStatusAction.mockClear();
  dismissApplicationToast();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("ApplicationToastHost (design §10, #630 PR 7)", () => {
  it("renderar statusbyte-toasten '{company}: {from} → {to}' med Ångra + stängning i en polite live-region", () => {
    render(<ApplicationToastHost />);
    showStatusToast();

    const region = screen.getByRole("status");
    expect(region).toHaveTextContent("Volvo: Skickad → Bekräftad");
    expect(
      screen.getByRole("button", { name: "Ångra" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Stäng meddelandet" }),
    ).toBeInTheDocument();
  });

  it("Ångra = kompenserande INVERS transition (ADR 0092 D3), ingen kedjad toast", async () => {
    render(<ApplicationToastHost />);
    showStatusToast();

    fireEvent.click(screen.getByRole("button", { name: "Ångra" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(APP_ID, "Submitted"),
    );
    // Toasten stängs — ingen ny ångra-på-ångra-toast (CTO-bind 3).
    await waitFor(() => expect(getApplicationToastSnapshot()).toBeNull());
  });

  it("uppföljningstoasten har INGEN Ångra (design §10)", () => {
    render(<ApplicationToastHost />);
    act(() => {
      showApplicationToast({ kind: "followUpLogged", company: "Volvo" });
    });
    expect(screen.getByRole("status")).toHaveTextContent(
      "Volvo: uppföljning sparad under Uppföljningar",
    );
    expect(
      screen.queryByRole("button", { name: "Ångra" }),
    ).not.toBeInTheDocument();
  });

  it("fel-toasten renderas i den assertiva regionen (role=alert)", () => {
    render(<ApplicationToastHost />);
    act(() => {
      showApplicationToast({ kind: "error", message: "Statusbytet misslyckades." });
    });
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Statusbytet misslyckades.",
    );
  });

  it("auto-stänger efter 8 s", () => {
    vi.useFakeTimers();
    render(<ApplicationToastHost />);
    showStatusToast();
    expect(getApplicationToastSnapshot()).not.toBeNull();

    act(() => {
      vi.advanceTimersByTime(8_000);
    });
    expect(getApplicationToastSnapshot()).toBeNull();
  });

  it("hover pausar auto-stängningen (WCAG 2.2.1); leave startar om fönstret", () => {
    vi.useFakeTimers();
    const { container } = render(<ApplicationToastHost />);
    showStatusToast();

    const toast = container.querySelector(".jp-toast")!;
    fireEvent.mouseEnter(toast);
    act(() => {
      vi.advanceTimersByTime(20_000);
    });
    // Pausad — fortfarande synlig.
    expect(getApplicationToastSnapshot()).not.toBeNull();

    fireEvent.mouseLeave(toast);
    act(() => {
      vi.advanceTimersByTime(8_000);
    });
    expect(getApplicationToastSnapshot()).toBeNull();
  });

  it("✕ stänger direkt", () => {
    render(<ApplicationToastHost />);
    showStatusToast();
    fireEvent.click(screen.getByRole("button", { name: "Stäng meddelandet" }));
    expect(getApplicationToastSnapshot()).toBeNull();
  });
});
