import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  applicationStatusLabel,
  channelLabel,
  followUpOutcomeLabel,
  applicationSourceLabel,
  getAllowedTransitions,
  isDestructiveTransition,
  isWaitingSignal,
  ALLOWED_TRANSITIONS,
  CHANNEL_KEYS,
} from "./status";
import { followUpOutcomeSchema } from "@/lib/dto/applications";
import svApplications from "../../../messages/sv/applications.json";
import type { ApplicationStatus } from "@/lib/types/applications";

// Build a real next-intl translator scoped to the `enums` namespace from the
// Swedish catalog (the source of truth). The helper functions accept this `t`;
// in production it comes from `useTranslations("applications.enums")`.
const t = createTranslator({
  locale: "sv",
  messages: { applications: svApplications },
  namespace: "applications.enums",
});

const ALL_STATUSES: ApplicationStatus[] = [
  "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
  "Interviewing", "OfferReceived", "Accepted", "Rejected", "Withdrawn", "Ghosted",
];

describe("applicationStatusLabel", () => {
  it("returns the Swedish label for every status", () => {
    for (const status of ALL_STATUSES) {
      expect(applicationStatusLabel(t, status)).toBe(
        svApplications.enums.status[status]
      );
      expect(applicationStatusLabel(t, status)).not.toBe(status); // translated
    }
  });

  it("covers all 10 statuses in the catalog", () => {
    expect(Object.keys(svApplications.enums.status)).toHaveLength(10);
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

describe("channelLabel", () => {
  it("translates every known channel key", () => {
    for (const key of CHANNEL_KEYS) {
      expect(channelLabel(t, key)).toBe(svApplications.enums.channel[key]);
    }
  });

  it("falls back to the raw value for an unknown channel", () => {
    expect(channelLabel(t, "Carrier pigeon")).toBe("Carrier pigeon");
  });
});

describe("followUpOutcomeLabel", () => {
  it("covers the backend FollowUpOutcome SmartEnum (Pending/Responded/NoResponse)", () => {
    expect(Object.keys(svApplications.enums.followUpOutcome).sort()).toEqual(
      [...followUpOutcomeSchema.options].sort()
    );
  });

  it("uses civic-utility Swedish copy without exclamation or emoji", () => {
    expect(followUpOutcomeLabel(t, "Pending")).toBe("Inväntar svar");
    expect(followUpOutcomeLabel(t, "Responded")).toBe("Svar mottaget");
    expect(followUpOutcomeLabel(t, "NoResponse")).toBe("Inget svar");
    for (const outcome of followUpOutcomeSchema.options) {
      expect(followUpOutcomeLabel(t, outcome)).not.toMatch(/[!]/);
    }
  });
});

describe("applicationSourceLabel", () => {
  it("translates known sources and falls back to the raw value", () => {
    expect(applicationSourceLabel(t, "Platsbanken")).toBe("Platsbanken");
    expect(applicationSourceLabel(t, "LinkedIn")).toBe("LinkedIn");
    expect(applicationSourceLabel(t, "Manual")).toBe("Manuellt");
    expect(applicationSourceLabel(t, "Unknown")).toBe("Unknown");
  });
});

// ── #630 PR 7: aktiv-vägen + nästa-steg + meny-/park-grupper ────────────────
describe("2a action-konstanter (#630 PR 7)", () => {
  it("ACTIVE_PATH_STATUSES är de 7 stegen på aktiva vägen (design §8.4)", async () => {
    const { ACTIVE_PATH_STATUSES, ACTIVE_PIPELINE_STATUSES } = await import(
      "./status"
    );
    expect(ACTIVE_PATH_STATUSES).toEqual([
      ...ACTIVE_PIPELINE_STATUSES,
      "Accepted",
    ]);
    expect(ACTIVE_PATH_STATUSES).toHaveLength(7);
  });

  it("STATUS_MENU_CLOSED_GROUP + ACTIVE_PIPELINE_STATUSES täcker alla 10 statusar exakt en gång", async () => {
    const { STATUS_MENU_CLOSED_GROUP, ACTIVE_PIPELINE_STATUSES, PIPELINE_ORDER } =
      await import("./status");
    const union = [...ACTIVE_PIPELINE_STATUSES, ...STATUS_MENU_CLOSED_GROUP];
    expect([...union].sort()).toEqual([...PIPELINE_ORDER].sort());
    expect(new Set(union).size).toBe(10);
  });

  it("PARK_STATUSES = Nekad/Återtagen/Ghosted (§8.5 — Accepterad nås via stegväljaren)", async () => {
    const { PARK_STATUSES } = await import("./status");
    expect(PARK_STATUSES).toEqual(["Rejected", "Withdrawn", "Ghosted"]);
  });
});

// ── #630 PR 10: Tabell-vyns "I steget"-varningsfärgning ─────────────────────
describe("isWaitingSignal (#630 PR 10, CTO-bind 2026-07-10)", () => {
  it("saknad/None/OfferAwaitingReply är INTE en väntesignal (ingen rödmarkering)", () => {
    expect(isWaitingSignal(undefined)).toBe(false);
    expect(isWaitingSignal("None")).toBe(false);
    // Erbjudande-väntan är positiv (success-axeln), aldrig en väntan att röda.
    expect(isWaitingSignal("OfferAwaitingReply")).toBe(false);
  });

  it("övriga firande signaler ÄR väntesignaler (steget har stått still för länge)", () => {
    for (const signal of [
      "NoResponseNudge",
      "GhostSuggested",
      "OverdueFollowUp",
      "DraftDeadlineApproaching",
      "SilentAfterInterview",
    ] as const) {
      expect(isWaitingSignal(signal)).toBe(true);
    }
  });
});

describe("nextStepOf (prototypens nextOf = facit)", () => {
  it("följer den aktiva vägen och återaktiverar Ghosted → Submitted", async () => {
    const { nextStepOf } = await import("./status");
    expect(nextStepOf("Draft")).toBe("Submitted");
    expect(nextStepOf("Submitted")).toBe("Acknowledged");
    expect(nextStepOf("Acknowledged")).toBe("InterviewScheduled");
    expect(nextStepOf("InterviewScheduled")).toBe("Interviewing");
    expect(nextStepOf("Interviewing")).toBe("OfferReceived");
    expect(nextStepOf("OfferReceived")).toBe("Accepted");
    expect(nextStepOf("Ghosted")).toBe("Submitted");
  });

  it("terminala statusar har inget nästa steg (ingen primär CTA)", async () => {
    const { nextStepOf } = await import("./status");
    expect(nextStepOf("Accepted")).toBeNull();
    expect(nextStepOf("Rejected")).toBeNull();
    expect(nextStepOf("Withdrawn")).toBeNull();
  });
});
