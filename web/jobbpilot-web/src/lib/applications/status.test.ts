import { describe, it, expect } from "vitest";
import {
  getStatusLabel,
  getAllowedTransitions,
  isDestructiveTransition,
  STATUS_LABELS,
  ALLOWED_TRANSITIONS,
} from "./status";
import type { ApplicationStatus } from "@/lib/types/applications";

const ALL_STATUSES: ApplicationStatus[] = [
  "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
  "Interviewing", "OfferReceived", "Accepted", "Rejected", "Withdrawn", "Ghosted",
];

describe("getStatusLabel", () => {
  it("returns Swedish label for every status", () => {
    for (const status of ALL_STATUSES) {
      expect(getStatusLabel(status)).toBe(STATUS_LABELS[status]);
      expect(getStatusLabel(status)).not.toBe(status); // must be translated
    }
  });

  it("covers all 10 statuses", () => {
    expect(Object.keys(STATUS_LABELS)).toHaveLength(10);
  });
});

describe("getAllowedTransitions", () => {
  it("Draft can only transition to Submitted", () => {
    expect(getAllowedTransitions("Draft")).toEqual(["Submitted"]);
  });

  it("Submitted can transition to Acknowledged, Rejected, Withdrawn", () => {
    expect(getAllowedTransitions("Submitted")).toEqual(
      expect.arrayContaining(["Acknowledged", "Rejected", "Withdrawn"])
    );
    expect(getAllowedTransitions("Submitted")).toHaveLength(3);
  });

  it("Accepted is a terminal state with no transitions", () => {
    expect(getAllowedTransitions("Accepted")).toHaveLength(0);
  });

  it("Rejected is a terminal state with no transitions", () => {
    expect(getAllowedTransitions("Rejected")).toHaveLength(0);
  });

  it("Withdrawn is a terminal state with no transitions", () => {
    expect(getAllowedTransitions("Withdrawn")).toHaveLength(0);
  });

  it("Ghosted can be reactivated to Submitted", () => {
    expect(getAllowedTransitions("Ghosted")).toEqual(["Submitted"]);
  });

  it("covers all 10 statuses", () => {
    expect(Object.keys(ALLOWED_TRANSITIONS)).toHaveLength(10);
  });
});

describe("isDestructiveTransition", () => {
  it("Rejected is destructive", () => {
    expect(isDestructiveTransition("Rejected")).toBe(true);
  });

  it("Withdrawn is destructive", () => {
    expect(isDestructiveTransition("Withdrawn")).toBe(true);
  });

  it("Submitted is not destructive", () => {
    expect(isDestructiveTransition("Submitted")).toBe(false);
  });

  it("Accepted is not destructive", () => {
    expect(isDestructiveTransition("Accepted")).toBe(false);
  });
});
