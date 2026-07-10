import { describe, it, expect } from "vitest";
import { latestEventOf } from "./latest-event";
import type { ApplicationDto } from "@/lib/dto/applications";

// latestEventOf tar en Pick<ApplicationDto, ...> — vi bygger exakt de fyra
// scalars den läser. Alla tider är fixa ISO-strängar (ingen wall-clock).
type EventInput = Pick<
  ApplicationDto,
  "status" | "createdAt" | "lastStatusChangeAt" | "lastFollowUpAt"
>;

function input(overrides: Partial<EventInput> = {}): EventInput {
  return {
    status: "Submitted",
    createdAt: "2026-05-01T00:00:00Z",
    lastStatusChangeAt: "2026-06-01T00:00:00Z",
    lastFollowUpAt: null,
    ...overrides,
  };
}

describe("latestEventOf", () => {
  it("(a) utan uppföljning → StatusReached vid lastStatusChangeAt med nuvarande status", () => {
    const event = latestEventOf(
      input({ status: "Acknowledged", lastStatusChangeAt: "2026-06-01T00:00:00Z" }),
    );
    expect(event).toEqual({
      kind: "StatusReached",
      at: "2026-06-01T00:00:00Z",
      toStatus: "Acknowledged",
    });
  });

  it("(b) saknad lastStatusChangeAt → faller tillbaka på createdAt", () => {
    const event = latestEventOf(
      input({
        status: "Draft",
        createdAt: "2026-05-01T00:00:00Z",
        lastStatusChangeAt: undefined,
      }),
    );
    expect(event).toEqual({
      kind: "StatusReached",
      at: "2026-05-01T00:00:00Z",
      toStatus: "Draft",
    });
  });

  it("(c) uppföljning STRIKT nyare än statusbytet → FollowUpLogged vid lastFollowUpAt", () => {
    const event = latestEventOf(
      input({
        lastStatusChangeAt: "2026-06-01T00:00:00Z",
        lastFollowUpAt: "2026-06-05T00:00:00Z",
      }),
    );
    expect(event).toEqual({
      kind: "FollowUpLogged",
      at: "2026-06-05T00:00:00Z",
    });
  });

  it("(d) uppföljning EXAKT lika gammal som statusbytet → StatusReached (oavgjort → status)", () => {
    const event = latestEventOf(
      input({
        status: "Interviewing",
        lastStatusChangeAt: "2026-06-01T00:00:00Z",
        lastFollowUpAt: "2026-06-01T00:00:00Z",
      }),
    );
    expect(event).toEqual({
      kind: "StatusReached",
      at: "2026-06-01T00:00:00Z",
      toStatus: "Interviewing",
    });
  });

  it("(d) uppföljning ÄLDRE än statusbytet → StatusReached", () => {
    const event = latestEventOf(
      input({
        status: "Interviewing",
        lastStatusChangeAt: "2026-06-10T00:00:00Z",
        lastFollowUpAt: "2026-06-01T00:00:00Z",
      }),
    );
    expect(event.kind).toBe("StatusReached");
    expect(event).toEqual({
      kind: "StatusReached",
      at: "2026-06-10T00:00:00Z",
      toStatus: "Interviewing",
    });
  });

  it("(e) lastFollowUpAt null hanteras → StatusReached", () => {
    const event = latestEventOf(
      input({ lastFollowUpAt: null, lastStatusChangeAt: "2026-06-01T00:00:00Z" }),
    );
    expect(event).toEqual({
      kind: "StatusReached",
      at: "2026-06-01T00:00:00Z",
      toStatus: "Submitted",
    });
  });

  it("(e) lastFollowUpAt undefined (deploy-skew) hanteras → StatusReached", () => {
    const event = latestEventOf(
      input({ lastFollowUpAt: undefined, lastStatusChangeAt: "2026-06-01T00:00:00Z" }),
    );
    expect(event.kind).toBe("StatusReached");
  });
});
