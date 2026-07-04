import { describe, it, expect, vi, afterEach } from "vitest";

// authed-fetch importerar `@/lib/env` (BACKEND_URL) och anropar global `fetch`.
// `import "server-only"` shim:as globalt av vitest.config (server-only-shim), så
// modulen laddas orörd här. Vi mockar env + stubbar fetch och inspekterar exakt
// vilken URL/init transport-primitiven bygger — samma kontrakt actionerna litar på.
vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://backend.test" },
}));

import { authedFetch } from "./authed-fetch";

describe("authedFetch", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
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
