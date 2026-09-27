import { describe, it, expect } from "vitest";
import {
  adSnapshotDtoSchema,
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

  // #1827: C#'s ApplicationDetailDto sends none of the list's derived fields, so the detail
  // schema must not declare them either.
  it("shares the core with the list schema and declares none of the list's own fields", () => {
    const listOnly = [
      "appliedAt",
      "lastStatusChangeAt",
      "lastFollowUpAt",
      "attentionSignal",
      "hasPreservedAdText",
    ];
    for (const key of listOnly) {
      expect(applicationDtoSchema.shape).toHaveProperty(key);
      expect(applicationDetailDtoSchema.shape).not.toHaveProperty(key);
    }
    for (const key of ["id", "jobSeekerId", "jobAdId", "status", "createdAt", "updatedAt", "jobAd"]) {
      expect(applicationDtoSchema.shape).toHaveProperty(key);
      expect(applicationDetailDtoSchema.shape).toHaveProperty(key);
    }
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

// ── #1827 — the list says whether the saved copy still holds its text ────────
describe("applicationDtoSchema — hasPreservedAdText (#1827)", () => {
  it("passes the backend's boolean through", () => {
    for (const value of [true, false]) {
      const parsed = applicationDtoSchema.safeParse({
        ...baseApplication,
        hasPreservedAdText: value,
      });
      expect(parsed.success).toBe(true);
      if (parsed.success) expect(parsed.data.hasPreservedAdText).toBe(value);
    }
  });

  it("deploy skew: an older response without the field parses as unknown", () => {
    const parsed = applicationDtoSchema.safeParse(baseApplication);
    expect(parsed.success).toBe(true);
    if (parsed.success) expect(parsed.data.hasPreservedAdText).toBeUndefined();
  });

  it("rejects a non-boolean value", () => {
    expect(
      applicationDtoSchema.safeParse({
        ...baseApplication,
        hasPreservedAdText: "true",
      }).success,
    ).toBe(false);
  });
});

// ── #842 PR4 — the preserved snapshot carries frozen recruiter contacts ──────
describe("adSnapshotDtoSchema (#842 PR4)", () => {
  const base = {
    title: "Systemutvecklare .NET",
    company: "Spotify",
    location: "Stockholm",
    url: null,
    source: "Platsbanken",
    publishedAt: "2026-04-10T08:00:00Z",
    expiresAt: null,
    description: "Vi söker en utvecklare.",
    contacts: [],
    capturedAt: "2026-04-12T08:00:00Z",
  };

  it("accepts a snapshot with an empty contacts array", () => {
    expect(adSnapshotDtoSchema.safeParse(base).success).toBe(true);
  });

  it("accepts frozen contacts projected through the shared schema", () => {
    const parsed = adSnapshotDtoSchema.safeParse({
      ...base,
      contacts: [
        { name: null, role: null, email: "jobb@acme.se", phone: null, isDerived: true },
      ],
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) expect(parsed.data.contacts).toHaveLength(1);
  });

  it("REQUIRES contacts (never absent on the wire — [] when the ad held none)", () => {
    const { contacts: _omit, ...withoutContacts } = base;
    expect(adSnapshotDtoSchema.safeParse(withoutContacts).success).toBe(false);
  });
});
