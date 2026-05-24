import { describe, it, expect } from "vitest";
import {
  computeApplicationCounts,
  daysSince,
  filterFutureDeadlines,
  findFollowUpCandidates,
  findLatestOffer,
  findRecentInterviews,
  flattenPipeline,
  formatDaysAgo,
  formatSwedishLongDate,
  formatSwedishShortDate,
} from "./aggregations";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

function makeApp(
  status: ApplicationStatus,
  createdAt: string,
  updatedAt: string = createdAt
): ApplicationDto {
  return {
    id: `app-${status}-${createdAt}`,
    jobSeekerId: "seeker-1",
    jobAdId: null,
    status,
    createdAt,
    updatedAt,
    jobAd: null,
  };
}

function makeGroup(
  status: ApplicationStatus,
  count: number,
  apps: ApplicationDto[] = []
): PipelineGroupDto {
  return { status, count, applications: apps };
}

describe("computeApplicationCounts", () => {
  it("returnerar nollor för tom pipeline", () => {
    expect(computeApplicationCounts([])).toEqual({
      active: 0,
      drafts: 0,
      interviews: 0,
      offers: 0,
      rejected: 0,
      ghosted: 0,
      submitted: 0,
      acknowledged: 0,
    });
  });

  it("räknar aktiva = alla statusar ∉ {Rejected, Withdrawn, Accepted}", () => {
    const pipeline: PipelineGroupDto[] = [
      makeGroup("Draft", 2),
      makeGroup("Submitted", 3),
      makeGroup("Acknowledged", 1),
      makeGroup("InterviewScheduled", 1),
      makeGroup("Interviewing", 2),
      makeGroup("OfferReceived", 1),
      makeGroup("Rejected", 5),
      makeGroup("Withdrawn", 2),
      makeGroup("Accepted", 1),
      makeGroup("Ghosted", 4),
    ];
    const c = computeApplicationCounts(pipeline);
    // 2+3+1+1+2+1+4 = 14 aktiva (rejected+withdrawn+accepted exkluderas)
    expect(c.active).toBe(14);
    expect(c.drafts).toBe(2);
    expect(c.submitted).toBe(3);
    expect(c.acknowledged).toBe(1);
    // InterviewScheduled + Interviewing
    expect(c.interviews).toBe(3);
    expect(c.offers).toBe(1);
    expect(c.rejected).toBe(5);
    expect(c.ghosted).toBe(4);
  });

  it("aggregerar interviews från båda statusarna även om bara en finns", () => {
    const c = computeApplicationCounts([makeGroup("Interviewing", 4)]);
    expect(c.interviews).toBe(4);
    expect(c.active).toBe(4);
  });

  it("hanterar pipeline med samma status duplicerat — sista vinner", () => {
    // backend bör inte skicka dupletter men vi failar inte
    const c = computeApplicationCounts([
      makeGroup("Draft", 2),
      makeGroup("Draft", 5),
    ]);
    expect(c.drafts).toBe(5);
  });
});

describe("flattenPipeline", () => {
  it("flattar applications från alla grupper", () => {
    const a = makeApp("Draft", "2026-05-01T00:00:00Z");
    const b = makeApp("Submitted", "2026-05-02T00:00:00Z");
    const flat = flattenPipeline([
      makeGroup("Draft", 1, [a]),
      makeGroup("Submitted", 1, [b]),
    ]);
    expect(flat).toEqual([a, b]);
  });

  it("returnerar tom array för tom pipeline", () => {
    expect(flattenPipeline([])).toEqual([]);
  });
});

describe("formatSwedishShortDate", () => {
  it("formaterar ISO till svensk kortform", () => {
    expect(formatSwedishShortDate("2026-05-13T12:00:00Z")).toBe("13 maj");
    expect(formatSwedishShortDate("2026-04-06T00:00:00Z")).toBe("6 apr");
  });

  it("returnerar streck för ogiltigt datum", () => {
    expect(formatSwedishShortDate("not-a-date")).toBe("—");
  });
});

describe("formatSwedishLongDate", () => {
  it("returnerar { day, weekday, monthYear } för 23 maj 2026 (lördag)", () => {
    const d = new Date(2026, 4, 23); // 4 = maj (0-indexed), 23 = lördag
    const out = formatSwedishLongDate(d);
    expect(out.day).toBe(23);
    expect(out.weekday).toBe("lördag");
    expect(out.monthYear).toBe("maj 2026");
  });
});

describe("daysSince", () => {
  it("räknar heltal kalenderdagar", () => {
    const now = new Date("2026-05-24T12:00:00Z");
    expect(daysSince("2026-05-22T00:00:00Z", now)).toBe(2);
    expect(daysSince("2026-05-24T00:00:00Z", now)).toBe(0);
  });

  it("returnerar negativ siffra för framtida datum", () => {
    const now = new Date("2026-05-24T00:00:00Z");
    expect(daysSince("2026-05-26T00:00:00Z", now)).toBe(-2);
  });

  it("är trunkerad till UTC-dag (DST-säker)", () => {
    // 2026-03-29 är DST-skifte i Sverige; vi vill ha exakt 1 dag
    const now = new Date("2026-03-30T00:00:00Z");
    expect(daysSince("2026-03-29T23:59:00Z", now)).toBe(1);
  });

  it("returnerar 0 för ogiltigt datum", () => {
    expect(daysSince("not-a-date", new Date())).toBe(0);
  });
});

describe("findFollowUpCandidates", () => {
  const now = new Date("2026-05-24T00:00:00Z");

  it("inkluderar Submitted >14d", () => {
    const old = makeApp("Submitted", "2026-05-01T00:00:00Z");
    const fresh = makeApp("Submitted", "2026-05-20T00:00:00Z");
    expect(findFollowUpCandidates([old, fresh], now)).toEqual([old]);
  });

  it("inkluderar Acknowledged >14d", () => {
    const old = makeApp("Acknowledged", "2026-05-01T00:00:00Z");
    expect(findFollowUpCandidates([old], now)).toEqual([old]);
  });

  it("exkluderar andra statusar oavsett ålder", () => {
    const oldDraft = makeApp("Draft", "2026-01-01T00:00:00Z");
    const oldInterview = makeApp(
      "InterviewScheduled",
      "2026-01-01T00:00:00Z"
    );
    expect(findFollowUpCandidates([oldDraft, oldInterview], now)).toEqual([]);
  });

  it("returnerar tom array när inga matchar", () => {
    expect(findFollowUpCandidates([], now)).toEqual([]);
  });
});

describe("findRecentInterviews", () => {
  const now = new Date("2026-05-24T12:00:00Z");

  it("inkluderar InterviewScheduled inom 24h", () => {
    const recent = makeApp(
      "InterviewScheduled",
      "2026-05-23T00:00:00Z",
      "2026-05-23T10:00:00Z"
    );
    expect(findRecentInterviews([recent], now)).toEqual([recent]);
  });

  it("exkluderar äldre intervjuer", () => {
    const old = makeApp(
      "InterviewScheduled",
      "2026-05-10T00:00:00Z",
      "2026-05-10T00:00:00Z"
    );
    expect(findRecentInterviews([old], now)).toEqual([]);
  });

  it("exkluderar andra statusar", () => {
    const draft = makeApp(
      "Draft",
      "2026-05-23T00:00:00Z",
      "2026-05-23T12:00:00Z"
    );
    expect(findRecentInterviews([draft], now)).toEqual([]);
  });

  it("inkluderar intervju ~47h gammal (UTC-kalenderdag-trunkering)", () => {
    // updatedAt 2026-05-23T01:00Z, now 2026-05-24T23:59Z = ~47h diff
    // daysSince UTC-kalenderdag-jämför ger 1 → inom fönstret per JSDoc-kontrakt
    const edge = makeApp(
      "InterviewScheduled",
      "2026-05-23T01:00:00Z",
      "2026-05-23T01:00:00Z"
    );
    const lateNow = new Date("2026-05-24T23:59:00Z");
    expect(findRecentInterviews([edge], lateNow)).toEqual([edge]);
  });
});

describe("formatDaysAgo", () => {
  const now = new Date("2026-05-24T12:00:00Z");

  it("ger 'i dag' för 0 dagar", () => {
    expect(formatDaysAgo("2026-05-24T01:00:00Z", now)).toBe("i dag");
  });

  it("ger 'i går' för 1 dag", () => {
    expect(formatDaysAgo("2026-05-23T10:00:00Z", now)).toBe("i går");
  });

  it("ger 'N dagar sedan' för 2+ dagar", () => {
    expect(formatDaysAgo("2026-05-20T00:00:00Z", now)).toBe("4 dagar sedan");
  });

  it("ger 'i dag' för framtida datum (defensiv — bör inte uppstå)", () => {
    expect(formatDaysAgo("2026-05-26T00:00:00Z", now)).toBe("i dag");
  });
});

describe("filterFutureDeadlines", () => {
  it("behåller deadlines som är idag eller i framtiden", () => {
    const now = new Date("2026-05-24T00:00:00Z");
    const deadlines = [
      { date: "2026-05-22", label: "22 maj" }, // passerat
      { date: "2026-05-24", label: "24 maj" }, // idag
      { date: "2026-05-27", label: "27 maj" }, // framtid
    ];
    const out = filterFutureDeadlines(deadlines, now);
    expect(out.map((d) => d.label)).toEqual(["24 maj", "27 maj"]);
  });

  it("returnerar tom array när alla passerat", () => {
    const now = new Date("2026-06-01T00:00:00Z");
    const deadlines = [
      { date: "2026-05-25" },
      { date: "2026-05-27" },
    ];
    expect(filterFutureDeadlines(deadlines, now)).toEqual([]);
  });
});

describe("findLatestOffer", () => {
  it("returnerar nyaste offer (sort på updatedAt desc)", () => {
    const older = makeApp(
      "OfferReceived",
      "2026-05-01T00:00:00Z",
      "2026-05-10T00:00:00Z"
    );
    const newer = makeApp(
      "OfferReceived",
      "2026-05-05T00:00:00Z",
      "2026-05-20T00:00:00Z"
    );
    expect(findLatestOffer([older, newer])).toBe(newer);
  });

  it("returnerar null när inga offers finns", () => {
    expect(findLatestOffer([])).toBeNull();
    expect(findLatestOffer([makeApp("Draft", "2026-05-01T00:00:00Z")])).toBeNull();
  });
});
