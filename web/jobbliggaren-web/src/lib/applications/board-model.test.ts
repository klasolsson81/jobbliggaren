import { describe, it, expect } from "vitest";
import { applyBoardMove, bucketsFromGroups } from "./board-model";
import { PIPELINE_ORDER } from "./status";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

function app(
  id: string,
  status: ApplicationStatus,
  title = "roll",
  company = "företag",
): ApplicationDto {
  return {
    id,
    jobSeekerId: "seeker",
    jobAdId: "ad",
    status,
    createdAt: "2026-05-01",
    updatedAt: "2026-05-02",
    jobAd: {
      jobAdId: "ad",
      title,
      company,
      url: null,
      source: "Platsbanken",
      publishedAt: null,
      expiresAt: null,
    },
  };
}

function groups(...gs: [ApplicationStatus, ApplicationDto[]][]): PipelineGroupDto[] {
  return gs.map(([status, applications]) => ({
    status,
    count: applications.length,
    applications,
  }));
}

describe("board-model — bucketsFromGroups", () => {
  it("nycklar alla 10 statusar, även frånvarande grupper (tom array)", () => {
    const buckets = bucketsFromGroups(groups(["Submitted", [app("a", "Submitted")]]), "");
    for (const status of PIPELINE_ORDER) {
      expect(buckets[status]).toBeInstanceOf(Array);
    }
    expect(buckets.Submitted.map((a) => a.id)).toEqual(["a"]);
    expect(buckets.Draft).toEqual([]);
  });

  it("sökfiltrerar på roll + företag (trimmad, gemener från kallaren)", () => {
    const buckets = bucketsFromGroups(
      groups([
        "Submitted",
        [app("a", "Submitted", "Backend", "Volvo"), app("b", "Submitted", "Frontend", "Spotify")],
      ]),
      "spotify",
    );
    expect(buckets.Submitted.map((a) => a.id)).toEqual(["b"]);
  });

  it("tom sökning matchar allt", () => {
    const buckets = bucketsFromGroups(
      groups(["Submitted", [app("a", "Submitted"), app("b", "Submitted")]]),
      "",
    );
    expect(buckets.Submitted).toHaveLength(2);
  });
});

describe("board-model — applyBoardMove (optimistisk flytt)", () => {
  it("flyttar kortet till målkolumnen och stämplar dess status", () => {
    const base = bucketsFromGroups(
      groups(["Submitted", [app("a", "Submitted")]]),
      "",
    );
    const next = applyBoardMove(base, { id: "a", to: "InterviewScheduled" });

    expect(next.Submitted).toEqual([]);
    expect(next.InterviewScheduled.map((a) => a.id)).toEqual(["a"]);
    // Kort-status uppdaterad så kolumn + status är konsistenta optimistiskt.
    expect(next.InterviewScheduled[0]?.status).toBe("InterviewScheduled");
  });

  it("Ghosted → Submitted (återaktivering) stöds som fri flytt", () => {
    const base = bucketsFromGroups(groups(["Ghosted", [app("g", "Ghosted")]]), "");
    const next = applyBoardMove(base, { id: "g", to: "Submitted" });
    expect(next.Ghosted).toEqual([]);
    expect(next.Submitted.map((a) => a.id)).toEqual(["g"]);
  });

  it("rör inte andra kort", () => {
    const base = bucketsFromGroups(
      groups(
        ["Submitted", [app("a", "Submitted"), app("b", "Submitted")]],
        ["Acknowledged", [app("c", "Acknowledged")]],
      ),
      "",
    );
    const next = applyBoardMove(base, { id: "a", to: "Acknowledged" });

    expect(next.Submitted.map((a) => a.id)).toEqual(["b"]);
    expect(next.Acknowledged.map((a) => a.id)).toEqual(["a", "c"]);
  });

  it("okänt id = ingen ändring (inget kort försvinner)", () => {
    const base = bucketsFromGroups(groups(["Submitted", [app("a", "Submitted")]]), "");
    const next = applyBoardMove(base, { id: "saknas", to: "Accepted" });
    expect(next.Submitted.map((a) => a.id)).toEqual(["a"]);
    expect(next.Accepted).toEqual([]);
  });

  it("bevarar attention/tidsderivat (klient-omräknar aldrig server-data)", () => {
    const withSignal = app("a", "Submitted");
    withSignal.attentionSignal = "OverdueFollowUp";
    withSignal.lastStatusChangeAt = "2026-05-01";
    const base = bucketsFromGroups(groups(["Submitted", [withSignal]]), "");
    const moved = applyBoardMove(base, { id: "a", to: "Acknowledged" })
      .Acknowledged[0];
    expect(moved?.attentionSignal).toBe("OverdueFollowUp");
    expect(moved?.lastStatusChangeAt).toBe("2026-05-01");
  });
});
