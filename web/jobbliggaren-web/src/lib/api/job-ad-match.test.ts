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

import { getJobAdMatchDetail, getJobAdMatchTags } from "./job-ad-match";

const ID_A = "11111111-1111-1111-1111-111111111111";
const ID_B = "22222222-2222-2222-2222-222222222222";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const validBody = {
  entries: {
    [ID_A]: {
      grade: "Strong",
      ssykOverlap: "Match",
      titleSimilarity: "Match",
      regionFit: "Partial",
      employmentFit: "NotAssessed",
    },
  },
};

const EMPTY = { entries: {} };

describe("getJobAdMatchTags", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("tom id-lista → tom batch UTAN backend-rundtur", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([]);

    expect(result).toEqual(EMPTY);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("utan session → tom batch UTAN backend-rundtur (ingen 401-friktion)", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([ID_A]);

    expect(result).toEqual(EMPTY);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 med giltig body → parsad batch; POST med Bearer + jobAdIds", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validBody));
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([ID_A, ID_B]);

    expect(result.entries[ID_A]?.grade).toBe("Strong");
    expect(result.entries[ID_A]?.regionFit).toBe("Partial");
    // POSITIVE-ONLY: ID_B saknas i svaret ⇒ ingen entry (ingen tagg).
    expect(result.entries[ID_B]).toBeUndefined();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/me/job-ad-match-tags");
    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1"
    );
    // #300 PR-5 — includeRelated defaultar till false (behaviour-inert).
    expect(JSON.parse(init.body as string)).toEqual({
      jobAdIds: [ID_A, ID_B],
      includeRelated: false,
    });
  });

  it("#300 PR-5 — includeRelated=true skickas som body-fält (master-switch)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validBody));
    global.fetch = fetchMock;

    await getJobAdMatchTags([ID_A], true);

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({
      jobAdIds: [ID_A],
      includeRelated: true,
    });
  });

  it("#300 PR-5 — batch parsar 'Related' (page-wipe-skydd, ny rung)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        entries: {
          [ID_A]: {
            grade: "Related",
            ssykOverlap: "Match",
            titleSimilarity: "Partial",
            regionFit: "Match",
            employmentFit: "Match",
          },
        },
      })
    );
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([ID_A], true);
    // Utan att Related finns i enum:et hade record-.catch:en blankat HELA mappen.
    expect(result.entries[ID_A]?.grade).toBe("Related");
  });

  it("!ok (500) → tom batch (civil degradering, inga taggar)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await getJobAdMatchTags([ID_A])).toEqual(EMPTY);
  });

  it("401 → tom batch (anonym på publik söksida)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await getJobAdMatchTags([ID_A])).toEqual(EMPTY);
  });

  it("nätverksfel (throw) → tom batch", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await getJobAdMatchTags([ID_A])).toEqual(EMPTY);
  });

  it("kontraktsdrift i entries → tom batch (.catch, ingen krasch)", async () => {
    // Felaktig grad-sträng (inte i enum:en) → record-.catch:ar till {}.
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ entries: { [ID_A]: { grade: "Perfect" } } })
    );
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([ID_A]);
    expect(result).toEqual(EMPTY);
  });

  it("batch parsar 'Top' (golden-rungen) efter widening (F4-16 page-wipe-skydd)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        entries: {
          [ID_A]: {
            grade: "Top",
            ssykOverlap: "Match",
            titleSimilarity: "NotAssessed",
            regionFit: "Match",
            employmentFit: "Match",
          },
        },
      })
    );
    global.fetch = fetchMock;

    const result = await getJobAdMatchTags([ID_A]);
    // Utan widening hade record-.catch:en blankat HELA mappen.
    expect(result.entries[ID_A]?.grade).toBe("Top");
  });
});

function detailRow(verdict: string) {
  return { verdict, matched: [], missing: [] };
}

const validDetail = {
  grade: "Top",
  ssykOverlap: detailRow("Match"),
  titleSimilarity: detailRow("NotAssessed"),
  regionFit: detailRow("Match"),
  employmentFit: detailRow("Match"),
  skillOverlap: { verdict: "Partial", matched: ["Java"], missing: ["AWS"] },
  mustHaveCoverage: detailRow("Match"),
  niceToHaveCoverage: detailRow("NotAssessed"),
};

describe("getJobAdMatchDetail", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("utan session → null UTAN backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getJobAdMatchDetail(ID_A)).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 med giltig detalj → parsad; GET med Bearer mot /{jobAdId}", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validDetail));
    global.fetch = fetchMock;

    const result = await getJobAdMatchDetail(ID_A);
    expect(result?.grade).toBe("Top");
    expect(result?.skillOverlap.matched).toEqual(["Java"]);
    expect(result?.skillOverlap.missing).toEqual(["AWS"]);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    // #300 PR-5 — includeRelated defaultar till false (query-param, ASP.NET
    // bool-binding tar "true"/"false", inte "1").
    expect(url).toBe(
      `http://test-backend/api/v1/me/job-ad-match-tags/${ID_A}?includeRelated=false`
    );
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1"
    );
  });

  it("#300 PR-5 — includeRelated=true skickas som query-param", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validDetail));
    global.fetch = fetchMock;

    await getJobAdMatchDetail(ID_A, true);

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/me/job-ad-match-tags/${ID_A}?includeRelated=true`
    );
  });

  it("#300 PR-5 — detalj parsar grade='Related' (ny rung)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ ...validDetail, grade: "Related" })
    );
    global.fetch = fetchMock;

    const result = await getJobAdMatchDetail(ID_A, true);
    expect(result?.grade).toBe("Related");
  });

  it("200 med null-body → null (ingen matchnings-sektion)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(null));
    expect(await getJobAdMatchDetail(ID_A)).toBeNull();
  });

  it("grade=null men rader finns → parsad (ärlig nedbrytning utan tagg)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ ...validDetail, grade: null })
    );
    global.fetch = fetchMock;

    const result = await getJobAdMatchDetail(ID_A);
    expect(result?.grade).toBeNull();
    expect(result?.ssykOverlap.verdict).toBe("Match");
  });

  it("!ok (500) → null (civil degradering)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await getJobAdMatchDetail(ID_A)).toBeNull();
  });

  it("nätverksfel (throw) → null", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await getJobAdMatchDetail(ID_A)).toBeNull();
  });

  it("kontraktsdrift (okänd grad) → null (parse-fail degraderar civilt)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ ...validDetail, grade: "Perfect" })
    );
    global.fetch = fetchMock;
    expect(await getJobAdMatchDetail(ID_A)).toBeNull();
  });
});
