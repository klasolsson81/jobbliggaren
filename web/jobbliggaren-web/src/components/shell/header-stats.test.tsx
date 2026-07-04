import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import { HeaderStats } from "./header-stats";
import type { LandingStatsDto } from "@/lib/dto/landing";

const INITIAL: LandingStatsDto = {
  activeCount: 45_580,
  newToday: 312,
  isStale: false,
  refreshedAt: "2026-05-24T03:00:00+00:00",
};

describe("HeaderStats (ADR 0064 — inloggad live-stats + delta)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("renderar initial-värden från prop", () => {
    render(<HeaderStats initialStats={INITIAL} />);
    // sv-SE non-breaking-space → regex matchar både space och U+00A0
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    expect(screen.getByText("312")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("visar INGEN delta vid initial render", () => {
    render(<HeaderStats initialStats={INITIAL} />);
    expect(screen.queryByText(/^\+\d+$/)).not.toBeInTheDocument();
  });

  it("names the live-stats cluster via role=group (not aria-label on a role=generic div) — #609", () => {
    render(<HeaderStats initialStats={INITIAL} />);
    // `aria-label` on a bare `<div>` lands on role=generic (name-from-author
    // prohibited). `role="group"` makes the statsAriaLabel reliably announced.
    // Bites on revert: without the role the div is role=generic and getByRole throws.
    expect(
      screen.getByRole("group", { name: "Liveräkning från Platsbanken" }),
    ).toBeInTheDocument();
  });

  it("visar +N delta när polling ger högre newToday", async () => {
    const fetchSpy = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          activeCount: 45_585,
          newToday: 315,
          isStale: false,
          refreshedAt: "2026-05-24T03:10:00+00:00",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    global.fetch = fetchSpy;

    render(<HeaderStats initialStats={INITIAL} />);

    // Trigga polling-intervallet (10 min).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      "/api/landing-stats",
      expect.objectContaining({ cache: "no-store" }),
    );
    expect(screen.getByText("315")).toBeInTheDocument();
    expect(screen.getByText("+3")).toBeInTheDocument();
  });

  it("uppdaterar siffrorna men visar INGEN delta när newToday ej ökat", async () => {
    const fetchSpy = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          activeCount: 45_590,
          newToday: 312,
          isStale: false,
          refreshedAt: "2026-05-24T03:10:00+00:00",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    global.fetch = fetchSpy;

    render(<HeaderStats initialStats={INITIAL} />);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });

    expect(screen.getByText(/45[\s ]590/)).toBeInTheDocument();
    expect(screen.queryByText(/^\+\d+$/)).not.toBeInTheDocument();
  });

  it("behåller nuvarande värde vid network-fail", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));

    render(<HeaderStats initialStats={INITIAL} />);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });

    // Civic-utility: ingen "Något gick fel"-text; siffran står kvar.
    expect(screen.getByText("312")).toBeInTheDocument();
    expect(screen.queryByText(/^\+\d+$/)).not.toBeInTheDocument();
  });

  it("behåller nuvarande värde vid 503 från proxy", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response("", { status: 503 }));

    render(<HeaderStats initialStats={INITIAL} />);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });

    expect(screen.getByText("312")).toBeInTheDocument();
    expect(screen.queryByText(/^\+\d+$/)).not.toBeInTheDocument();
  });

  it("ratchar delta korrekt över flera sekventiella polls", async () => {
    // code-reviewer M3 — verifierar att previousNewToday-ref ratchar mellan
    // polls så delta beräknas mot SENASTE sedda värdet, inte initial.
    const fetchSpy = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            activeCount: 45_585,
            newToday: 315,
            isStale: false,
            refreshedAt: "2026-05-24T03:10:00+00:00",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            activeCount: 45_590,
            newToday: 320,
            isStale: false,
            refreshedAt: "2026-05-24T03:20:00+00:00",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      );
    global.fetch = fetchSpy;

    render(<HeaderStats initialStats={INITIAL} />);

    // Polling 1: 312 → 315 = +3
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });
    expect(screen.getByText("315")).toBeInTheDocument();
    expect(screen.getByText("+3")).toBeInTheDocument();

    // Polling 2: 315 → 320 = +5 (NOT +8 från initial 312, vilket vore ratchet-bugg)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
    });
    expect(screen.getByText("320")).toBeInTheDocument();
    expect(screen.getByText("+5")).toBeInTheDocument();
    expect(screen.queryByText("+8")).not.toBeInTheDocument();
  });

  it("pollar EJ när tabben är hidden (visibility-aware)", async () => {
    // code-reviewer M1 — undvik onödig nätverkslast i bakgrunds-tabbar.
    const fetchSpy = vi.fn();
    global.fetch = fetchSpy;

    // Simulera hidden-state innan render (default i jsdom är "visible").
    const originalGetter = Object.getOwnPropertyDescriptor(
      Document.prototype,
      "visibilityState",
    );
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      get: () => "hidden",
    });

    try {
      render(<HeaderStats initialStats={INITIAL} />);
      await act(async () => {
        await vi.advanceTimersByTimeAsync(10 * 60 * 1000);
      });

      expect(fetchSpy).not.toHaveBeenCalled();
    } finally {
      // Restaurera default jsdom-behaviour.
      if (originalGetter) {
        Object.defineProperty(Document.prototype, "visibilityState", originalGetter);
      }
      // @ts-expect-error — ta bort instans-overriden så andra tester inte påverkas.
      delete document.visibilityState;
    }
  });
});
