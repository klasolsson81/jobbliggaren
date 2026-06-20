import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
}));

import { getParsedResume, getCvReview, getCvImprovements } from "./resumes";

const VALID_ID = "11111111-1111-4111-8111-111111111111";

const parsedResumeFixture = {
  id: VALID_ID,
  status: "PendingReview",
  detectedLanguage: "Swedish",
  sourceFileName: "cv.pdf",
  confidence: {
    overall: "Confident",
    requiresManualReview: false,
    fallback: "None",
    sections: [
      { section: "Experience", level: "Confident", evidence: ["rubrik hittad"] },
    ],
  },
  personnummer: { found: false, count: 0, kinds: [] },
  content: {
    contact: { fullName: "Anna Andersson", email: null, phone: null, location: null },
    profile: null,
    experiences: [
      { title: "Utvecklare", organization: "Acme", period: "2021–2024", rawText: "…" },
    ],
    educations: [],
    skills: ["C#"],
    languages: ["Svenska"],
  },
  occupationProposals: [
    { conceptId: "abc", label: "Mjukvaruutvecklare", matchedOn: "Utvecklare" },
  ],
  createdAt: "2026-06-16T10:00:00+00:00",
  updatedAt: "2026-06-16T10:00:00+00:00",
};

const reviewFixture = {
  rubricVersion: "1.0.0",
  profile: "Ats",
  categories: [
    {
      category: "Content",
      passCount: 3,
      warnCount: 1,
      failCount: 0,
      notAssessedCount: 2,
      band: "Competitive",
    },
  ],
  verdicts: [
    {
      criterionId: "A1",
      name: "Kontaktuppgifter",
      category: "Content",
      verdict: "Pass",
      evidence: [
        { kind: "TextSpan", start: 0, length: 5, quote: "Anna", note: "tydligt namn", observation: null },
      ],
      notAssessedReason: null,
    },
  ],
  criticalFails: [],
  assessedCount: 4,
  totalCount: 42,
};

// Fixture covering BOTH change shapes the engine emits:
//  1. a replacement-based change (KnowledgeBank provenance, operation null)
//  2. an operation-only change (StructuralTransform provenance, replacement null)
// This mirrors the propose-and-approve contract — every change is ANTINGEN
// ersättning ELLER strukturell operation, never both.
const improvementFixture = {
  clicheListVersion: "cliche-1.2.0",
  verbMappingVersion: "verb-1.1.0",
  rubricVersion: "1.0.0",
  profile: "Ats",
  changes: [
    {
      targetId: "exp-0-line-2",
      kind: "ClicheReplacement",
      category: "Language",
      criterionId: "A7",
      evidence: {
        kind: "TextSpan",
        start: 12,
        length: 18,
        quote: "teamplayer med driv",
        note: "klyscha utan konkret stöd",
        observation: null,
      },
      replacement: { before: "teamplayer med driv", after: "ledde ett team om fyra" },
      operation: null,
      rationale: "Konkretisera klyschan med en mätbar handling.",
      provenance: {
        kind: "KnowledgeBank",
        source: "cliche-bank",
        version: "1.2.0",
        key: "teamplayer",
        transform: null,
      },
    },
    {
      targetId: "header-personnummer",
      kind: "PersonnummerStrip",
      category: "AtsParsability",
      criterionId: null,
      evidence: {
        kind: "Structural",
        start: null,
        length: null,
        quote: null,
        note: null,
        observation: "Ett personnummer hittades i sidhuvudet.",
      },
      replacement: null,
      operation: { kind: "RemovePersonnummer", target: "header" },
      rationale: "Personnummer behövs inte i ett CV och bör tas bort.",
      provenance: {
        kind: "StructuralTransform",
        source: null,
        version: null,
        key: null,
        transform: "RemovePersonnummer",
      },
    },
  ],
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("getParsedResume", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("returnerar unauthorized utan session", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const result = await getParsedResume(VALID_ID);
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("returnerar notFound för ogiltigt (icke-GUID) id utan att nå backend", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    const result = await getParsedResume("inte-en-guid");
    expect(result).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returnerar ok + parsad DTO och anropar ägar-scopad parsed-endpoint med Bearer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(parsedResumeFixture));
    global.fetch = fetchMock;

    const result = await getParsedResume(VALID_ID);

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.id).toBe(VALID_ID);
      expect(result.data.personnummer.found).toBe(false);
    }
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://test-backend/api/v1/resumes/parsed/${VALID_ID}`);
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer sess-1");
  });

  it("mappar 404 → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));
    expect(await getParsedResume(VALID_ID)).toEqual({ kind: "notFound" });
  });

  it("mappar 401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await getParsedResume(VALID_ID)).toEqual({ kind: "unauthorized" });
  });

  it("mappar 429 → rateLimited med retryAfterSeconds", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "30" } }),
    );
    expect(await getParsedResume(VALID_ID)).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
  });

  it("mappar 500 → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await getParsedResume(VALID_ID)).toEqual({ kind: "error" });
  });

  it("mappar shape-mismatch → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ id: VALID_ID }));
    expect(await getParsedResume(VALID_ID)).toEqual({ kind: "error" });
  });

  it("mappar nätverksfel → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await getParsedResume(VALID_ID)).toEqual({ kind: "error" });
  });
});

describe("getCvReview", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("returnerar ok + review och skickar profilen i query (exakt case)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(reviewFixture));
    global.fetch = fetchMock;

    const result = await getCvReview(VALID_ID, "Visual");

    expect(result.kind).toBe("ok");
    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/review?profile=Visual`,
    );
  });

  it("default-profilen Ats bär exakt case i query", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(reviewFixture));
    global.fetch = fetchMock;
    await getCvReview(VALID_ID, "Ats");
    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toContain("review?profile=Ats");
  });

  it("returnerar unauthorized utan session", async () => {
    getSessionIdMock.mockResolvedValue(null);
    expect(await getCvReview(VALID_ID, "Ats")).toEqual({ kind: "unauthorized" });
  });

  it("returnerar notFound för ogiltigt id", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCvReview("xxx", "Ats")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("mappar 404 → notFound (degraderar civilt på granska-sidan)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));
    expect(await getCvReview(VALID_ID, "Ats")).toEqual({ kind: "notFound" });
  });
});

describe("getCvImprovements", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("returnerar ok + förbättrings-DTO och anropar ägar-scopad improvements-endpoint med Bearer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(improvementFixture));
    global.fetch = fetchMock;

    const result = await getCvImprovements(VALID_ID, "Ats");

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.changes).toHaveLength(2);
      // Both shapes survived the parse: one replacement-based, one operation-only.
      expect(result.data.changes[0]?.replacement).not.toBeNull();
      expect(result.data.changes[0]?.operation).toBeNull();
      expect(result.data.changes[1]?.operation).not.toBeNull();
      expect(result.data.changes[1]?.replacement).toBeNull();
    }
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/improvements?profile=Ats`,
    );
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1",
    );
  });

  it("skickar profilen i query med exakt case (Visual)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(improvementFixture));
    global.fetch = fetchMock;
    await getCvImprovements(VALID_ID, "Visual");
    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toContain("improvements?profile=Visual");
  });

  it("sätter cache: no-store (compute-on-demand, aldrig cachad)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(improvementFixture));
    global.fetch = fetchMock;
    await getCvImprovements(VALID_ID, "Ats");
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(init.cache).toBe("no-store");
  });

  it("returnerar unauthorized utan session (BFF läser aldrig PII utan Bearer)", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({
      kind: "unauthorized",
    });
    // Ingen backend-träff utan session.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returnerar notFound för ogiltigt (icke-GUID) id utan att nå backend", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCvImprovements("inte-en-guid", "Ats")).toEqual({
      kind: "notFound",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("mappar 404 → notFound (befordrat/borttaget parsed-artefakt; sidan degraderar civilt)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({
      kind: "notFound",
    });
  });

  it("mappar 401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({
      kind: "unauthorized",
    });
  });

  it("mappar 500 → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({ kind: "error" });
  });

  it("mappar nätverksfel (throw) → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({ kind: "error" });
  });

  it("mappar zod-drift (okänt enum-värde) → error (fail-loud, ingen tyst fel-render)", async () => {
    const drifted = {
      ...improvementFixture,
      changes: [
        {
          ...improvementFixture.changes[0],
          // Okänd ändringstyp som inte finns i den låsta mängden.
          kind: "TeleportSection",
        },
      ],
    };
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(drifted));
    expect(await getCvImprovements(VALID_ID, "Ats")).toEqual({ kind: "error" });
  });
});
