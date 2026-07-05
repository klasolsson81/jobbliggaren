import { describe, it, expect } from "vitest";
import { composeTimeline, daysInCurrentStep } from "./timeline";
import type {
  FollowUpDto,
  NoteDto,
  StatusChangeDto,
} from "@/lib/dto/applications";

const note = (id: string, createdAt: string): NoteDto => ({
  id,
  content: "n",
  createdAt,
});

const followUp = (
  id: string,
  scheduledAt: string,
  outcome: FollowUpDto["outcome"] = "Pending",
  outcomeAt: string | null = null,
): FollowUpDto => ({
  id,
  channel: "Email",
  scheduledAt,
  note: null,
  outcome,
  outcomeAt,
  createdAt: scheduledAt,
});

const statusChange = (
  from: StatusChangeDto["from"],
  to: StatusChangeDto["to"],
  changedAt: string,
): StatusChangeDto => ({ from, to, changedAt });

describe("composeTimeline", () => {
  it("emits created + note + follow-up + status-change events, newest first", () => {
    const events = composeTimeline({
      createdAt: "2026-01-01T08:00:00Z",
      notes: [note("1", "2026-01-03T08:00:00Z")],
      followUps: [followUp("f1", "2026-01-02T08:00:00Z")],
      statusChanges: [
        statusChange("Draft", "Submitted", "2026-01-04T08:00:00Z"),
      ],
    });

    expect(events.map((e) => e.kind)).toEqual([
      "statusChange", // 01-04 (newest)
      "note", // 01-03
      "followUpScheduled", // 01-02
      "created", // 01-01 (oldest)
    ]);
  });

  it("adds an outcome event only for a recorded (non-Pending) follow-up", () => {
    const pending = composeTimeline({
      createdAt: "2026-01-01T08:00:00Z",
      notes: [],
      followUps: [followUp("f1", "2026-01-02T08:00:00Z")],
      statusChanges: [],
    });
    expect(pending.filter((e) => e.kind === "followUpOutcome")).toHaveLength(0);

    const recorded = composeTimeline({
      createdAt: "2026-01-01T08:00:00Z",
      notes: [],
      followUps: [
        followUp("f1", "2026-01-02T08:00:00Z", "Responded", "2026-01-05T08:00:00Z"),
      ],
      statusChanges: [],
    });
    const outcome = recorded.find((e) => e.kind === "followUpOutcome");
    expect(outcome).toMatchObject({ kind: "followUpOutcome", outcome: "Responded" });
  });

  it("never synthesises a status event from updatedAt (only recorded StatusChanges)", () => {
    // No updatedAt is even an input; with no statusChanges there are zero
    // statusChange events — the retired fabrication cannot recur.
    const events = composeTimeline({
      createdAt: "2026-01-01T08:00:00Z",
      notes: [],
      followUps: [],
      statusChanges: [],
    });
    expect(events.filter((e) => e.kind === "statusChange")).toHaveLength(0);
    expect(events.map((e) => e.kind)).toEqual(["created"]);
  });

  it("tolerates an absent statusChanges array (deploy-skew)", () => {
    const events = composeTimeline({
      createdAt: "2026-01-01T08:00:00Z",
      notes: [],
      followUps: [],
    });
    expect(events.map((e) => e.kind)).toEqual(["created"]);
  });
});

describe("daysInCurrentStep", () => {
  const now = new Date("2026-01-10T12:00:00Z");

  it("returns null when no status change has been recorded", () => {
    expect(daysInCurrentStep([], now)).toBeNull();
    expect(daysInCurrentStep(undefined, now)).toBeNull();
  });

  it("counts whole days since the LATEST recorded transition", () => {
    const changes = [
      statusChange("Draft", "Submitted", "2026-01-02T08:00:00Z"),
      statusChange("Submitted", "InterviewScheduled", "2026-01-07T08:00:00Z"),
    ];
    expect(daysInCurrentStep(changes, now)).toBe(3); // 01-07 -> 01-10
  });

  it("clamps a future-dated transition to 0 (never negative)", () => {
    const changes = [statusChange("Draft", "Submitted", "2026-01-20T08:00:00Z")];
    expect(daysInCurrentStep(changes, now)).toBe(0);
  });
});
