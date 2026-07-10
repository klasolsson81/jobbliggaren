import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  render,
  screen,
  within,
  fireEvent,
  waitFor,
} from "@testing-library/react";
import { ApplicationsBoard } from "./applications-board";
import { ApplicationActionsProvider } from "./application-actions";
import { transitionStatusAction } from "@/lib/actions/applications";
import { showApplicationToast } from "@/lib/applications/toast-store";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn(async () => ({ success: true as const })),
  logFollowUpAction: vi.fn(async () => ({ success: true as const })),
}));
// Boardet publicerar ångra-/error-toasten via toast-store (samma modul-global
// som providern/drawern) — spionera på den för att pinna drop-vägens toasts.
vi.mock("@/lib/applications/toast-store", () => ({
  showApplicationToast: vi.fn(),
  dismissApplicationToast: vi.fn(),
  subscribeApplicationToast: vi.fn(() => () => {}),
  getApplicationToastSnapshot: vi.fn(() => null),
  getApplicationToastServerSnapshot: vi.fn(() => null),
}));
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({
      push: vi.fn(),
      back: vi.fn(),
      refresh: vi.fn(),
      replace: vi.fn(),
      prefetch: vi.fn(),
    }),
  };
});

const NOW = new Date("2026-05-20T12:00:00Z");

function app(
  id: string,
  status: ApplicationStatus,
  title = `${status}-roll`,
): ApplicationDto {
  return {
    id,
    jobSeekerId: "seeker",
    jobAdId: "ad",
    status,
    createdAt: "2026-05-01",
    updatedAt: "2026-05-02",
    lastStatusChangeAt: "2026-05-15",
    jobAd: {
      jobAdId: "ad",
      title,
      company: "Volvo",
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
    },
  };
}

function makeGroups(
  populated: Partial<Record<ApplicationStatus, number>>,
): PipelineGroupDto[] {
  return PIPELINE_ORDER.map((status) => {
    const n = populated[status] ?? 0;
    return {
      status,
      count: n,
      applications: Array.from({ length: n }, (_, i) =>
        app(`${status}-${i}`, status, `${status}-titel-${i}`),
      ),
    };
  });
}

function renderBoard(
  groups: PipelineGroupDto[],
  query = "",
) {
  return render(
    <ApplicationActionsProvider>
      <ApplicationsBoard groups={groups} now={NOW} query={query} />
    </ApplicationActionsProvider>,
  );
}

const dataTransferFor = (id: string) => ({
  setData: vi.fn(),
  getData: vi.fn(() => id),
  effectAllowed: "",
  dropEffect: "",
});

beforeEach(() => {
  vi.mocked(transitionStatusAction).mockClear();
  vi.mocked(transitionStatusAction).mockResolvedValue({ success: true });
  vi.mocked(showApplicationToast).mockClear();
});

describe("ApplicationsBoard — layout", () => {
  it("renderar 6 aktiva kolumner + 4 terminal-zoner (10 status-grupper)", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    for (const label of [
      "Utkast",
      "Skickad",
      "Bekräftad",
      "Intervju bokad",
      "Pågående intervju",
      "Erbjudande",
      "Accepterad",
      "Nekad",
      "Återtagen",
      "Inget svar",
    ]) {
      expect(screen.getByRole("group", { name: label })).toBeInTheDocument();
    }
  });

  it("toppband keyar off getStatusVariantKey (SAMMA SSOT som rail/tagg)", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    // Submitted → STATUS_BADGE_VARIANT Info → "info" (#683, design §11).
    expect(screen.getByRole("group", { name: "Skickad" })).toHaveAttribute(
      "data-status-variant",
      "info",
    );
    // Ghosted → Neutral → "neutral" (#683, design §11).
    expect(screen.getByRole("group", { name: "Inget svar" })).toHaveAttribute(
      "data-status-variant",
      "neutral",
    );
  });

  it("kortet visar roll, företag och 'N DGR'", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    const column = screen.getByRole("group", { name: "Skickad" });
    expect(within(column).getByText("Submitted-titel-0")).toBeInTheDocument();
    expect(within(column).getByText("Volvo")).toBeInTheDocument();
    // lastStatusChangeAt 2026-05-15 → 5 dagar till NOW.
    expect(within(column).getByText("5 dgr")).toBeInTheDocument();
  });

  it("tomma ytor: kolumn/Accepterad/övrig zon har rätt tomtext", () => {
    renderBoard(makeGroups({}));
    expect(
      within(screen.getByRole("group", { name: "Skickad" })).getByText(
        "Inga ansökningar här",
      ),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole("group", { name: "Accepterad" })).getByText(
        "Dra hit när du accepterar",
      ),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole("group", { name: "Nekad" })).getByText("Tomt"),
    ).toBeInTheDocument();
  });

  it("kolumn kapar vid 4 kort + 'Visa 2 fler', expanderar vid klick", () => {
    renderBoard(makeGroups({ Submitted: 6 }));
    const column = screen.getByRole("group", { name: "Skickad" });
    expect(within(column).getAllByRole("article")).toHaveLength(4);
    const more = within(column).getByRole("button", { name: "Visa 2 fler" });
    fireEvent.click(more);
    expect(within(column).getAllByRole("article")).toHaveLength(6);
  });

  it("verktygsraden visar antal ansökningar + aktiva", () => {
    renderBoard(makeGroups({ Submitted: 2, Accepted: 1 }));
    // 3 totalt, 2 aktiva (Accepted är terminal).
    expect(screen.getByText(/3 ansökningar/)).toBeInTheDocument();
    expect(screen.getByText(/2 aktiva/)).toBeInTheDocument();
  });

  it("varje kort bär StatusMenu (tangentbords-/no-drag-vägen)", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    const column = screen.getByRole("group", { name: "Skickad" });
    expect(
      within(column).getByRole("button", { name: /Byt status/ }),
    ).toBeInTheDocument();
  });
});

describe("ApplicationsBoard — drag & drop (statusbyte)", () => {
  it("släpp på en annan kolumn kör den auditerade transitionen med målstatus", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    const card = within(
      screen.getByRole("group", { name: "Skickad" }),
    ).getByRole("article");
    const target = screen.getByRole("group", { name: "Bekräftad" });
    const dt = dataTransferFor("Submitted-0");

    fireEvent.dragStart(card, { dataTransfer: dt });
    fireEvent.dragOver(target, { dataTransfer: dt });
    fireEvent.drop(target, { dataTransfer: dt });

    expect(transitionStatusAction).toHaveBeenCalledWith(
      "Submitted-0",
      "Acknowledged",
    );
  });

  it("fri flytt Ghosted → Skickad (återaktivering)", () => {
    renderBoard(makeGroups({ Ghosted: 1 }));
    const card = within(
      screen.getByRole("group", { name: "Inget svar" }),
    ).getByRole("article");
    const target = screen.getByRole("group", { name: "Skickad" });
    const dt = dataTransferFor("Ghosted-0");

    fireEvent.dragStart(card, { dataTransfer: dt });
    fireEvent.drop(target, { dataTransfer: dt });

    expect(transitionStatusAction).toHaveBeenCalledWith("Ghosted-0", "Submitted");
  });

  it("släpp på samma kolumn är en no-op (ingen transition)", () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    const column = screen.getByRole("group", { name: "Skickad" });
    const card = within(column).getByRole("article");
    const dt = dataTransferFor("Submitted-0");

    fireEvent.dragStart(card, { dataTransfer: dt });
    fireEvent.drop(column, { dataTransfer: dt });

    expect(transitionStatusAction).not.toHaveBeenCalled();
  });

  it("lyckad flytt publicerar ångra-toasten (from → to, samta toast-store)", async () => {
    renderBoard(makeGroups({ Submitted: 1 }));
    const card = within(
      screen.getByRole("group", { name: "Skickad" }),
    ).getByRole("article");
    const dt = dataTransferFor("Submitted-0");

    fireEvent.dragStart(card, { dataTransfer: dt });
    fireEvent.drop(screen.getByRole("group", { name: "Bekräftad" }), {
      dataTransfer: dt,
    });

    await waitFor(() =>
      expect(showApplicationToast).toHaveBeenCalledWith(
        expect.objectContaining({
          kind: "statusChange",
          applicationId: "Submitted-0",
          from: "Submitted",
          to: "Acknowledged",
        }),
      ),
    );
  });

  it("misslyckad flytt publicerar error-toasten (optimistiken auto-återgår)", async () => {
    vi.mocked(transitionStatusAction).mockResolvedValueOnce({
      success: false,
      error: "Statusbytet misslyckades.",
    });
    renderBoard(makeGroups({ Submitted: 1 }));
    const card = within(
      screen.getByRole("group", { name: "Skickad" }),
    ).getByRole("article");
    const dt = dataTransferFor("Submitted-0");

    fireEvent.dragStart(card, { dataTransfer: dt });
    fireEvent.drop(screen.getByRole("group", { name: "Bekräftad" }), {
      dataTransfer: dt,
    });

    await waitFor(() =>
      expect(showApplicationToast).toHaveBeenCalledWith({
        kind: "error",
        message: "Statusbytet misslyckades.",
      }),
    );
  });
});
