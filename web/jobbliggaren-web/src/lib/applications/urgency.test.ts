import { describe, it, expect } from "vitest";
import { daysInStatus, effectiveWaitDays, urgencyTagFor } from "./urgency";
import type { ApplicationDto } from "@/lib/dto/applications";

const NOW = new Date("2026-05-20T12:00:00Z");

function makeApplication(
  overrides: Partial<ApplicationDto> = {},
): ApplicationDto {
  return {
    id: "app-1",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    lastStatusChangeAt: "2026-05-10",
    lastFollowUpAt: null,
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: "2026-06-01",
    },
    ...overrides,
  };
}

describe("daysInStatus", () => {
  it("räknar hela kalenderdagar sedan lastStatusChangeAt", () => {
    expect(daysInStatus("2026-05-10T08:00:00Z", NOW)).toBe(10);
  });

  it("null när scalar saknas (deploy-skew) — fabricerar aldrig", () => {
    expect(daysInStatus(undefined, NOW)).toBeNull();
  });

  it("clampar framtida datum till 0", () => {
    expect(daysInStatus("2026-05-25T08:00:00Z", NOW)).toBe(0);
  });
});

describe("effectiveWaitDays (ADR 0092 D5-klockan)", () => {
  it("= dagar sedan statusbytet när ingen uppföljning finns", () => {
    expect(
      effectiveWaitDays(
        { lastStatusChangeAt: "2026-05-01", lastFollowUpAt: null },
        NOW,
      ),
    ).toBe(19);
  });

  it("en senare uppföljning nollställer klockan (min av de två)", () => {
    expect(
      effectiveWaitDays(
        { lastStatusChangeAt: "2026-04-01", lastFollowUpAt: "2026-05-15" },
        NOW,
      ),
    ).toBe(5);
  });

  it("null när statusbytes-ankaret saknas", () => {
    expect(
      effectiveWaitDays(
        { lastStatusChangeAt: undefined, lastFollowUpAt: "2026-05-15" },
        NOW,
      ),
    ).toBeNull();
  });
});

describe("urgencyTagFor (design §11 — signal är SSOT, taggen är display)", () => {
  it("DraftDeadlineApproaching → DEADLINE-tagg (warning) ur annonsens datum", () => {
    const tag = urgencyTagFor(
      makeApplication({
        status: "Draft",
        attentionSignal: "DraftDeadlineApproaching",
      }),
      NOW,
    );
    expect(tag).toEqual({
      kind: "deadline",
      variant: "warning",
      dateIso: "2026-06-01",
    });
  });

  it("DraftDeadlineApproaching utan annons-deadline → ingen tagg (aldrig fabricerat)", () => {
    const app = makeApplication({
      status: "Draft",
      attentionSignal: "DraftDeadlineApproaching",
    });
    const tag = urgencyTagFor(
      { ...app, jobAd: { ...app.jobAd!, expiresAt: null } },
      NOW,
    );
    expect(tag).toBeNull();
  });

  it.each(["GhostSuggested", "NoResponseNudge"] as const)(
    "%s → 'N dgr utan svar'-tagg (info) på den effektiva väntetiden",
    (signal) => {
      const tag = urgencyTagFor(
        makeApplication({
          attentionSignal: signal,
          lastStatusChangeAt: "2026-05-01",
        }),
        NOW,
      );
      expect(tag).toEqual({ kind: "waitDays", variant: "info", days: 19 });
    },
  );

  it("SilentAfterInterview → 'N dgr sedan intervjun'-tagg (info)", () => {
    const tag = urgencyTagFor(
      makeApplication({
        status: "Interviewing",
        attentionSignal: "SilentAfterInterview",
        lastStatusChangeAt: "2026-05-12",
      }),
      NOW,
    );
    expect(tag).toEqual({ kind: "sinceInterview", variant: "info", days: 8 });
  });

  it("OfferAwaitingReply → ingen tagg (SVAR SENAST-datumet är deferrat, D5)", () => {
    expect(
      urgencyTagFor(
        makeApplication({
          status: "OfferReceived",
          attentionSignal: "OfferAwaitingReply",
        }),
        NOW,
      ),
    ).toBeNull();
  });

  it("None/undefined → ingen tagg", () => {
    expect(urgencyTagFor(makeApplication(), NOW)).toBeNull();
    expect(
      urgencyTagFor(makeApplication({ attentionSignal: "None" }), NOW),
    ).toBeNull();
  });
});
