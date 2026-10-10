import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvFindingStatusControl } from "./cv-finding-status-control";
import type { ActionResult } from "@/lib/actions/_action-result";

/**
 * The per-finding status control in the review ledger's action column (#2083; Fas 4b PR-8.4).
 * `setFindingStatusAction` is mocked; its signature mirrors the real `(resumeId, criterionId,
 * status)`. `useTranslations` is REAL via the test shim's NextIntlClientProvider, so the test runs
 * against the Swedish catalogue, as production does.
 *
 * Core invariant (CLAUDE.md §5 honesty): "Ignorera regeln (stilfråga)" renders ONLY when
 * `isIgnorable === true` — never an offer the server refuses (400 FindingNotIgnorable).
 */

const setFindingStatusMock =
  vi.fn<(...args: [string, string, string]) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resumes", () => ({
  setFindingStatusAction: (resumeId: string, criterionId: string, status: string) =>
    setFindingStatusMock(resumeId, criterionId, status),
}));

const RESUME_ID = "11111111-1111-4111-8111-111111111111";
const CRITERION = "A7";
const NAME_ID = "cvledger-A7-name";

type ControlProps = Parameters<typeof CvFindingStatusControl>[0];

function control(props: Partial<ControlProps> = {}) {
  return (
    <>
      <span id={NAME_ID}>Anti-klyschor</span>
      <CvFindingStatusControl
        resumeId={RESUME_ID}
        criterionId={CRITERION}
        labelledBy={NAME_ID}
        userStatus={null}
        userStatusStaleAt={null}
        isIgnorable={false}
        {...props}
      />
    </>
  );
}

function renderControl(props: Partial<ControlProps> = {}) {
  return render(control(props));
}

beforeEach(() => {
  setFindingStatusMock.mockReset();
  setFindingStatusMock.mockResolvedValue({ success: true });
});

describe("CvFindingStatusControl — the §5 honesty gate", () => {
  it("hides 'Ignorera regeln (stilfråga)' when isIgnorable=false", () => {
    renderControl({ isIgnorable: false });
    expect(screen.queryByRole("button", { name: /Ignorera regeln/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Markera som åtgärdad/ })).toBeInTheDocument();
  });

  it("shows it when isIgnorable=true and the finding is open", () => {
    renderControl({ isIgnorable: true });
    expect(screen.getByRole("button", { name: /Ignorera regeln/ })).toBeInTheDocument();
  });

  it("hides it again once the finding is Ignored", () => {
    renderControl({ isIgnorable: true, userStatus: "Ignored" });
    expect(screen.queryByRole("button", { name: /Ignorera regeln/ })).not.toBeInTheDocument();
  });
});

describe("CvFindingStatusControl — what each status shows", () => {
  it("names its group after the row's criterion", () => {
    renderControl();
    expect(screen.getByRole("group", { name: "Anti-klyschor" })).toBeInTheDocument();
  });

  it("open: only the action — no pill and no hint, since a row without a status reads as open", () => {
    const { container } = renderControl({ userStatus: null });
    expect(screen.getByRole("button", { name: /Markera som åtgärdad/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Återställ" })).not.toBeInTheDocument();
    expect(container.querySelectorAll(".jp-pill")).toHaveLength(0);
    expect(container.querySelectorAll(".jp-findingstatus__hint")).toHaveLength(0);
  });

  it("an explicit Open reads the same as no decision", () => {
    const { container } = renderControl({ userStatus: "Open" });
    expect(screen.getByRole("button", { name: /Markera som åtgärdad/ })).toBeInTheDocument();
    expect(container.querySelectorAll(".jp-pill")).toHaveLength(0);
  });

  it("Resolved: the Åtgärdad pill, Återställ and the short hint", () => {
    renderControl({ userStatus: "Resolved", userStatusStaleAt: null });
    expect(screen.getByText("Åtgärdad")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Återställ" })).toBeInTheDocument();
    expect(screen.getByText("Ligger kvar tills den är borta ur ditt CV.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Markera som åtgärdad/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/finns fortfarande kvar i ditt CV/)).not.toBeInTheDocument();
  });

  it("Resolved but stale: the stale hint replaces the short one", () => {
    renderControl({ userStatus: "Resolved", userStatusStaleAt: "2026-07-10T08:00:00Z" });
    expect(screen.getByText(/finns fortfarande kvar i ditt CV/)).toBeInTheDocument();
    expect(screen.queryByText("Ligger kvar tills den är borta ur ditt CV.")).not.toBeInTheDocument();
  });

  it("Ignored: the Ignorerad pill, Återställ and its hint, and no Markera", () => {
    renderControl({ userStatus: "Ignored", isIgnorable: true });
    expect(screen.getByText("Ignorerad")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Återställ" })).toBeInTheDocument();
    expect(screen.getByText(/räknas inte längre som en åtgärd/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Markera som åtgärdad/ })).not.toBeInTheDocument();
  });

  it("an unknown status renders no pill — it is a state we do not know", () => {
    // Deploy skew: the zod schema keeps the set open rather than fail the whole review, and a
    // pill would claim a state the page cannot name.
    const { container } = renderControl({ userStatus: "SomethingNewFromANewerBackend" });
    expect(container.querySelectorAll(".jp-pill")).toHaveLength(0);
    expect(screen.getByRole("button", { name: /Markera som åtgärdad/ })).toBeInTheDocument();
  });
});

describe("CvFindingStatusControl — calls setFindingStatusAction", () => {
  it.each([
    [{ userStatus: null }, /Markera som åtgärdad/, "Resolved"],
    [{ userStatus: null, isIgnorable: true }, /Ignorera regeln/, "Ignored"],
    [{ userStatus: "Resolved" }, /^Återställ$/, "Open"],
  ] as const)("%o: %s sends %s", async (props, name, status) => {
    const user = userEvent.setup();
    renderControl(props);
    await user.click(screen.getByRole("button", { name }));
    await waitFor(() =>
      expect(setFindingStatusMock).toHaveBeenCalledWith(RESUME_ID, CRITERION, status),
    );
  });

  it("surfaces the action's error in a role='alert'", async () => {
    const user = userEvent.setup();
    setFindingStatusMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att uppdatera åtgärdsstatusen.",
    });
    renderControl({ userStatus: null });
    await user.click(screen.getByRole("button", { name: /Markera som åtgärdad/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Det gick inte att uppdatera åtgärdsstatusen.",
    );
  });

  it("keeps the pressed button focusable while pending: aria-disabled, never disabled", async () => {
    const user = userEvent.setup();
    let resolve: (result: ActionResult) => void = () => {};
    setFindingStatusMock.mockReturnValue(
      new Promise<ActionResult>((r) => {
        resolve = r;
      }),
    );
    renderControl({ userStatus: null });
    const button = screen.getByRole("button", { name: /Markera som åtgärdad/ });
    await user.click(button);

    const pending = await screen.findByRole("button", { name: "Uppdaterar…" });
    expect(pending).toHaveAttribute("aria-disabled", "true");
    expect(pending).not.toBeDisabled();
    expect(pending).toHaveFocus();

    // A second press while pending sends nothing.
    await user.click(pending);
    expect(setFindingStatusMock).toHaveBeenCalledTimes(1);
    resolve({ success: true });
  });

  it("moves focus to the group's new first button when the status it changed arrives", async () => {
    const user = userEvent.setup();
    const { rerender } = renderControl({ userStatus: null });
    await user.click(screen.getByRole("button", { name: /Markera som åtgärdad/ }));
    await waitFor(() => expect(setFindingStatusMock).toHaveBeenCalled());

    // The revalidated page brings the new status as a prop; the pressed button is gone.
    rerender(control({ userStatus: "Resolved" }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Återställ" })).toHaveFocus(),
    );
  });
});
