import { beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

const { getSessionId, authedFetch } = vi.hoisted(() => ({
  getSessionId: vi.fn(),
  authedFetch: vi.fn(),
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch }));
import { GET } from "./route";

const BODY = {
  rows: [
    {
      kind: "Word",
      groupConceptId: null,
      groupLabel: null,
      matchedNames: [],
      labelHit: false,
      headWord: "Elingenjör",
      variants: [
        {
          occupationNameConceptId: "kx3b_92W_PuD",
          nameLabel: "Elingenjör",
          groupConceptId: "nDaB_vdy_eAy",
          groupLabel: "Ingenjörer och tekniker inom elektroteknik",
        },
        {
          occupationNameConceptId: "YEm9_pLR_XoT",
          nameLabel: "Elingenjör, fartyg/Fartygseltekniker",
          groupConceptId: "Rsoy_sS6_6c4",
          groupLabel: "Maskinbefäl",
        },
      ],
      exactMatch: true,
    },
    // #2199 PR 3 — a Group row's names carry their own ids; `toEqual(BODY)` below pins that the
    // BFF's parse does not strip them.
    {
      kind: "Group",
      groupConceptId: "DJh5_yyF_hEM",
      groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
      matchedNames: [{ occupationNameConceptId: "7wdX_4rv_33z", nameLabel: "Backend-utvecklare" }],
      labelHit: false,
      headWord: null,
      variants: [],
      exactMatch: false,
    },
  ],
  totalRows: 2,
};

function request(query: string) {
  return new NextRequest(`http://localhost/api/me/occupation-search?${query}`);
}

beforeEach(() => {
  getSessionId.mockReset().mockResolvedValue("session");
  authedFetch.mockReset();
});

describe("GET /api/me/occupation-search (#2198)", () => {
  it("forwards q and nothing else, with the request's cancellation", async () => {
    authedFetch.mockResolvedValue(Response.json(BODY));
    const incoming = request("q=a&term=b&query=c");

    const response = await GET(incoming);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual(BODY);
    expect(authedFetch).toHaveBeenCalledTimes(1);
    expect(authedFetch).toHaveBeenCalledWith(
      "session",
      "/api/v1/job-ads/taxonomy/occupations/search?q=a",
      { signal: incoming.signal },
    );
  });

  it("forwards a q holding an encoded & and = as one q, intact", async () => {
    authedFetch.mockResolvedValue(Response.json(BODY));

    await GET(request("q=a%26term%3Db"));

    const forwarded = new URL(String(authedFetch.mock.calls[0]?.[1]), "http://backend");
    expect(forwarded.pathname).toBe("/api/v1/job-ads/taxonomy/occupations/search");
    expect([...forwarded.searchParams.keys()]).toEqual(["q"]);
    expect(forwarded.searchParams.getAll("q")).toEqual(["a&term=b"]);
  });

  it("is 401 without a session and never reaches the backend", async () => {
    getSessionId.mockResolvedValue(null);

    const response = await GET(request("q=chef"));

    expect(response.status).toBe(401);
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("keeps Retry-After on 429", async () => {
    authedFetch.mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "17" } }));

    const response = await GET(request("q=chef"));

    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("17");
  });

  it.each([500, 503, 400, 403])("answers a backend %i with 502 and an empty body", async (status) => {
    authedFetch.mockResolvedValue(new Response("private backend detail", { status }));

    const response = await GET(request("q=chef"));

    expect(response.status).toBe(502);
    expect(await response.text()).toBe("");
  });

  it("answers a body that does not parse with 502, never a 200 with an empty answer", async () => {
    authedFetch.mockResolvedValue(Response.json({ rows: "nope", totalRows: 0 }));

    expect((await GET(request("q=chef"))).status).toBe(502);
  });

  it("says private, no-store on every branch", async () => {
    const responses: Response[] = [];

    authedFetch.mockResolvedValue(Response.json(BODY));
    responses.push(await GET(request("q=chef")));
    authedFetch.mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "3" } }));
    responses.push(await GET(request("q=chef")));
    authedFetch.mockResolvedValue(new Response(null, { status: 500 }));
    responses.push(await GET(request("q=chef")));
    getSessionId.mockResolvedValue(null);
    responses.push(await GET(request("q=chef")));

    expect(responses.map((r) => r.status)).toEqual([200, 429, 502, 401]);
    for (const response of responses) {
      expect(response.headers.get("cache-control")).toBe("private, no-store");
    }
  });

  // ADR 0018: a cross-site GET arrives signed in, so a GET must never write.
  it("never writes", async () => {
    authedFetch.mockResolvedValue(Response.json(BODY));

    await GET(request("q=chef"));

    expect(authedFetch.mock.calls[0]?.[2]).not.toHaveProperty("method");
    expect(authedFetch.mock.calls[0]?.[2]).not.toHaveProperty("body");
  });
});
