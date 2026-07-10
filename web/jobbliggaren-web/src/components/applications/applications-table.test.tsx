import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationActionsProvider } from "./application-actions";
import { ApplicationsTable } from "./applications-table";
import { dismissApplicationToast } from "@/lib/applications/toast-store";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  ApplicationStatus,
} from "@/lib/dto/applications";

// Render-trädet importerar tre actions statiskt (tabellen: batchTransitionAction;
// provider + finish-draft-dialog: transitionStatusAction; log-follow-up-dialog:
// logFollowUpAction) — alla mockas så inget verkligt server-anrop sker.
const batchTransitionAction = vi.hoisted(() =>
  vi.fn<
    (items: { applicationId: string; targetStatus: string }[]) => Promise<{
      success: true;
    }>
  >(async () => ({ success: true })),
);
const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  batchTransitionAction,
  transitionStatusAction,
  logFollowUpAction,
}));

// FIXED referenstidpunkt trädas in som `now` (date-flake-fri, #336).
const NOW = new Date("2026-07-10T12:00:00Z");

function makeRow(
  id: string,
  title: string,
  status: ApplicationStatus,
  lastStatusChangeAt: string,
  attentionSignal?: ApplicationAttentionSignal,
): ApplicationDto {
  return {
    id,
    jobSeekerId: "seeker-1",
    jobAdId: `ad-${id}`,
    status,
    createdAt: "2026-05-01T00:00:00Z",
    updatedAt: "2026-05-01T00:00:00Z",
    lastStatusChangeAt,
    attentionSignal,
    jobAd: {
      jobAdId: `ad-${id}`,
      title,
      company: `Företag ${title}`,
      url: null,
      source: "Manual",
      publishedAt: null,
      expiresAt: null,
    },
  };
}

// 5 rader med distinkta statusar + lastStatusChangeAt. daysInStatus mot NOW:
//   Beta 9 dgr (äldst) · Gamma 5 · Delta 2 · Alfa 1 · Epsilon 0 (nyast)
function fiveRows(): ApplicationDto[] {
  return [
    makeRow("id-a", "Alfa", "Draft", "2026-07-09T00:00:00Z"),
    makeRow("id-b", "Beta", "Submitted", "2026-07-01T00:00:00Z"),
    makeRow("id-c", "Gamma", "Acknowledged", "2026-07-05T00:00:00Z"),
    makeRow("id-d", "Delta", "Interviewing", "2026-07-08T00:00:00Z"),
    makeRow("id-e", "Epsilon", "OfferReceived", "2026-07-10T00:00:00Z"),
  ];
}

function renderTable(rows: ApplicationDto[]) {
  return render(
    <ApplicationActionsProvider>
      <ApplicationsTable rows={rows} now={NOW} />
    </ApplicationActionsProvider>,
  );
}

function dataRows(): HTMLElement[] {
  return screen.getAllByRole("row").slice(1); // släpp rubrikraden
}

function firstRowTitle(): string | null | undefined {
  return within(dataRows()[0]!).getByRole("link").textContent;
}

function sortHeader(column: string): HTMLElement {
  return screen
    .getByRole("button", { name: `Sortera på ${column}` })
    .closest("th") as HTMLElement;
}

function batchArg(): { applicationId: string; targetStatus: string }[] {
  return batchTransitionAction.mock.calls[0]![0];
}

beforeEach(() => {
  batchTransitionAction.mockClear();
  transitionStatusAction.mockClear();
  logFollowUpAction.mockClear();
});

afterEach(() => {
  dismissApplicationToast();
});

describe("ApplicationsTable — sortering (#630 PR 10, design §7)", () => {
  it("(a) default = 'I steget' fallande: äldsta lastStatusChangeAt överst, th aria-sort=descending", () => {
    renderTable(fiveRows());

    expect(firstRowTitle()).toBe("Beta"); // 9 dgr → längst väntan
    expect(sortHeader("I steget")).toHaveAttribute("aria-sort", "descending");
  });

  it("(b) klick på 'Roll & företag' sorterar om (första raden byts), flyttar aria-sort; aktiv kolumn vänder asc↔desc", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());
    expect(firstRowTitle()).toBe("Beta");

    await user.click(screen.getByRole("button", { name: "Sortera på Roll & företag" }));
    expect(firstRowTitle()).toBe("Alfa"); // role stigande (A→Ö)

    const active = screen
      .getAllByRole("columnheader")
      .filter((h) => {
        const s = h.getAttribute("aria-sort");
        return s != null && s !== "none";
      });
    expect(active).toHaveLength(1);
    expect(sortHeader("Roll & företag")).toHaveAttribute("aria-sort", "ascending");

    // Klick på aktiv kolumn vänder riktningen.
    await user.click(screen.getByRole("button", { name: "Sortera på Roll & företag" }));
    expect(sortHeader("Roll & företag")).toHaveAttribute("aria-sort", "descending");
    expect(firstRowTitle()).toBe("Gamma"); // role fallande (Ö→A)
  });
});

describe("ApplicationsTable — urval (efemärt, sidbundet)", () => {
  it("(c) radkryssruta togglar; rubrik-kryssrutan markerar hela sidan; delvis urval → indeterminate", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());

    const alfa = screen.getByRole("checkbox", { name: "Markera Alfa" });
    expect(alfa).not.toBeChecked();
    await user.click(alfa);
    expect(alfa).toBeChecked();

    const selectAll = screen.getByRole("checkbox", {
      name: "Markera alla på sidan",
    }) as HTMLInputElement;
    await user.click(selectAll);
    for (const title of ["Alfa", "Beta", "Gamma", "Delta", "Epsilon"]) {
      expect(
        screen.getByRole("checkbox", { name: `Markera ${title}` }),
      ).toBeChecked();
    }
    expect(selectAll).toBeChecked();
    expect(selectAll.indeterminate).toBe(false);

    // Avmarkera en → delvis urval → indeterminate på DOM-propertyn.
    await user.click(screen.getByRole("checkbox", { name: "Markera Beta" }));
    expect(selectAll).not.toBeChecked();
    expect(selectAll.indeterminate).toBe(true);
  });

  it("(d) bulkraden döljs vid 0 markerade, visar '2 valda' vid 2, 'Rensa urval' tömmer", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());

    expect(
      screen.queryByRole("button", { name: "Rensa urval" }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("checkbox", { name: "Markera Alfa" }));
    await user.click(screen.getByRole("checkbox", { name: "Markera Beta" }));
    expect(screen.getByText("2 valda")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Rensa urval" }));
    expect(
      screen.queryByRole("button", { name: "Rensa urval" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Markera Alfa" }),
    ).not.toBeChecked();
  });
});

describe("ApplicationsTable — bulkåtgärder (#630 PR 10)", () => {
  it("(e) 'Markera Inget svar' kör batchTransitionAction DIREKT (ingen dialog) → Ghosted; urvalet töms vid success", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());

    await user.click(screen.getByRole("checkbox", { name: "Markera Alfa" }));
    await user.click(screen.getByRole("checkbox", { name: "Markera Delta" }));

    await user.click(screen.getByRole("button", { name: "Markera Inget svar" }));

    await waitFor(() =>
      expect(batchTransitionAction).toHaveBeenCalledTimes(1),
    );
    const arg = batchArg();
    expect(arg).toHaveLength(2);
    expect(arg.every((i) => i.targetStatus === "Ghosted")).toBe(true);
    expect(new Set(arg.map((i) => i.applicationId))).toEqual(
      new Set(["id-a", "id-d"]),
    );

    // Ingen bekräftelsedialog för det icke-destruktiva bytet.
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    // Urvalet töms vid success → bulkraden avmonteras.
    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: "Rensa urval" }),
      ).not.toBeInTheDocument(),
    );
  });

  it("(f) 'Markera Nekad' öppnar bekräftelsedialog (ingen action direkt); bekräfta → batchTransitionAction med Rejected", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());

    await user.click(screen.getByRole("checkbox", { name: "Markera Alfa" }));
    await user.click(screen.getByRole("checkbox", { name: "Markera Beta" }));

    await user.click(screen.getByRole("button", { name: "Markera Nekad" }));

    const dialog = await screen.findByRole("dialog");
    expect(batchTransitionAction).not.toHaveBeenCalled();
    expect(
      within(dialog).getByText(/Markera 2 ansökningar som Nekad/),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Markera Nekad" }));

    await waitFor(() =>
      expect(batchTransitionAction).toHaveBeenCalledTimes(1),
    );
    const arg = batchArg();
    expect(arg.every((i) => i.targetStatus === "Rejected")).toBe(true);
    expect(new Set(arg.map((i) => i.applicationId))).toEqual(
      new Set(["id-a", "id-b"]),
    );
  });

  it("(f) 'Avbryt' i bekräftelsedialogen stänger utan att anropa action", async () => {
    const user = userEvent.setup();
    renderTable(fiveRows());

    await user.click(screen.getByRole("checkbox", { name: "Markera Alfa" }));
    await user.click(screen.getByRole("button", { name: "Markera Nekad" }));

    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(batchTransitionAction).not.toHaveBeenCalled();
  });
});

describe("ApplicationsTable — paginering (klient-side, Option B)", () => {
  it("(g) 60 rader → 50 på sida 1 + footer + pager; sida 2 visar 10 och TÖMMER urval", async () => {
    const user = userEvent.setup();
    const rows = Array.from({ length: 60 }, (_, i) =>
      makeRow(
        `id-${i}`,
        `Roll ${String(i).padStart(2, "0")}`,
        "Submitted",
        `2026-06-${String((i % 28) + 1).padStart(2, "0")}T00:00:00Z`,
      ),
    );
    renderTable(rows);

    expect(document.querySelectorAll("tbody tr")).toHaveLength(50);
    expect(
      screen.getByText("Visar 50 av 60 ansökningar"),
    ).toBeInTheDocument();
    const pager = screen.getByRole("navigation", { name: "Paginering" });

    // Markera hela sida 1 …
    await user.click(screen.getByRole("checkbox", { name: "Markera alla på sidan" }));
    expect(screen.getByText("50 valda")).toBeInTheDocument();

    // … och byt sida: urvalet nollställs, sida 2 har de återstående 10.
    // (accname trimmar sr-only-prefixets efterföljande blanksteg → "Sida2";
    // \s* tål både "Sida 2" och "Sida2".)
    await user.click(within(pager).getByRole("button", { name: /^Sida\s*2$/ }));
    expect(document.querySelectorAll("tbody tr")).toHaveLength(10);
    expect(
      screen.queryByRole("button", { name: "Rensa urval" }),
    ).not.toBeInTheDocument();
  });

  it("(g) <=50 rader → ingen pager", () => {
    renderTable(fiveRows());
    expect(
      screen.queryByRole("navigation", { name: "Paginering" }),
    ).not.toBeInTheDocument();
  });
});

describe("ApplicationsTable — tomt + varningsfärgning", () => {
  it("(h) tom rows → role=status tomt-meddelande, ingen tabell", () => {
    renderTable([]);
    expect(screen.getByRole("status")).toHaveTextContent(
      "Inga ansökningar matchar sökningen eller filtret.",
    );
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("(i) 'I steget'-cellen får data-waiting endast för en väntande signal, ej OfferAwaitingReply/None", () => {
    renderTable([
      makeRow("id-w", "Väntar", "Submitted", "2026-07-05T00:00:00Z", "NoResponseNudge"),
      makeRow("id-o", "Erbjuden", "OfferReceived", "2026-07-05T00:00:00Z", "OfferAwaitingReply"),
      makeRow("id-n", "Neutral", "Acknowledged", "2026-07-05T00:00:00Z", "None"),
    ]);

    const stepOf = (title: string) =>
      screen
        .getByRole("link", { name: title })
        .closest("tr")!
        .querySelector(".jp-apptable__step");

    expect(stepOf("Väntar")).toHaveAttribute("data-waiting");
    expect(stepOf("Erbjuden")).not.toHaveAttribute("data-waiting");
    expect(stepOf("Neutral")).not.toHaveAttribute("data-waiting");
  });
});
