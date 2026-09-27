import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationActionsProvider } from "./application-actions";
import { AttentionQueue } from "./attention-queue";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  ApplicationStatus,
  JobAdSummaryDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";

const FIXED_NOW = new Date("2026-05-20T12:00:00Z");

// #630 PR 7: radernas CTA muterar via providerns server actions och "Läs
// erbjudandet" soft-navigerar till detaljmodalen — mocka båda sömmarna.
const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction,
  logFollowUpAction,
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({
      push: routerPush,
      back: vi.fn(),
      refresh: vi.fn(),
      replace: vi.fn(),
      prefetch: vi.fn(),
    }),
  };
});

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
};

function makeApplication(overrides: Partial<ApplicationDto> = {}): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    lastStatusChangeAt: "2026-05-10",
    jobAd,
    ...overrides,
  };
}

function makePipeline(
  populated: Partial<Record<ApplicationStatus, number>>,
  signals: Partial<Record<ApplicationStatus, ApplicationAttentionSignal[]>> = {},
): PipelineGroupDto[] {
  return PIPELINE_ORDER.map((status) => {
    const n = populated[status] ?? 0;
    const sig = signals[status] ?? [];
    return {
      status,
      count: n,
      applications: Array.from({ length: n }, (_, i) =>
        makeApplication({
          id: `${status}-${i}-0000-0000-000000000000`,
          status,
          jobAd: { ...jobAd, title: `${status}-titel-${i}` },
          attentionSignal: sig[i] ?? "None",
        }),
      ),
    };
  });
}

function renderQueue(groups: PipelineGroupDto[]) {
  return render(
    <ApplicationActionsProvider>
      <AttentionQueue groups={groups} now={FIXED_NOW} />
    </ApplicationActionsProvider>,
  );
}

beforeEach(() => {
  transitionStatusAction.mockClear();
  logFollowUpAction.mockClear();
  routerPush.mockClear();
});

describe("AttentionQueue", () => {
  it("lyfter ansökningar med fyrande signal som liggarrader i en ordnad lista (#1827 M1)", () => {
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );

    const queue = screen.getByRole("region", { name: "Kräver åtgärd" });
    expect(
      within(queue).getByRole("heading", { name: "Kräver åtgärd" }),
    ).toHaveAttribute("id", "attention-heading");
    // #1827 M2: the kickers show the order; no line explains the sorting.
    expect(within(queue).queryByText(/Sorterat på/)).not.toBeInTheDocument();
    const rows = within(queue).getAllByRole("listitem");
    expect(rows).toHaveLength(1);
    expect(rows[0]!.parentElement?.tagName).toBe("OL");
    expect(
      within(rows[0]!).getByText("Uppföljning försenad"),
    ).toBeInTheDocument();
    // Radens enda länk är titeln (länk-overlayn); företaget står bredvid.
    expect(within(rows[0]!).getAllByRole("link")).toHaveLength(1);
    expect(within(rows[0]!).getByText("Volvo")).toBeInTheDocument();
  });

  it("signalkickern bär signalens färg-bucket (warning för OverdueFollowUp)", () => {
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );
    expect(screen.getByText("Uppföljning försenad")).toHaveAttribute(
      "data-signal",
      "warning",
    );
  });

  // lastStatusChangeAt 2026-05-10 mot FIXED_NOW 2026-05-20 → 10 dagar; annonsens
  // sista ansökningsdag 2026-06-01. Erbjudande och försenad uppföljning har inget
  // värde i list-DTO:n och visar bara kickern.
  it.each([
    ["NoResponseNudge", "Submitted", "Väntar på svar", "10 dagar"],
    ["GhostSuggested", "Submitted", "Väntar på svar", "10 dagar"],
    [
      "SilentAfterInterview",
      "Interviewing",
      "Väntar på svar efter intervjun",
      "10 dagar",
    ],
    ["DraftDeadlineApproaching", "Draft", "Sista ansökningsdag", "1 juni"],
    ["OfferAwaitingReply", "OfferReceived", "Erbjudande", null],
    ["OverdueFollowUp", "Submitted", "Uppföljning försenad", null],
  ] as const)(
    "%s: kickern följs av urgens-hjälparens värde, eller står ensam",
    (signal, status, kicker, value) => {
      const { container } = renderQueue(
        makePipeline({ [status]: 1 }, { [status]: [signal] }),
      );
      const row = container.querySelector(".jp-attentionqueue__row")!;
      expect(row.querySelector(".jp-attentionqueue__kicker")).toHaveTextContent(
        kicker,
      );
      const valueEl = row.querySelector(".jp-attentionqueue__value");
      if (value == null) expect(valueEl).toBeNull();
      else expect(valueEl).toHaveTextContent(value);
    },
  );

  it("länkens beskrivning är kicker, värde och företag", () => {
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["NoResponseNudge"] }),
    );
    expect(
      screen.getByRole("link", {
        name: "Submitted-titel-0",
        description: "Väntar på svar, 10 dagar, Volvo",
      }),
    ).toBeInTheDocument();
  });

  it("köraden bär varken statustagg, dagar i steget, händelserad eller bråttom-tagg", () => {
    const { container } = renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["NoResponseNudge"] }),
    );
    const row = container.querySelector(".jp-attentionqueue__row")!;
    expect(row.querySelector("[data-tag]")).toBeNull();
    expect(row.querySelector("[data-urgency]")).toBeNull();
    expect(row.textContent).not.toMatch(/i steget|Ansökan skickad/);
  });

  // #892 (CTO R1): en raderad annons bär den bevarade identiteten, som utan
  // borttagen-markören skulle se levande ut; kön visar markören som Lista-raden.
  it("en raderad annons behåller borttagen-markören i köraden och i beskrivningen", () => {
    const erased = makeApplication({
      id: "erased-0",
      attentionSignal: "NoResponseNudge",
      jobAd: {
        ...jobAd,
        title: "Bevarad roll",
        company: "Bevarat AB",
        status: "Erased",
      },
    });
    const groups: PipelineGroupDto[] = PIPELINE_ORDER.map((status) => ({
      status,
      count: status === "Submitted" ? 1 : 0,
      applications: status === "Submitted" ? [erased] : [],
    }));
    renderQueue(groups);

    expect(screen.getByText("Annonsen är borttagen")).toHaveClass("jp-tag");
    expect(
      screen.getByRole("link", {
        name: "Bevarad roll",
        description:
          "Väntar på svar, 10 dagar, Bevarat AB, Annonsen är borttagen",
      }),
    ).toBeInTheDocument();
  });

  it("ordnar raderna på signalprioritet (offer → overdue → nudge)", () => {
    renderQueue(
      makePipeline(
        { Submitted: 1, OfferReceived: 1, Acknowledged: 1 },
        {
          Submitted: ["NoResponseNudge"],
          OfferReceived: ["OfferAwaitingReply"],
          Acknowledged: ["OverdueFollowUp"],
        },
      ),
    );

    const titles = screen
      .getAllByRole("link")
      .map((link) => link.textContent ?? "");
    expect(titles[0]).toContain("OfferReceived-titel-0");
    expect(titles[1]).toContain("Acknowledged-titel-0");
    expect(titles[2]).toContain("Submitted-titel-0");
  });

  it("kapar till 4 synliga rader och expanderar med 'Visa N till'", async () => {
    const user = userEvent.setup();
    const signals = Array.from(
      { length: 6 },
      () => "OverdueFollowUp" as ApplicationAttentionSignal,
    );
    renderQueue(makePipeline({ Submitted: 6 }, { Submitted: signals }));

    expect(screen.getAllByRole("link")).toHaveLength(4);
    const more = screen.getByRole("button", { name: "Visa 2 till" });
    await user.click(more);
    expect(screen.getAllByRole("link")).toHaveLength(6);
    expect(
      screen.getByRole("button", { name: "Visa färre" }),
    ).toBeInTheDocument();
  });

  it("tom kö: en rad text, ingen lista", () => {
    renderQueue(makePipeline({ Submitted: 2 }));

    expect(screen.getByText("Inget kräver åtgärd just nu.").tagName).toBe("P");
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    // Rubriken finns kvar (2a — kön är alltid synlig).
    expect(
      screen.getByRole("heading", { name: "Kräver åtgärd" }),
    ).toBeInTheDocument();
  });

  it("deploy-skew: undefined attentionSignal lyfts inte", () => {
    const app = makeApplication({
      id: "skew-0",
      attentionSignal: undefined,
    });
    const groups: PipelineGroupDto[] = PIPELINE_ORDER.map((status) => ({
      status,
      count: status === "Submitted" ? 1 : 0,
      applications: status === "Submitted" ? [app] : [],
    }));
    renderQueue(groups);

    expect(
      screen.getByText("Inget kräver åtgärd just nu."),
    ).toBeInTheDocument();
  });

  // ── §11 rad-CTA (PR 7, Klas-låst: ingår) ────────────────────────────────

  // #1827 B1: the signal is a Pending follow-up past its date, and only its outcome clears it,
  // so the card opens the detail, where the outcome form is, instead of logging a new contact.
  it("OverdueFollowUp-raden: primär 'Registrera utfall' öppnar detaljmodalen; ingen statusmeny", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );

    expect(
      screen.queryByRole("button", { name: "Byt status" }),
    ).not.toBeInTheDocument();
    // Kortets primär är urgens-CTA:n, INTE radens "Flytta till nästa".
    expect(screen.queryByText(/Flytta till/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Registrera utfall" }));
    expect(routerPush).toHaveBeenCalledWith(
      "/ansokningar/Submitted-0-0000-0000-000000000000",
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(logFollowUpAction).not.toHaveBeenCalled();
  });

  it("GhostSuggested-raden: 'Markera som Inget svar' är en direkt transition till Ghosted", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["GhostSuggested"] }),
    );

    await user.click(
      screen.getByRole("button", { name: "Markera som Inget svar" }),
    );
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "Submitted-0-0000-0000-000000000000",
        "Ghosted",
      ),
    );
    // Sekundären finns också (§11).
    expect(
      screen.getByRole("button", { name: "Följ upp igen" }),
    ).toBeInTheDocument();
  });

  it("OfferAwaitingReply-raden: 'Läs erbjudandet' öppnar detaljmodalen", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ OfferReceived: 1 }, { OfferReceived: ["OfferAwaitingReply"] }),
    );

    await user.click(screen.getByRole("button", { name: "Läs erbjudandet" }));
    expect(routerPush).toHaveBeenCalledWith(
      "/ansokningar/OfferReceived-0-0000-0000-000000000000",
    );
  });

  // #1827 item 10: accepting deletes the saved copy's text, which undo does not bring back.
  it.each([
    [
      true,
      "Annonstexten och eventuella kontaktuppgifter i den sparade kopian raderas och kommer inte tillbaka om du ångrar.",
    ],
    [
      undefined,
      "Om ansökan har en sparad kopia av annonsen raderas dess text och eventuella kontaktuppgifter och kommer inte tillbaka om du ångrar.",
    ],
  ])(
    "OfferAwaitingReply-raden: 'Acceptera' asks first when the flag is %s, and moves on confirm",
    async (flag, body) => {
      const user = userEvent.setup();
      const groups = makePipeline(
        { OfferReceived: 1 },
        { OfferReceived: ["OfferAwaitingReply"] },
      ).map((group) => ({
        ...group,
        applications: group.applications.map((a) => ({
          ...a,
          hasPreservedAdText: flag,
        })),
      }));
      renderQueue(groups);

      await user.click(screen.getByRole("button", { name: "Acceptera" }));
      const dialog = await screen.findByRole("dialog", {
        name: "Markera som Accepterad?",
      });
      expect(dialog).toHaveAccessibleDescription(body);
      expect(transitionStatusAction).not.toHaveBeenCalled();

      await user.click(
        within(dialog).getByRole("button", { name: "Markera som Accepterad" }),
      );
      await waitFor(() =>
        expect(transitionStatusAction).toHaveBeenCalledWith(
          "OfferReceived-0-0000-0000-000000000000",
          "Accepted",
        ),
      );
    },
  );

  it("OfferAwaitingReply-raden: 'Acceptera' moves at once when the copy has no text", async () => {
    const user = userEvent.setup();
    const groups = makePipeline(
      { OfferReceived: 1 },
      { OfferReceived: ["OfferAwaitingReply"] },
    ).map((group) => ({
      ...group,
      applications: group.applications.map((a) => ({
        ...a,
        hasPreservedAdText: false,
      })),
    }));
    renderQueue(groups);

    await user.click(screen.getByRole("button", { name: "Acceptera" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "OfferReceived-0-0000-0000-000000000000",
        "Accepted",
      ),
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("DraftDeadlineApproaching-raden: 'Slutför och skicka' öppnar dialogen (mellansteg, ingen direkt transition)", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ Draft: 1 }, { Draft: ["DraftDeadlineApproaching"] }),
    );

    await user.click(
      screen.getByRole("button", { name: "Slutför och skicka" }),
    );
    expect(
      await screen.findByRole("button", { name: "Skicka ansökan" }),
    ).toBeInTheDocument();
    expect(transitionStatusAction).not.toHaveBeenCalled();
  });
});
