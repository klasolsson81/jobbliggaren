import { describe, it, expect } from "vitest";
import {
  applicationDetailDtoSchema,
  applicationDtoSchema,
  applicationStatusSchema,
  getApplicationsResultSchema,
  pipelineGroupDtoSchema,
  pipelineResponseSchema,
} from "./applications";

const baseApplication = {
  id: "11111111-1111-1111-1111-111111111111",
  jobSeekerId: "22222222-2222-2222-2222-222222222222",
  jobAdId: null,
  status: "Submitted",
  createdAt: "2026-05-11T10:00:00Z",
  updatedAt: "2026-05-11T10:00:00Z",
};

describe("applicationStatusSchema", () => {
  it("accepts all 10 known statuses", () => {
    const all = [
      "Draft",
      "Submitted",
      "Acknowledged",
      "InterviewScheduled",
      "Interviewing",
      "OfferReceived",
      "Accepted",
      "Rejected",
      "Withdrawn",
      "Ghosted",
    ];
    for (const s of all) {
      expect(applicationStatusSchema.safeParse(s).success).toBe(true);
    }
  });

  it("rejects unknown status", () => {
    expect(applicationStatusSchema.safeParse("Unknown").success).toBe(false);
  });
});

describe("applicationDtoSchema", () => {
  it("accepts valid application", () => {
    expect(applicationDtoSchema.safeParse(baseApplication).success).toBe(true);
  });

  it("accepts jobAdId as string", () => {
    expect(
      applicationDtoSchema.safeParse({ ...baseApplication, jobAdId: "abc" })
        .success
    ).toBe(true);
  });

  it("rejects status with unknown value", () => {
    expect(
      applicationDtoSchema.safeParse({
        ...baseApplication,
        status: "Bogus",
      }).success
    ).toBe(false);
  });
});

describe("applicationDetailDtoSchema", () => {
  const validDetail = {
    ...baseApplication,
    coverLetter: null,
    followUps: [],
    notes: [],
  };

  it("accepts valid detail with empty arrays", () => {
    expect(applicationDetailDtoSchema.safeParse(validDetail).success).toBe(
      true
    );
  });

  it("accepts followUp + note entries", () => {
    const detail = {
      ...validDetail,
      followUps: [
        {
          id: "f1",
          channel: "Email",
          scheduledAt: "2026-05-12T10:00:00Z",
          note: null,
          outcome: "Pending",
          outcomeAt: null,
          createdAt: "2026-05-11T10:00:00Z",
        },
      ],
      notes: [
        {
          id: "n1",
          content: "Test",
          createdAt: "2026-05-11T10:00:00Z",
        },
      ],
    };
    expect(applicationDetailDtoSchema.safeParse(detail).success).toBe(true);
  });

  it("rejects when followUps array missing", () => {
    const withoutFollowUps: Partial<typeof validDetail> = { ...validDetail };
    delete withoutFollowUps.followUps;
    expect(
      applicationDetailDtoSchema.safeParse(withoutFollowUps).success
    ).toBe(false);
  });
});

describe("pipelineGroupDtoSchema", () => {
  it("accepts valid group", () => {
    const group = {
      status: "Submitted",
      count: 1,
      applications: [baseApplication],
    };
    expect(pipelineGroupDtoSchema.safeParse(group).success).toBe(true);
  });

  it("rejects negative count", () => {
    const group = {
      status: "Submitted",
      count: -1,
      applications: [],
    };
    expect(pipelineGroupDtoSchema.safeParse(group).success).toBe(false);
  });
});

describe("pipelineResponseSchema", () => {
  it("accepts array of groups", () => {
    const groups = [
      { status: "Submitted", count: 1, applications: [baseApplication] },
      { status: "Acknowledged", count: 0, applications: [] },
    ];
    expect(pipelineResponseSchema.safeParse(groups).success).toBe(true);
  });

  it("rejects when items in array are not groups", () => {
    expect(pipelineResponseSchema.safeParse([{ foo: "bar" }]).success).toBe(
      false
    );
  });
});

describe("getApplicationsResultSchema", () => {
  it("accepts valid paged result", () => {
    const result = {
      items: [baseApplication],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(getApplicationsResultSchema.safeParse(result).success).toBe(true);
  });

  it("rejects when item shape invalid", () => {
    const result = {
      items: [{ ...baseApplication, status: "WrongValue" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(getApplicationsResultSchema.safeParse(result).success).toBe(false);
  });
});

// ── #630 PR 7: list-DTO:ns tids-scalars släpps igenom (design §5/§11) ────────
describe("applicationDtoSchema — lastStatusChangeAt/lastFollowUpAt (PR 7)", () => {
  it("parsar scalars som backend burit sedan PR 3 (rådata för display-derivering)", () => {
    const parsed = applicationDtoSchema.safeParse({
      ...baseApplication,
      lastStatusChangeAt: "2026-05-10T08:00:00Z",
      lastFollowUpAt: "2026-05-12T08:00:00Z",
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data.lastStatusChangeAt).toBe("2026-05-10T08:00:00Z");
      expect(parsed.data.lastFollowUpAt).toBe("2026-05-12T08:00:00Z");
    }
  });

  it("deploy-skew: saknade/nullade fält kraschar inte parse", () => {
    expect(applicationDtoSchema.safeParse(baseApplication).success).toBe(true);
    expect(
      applicationDtoSchema.safeParse({
        ...baseApplication,
        lastFollowUpAt: null,
      }).success
    ).toBe(true);
  });
});
