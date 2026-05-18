import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationsPipeline } from "./applications-pipeline";
import { ApplicationRow } from "./application-row";
import type {
  ApplicationDto,
  JobAdSummaryDto,
  PipelineGroupDto,
} from "@/lib/types/applications";

// next/link renderas som <a> i jsdom utan extra mock (Next 15 client Link).

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

// Bygger en full 10-status-pipeline. Endast statusarna i `populated` får
// applikationer; resten är count 0 (tomma — döljs i sektionsvyn men finns
// kvar i översiktsraden).
function makePipeline(
  populated: Partial<Record<ApplicationDto["status"], number>>
): PipelineGroupDto[] {
  const order: ApplicationDto["status"][] = [
    "Draft",
    "Submitted",
    "Acknowledged",
    "InterviewScheduled",
    "Interviewing",
    "OfferReceived",
    "Accepted",
    "Rejected",
    "Withdrawn",
    "Ghosted",
  ];
  return order.map((status) => {
    const n = populated[status] ?? 0;
    return {
      status,
      count: n,
      applications: Array.from({ length: n }, (_, i) => ({
        ...makeApplication({
          id: `${status}-${i}-0000-0000-000000000000`,
          status,
        }),
      })),
    };
  });
}

// Server-renderbar ApplicationRow passas in via render-prop (CTO punkt 4 —
// ApplicationRow förblir server-renderbar, görs inte client).
const renderRow = (app: ApplicationDto) => (
  <ApplicationRow key={app.id} application={app} />
);

describe("ApplicationsPipeline — översiktsrad (snabblänkar)", () => {
  it("renderar alla 10 PIPELINE_ORDER-statusar i fast ordning", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 2 })}
        renderRow={renderRow}
      />
    );

    const overview = screen.getByRole("navigation", {
      name: "Statusöversikt",
    });
    const items = Array.from(
      overview.querySelectorAll("[data-overview-item]")
    );
    expect(items).toHaveLength(10);

    const labels = items.map((el) => el.getAttribute("data-status"));
    expect(labels).toEqual([
      "Draft",
      "Submitted",
      "Acknowledged",
      "InterviewScheduled",
      "Interviewing",
      "OfferReceived",
      "Accepted",
      "Rejected",
      "Withdrawn",
      "Ghosted",
    ]);
  });

  it("0-count-status är ett inert <span> (inte <a>) och dämpat", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 1 })}
        renderRow={renderRow}
      />
    );

    const overview = screen.getByRole("navigation", {
      name: "Statusöversikt",
    });
    const draft = within(overview).getByTestId("overview-item-Draft");

    expect(draft.tagName).toBe("SPAN");
    expect(draft).not.toHaveAttribute("href");
    expect(draft.className).toContain("text-text-secondary");
    expect(draft).toHaveTextContent("Utkast");
    expect(draft).toHaveTextContent("0");
  });

  it("icke-tom status är en ankarlänk till #status-<Status>", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 3, Accepted: 1 })}
        renderRow={renderRow}
      />
    );

    const overview = screen.getByRole("navigation", {
      name: "Statusöversikt",
    });

    const submitted = within(overview).getByTestId("overview-item-Submitted");
    expect(submitted.tagName).toBe("A");
    expect(submitted).toHaveAttribute("href", "#status-Submitted");
    expect(submitted).toHaveTextContent("Skickad");
    expect(submitted).toHaveTextContent("3");

    const accepted = within(overview).getByTestId("overview-item-Accepted");
    expect(accepted.tagName).toBe("A");
    expect(accepted).toHaveAttribute("href", "#status-Accepted");
  });

  it("count renderas med font-mono (konsekvent med page.tsx)", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 7 })}
        renderRow={renderRow}
      />
    );

    const overview = screen.getByRole("navigation", {
      name: "Statusöversikt",
    });
    const submitted = within(overview).getByTestId("overview-item-Submitted");
    const count = within(submitted).getByText("7");
    expect(count.className).toContain("font-mono");
  });
});

describe("ApplicationsPipeline — sektioner och tom-grupp-filtrering", () => {
  it("renderar bara sektioner för grupper med count > 0, i pipeline-ordning", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Accepted: 1, Draft: 2 })}
        renderRow={renderRow}
      />
    );

    const sections = screen.getAllByRole("region");
    expect(sections).toHaveLength(2);
    // Draft före Accepted enligt PIPELINE_ORDER.
    expect(sections[0]).toHaveAttribute("id", "status-Draft");
    expect(sections[1]).toHaveAttribute("id", "status-Accepted");
  });

  it("sektioner har id=status-<Status> som matchar översiktsrad-href", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 1 })}
        renderRow={renderRow}
      />
    );

    const overview = screen.getByRole("navigation", {
      name: "Statusöversikt",
    });
    const link = within(overview).getByTestId("overview-item-Submitted");
    const href = link.getAttribute("href");
    expect(href).toBe("#status-Submitted");

    const section = document.getElementById("status-Submitted");
    expect(section).not.toBeNull();
    expect(section?.tagName).toBe("SECTION");
  });

  it("passar in server-renderad ApplicationRow korrekt (titel — företag)", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Submitted: 1 })}
        renderRow={renderRow}
      />
    );

    expect(
      screen.getByText("Backend-utvecklare — Volvo")
    ).toBeInTheDocument();
  });
});

describe("ApplicationsPipeline — kollaps-state-maskin", () => {
  it("alla grupper är expanderade vid sidladdning (default)", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Draft: 1, Submitted: 1 })}
        renderRow={renderRow}
      />
    );

    const toggles = screen.getAllByRole("button", { name: /minimera/i });
    expect(toggles).toHaveLength(2);
    for (const t of toggles) {
      expect(t).toHaveAttribute("aria-expanded", "true");
    }

    // Båda raderna synliga.
    expect(
      screen.getAllByText("Backend-utvecklare — Volvo")
    ).toHaveLength(2);
  });

  it("minimera en grupp döljer dess rader och flippar aria-expanded", async () => {
    const user = userEvent.setup();
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Draft: 1, Submitted: 1 })}
        renderRow={renderRow}
      />
    );

    const draftSection = document.getElementById("status-Draft")!;
    const draftToggle = within(draftSection).getByRole("button");

    expect(draftToggle).toHaveAttribute("aria-expanded", "true");
    await user.click(draftToggle);
    expect(draftToggle).toHaveAttribute("aria-expanded", "false");

    // Draft-panelen borta, men Submitted kvar (per-grupp-state, ej globalt).
    const submittedSection = document.getElementById("status-Submitted")!;
    const submittedToggle = within(submittedSection).getByRole("button");
    expect(submittedToggle).toHaveAttribute("aria-expanded", "true");
    expect(
      within(submittedSection).getByText("Backend-utvecklare — Volvo")
    ).toBeInTheDocument();
    expect(
      within(draftSection).queryByText("Backend-utvecklare — Volvo")
    ).not.toBeInTheDocument();
  });

  it("toggle tillbaka expanderar gruppen igen", async () => {
    const user = userEvent.setup();
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Draft: 1 })}
        renderRow={renderRow}
      />
    );

    const draftSection = document.getElementById("status-Draft")!;
    const toggle = within(draftSection).getByRole("button");

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(
      within(draftSection).getByText("Backend-utvecklare — Volvo")
    ).toBeInTheDocument();
  });

  it("disclosure-toggle kopplar aria-controls till panel-id (a11y)", () => {
    render(
      <ApplicationsPipeline
        groups={makePipeline({ Draft: 1 })}
        renderRow={renderRow}
      />
    );

    const draftSection = document.getElementById("status-Draft")!;
    const toggle = within(draftSection).getByRole("button");
    const controls = toggle.getAttribute("aria-controls");
    expect(controls).toBeTruthy();
    const panel = document.getElementById(controls!);
    expect(panel).not.toBeNull();
  });
});
