import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvFindingStatusControl } from "./cv-finding-status-control";
import type { ActionResult } from "@/lib/actions/resumes";

/**
 * Fas 4b PR-8.4 (CTO-bind Q3/Q4) — per-anmärkning statuskontrollen på den KANONISKA
 * granskningen. `setFindingStatusAction` mockas (klient-ön kallar den vid klick); dess
 * signatur speglar den äkta `(resumeId, criterionId, status)`. `useTranslations`
 * (resumes.review.status) är ÄKTA via test-shimmens NextIntlClientProvider — testet kör mot
 * de svenska katalogerna, precis som produktion.
 *
 * Kärninvarianten (CLAUDE.md §5-ärlighet): "Ignorera regeln (stilfråga)" får renderas ENBART
 * när `isIgnorable === true` — aldrig ett erbjudande servern nekar (400 FindingNotIgnorable).
 */

const setFindingStatusMock =
  vi.fn<(...args: [string, string, string]) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resumes", () => ({
  setFindingStatusAction: (resumeId: string, criterionId: string, status: string) =>
    setFindingStatusMock(resumeId, criterionId, status),
}));

const RESUME_ID = "11111111-1111-4111-8111-111111111111";
const CRITERION = "A7";

type ControlProps = Parameters<typeof CvFindingStatusControl>[0];

function renderControl(props: Partial<ControlProps> = {}) {
  return render(
    <CvFindingStatusControl
      resumeId={RESUME_ID}
      criterionId={CRITERION}
      userStatus={null}
      userStatusStaleAt={null}
      isIgnorable={false}
      {...props}
    />,
  );
}

beforeEach(() => {
  setFindingStatusMock.mockReset();
  setFindingStatusMock.mockResolvedValue({ success: true });
});

describe("CvFindingStatusControl — §5-honesty-gate (Ignorera-knappen)", () => {
  it("döljer 'Ignorera regeln (stilfråga)' när isIgnorable=false", () => {
    renderControl({ isIgnorable: false });
    expect(
      screen.queryByRole("button", { name: /Ignorera regeln/ }),
    ).not.toBeInTheDocument();
    // Den lugna åtgärdsknappen finns kvar (det är bara Ignorera-erbjudandet som gate:as).
    expect(
      screen.getByRole("button", { name: /Markera som åtgärdad/ }),
    ).toBeInTheDocument();
  });

  it("visar 'Ignorera regeln (stilfråga)' när isIgnorable=true och status är öppen", () => {
    renderControl({ isIgnorable: true });
    expect(
      screen.getByRole("button", { name: /Ignorera regeln/ }),
    ).toBeInTheDocument();
  });

  it("döljer Ignorera-knappen igen när anmärkningen redan är Ignored", () => {
    renderControl({ isIgnorable: true, userStatus: "Ignored" });
    expect(
      screen.queryByRole("button", { name: /Ignorera regeln/ }),
    ).not.toBeInTheDocument();
  });
});

describe("CvFindingStatusControl — knapp-synlighet per status", () => {
  it("öppen status: 'Markera som åtgärdad' synlig, 'Återställ' dold", () => {
    renderControl({ userStatus: null });
    expect(
      screen.getByRole("button", { name: /Markera som åtgärdad/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Återställ" }),
    ).not.toBeInTheDocument();
  });

  it("Resolved: 'Markera som åtgärdad' dold, 'Återställ' synlig", () => {
    renderControl({ userStatus: "Resolved" });
    expect(
      screen.queryByRole("button", { name: /Markera som åtgärdad/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Återställ" }),
    ).toBeInTheDocument();
  });

  it("Ignored: 'Återställ' synlig", () => {
    renderControl({ userStatus: "Ignored", isIgnorable: true });
    expect(
      screen.getByRole("button", { name: "Återställ" }),
    ).toBeInTheDocument();
  });
});

describe("CvFindingStatusControl — pill + hjälptext per status", () => {
  it("Resolved utan stale: Åtgärdad-pill + resolvedHint (inte staleHint)", () => {
    renderControl({ userStatus: "Resolved", userStatusStaleAt: null });
    expect(screen.getByText("Åtgärdad")).toBeInTheDocument();
    expect(screen.getByText(/ligger kvar i granskningen/)).toBeInTheDocument();
    expect(
      screen.queryByText(/finns fortfarande kvar i ditt CV/),
    ).not.toBeInTheDocument();
  });

  it("Resolved med stale-stämpel: staleHint (inte resolvedHint)", () => {
    renderControl({
      userStatus: "Resolved",
      userStatusStaleAt: "2026-07-10T08:00:00Z",
    });
    expect(
      screen.getByText(/finns fortfarande kvar i ditt CV/),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/ligger kvar i granskningen/),
    ).not.toBeInTheDocument();
  });

  it("Ignored: Ignorerad-pill + ignoredHint", () => {
    renderControl({ userStatus: "Ignored", isIgnorable: true });
    expect(screen.getByText("Ignorerad")).toBeInTheDocument();
    expect(
      screen.getByText(/räknas inte längre som en åtgärd/),
    ).toBeInTheDocument();
  });
});

describe("CvFindingStatusControl — anropar setFindingStatusAction", () => {
  it("klick på 'Markera som åtgärdad' skickar (resumeId, criterionId, 'Resolved')", async () => {
    const user = userEvent.setup();
    renderControl({ isIgnorable: false, userStatus: null });

    await user.click(
      screen.getByRole("button", { name: /Markera som åtgärdad/ }),
    );

    await waitFor(() =>
      expect(setFindingStatusMock).toHaveBeenCalledWith(
        RESUME_ID,
        CRITERION,
        "Resolved",
      ),
    );
  });

  it("klick på 'Ignorera regeln' skickar status 'Ignored'", async () => {
    const user = userEvent.setup();
    renderControl({ isIgnorable: true, userStatus: null });

    await user.click(screen.getByRole("button", { name: /Ignorera regeln/ }));

    await waitFor(() =>
      expect(setFindingStatusMock).toHaveBeenCalledWith(
        RESUME_ID,
        CRITERION,
        "Ignored",
      ),
    );
  });

  it("klick på 'Återställ' skickar status 'Open'", async () => {
    const user = userEvent.setup();
    renderControl({ userStatus: "Resolved" });

    await user.click(screen.getByRole("button", { name: "Återställ" }));

    await waitFor(() =>
      expect(setFindingStatusMock).toHaveBeenCalledWith(
        RESUME_ID,
        CRITERION,
        "Open",
      ),
    );
  });

  it("ytar action-felet i en role='alert' när skrivningen misslyckas", async () => {
    const user = userEvent.setup();
    setFindingStatusMock.mockResolvedValue({
      success: false,
      error: "Det gick inte att uppdatera åtgärdsstatusen.",
    });
    renderControl({ userStatus: null });

    await user.click(
      screen.getByRole("button", { name: /Markera som åtgärdad/ }),
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Det gick inte att uppdatera åtgärdsstatusen.",
    );
  });
});
