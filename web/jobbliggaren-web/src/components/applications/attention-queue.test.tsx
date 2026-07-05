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

// #630 PR 7: kortens CTA muterar via providerns server actions och "Läs
// erbjudandet" soft-navigerar till drawern — mocka båda sömmarna.
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
  it("lyfter ansökningar med fyrande signal som åtgärdskort med orsaksrad", () => {
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );

    const queue = screen.getByRole("region", { name: "Kräver åtgärd" });
    expect(
      within(queue).getByRole("heading", { name: "Kräver åtgärd" }),
    ).toHaveAttribute("id", "attention-heading");
    expect(
      within(queue).getByText("Uppföljningen har passerat sin tid."),
    ).toBeInTheDocument();
    // Kortet bär den återbrukade raden (en länk → detalj).
    expect(within(queue).getByRole("link")).toBeInTheDocument();
  });

  it("färgbucket sätts på orsaksraden (warning för OverdueFollowUp)", () => {
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );
    const reason = document.querySelector(".jp-actioncard__reason");
    expect(reason).toHaveAttribute("data-signal", "warning");
  });

  it("ordnar korten på signalprioritet (offer → overdue → nudge)", () => {
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

  it("kapar till 4 synliga kort och expanderar med 'Visa N till'", async () => {
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

  it("tom kö: streckat tomläge, inga kort", () => {
    renderQueue(makePipeline({ Submitted: 2 }));

    expect(
      screen.getByText("Inget kräver åtgärd just nu."),
    ).toBeInTheDocument();
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

  // ── §11 kort-CTA (PR 7, Klas-låst: ingår) ────────────────────────────────

  it("OverdueFollowUp-kortet: primär 'Följ upp' öppnar Logga uppföljning-dialogen; ingen statusmeny", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ Submitted: 1 }, { Submitted: ["OverdueFollowUp"] }),
    );

    expect(
      screen.queryByRole("button", { name: "Byt status" }),
    ).not.toBeInTheDocument();
    // Kortets primär är urgens-CTA:n, INTE radens "Flytta till nästa".
    expect(screen.queryByText(/Flytta till/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Följ upp" }));
    expect(
      await screen.findByRole("dialog", { name: "Logga uppföljning" }),
    ).toBeInTheDocument();
    expect(logFollowUpAction).not.toHaveBeenCalled();
  });

  it("GhostSuggested-kortet: 'Markera som Inget svar' är en direkt transition till Ghosted", async () => {
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

  it("OfferAwaitingReply-kortet: 'Läs erbjudandet' öppnar panelen; 'Acceptera' är direkt transition", async () => {
    const user = userEvent.setup();
    renderQueue(
      makePipeline({ OfferReceived: 1 }, { OfferReceived: ["OfferAwaitingReply"] }),
    );

    await user.click(screen.getByRole("button", { name: "Läs erbjudandet" }));
    expect(routerPush).toHaveBeenCalledWith(
      "/ansokningar/OfferReceived-0-0000-0000-000000000000",
    );

    await user.click(screen.getByRole("button", { name: "Acceptera" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "OfferReceived-0-0000-0000-000000000000",
        "Accepted",
      ),
    );
  });

  it("DraftDeadlineApproaching-kortet: 'Slutför och skicka' öppnar dialogen (mellansteg, ingen direkt transition)", async () => {
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
