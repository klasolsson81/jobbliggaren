import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// authed-fetch importerar `@/lib/env` (BACKEND_URL) och anropar global `fetch`.
// `import "server-only"` shim:as globalt av vitest.config (server-only-shim), så
// modulen laddas orörd här. Vi mockar env + stubbar fetch och inspekterar exakt
// vilken URL/init transport-primitiven bygger — samma kontrakt actionerna litar på.
vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://backend.test" },
}));

// The forwarding relay (#1202). `next/headers` is mocked per-test rather than globally so
// the existing assertions below keep exercising the no-request-scope path, which is what a
// build-time or background call actually hits.
const headersMock = vi.fn();
vi.mock("next/headers", () => ({ headers: () => headersMock() }));

import { authedFetch } from "./authed-fetch";

describe("authedFetch", () => {
  beforeEach(() => {
    // Default to the out-of-request-scope behaviour, which is what these tests saw before
    // `next/headers` was mocked here at all: the real `headers()` throws outside a request.
    // It never returns undefined, so the production relay is not hardened against that —
    // guarding a state no path produces would be a fiction, and the mock is the thing that
    // has to be honest.
    headersMock.mockRejectedValue(new Error("`headers` was called outside a request scope"));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    headersMock.mockReset();
  });

  it("prefixes BACKEND_URL, injects Bearer + JSON headers, forces no-store, passes method/body", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    await authedFetch("sess-1", "/api/v1/resumes", {
      method: "POST",
      body: JSON.stringify({ name: "CV" }),
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://backend.test/api/v1/resumes");
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ name: "CV" }));
    expect(init.cache).toBe("no-store");
    expect(init.headers).toEqual({
      Authorization: "Bearer sess-1",
      "Content-Type": "application/json",
    });
  });

  it("works for a bodyless request (DELETE) — still Bearer + no-store, no body", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    await authedFetch("sess-2", "/api/v1/resumes/abc", { method: "DELETE" });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://backend.test/api/v1/resumes/abc");
    expect(init.method).toBe("DELETE");
    expect(init.body).toBeUndefined();
    expect(init.cache).toBe("no-store");
    expect(init.headers).toMatchObject({ Authorization: "Bearer sess-2" });
  });

  it("relays the inbound client IP alongside the auth pair (#1202)", async () => {
    headersMock.mockResolvedValue({
      get: (name: string) =>
        ({
          "x-forwarded-for": "198.51.100.10",
          "x-forwarded-proto": "https",
          // An inbound Authorization is present to show it goes nowhere. The guarantee is
          // the two-name allowlist in RELAYED, NOT the spread order — dotnet-architect
          // measured that reversing the spread still passes, because the relayed and auth
          // key sets are disjoint by construction. The earlier comment here also claimed
          // Caddy strips an inbound Authorization; nothing in the Caddyfile does that, and
          // an untrue security claim is worse than none.
          authorization: "Bearer attacker-supplied",
        })[name.toLowerCase()] ?? null,
    });
    const fetchMock = vi.fn(async () => ({ ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    await authedFetch("sess-4", "/api/v1/me", { method: "GET" });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.headers).toEqual({
      "x-forwarded-for": "198.51.100.10",
      "x-forwarded-proto": "https",
      Authorization: "Bearer sess-4",
      "Content-Type": "application/json",
    });
  });

  it("sends no forwarding headers outside a request scope — build and background calls", async () => {
    headersMock.mockRejectedValue(new Error("outside a request scope"));
    const fetchMock = vi.fn(async () => ({ ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    await authedFetch("sess-5", "/api/v1/me", { method: "GET" });

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.headers).toEqual({
      Authorization: "Bearer sess-5",
      "Content-Type": "application/json",
    });
  });

  it("returns the raw Response and never reads the body (TD-10 invariant)", async () => {
    const jsonSpy = vi.fn();
    const textSpy = vi.fn();
    const sentinel = { ok: false, status: 500, json: jsonSpy, text: textSpy } as unknown as Response;
    const fetchMock = vi.fn(async () => sentinel);
    vi.stubGlobal("fetch", fetchMock);

    const res = await authedFetch("sess-3", "/api/v1/me/profile", { method: "PATCH" });

    expect(res).toBe(sentinel);
    expect(jsonSpy).not.toHaveBeenCalled();
    expect(textSpy).not.toHaveBeenCalled();
  });
});
