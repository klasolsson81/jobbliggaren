import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import { ApplicationToastHost } from "./application-toast-host";
import {
  dismissApplicationToast,
  getApplicationToastSnapshot,
  showApplicationToast,
} from "@/lib/applications/toast-store";

// Bulk-toasten (#630 PR 10, statusChangeBatch) + grupp-ångra. Grupp-ångran är EN
// batch tillbaka där VARJE app återförs till SIN egen previous status (per-item
// previous, ADR 0092 D3) — inte en delad `from`. Mockar batch-action:en.
type Res = { success: true } | { success: false; error: string };
const batchTransitionAction = vi.hoisted(() =>
  vi.fn<(items: unknown) => Promise<Res>>(async () => ({ success: true })),
);
const transitionStatusAction = vi.hoisted(() =>
  vi.fn<(id: string, to: string) => Promise<Res>>(async () => ({ success: true })),
);
vi.mock("@/lib/actions/applications", () => ({
  batchTransitionAction,
  transitionStatusAction,
}));

function showBatchToast() {
  act(() => {
    showApplicationToast({
      kind: "statusChangeBatch",
      count: 3,
      to: "Ghosted",
      items: [
        { applicationId: "a", from: "Submitted" },
        { applicationId: "b", from: "Acknowledged" },
        { applicationId: "c", from: "Interviewing" },
      ],
    });
  });
}

beforeEach(() => {
  batchTransitionAction.mockClear();
  batchTransitionAction.mockResolvedValue({ success: true });
  dismissApplicationToast();
});

describe("ApplicationToastHost — bulk-toast + grupp-ångra (#630 PR 10)", () => {
  it("(a) renderar plural '3 ansökningar markerade som Inget svar' med Ångra", () => {
    render(<ApplicationToastHost />);
    showBatchToast();

    const region = screen.getByRole("status");
    expect(region).toHaveTextContent(
      "3 ansökningar markerade som Inget svar",
    );
    expect(
      screen.getByRole("button", { name: "Ångra" }),
    ).toBeInTheDocument();
  });

  it("(b) Ångra → EXAKT ETT batch-anrop, varje app till SIN egen previous status; stängs vid success", async () => {
    render(<ApplicationToastHost />);
    showBatchToast();

    fireEvent.click(screen.getByRole("button", { name: "Ångra" }));

    await waitFor(() =>
      expect(batchTransitionAction).toHaveBeenCalledTimes(1),
    );
    expect(batchTransitionAction).toHaveBeenCalledWith([
      { applicationId: "a", targetStatus: "Submitted" },
      { applicationId: "b", targetStatus: "Acknowledged" },
      { applicationId: "c", targetStatus: "Interviewing" },
    ]);

    // Framgångsrik ångra stänger toasten — ingen kedjad ångra-på-ångra.
    await waitFor(() => expect(getApplicationToastSnapshot()).toBeNull());
  });

  it("(c) misslyckad ångra → fel-toast ersätter (assertiv region)", async () => {
    batchTransitionAction.mockResolvedValueOnce({
      success: false,
      error: "Statusbytet misslyckades.",
    });
    render(<ApplicationToastHost />);
    showBatchToast();

    fireEvent.click(screen.getByRole("button", { name: "Ångra" }));

    await waitFor(() =>
      expect(getApplicationToastSnapshot()?.kind).toBe("error"),
    );
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Statusbytet misslyckades.",
    );
  });
});
