import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  computeApplicationCounts,
  daysSince,
  filterFutureDeadlines,
  findFollowUpCandidates,
  findLatestOffer,
  findRecentInterviews,
  flattenPipeline,
  formatDaysAgo,
  formatNoticesStamp,
  formatSwedishLongDate,
  formatSwedishShortDate,
  OVERSIKT_FOLLOW_UP_DAYS,
} from "./aggregations";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import svOversikt from "../../../messages/sv/oversikt.json";

// Real next-intl translator scoped to `oversikt.relativeTime` (Swedish catalog
// = source of truth). In production `formatDaysAgo` receives this `t` from
// `useTranslations("oversikt.relativeTime")`.
const tRelativeTime = createTranslator({
  locale: "sv",
  messages: { oversikt: svOversikt },
  namespace: "oversikt.relativeTime",
});

function makeApp(
  status: ApplicationStatus,
  createdAt: string,
  updatedAt: string = createdAt,
  // #384: `findFollowUpCandidates` ankras numera i `appliedAt` (datumet ansökan
  // SKICKADES), inte `createdAt`. Defaultar till `createdAt` så de befintliga
  // follow-up-testerna — som bara skickar ett gammalt `createdAt` — fortsatt
  // ankrar mot det datumet och förblir gröna. Sätt explicit (inkl. `null`) för
  // att skilja skapande- från ansökningsdatum, som i #384-regressionsfallen.
  appliedAt: string | null = createdAt
): ApplicationDto {
  return {
    id: `app-${status}-${createdAt}`,
    jobSeekerId: "seeker-1",
    jobAdId: null,
    status,
    createdAt,
    updatedAt,
    appliedAt,
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
    expect(formatSwedishShortDate("not-a-date")).toBe("–");
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

  // #384 (THE pin): notisen "inte fått svar på över 14 dagar" ankras i
  // `appliedAt` (när arbetsgivaren fick ansökan), INTE `createdAt`. Buggen:
  // ett utkast skapat 2026-06-11 men SKICKAT 2026-06-28 räknades som 18 dagar
  // obesvarat (createdAt) medan "Mina ansökningar" korrekt visade "skickad i
  // går" (appliedAt). Här: createdAt 18 dagar sedan men appliedAt 1 dag sedan
  // ⇒ INGEN kandidat (ansökan har väntat 1 dag på svar, inte 18).
  it("exkluderar Submitted med gammalt createdAt men färskt appliedAt (#384)", () => {
    const sentYesterday = makeApp(
      "Submitted",
      "2026-05-06T00:00:00Z", // createdAt: 18 dagar sedan (utkast skapat)
      "2026-05-23T00:00:00Z",
      "2026-05-23T00:00:00Z" // appliedAt: 1 dag sedan (faktiskt skickad)
    );
    expect(findFollowUpCandidates([sentYesterday], now)).toEqual([]);
  });

  // Spegel: appliedAt 15 dagar sedan ⇒ kandidat (createdAt irrelevant här).
  it("inkluderar Submitted med appliedAt 15 dagar sedan (ankrar i appliedAt)", () => {
    const waitedTooLong = makeApp(
      "Submitted",
      "2026-05-23T00:00:00Z", // createdAt: färskt — bevisar att appliedAt styr
      "2026-05-23T00:00:00Z",
      "2026-05-09T00:00:00Z" // appliedAt: 15 dagar sedan
    );
    expect(findFollowUpCandidates([waitedTooLong], now)).toEqual([
      waitedTooLong,
    ]);
  });

  // Gräns: filtret är strikt `> OVERSIKT_FOLLOW_UP_DAYS` (14). Exakt 14 dagar
  // ⇒ INTE kandidat; 15 dagar ⇒ kandidat. Pinnar `>`-gränsen mot `>=`-drift.
  it("exkluderar appliedAt exakt 14 dagar sedan (gräns: strikt >)", () => {
    const exactlyFourteen = makeApp(
      "Submitted",
      "2026-05-10T00:00:00Z",
      "2026-05-10T00:00:00Z",
      "2026-05-10T00:00:00Z" // appliedAt: exakt 14 dagar sedan
    );
    expect(findFollowUpCandidates([exactlyFourteen], now)).toEqual([]);
  });

  it("inkluderar appliedAt 15 dagar sedan (gräns: precis över tröskeln)", () => {
    const fifteen = makeApp(
      "Submitted",
      "2026-05-09T00:00:00Z",
      "2026-05-09T00:00:00Z",
      "2026-05-09T00:00:00Z" // appliedAt: 15 dagar sedan
    );
    expect(findFollowUpCandidates([fifteen], now)).toEqual([fifteen]);
  });

  // Null-guard: ingen apply-stämpel ⇒ inget ankare ⇒ ingen kandidat (även om
  // createdAt vore gammalt). Defensiv paritet med BE-evaluatorn.
  it("exkluderar Submitted med appliedAt = null (ingen anchor, null-guard)", () => {
    const noAnchor = makeApp(
      "Submitted",
      "2026-01-01T00:00:00Z", // gammalt createdAt — får INTE läcka in
      "2026-01-01T00:00:00Z",
      null // appliedAt saknas
    );
    expect(findFollowUpCandidates([noAnchor], now)).toEqual([]);
  });
});

describe("OVERSIKT_FOLLOW_UP_DAYS / followUpText drift-guard", () => {
  // Real next-intl translator scoped to `oversikt.notices` (Swedish catalog =
  // source of truth). Speglar produktionens `t.rich("notices.followUpText", …)`
  // i notice-list.tsx. Renderar copyn med samma konstant som filtret läser, så
  // ett framtida byte av OVERSIKT_FOLLOW_UP_DAYS MÅSTE flöda in i copyn — det
  // hårdkodade talet och tröskeln kan aldrig drifta isär (mönster från #291).
  const tNotices = createTranslator({
    locale: "sv",
    messages: { oversikt: svOversikt },
    namespace: "oversikt.notices",
  });

  it("renderar copyn med tröskelkonstanten (count: 1, days: konstanten)", () => {
    // `t.rich` med en pass-through `b`-chunk ger en sträng (inga nästlade noder).
    const rendered = tNotices.rich("followUpText", {
      count: 1,
      days: OVERSIKT_FOLLOW_UP_DAYS,
      b: (chunks) => chunks,
    });
    const text = Array.isArray(rendered) ? rendered.join("") : String(rendered);
    // Talet kommer från konstanten via `{days}`-paramen, följt av "dagar" (sv).
    expect(text).toContain(`${OVERSIKT_FOLLOW_UP_DAYS} dagar`);
  });

  it("har en {days}-param i katalogen (inget hårdkodat tal i copyn)", () => {
    // Katalog-strängen bär INTE ett standalone-tal — tröskeln injiceras via
    // `{days}`. Skulle någon hårdkoda "14" i copyn skulle drift bli möjlig.
    const raw = svOversikt.notices.followUpText;
    expect(raw).toContain("{days");
    expect(raw).not.toContain(String(OVERSIKT_FOLLOW_UP_DAYS));
  });
});

describe("formatNoticesStamp", () => {
  it("formaterar känt UTC-datum som 'YYYY-MM-DD · HH:mm'", () => {
    const out = formatNoticesStamp(new Date("2026-06-29T13:52:30Z"));
    expect(out).toBe("2026-06-29 · 13:52");
  });

  it("är INTE den gamla hårdkodade mock-stämpeln (#384)", () => {
    const out = formatNoticesStamp(new Date("2026-06-29T13:52:30Z"));
    // Tidigare visades en stale mock "2026-05-23 · 08:42"; nu är stämpeln live.
    expect(out).not.toBe("2026-05-23 · 08:42");
  });

  it("returnerar streck för ogiltigt datum", () => {
    expect(formatNoticesStamp(new Date("invalid"))).toBe("–");
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
    expect(formatDaysAgo(tRelativeTime, "2026-05-24T01:00:00Z", now)).toBe(
      "i dag"
    );
  });

  it("ger 'i går' för 1 dag", () => {
    expect(formatDaysAgo(tRelativeTime, "2026-05-23T10:00:00Z", now)).toBe(
      "i går"
    );
  });

  it("ger 'N dagar sedan' för 2+ dagar", () => {
    expect(formatDaysAgo(tRelativeTime, "2026-05-20T00:00:00Z", now)).toBe(
      "4 dagar sedan"
    );
  });

  it("ger 'i dag' för framtida datum (defensiv — bör inte uppstå)", () => {
    expect(formatDaysAgo(tRelativeTime, "2026-05-26T00:00:00Z", now)).toBe(
      "i dag"
    );
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
