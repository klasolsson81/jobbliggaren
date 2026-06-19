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

import { getJobAdMatchTags } from "./job-ad-match";

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
    expect(JSON.parse(init.body as string)).toEqual({
      jobAdIds: [ID_A, ID_B],
    });
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
});
