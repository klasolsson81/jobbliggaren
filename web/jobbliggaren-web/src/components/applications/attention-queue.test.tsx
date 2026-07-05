import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
  return render(<AttentionQueue groups={groups} now={FIXED_NOW} />);
}

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
});
