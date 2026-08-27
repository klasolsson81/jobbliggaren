import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/**
 * DEV-ONLY — REMOVE BEFORE LAUNCH with the component
 * (docs/runbooks/release-checklist.md 2.7).
 *
 * This file exists because of a defect a review caught and a green suite would not have:
 * the dialog is CONTROLLED, so Radix never flips its own state and the trigger's open
 * arrives through `onOpenChange` and nowhere else. A handler that only covered the close
 * direction threw it away, and the only route to an irreversible operation was dead —
 * with nothing throwing and no type error.
 */

const { resetMock, refreshMock } = vi.hoisted(() => ({
  resetMock: vi.fn(),
  refreshMock: vi.fn(),
}));

vi.mock("@/lib/dev/reset-actions", () => ({
  resetMyDataAction: resetMock,
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    refresh: refreshMock,
    prefetch: vi.fn(),
    back: vi.fn(),
  }),
  useSearchParams: () => new URLSearchParams(),
  usePathname: () => "/oversikt",
}));

import { ResetMyDataNote } from "./reset-my-data-note";

beforeEach(() => {
  resetMock.mockReset();
  refreshMock.mockReset();
  resetMock.mockResolvedValue({ success: true });
});

describe("ResetMyDataNote", () => {
  async function openDialog() {
    const user = userEvent.setup();
    render(<ResetMyDataNote />);
    await user.click(
      screen.getByRole("button", {
        name: "Dev: återställ dina testdata (tas bort före lansering)",
      }),
    );
    return user;
  }

  it("öppnar bekräftelsedialogen när triggern klickas", async () => {
    await openDialog();

    expect(
      await screen.findByRole("heading", { name: "Återställ dina testdata?" }),
    ).toBeInTheDocument();
  });

  it("beskriver konsekvensen innan handlingen, och nollar inget på vägen dit", async () => {
    await openDialog();

    // The consent sentence is the whole reason the dialog exists. It must name what goes
    // (saved job ads) and what stays (saved searches) — the two were conflated once.
    const body = await screen.findByText(/sparade annonser/i);
    expect(body).toHaveTextContent(/dina sparade sökningar påverkas inte/i);
    expect(resetMock).not.toHaveBeenCalled();
  });

  it("kör återställningen först när bekräftelseknappen trycks", async () => {
    const user = await openDialog();

    await user.click(screen.getByRole("button", { name: "Återställ testdata" }));

    expect(resetMock).toHaveBeenCalledTimes(1);
    expect(refreshMock).toHaveBeenCalledTimes(1);
  });

  it("visar felet i en role=alert och nollar inte välkomsttillståndet", async () => {
    resetMock.mockResolvedValue({
      success: false,
      error: "Återställning är avstängd på servern.",
    });
    const user = await openDialog();

    await user.click(screen.getByRole("button", { name: "Återställ testdata" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Återställning är avstängd på servern.",
    );
    // The dialog stays open on failure — closing it would read as "done".
    expect(
      screen.getByRole("heading", { name: "Återställ dina testdata?" }),
    ).toBeInTheDocument();
    expect(refreshMock).not.toHaveBeenCalled();
  });
});
