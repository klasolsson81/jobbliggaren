import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { ApplicationsPipeline } from "./applications-pipeline";
import { ApplicationRow } from "./application-row";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  ApplicationStatus,
  JobAdSummaryDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";

// next/link renderas som <a> i jsdom utan extra mock.

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
};

function makeApplication(
  overrides: Partial<ApplicationDto> = {}
): ApplicationDto {
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

// Bygger pipelinen. `signals` mappar status -> per-rad attention-signaler
// (positionellt alignat med applications/rowSlots). Saknad post = "None".
function makePipeline(
  populated: Partial<Record<ApplicationStatus, number>>,
  signals: Partial<Record<ApplicationStatus, ApplicationAttentionSignal[]>> = {}
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
        })
      ),
    };
  });
}

// Serialiserbart slot-kontrakt (F3-mönster). page.tsx (RSC) server-renderar
// ApplicationRow-elementen och passar in dem som en ReactNode[]-map keyad på
// status — renderad ReactNode är serialiserbar över RSC→Client-gränsen, en
// render-prop-funktion är det INTE. rowSlots[status][i] är positionellt alignat
// med applications[i] (samma index bär .attentionSignal).
const FIXED_NOW = new Date("2026-05-20T12:00:00Z");

function makeRowSlots(
  groups: PipelineGroupDto[]
): Record<ApplicationStatus, ReactNode[]> {
  const slots = {} as Record<ApplicationStatus, ReactNode[]>;
  for (const group of groups) {
    slots[group.status] = group.applications.map((app) => (
      <ApplicationRow key={app.id} application={app} now={FIXED_NOW} />
    ));
  }
  return slots;
}

function renderPipeline(groups: PipelineGroupDto[]) {
  return render(
    <ApplicationsPipeline groups={groups} rowSlots={makeRowSlots(groups)} />
  );
}

describe("ApplicationsPipeline — kollapsbara statussektioner", () => {
  it("renderar bara sektioner med count > 0, i pipeline-ordning", () => {
    const groups = makePipeline({ Accepted: 1, Draft: 2 });
    renderPipeline(groups);

    expect(document.getElementById("status-Draft")).not.toBeNull();
    expect(document.getElementById("status-Accepted")).not.toBeNull();
    // Draft (aktiv) före Accepted (terminal) i DOM.
    const draftIdx = document.body.innerHTML.indexOf("status-Draft");
    const acceptedIdx = document.body.innerHTML.indexOf("status-Accepted");
    expect(draftIdx).toBeLessThan(acceptedIdx);
  });

  it("section head är en knapp med aria-expanded + aria-controls", () => {
    const groups = makePipeline({ Submitted: 1 });
    renderPipeline(groups);

    const section = document.getElementById("status-Submitted")!;
    const toggle = within(section).getByRole("button", { name: /Skickad/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(toggle).toHaveAttribute("aria-controls", "status-Submitted-list");
    expect(document.getElementById("status-Submitted-list")).not.toBeNull();
  });

  it("WAI accordion: h2 wrappar knappen, ingen aria-label, count i namnet", () => {
    // Blocker B-regression: (1) en <h2> WRAPPAR knappen (navigerbar rubrik),
    // (2) ingen aria-label överrider subträdet, så "visar X av Y" hamnar i
    // knappens accessible name och annonseras.
    const groups = makePipeline({ Submitted: 3 });
    renderPipeline(groups);

    const heading = screen.getByRole("heading", { name: /Skickad/ });
    const toggle = within(heading).getByRole("button");
    expect(toggle).not.toHaveAttribute("aria-label");
    // Accessible name = synlig text (label + count), inte en aria-label.
    const name = toggle.getAttribute("aria-label") ?? toggle.textContent ?? "";
    expect(name).toContain("Skickad");
    expect(name).toContain("visar 3 av 3");
  });

  it("aktiva tillstånd är öppna, terminala är kollapsade vid sidladdning", () => {
    const groups = makePipeline({ Submitted: 1, Rejected: 1 });
    renderPipeline(groups);

    const submitted = within(
      document.getElementById("status-Submitted")!
    ).getByRole("button", { name: /Skickad/ });
    const rejected = within(
      document.getElementById("status-Rejected")!
    ).getByRole("button", { name: /Nekad/ });

    expect(submitted).toHaveAttribute("aria-expanded", "true");
    expect(rejected).toHaveAttribute("aria-expanded", "false");
    // Kollapsad sektion: rad-listan är inte i DOM.
    expect(document.getElementById("status-Submitted-list")).not.toBeNull();
    expect(document.getElementById("status-Rejected-list")).toBeNull();
  });

  it("klick på head växlar aria-expanded och visar/döljer raderna", async () => {
    const user = userEvent.setup();
    const groups = makePipeline({ Submitted: 1 });
    renderPipeline(groups);

    const toggle = screen.getByRole("button", { name: /Skickad/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(document.getElementById("status-Submitted-list")).toBeNull();

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(document.getElementById("status-Submitted-list")).not.toBeNull();
  });

  it("head visar 'visar {shown} av {total}' (backend-total)", () => {
    const groups = makePipeline({ Submitted: 3 });
    renderPipeline(groups);

    const section = document.getElementById("status-Submitted")!;
    expect(section.querySelector(".jp-section__count")).toHaveTextContent(
      "visar 3 av 3"
    );
  });
});

describe("ApplicationsPipeline — Visa fler (cap 10)", () => {
  it("visar max 10 rader och en 'Visa fler (N)'-knapp", () => {
    const groups = makePipeline({ Submitted: 13 });
    renderPipeline(groups);

    const section = document.getElementById("status-Submitted")!;
    const list = document.getElementById("status-Submitted-list")!;
    // 10 av 13 synliga.
    expect(within(list).getAllByRole("link")).toHaveLength(10);
    // Knappen anger antalet dolda.
    expect(
      within(section).getByRole("button", { name: "Visa fler (3)" })
    ).toBeInTheDocument();
    // Count förblir sanningsenlig backend-total.
    expect(section.querySelector(".jp-section__count")).toHaveTextContent(
      "visar 13 av 13"
    );
  });

  it("klick på 'Visa fler' expanderar hela sektionen och tar bort knappen", async () => {
    const user = userEvent.setup();
    const groups = makePipeline({ Submitted: 13 });
    renderPipeline(groups);

    await user.click(screen.getByRole("button", { name: "Visa fler (3)" }));

    const list = document.getElementById("status-Submitted-list")!;
    expect(within(list).getAllByRole("link")).toHaveLength(13);
    expect(
      screen.queryByRole("button", { name: /Visa fler/ })
    ).not.toBeInTheDocument();
  });

  it("ingen 'Visa fler' när raderna ryms under cap", () => {
    const groups = makePipeline({ Submitted: 10 });
    renderPipeline(groups);

    expect(
      screen.queryByRole("button", { name: /Visa fler/ })
    ).not.toBeInTheDocument();
  });
});

describe("ApplicationsPipeline — Kräver åtgärd-feed (MOVE)", () => {
  it("lyfter en ansökan med signal till feeden med en orsaksrad", () => {
    const groups = makePipeline(
      { Submitted: 1 },
      { Submitted: ["OverdueFollowUp"] }
    );
    renderPipeline(groups);

    const feed = screen.getByRole("region", { name: "Kräver åtgärd" });
    expect(
      within(feed).getByRole("heading", { name: "Kräver åtgärd" })
    ).toHaveAttribute("id", "attention-heading");
    expect(
      within(feed).getByText("Uppföljningen har passerat sin tid.")
    ).toBeInTheDocument();
    expect(within(feed).getByRole("link")).toBeInTheDocument();
  });

  it("MOVE: en ansökan med signal renderas EXAKT en gång (feed, ej sektionen)", () => {
    const groups = makePipeline(
      { Submitted: 1 },
      { Submitted: ["OverdueFollowUp"] }
    );
    renderPipeline(groups);

    // Endast en rad-länk totalt (feeden), inte två.
    expect(screen.getAllByRole("link")).toHaveLength(1);
    // Den enda Submitted-raden är lyft → sektionen dräneras helt → ingen rubrik.
    expect(document.getElementById("status-Submitted")).toBeNull();
  });

  it("blandad grupp: lyft rad i feeden, övriga kvar i sektionen", () => {
    const groups = makePipeline(
      { Submitted: 2 },
      { Submitted: ["OverdueFollowUp", "None"] }
    );
    renderPipeline(groups);

    const feed = screen.getByRole("region", { name: "Kräver åtgärd" });
    const section = document.getElementById("status-Submitted")!;

    // index 0 (lyft) i feeden, ej i sektionen.
    expect(within(feed).getByText("Submitted-titel-0")).toBeInTheDocument();
    expect(
      within(section).queryByText("Submitted-titel-0")
    ).not.toBeInTheDocument();
    // index 1 (None) i sektionen, ej i feeden.
    expect(within(section).getByText("Submitted-titel-1")).toBeInTheDocument();
    expect(
      within(feed).queryByText("Submitted-titel-1")
    ).not.toBeInTheDocument();
    // Count = backend-total (2), shown = 1.
    expect(section.querySelector(".jp-section__count")).toHaveTextContent(
      "visar 1 av 2"
    );
  });

  it("deploy-skew: undefined attentionSignal stannar i sektionen (ej feeden)", () => {
    // Äldre BE-svar utan fältet → application.attentionSignal === undefined.
    // isFiringSignal(undefined) === false → raden ska INTE lyftas.
    const application = makeApplication({
      id: "Submitted-skew-0000-0000-000000000000",
      status: "Submitted",
      jobAd: { ...jobAd, title: "Skew-titel" },
      attentionSignal: undefined,
    });
    const groups: PipelineGroupDto[] = PIPELINE_ORDER.map((status) => ({
      status,
      count: status === "Submitted" ? 1 : 0,
      applications: status === "Submitted" ? [application] : [],
    }));
    renderPipeline(groups);

    // Ingen feed (ingen fyrande signal).
    expect(
      screen.queryByRole("region", { name: "Kräver åtgärd" })
    ).not.toBeInTheDocument();
    // Raden renderas i sin statussektion.
    const section = document.getElementById("status-Submitted")!;
    expect(within(section).getByText("Skew-titel")).toBeInTheDocument();
    expect(section.querySelector(".jp-section__count")).toHaveTextContent(
      "visar 1 av 1"
    );
  });

  it("ordnar feeden på signalprioritet (offer → overdue → nudge)", () => {
    const groups = makePipeline(
      { Submitted: 1, OfferReceived: 1, Acknowledged: 1 },
      {
        Submitted: ["NoResponseNudge"],
        OfferReceived: ["OfferAwaitingReply"],
        Acknowledged: ["OverdueFollowUp"],
      }
    );
    renderPipeline(groups);

    const feed = screen.getByRole("region", { name: "Kräver åtgärd" });
    const reasons = within(feed)
      .getAllByRole("link")
      .map((link) => link.textContent ?? "");
    // OfferReceived-raden (OfferAwaitingReply) först, Acknowledged (Overdue),
    // Submitted (nudge) sist.
    expect(reasons[0]).toContain("OfferReceived-titel-0");
    expect(reasons[1]).toContain("Acknowledged-titel-0");
    expect(reasons[2]).toContain("Submitted-titel-0");
  });

  it("tom feed (inga signaler) → ingen 'Kräver åtgärd'-rubrik", () => {
    const groups = makePipeline({ Submitted: 2 });
    renderPipeline(groups);

    expect(
      screen.queryByRole("region", { name: "Kräver åtgärd" })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("Kräver åtgärd")
    ).not.toBeInTheDocument();
  });

  it("feeden capas ALDRIG (allt som kräver åtgärd visas, även > 10)", () => {
    const signals = Array.from(
      { length: 12 },
      () => "OverdueFollowUp" as ApplicationAttentionSignal
    );
    const groups = makePipeline({ Submitted: 12 }, { Submitted: signals });
    renderPipeline(groups);

    const feed = screen.getByRole("region", { name: "Kräver åtgärd" });
    expect(within(feed).getAllByRole("link")).toHaveLength(12);
    expect(
      within(feed).queryByRole("button", { name: /Visa fler/ })
    ).not.toBeInTheDocument();
  });
});

describe("ApplicationsPipeline — empty state", () => {
  it("visar civic empty-state när allt är tomt", () => {
    const groups = makePipeline({});
    renderPipeline(groups);

    expect(
      screen.getByText("Inga ansökningar i den här statusen")
    ).toBeInTheDocument();
  });
});
