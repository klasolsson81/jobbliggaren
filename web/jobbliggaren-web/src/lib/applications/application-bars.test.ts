import { describe, it, expect } from "vitest";
import { countByStatus } from "./pipeline-counts";
import { PIPELINE_ORDER } from "./status";
import { applicationBars } from "./application-bars";
import type { PipelineGroupDto } from "@/lib/dto/applications";

// Groups as the backend projects them: GroupBy(Status) omits empty statuses entirely — a status
// with no applications has no group, not a group with zero.
const group = (status: PipelineGroupDto["status"], count: number): PipelineGroupDto => ({
  status,
  count,
  applications: [],
});

describe("applicationBars", () => {
  it("one bar per step in pipeline order, the interview steps summed into one", () => {
    const bars = applicationBars(
      countByStatus([
        group("Submitted", 2),
        group("Acknowledged", 1),
        group("InterviewScheduled", 1),
        group("Interviewing", 2),
        group("OfferReceived", 1),
      ]),
    );
    expect(bars.rows.map((r) => [r.key, r.count])).toEqual([
      ["submitted", 2],
      ["acknowledged", 1],
      ["interview", 3],
      ["offer", 1],
    ]);
    expect(bars.total).toBe(7);
    expect(bars.active).toBe(7);
    expect(bars.terminal).toBe(0);
  });

  it("the draft bar appears only when something is in it", () => {
    const without = applicationBars(countByStatus([group("Submitted", 1)]));
    expect(without.rows.map((r) => r.key)).not.toContain("draft");

    const withDraft = applicationBars(countByStatus([group("Draft", 1), group("Submitted", 1)]));
    expect(withDraft.rows[0]).toMatchObject({ key: "draft", count: 1 });
  });

  it("every other bar renders at zero — a zero there is the answer", () => {
    const bars = applicationBars(countByStatus([group("Submitted", 1)]));
    expect(bars.rows.map((r) => [r.key, r.count])).toEqual([
      ["submitted", 1],
      ["acknowledged", 0],
      ["interview", 0],
      ["offer", 0],
    ]);
  });

  it("the fill fraction is count over TOTAL, so terminal applications count in the denominator", () => {
    const bars = applicationBars(countByStatus([group("Submitted", 1), group("Rejected", 3)]));
    expect(bars.total).toBe(4);
    expect(bars.active).toBe(1);
    expect(bars.terminal).toBe(3);
    expect(bars.rows.find((r) => r.key === "submitted")?.fraction).toBe(0.25);
  });

  // BARS is a hand-written list beside ACTIVE_PIPELINE_STATUSES; nothing in the type system ties
  // them together. This pins that the bars sum to the big number they sit under — a seventh active
  // status without a bar would break it (dotnet-architect, 2026-09-13).
  it("the bars partition the active statuses: their counts sum to the active count", () => {
    const bars = applicationBars(countByStatus(PIPELINE_ORDER.map((s) => group(s, 1))));
    expect(bars.rows.reduce((n, r) => n + r.count, 0)).toBe(bars.active);
  });

  it("an empty pipeline has no denominator and no fractions above zero", () => {
    const bars = applicationBars(countByStatus([]));
    expect(bars.total).toBe(0);
    expect(bars.rows.every((r) => r.fraction === 0)).toBe(true);
  });
});
