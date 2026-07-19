import { describe, it, expect } from "vitest";
import {
  adContactDtoSchema,
  jobAdDetailDtoSchema,
  jobAdDtoSchema,
  jobAdFiltersSchema,
  jobAdSortBySchema,
  jobAdStatusSchema,
  jobSourceSchema,
  listJobAdsResultSchema,
  suggestJobAdTermsResultSchema,
  suggestionDtoSchema,
} from "./job-ads";

const baseJobAd = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Senior Backend Developer",
  companyName: "Acme AB",
  description: "Vi söker en .NET-utvecklare.",
  url: "https://example.com/jobb/123",
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-05-13T08:00:00Z",
  expiresAt: "2026-06-13T08:00:00Z",
  createdAt: "2026-05-13T08:01:00Z",
};

describe("jobAdStatusSchema", () => {
  it("accepts Active, Archived", () => {
    for (const s of ["Active", "Archived"]) {
      expect(jobAdStatusSchema.safeParse(s).success).toBe(true);
    }
  });

  it("rejects unknown status", () => {
    expect(jobAdStatusSchema.safeParse("Pending").success).toBe(false);
  });

  it("rejects the retired Expired status (#886 regression lock)", () => {
    // Expired was declared, shipped to this schema and rendered for the
    // product's entire history without a single writer. Re-adding it to the
    // enum without a backend writer would resurrect the fiction — this lock
    // makes that a red test instead of a silent drift.
    expect(jobAdStatusSchema.safeParse("Expired").success).toBe(false);
  });
});

describe("jobSourceSchema", () => {
  it("accepts Manual, Platsbanken, LinkedIn, Eures", () => {
    for (const s of ["Manual", "Platsbanken", "LinkedIn", "Eures"]) {
      expect(jobSourceSchema.safeParse(s).success).toBe(true);
    }
  });

  it("rejects unknown source", () => {
    expect(jobSourceSchema.safeParse("Indeed").success).toBe(false);
  });
});

describe("jobAdSortBySchema", () => {
  it("accepts the six sort-by values (incl. Relevance ADR 0042 D + MatchDesc F4-14 ADR 0076)", () => {
    for (const v of [
      "PublishedAtDesc",
      "PublishedAtAsc",
      "ExpiresAtDesc",
      "ExpiresAtAsc",
      "Relevance",
      "MatchDesc",
    ]) {
      expect(jobAdSortBySchema.safeParse(v).success).toBe(true);
    }
  });

  it("accepts MatchDesc (read-side ListJobAdsSort widening, F4-14)", () => {
    expect(jobAdSortBySchema.safeParse("MatchDesc").success).toBe(true);
  });

  it("rejects unknown sort-by", () => {
    expect(jobAdSortBySchema.safeParse("UpdatedAt").success).toBe(false);
  });
});

describe("jobAdDtoSchema", () => {
  it("accepts a valid job ad", () => {
    expect(jobAdDtoSchema.safeParse(baseJobAd).success).toBe(true);
  });

  it("accepts null expiresAt (annons utan slut-datum)", () => {
    expect(
      jobAdDtoSchema.safeParse({ ...baseJobAd, expiresAt: null }).success
    ).toBe(true);
  });

  // #293/#306 — `isNew` är BORTTAGET ur kontraktet (tidsbaserad NY ersatt av
  // FE-beräknad oläst-watermark). Schemat ignorerar extra okända fält (Zod
  // default strip), så en wire-payload som ännu bär `isNew` parsar ändå — men
  // typen exponerar det inte längre, och FE läser det aldrig.
  it("strips an unknown isNew field if present (no longer in the contract)", () => {
    const parsed = jobAdDtoSchema.safeParse({ ...baseJobAd, isNew: true });
    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect("isNew" in parsed.data).toBe(false);
    }
  });

  it("rejects unknown status value", () => {
    expect(
      jobAdDtoSchema.safeParse({ ...baseJobAd, status: "Bogus" }).success
    ).toBe(false);
  });

  it("rejects when title missing", () => {
    const partial: Partial<typeof baseJobAd> = { ...baseJobAd };
    delete partial.title;
    expect(jobAdDtoSchema.safeParse(partial).success).toBe(false);
  });

  describe("URL scheme (defense-in-depth XSS-skydd, security-auditor F2-P10)", () => {
    it("accepts https URL", () => {
      expect(jobAdDtoSchema.safeParse(baseJobAd).success).toBe(true);
    });

    it("accepts http URL", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "http://example.com/jobb/123",
        }).success
      ).toBe(true);
    });

    it("accepts empty URL (Manual-källa kan ha tomt fält)", () => {
      expect(
        jobAdDtoSchema.safeParse({ ...baseJobAd, url: "" }).success
      ).toBe(true);
    });

    it("rejects javascript: scheme (XSS-vektor)", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "javascript:alert(document.cookie)",
        }).success
      ).toBe(false);
    });

    it("rejects data: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "data:text/html,<script>alert(1)</script>",
        }).success
      ).toBe(false);
    });

    it("rejects vbscript: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "vbscript:msgbox(1)",
        }).success
      ).toBe(false);
    });

    it("rejects file: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "file:///etc/passwd",
        }).success
      ).toBe(false);
    });

    it("is case-insensitive (rejects uppercase JAVASCRIPT:)", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "JAVASCRIPT:alert(1)",
        }).success
      ).toBe(false);
    });
  });
});

describe("listJobAdsResultSchema", () => {
  it("accepts a paged result", () => {
    const result = {
      items: [baseJobAd],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(true);
  });

  it("accepts empty items array (legitimt tomt sökresultat)", () => {
    const result = { items: [], totalCount: 0, page: 1, pageSize: 20 };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(true);
  });

  it("#745 — a real description-less list row parses (production wire shape)", () => {
    // Post-#745 the backend omits description on the list wire. This asserts the ACTUAL
    // shape parses — and reds if someone re-adds a required `description: z.string()` to
    // jobAdDtoSchema, which would reject every real production row. The strip-lock in the
    // jobAdDtoSchema block feeds a description-BEARING baseJobAd and cannot see this failure
    // mode. Hand-built (not via jobAdDtoSchema.parse) so the mutation is actually caught.
    const listWireRow = {
      id: "22222222-2222-2222-2222-222222222222",
      title: "Backend-utvecklare",
      companyName: "Acme AB",
      url: "https://example.com/jobb/222",
      source: "Platsbanken",
      status: "Active",
      publishedAt: "2026-05-13T08:00:00Z",
      expiresAt: null,
      createdAt: "2026-05-13T08:01:00Z",
    };
    const result = { items: [listWireRow], totalCount: 1, page: 1, pageSize: 20 };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(true);
  });

  it("rejects when item shape invalid", () => {
    const result = {
      items: [{ ...baseJobAd, source: "Indeed" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(false);
  });
});

describe("jobAdFiltersSchema (ADR 0042 Beslut B multi + D Relevance)", () => {
  const valid = {
    occupationGroup: [] as string[],
    region: [] as string[],
    q: "",
    sortBy: "PublishedAtDesc",
  };

  it("accepts all-empty filter (default state)", () => {
    expect(jobAdFiltersSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts multiple JobTech-style concept-ids (OR-bevakning)", () => {
    expect(
      jobAdFiltersSchema.safeParse({
        ...valid,
        occupationGroup: ["MVqp_eS8_kDZ", "CifL_Rzy_Mku"],
      }).success
    ).toBe(true);
  });

  it("rejects a concept-id with invalid characters", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, occupationGroup: ["ssyk!hack"] })
        .success
    ).toBe(false);
  });

  it("rejects a concept-id longer than 32 chars", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, occupationGroup: ["a".repeat(33)] })
        .success
    ).toBe(false);
  });

  it("rejects more than 400 occupationGroup values (mirrors SearchCriteria.MaxConceptIds)", () => {
    const tooMany = Array.from({ length: 401 }, (_, i) => `code${i}`);
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, occupationGroup: tooMany })
        .success
    ).toBe(false);
  });

  it("accepts exactly 400 occupationGroup values (cap boundary)", () => {
    const capped = Array.from({ length: 400 }, (_, i) => `code${i}`);
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, occupationGroup: capped })
        .success
    ).toBe(true);
  });

  it("rejects q shorter than 2 chars (matches backend validator)", () => {
    expect(jobAdFiltersSchema.safeParse({ ...valid, q: "a" }).success).toBe(
      false
    );
  });

  it("rejects q longer than 100 chars", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, q: "a".repeat(101) }).success
    ).toBe(false);
  });

  it("accepts q at boundary (2 chars and 100 chars)", () => {
    expect(jobAdFiltersSchema.safeParse({ ...valid, q: "ab" }).success).toBe(
      true
    );
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, q: "a".repeat(100) }).success
    ).toBe(true);
  });

  it("rejects unknown sortBy", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, sortBy: "Bogus" }).success
    ).toBe(false);
  });

  it("rejects Relevance without a search term (Beslut D fail-fast)", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, sortBy: "Relevance", q: "" })
        .success
    ).toBe(false);
  });

  it("accepts Relevance with a >=2 char search term", () => {
    expect(
      jobAdFiltersSchema.safeParse({
        ...valid,
        sortBy: "Relevance",
        q: "java",
      }).success
    ).toBe(true);
  });
});

// ADR 0067 Beslut 5a — suggest-union (titel + taxonomi). Verifierar wire-
// kontraktet: kind serialiseras som HELTAL av backend (native enum utan
// JsonStringEnumConverter), men schemat accepterar även sträng-namn defensivt.
describe("suggestionDtoSchema (ADR 0067 Beslut 5a)", () => {
  it("maps integer kind (faktisk wire-form) to the enum name", () => {
    const parsed = suggestionDtoSchema.safeParse({
      kind: 0,
      conceptId: null,
      label: "Backend-utvecklare",
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) expect(parsed.data.kind).toBe("Title");
  });

  it("maps each ordinal to the matching SuggestionKind name", () => {
    const expected = [
      "Title",
      "Region",
      "Municipality",
      "OccupationField",
      "OccupationGroup",
    ] as const;
    expected.forEach((name, i) => {
      const parsed = suggestionDtoSchema.safeParse({
        kind: i,
        conceptId: name === "Title" ? null : "abc_123",
        label: name,
      });
      expect(parsed.success).toBe(true);
      if (parsed.success) expect(parsed.data.kind).toBe(name);
    });
  });

  it("accepts a string kind name defensively (om converter senare adderas)", () => {
    const parsed = suggestionDtoSchema.safeParse({
      kind: "OccupationGroup",
      conceptId: "ssyk_1234",
      label: "Systemutvecklare m.fl.",
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) expect(parsed.data.kind).toBe("OccupationGroup");
  });

  it("rejects an out-of-range kind index", () => {
    expect(
      suggestionDtoSchema.safeParse({ kind: 99, conceptId: null, label: "x" })
        .success
    ).toBe(false);
  });

  it("rejects an unknown string kind", () => {
    expect(
      suggestionDtoSchema.safeParse({
        kind: "Bogus",
        conceptId: null,
        label: "x",
      }).success
    ).toBe(false);
  });

  it("parses an array of mixed title + taxonomy suggestions", () => {
    const parsed = suggestJobAdTermsResultSchema.safeParse([
      { kind: 0, conceptId: null, label: "Systemutvecklare" },
      { kind: 4, conceptId: "ssyk_1234", label: "Mjukvaru- och systemutvecklare m.fl." },
      { kind: 1, conceptId: "lan_01", label: "Stockholms län" },
    ]);
    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data).toHaveLength(3);
      expect(parsed.data[1]).toEqual({
        kind: "OccupationGroup",
        conceptId: "ssyk_1234",
        label: "Mjukvaru- och systemutvecklare m.fl.",
      });
    }
  });
});

// ── #842 PR4 — recruiter contacts on the DETAIL surfaces (R2/ISP split) ──────
describe("adContactDtoSchema (#842 PR4)", () => {
  const declared = {
    name: "Anna Svensson",
    role: "Rekryterare",
    email: "anna.svensson@acme.se",
    phone: "070-123 45 67",
    isDerived: false,
  };

  it("accepts a fully-populated declared contact", () => {
    expect(adContactDtoSchema.safeParse(declared).success).toBe(true);
  });

  it("accepts a derived contact with null name (the backend never guesses a name)", () => {
    const derived = {
      name: null,
      role: null,
      email: "jobb@acme.se",
      phone: null,
      isDerived: true,
    };
    expect(adContactDtoSchema.safeParse(derived).success).toBe(true);
  });

  it("requires every field key (nullable, not optional — the C# record always emits them)", () => {
    const { isDerived: _omitFlag, ...withoutFlag } = declared;
    expect(adContactDtoSchema.safeParse(withoutFlag).success).toBe(false);
    const { name: _omitName, ...withoutName } = declared;
    expect(adContactDtoSchema.safeParse(withoutName).success).toBe(false);
  });

  it("rejects a non-boolean isDerived discriminator", () => {
    expect(
      adContactDtoSchema.safeParse({ ...declared, isDerived: "true" }).success
    ).toBe(false);
  });
});

describe("jobAdDetailDtoSchema (#842 PR4 — detail twin of the list schema)", () => {
  it("accepts the list fields plus an empty contacts array", () => {
    expect(
      jobAdDetailDtoSchema.safeParse({ ...baseJobAd, contacts: [] }).success
    ).toBe(true);
  });

  it("accepts a declared + a derived contact", () => {
    const parsed = jobAdDetailDtoSchema.safeParse({
      ...baseJobAd,
      contacts: [
        {
          name: "Anna",
          role: "Rekryterare",
          email: "anna@acme.se",
          phone: null,
          isDerived: false,
        },
        { name: null, role: null, email: "jobb@acme.se", phone: null, isDerived: true },
      ],
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) expect(parsed.data.contacts).toHaveLength(2);
  });

  it("REQUIRES contacts (never absent on the wire — backend sends [] when none)", () => {
    // The twin of the list-schema lock below: the detail wire always carries the
    // field, so a payload without it must fail rather than silently drop the block.
    expect(jobAdDetailDtoSchema.safeParse(baseJobAd).success).toBe(false);
  });

  it("#745 — REQUIRES description (re-declared on the detail wire, never inherited)", () => {
    // #745 removed description from the list schema; the detail schema re-adds it via
    // .extend(). The detail wire must still carry the ad body — a payload without it must
    // fail rather than render an empty description block. Building the input from the LIST
    // parse (which strips description) proves the re-declaration is load-bearing: without
    // the explicit .extend({ description }), a description-less row would wrongly validate.
    const listRowWithoutDescription = jobAdDtoSchema.parse(baseJobAd);
    expect(
      jobAdDetailDtoSchema.safeParse({ ...listRowWithoutDescription, contacts: [] }).success,
    ).toBe(false);
  });

  it("rejects a malformed contact entry", () => {
    expect(
      jobAdDetailDtoSchema.safeParse({
        ...baseJobAd,
        contacts: [{ isDerived: false }],
      }).success
    ).toBe(false);
  });
});

describe("jobAdDtoSchema stays the LIST wire (R2 — contacts never on search; #745 — no description)", () => {
  it("does not carry contacts: a stray contacts field is stripped, never exposed", () => {
    // Widening the list schema would put every recruiter's structured contacts on
    // the bulk-harvest search surface (re-bind R2). Zod strips unknown keys by
    // default, so even a stray contacts field is dropped and the type never
    // exposes it — contacts live ONLY on jobAdDetailDtoSchema.
    const parsed = jobAdDtoSchema.safeParse({
      ...baseJobAd,
      contacts: [
        { name: null, role: null, email: "x@y.se", phone: null, isDerived: true },
      ],
    });
    expect(parsed.success).toBe(true);
    if (parsed.success) expect("contacts" in parsed.data).toBe(false);
  });

  it("#745 — does not carry description: a stray description field is stripped, never exposed", () => {
    // #745 (epic #737, d1-list-dto-ships-full-description): the backend dropped the ad body
    // from the list wire (no list card renders it; the detail fetches it separately via
    // getJobAd). This must be mirrored here or listJobAdsResultSchema's required
    // description would reject every list row. baseJobAd still carries description; Zod
    // strips it so the parsed list row never exposes it — description lives ONLY on the
    // detail schema. Twin of the detail-requires-description lock below.
    const parsed = jobAdDtoSchema.safeParse(baseJobAd);
    expect(parsed.success).toBe(true);
    if (parsed.success) expect("description" in parsed.data).toBe(false);
  });
});
