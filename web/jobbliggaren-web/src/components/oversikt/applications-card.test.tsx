import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ApplicationsCard } from "./applications-card";
import messages from "../../../messages/sv";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { PipelineGroupDto } from "@/lib/dto/applications";

const group = (status: PipelineGroupDto["status"], count: number): PipelineGroupDto => ({
  status,
  count,
  applications: [],
});
const ok = (groups: PipelineGroupDto[]): ApiResult<PipelineGroupDto[]> => ({
  kind: "ok",
  data: groups,
});
const COPY = messages.oversikt;

function card() {
  return screen.getByRole("region", { name: COPY.cards.applications });
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("ApplicationsCard", () => {
  it("the big number is the ACTIVE count, the unit names the total", () => {
    render(<ApplicationsCard pipeline={ok([group("Submitted", 2), group("Rejected", 1)])} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe("2");
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__unit"))).toBe("aktiva av 3 ansökningar");
  });

  it("one bar per step, the interview steps summed under one label, the terminal line last", () => {
    render(
      <ApplicationsCard
        pipeline={ok([
          group("Submitted", 2),
          group("InterviewScheduled", 1),
          group("Interviewing", 2),
          group("Ghosted", 1),
        ])}
      />,
    );
    const list = within(card()).getByRole("list", { name: COPY.summary.stepsAriaLabel });
    const rows = [...list.querySelectorAll<HTMLElement>(".jp-ov-bars__row")].map(
      (r) => `${text(r.querySelector<HTMLElement>(".jp-ov-bars__name"))} ${text(r.querySelector<HTMLElement>(".jp-ov-bars__num"))}`,
    );
    expect(rows).toEqual([
      "Skickad 2",
      "Bekräftad 0",
      "Intervju 3",
      "Erbjudande 0",
      "Avslut och vilande 1",
    ]);
  });

  it("a zero row carries data-empty so weight, not opacity, tells it apart", () => {
    render(<ApplicationsCard pipeline={ok([group("Submitted", 1)])} />);
    const rows = card().querySelectorAll<HTMLElement>(".jp-ov-bars__row");
    expect(rows[0]).not.toHaveAttribute("data-empty");
    expect(rows[1]).toHaveAttribute("data-empty", "true");
  });

  it("the CTA is an outline link to the list — never the solid level", () => {
    render(<ApplicationsCard pipeline={ok([group("Submitted", 1)])} />);
    const cta = within(card()).getByRole("link", { name: COPY.summary.link });
    expect(cta).toHaveAttribute("href", "/ansokningar");
    expect(cta.className).toContain("jp-ov-cta--outline");
    expect(cta.className).not.toContain("jp-btn--primary");
  });

  it("empty: no number, the empty copy, and the ONLY create-link the page allows", () => {
    render(<ApplicationsCard pipeline={ok([])} />);
    expect(card().querySelector<HTMLElement>(".jp-ov-num")).toBeNull();
    expect(within(card()).getByText(COPY.summary.emptyTitle)).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: COPY.summary.emptyCta })).toHaveAttribute(
      "href",
      "/ny-ansokan",
    );
  });

  it("unavailable: an en-dash, the unavailable copy, no bars and no CTA — never a false zero", () => {
    render(<ApplicationsCard pipeline={{ kind: "error" }} />);
    expect(text(card().querySelector<HTMLElement>(".jp-ov-num__value"))).toBe(COPY.cards.unmeasured);
    expect(card().querySelector<HTMLElement>(".jp-ov-num__unit")).toBeNull();
    expect(within(card()).getByText(COPY.summary.unavailable)).toBeInTheDocument();
    expect(within(card()).queryByRole("link")).toBeNull();
    expect(within(card()).queryByRole("list")).toBeNull();
  });
});
