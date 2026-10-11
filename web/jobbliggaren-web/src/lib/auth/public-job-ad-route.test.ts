import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie-names";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import { publicJobAdRoute } from "./public-job-ad-route";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const fetchMock = vi.fn<typeof fetch>();

function request(
  path: string,
  opts?: { cookie?: boolean; headers?: Record<string, string>; method?: string }
): NextRequest {
  const headers = new Headers(opts?.headers);
  if (opts?.cookie) headers.set("cookie", `${SESSION_COOKIE_NAME}=abc`);
  return new NextRequest(`https://app.test${path}`, { headers, method: opts?.method ?? "GET" });
}

/** The proxy's own header bag after #2128 prepares a nonce; the rewrite must carry exactly this. */
function forwardedBag(source: NextRequest): Headers {
  const bag = new Headers(source.headers);
  bag.set("x-nonce", "NONCE-1");
  bag.set("content-security-policy", "script-src 'nonce-NONCE-1'");
  return bag;
}

function answer(status: number, headers?: Record<string, string>): Response {
  return new Response(status === 200 ? "{}" : null, { status, headers });
}

async function route(req: NextRequest, hasSession = false) {
  return publicJobAdRoute(req, forwardedBag(req), hasSession);
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("publicJobAdRoute — an anonymous /jobb/<uuid>", () => {
  it("rewrites a listed ad to its internal page and keeps the URL", async () => {
    fetchMock.mockResolvedValue(answer(200));
    const res = await route(request(`/jobb/${ID}`));

    expect(res?.headers.get("x-middleware-rewrite")).toBe(`https://app.test/publik/jobb/${ID}`);
    expect(res?.status).toBe(200);
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("x-robots-tag")).toBeNull();
  });

  it("answers an ended and an erased ad (both 410 upstream) with the same 410 rewrite, byte for byte", async () => {
    fetchMock.mockResolvedValueOnce(answer(410)).mockResolvedValueOnce(answer(410));
    const ended = await route(request(`/jobb/${ID}`));
    const erased = await route(request(`/jobb/${ID}`));

    for (const res of [ended, erased]) {
      expect(res?.status).toBe(410);
      expect(res?.headers.get("x-middleware-rewrite")).toBe("https://app.test/publik/borttagen");
      expect(res?.headers.get("cache-control")).toBe("no-store");
      expect(res?.headers.get("x-robots-tag")).toBe("noindex");
    }
    expect([...(ended?.headers ?? [])]).toEqual([...(erased?.headers ?? [])]);
  });

  it.each([404, 400])("answers an upstream %i with a path no route matches (a real 404)", async (status) => {
    fetchMock.mockResolvedValue(answer(status));
    const res = await route(request(`/jobb/${ID}`));

    expect(res?.headers.get("x-middleware-rewrite")).toBe("https://app.test/publik/saknas");
    expect(res?.status).toBe(200); // the proxy sets no status; the unmatched path is what Next answers 404
  });

  it("answers a 429 with a 503 and the API's Retry-After, bounded", async () => {
    fetchMock.mockResolvedValue(answer(429, { "Retry-After": "7" }));
    const res = await route(request(`/jobb/${ID}`));

    expect(res?.status).toBe(503);
    expect(res?.headers.get("retry-after")).toBe("7");
    expect(res?.headers.get("x-middleware-rewrite")).toBe("https://app.test/publik/overbelastad");
    expect(res?.headers.get("x-robots-tag")).toBe("noindex");
  });

  it.each([500, 502, 503, 504, 302, 401])("answers an upstream %i with a 503, never a 404 or a 410", async (status) => {
    fetchMock.mockResolvedValue(answer(status, { Location: "/somewhere" }));
    const res = await route(request(`/jobb/${ID}`));

    expect(res?.status).toBe(503);
    expect(res?.headers.get("retry-after")).toBe("30");
  });

  it("answers a network error and a timeout with a 503", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect((await route(request(`/jobb/${ID}`)))?.status).toBe(503);

    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect((await route(request(`/jobb/${ID}`)))?.status).toBe(503);
  });

  it("serves HEAD like GET", async () => {
    fetchMock.mockResolvedValue(answer(410));
    const res = await route(request(`/jobb/${ID}`, { method: "HEAD" }));
    expect(res?.status).toBe(410);
  });

  it("redirects an upper-case GUID to its one lower-case URL, query kept, without asking the API", async () => {
    const res = await route(request(`/jobb/${ID.toUpperCase()}?vidare=1`));

    expect(res?.status).toBe(308);
    expect(res?.headers.get("location")).toBe(`https://app.test/jobb/${ID}?vidare=1`);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("is served at /jobb/<id>?vidare=1 as at the clean URL, and the query never reaches the internal page", async () => {
    fetchMock.mockResolvedValue(answer(200));
    const res = await route(request(`/jobb/${ID}?vidare=1`));
    expect(res?.headers.get("x-middleware-rewrite")).toBe(`https://app.test/publik/jobb/${ID}`);
  });

  it("carries exactly the request headers it was given, so a nonce and its policy reach the page", async () => {
    fetchMock.mockResolvedValue(answer(200));
    const res = await route(request(`/jobb/${ID}`));

    const overridden = res?.headers.get("x-middleware-override-headers") ?? "";
    expect(overridden).toContain("x-nonce");
    expect(overridden).toContain("content-security-policy");
    expect(res?.headers.get("x-middleware-request-x-nonce")).toBe("NONCE-1");
    expect(res?.headers.get("x-middleware-request-content-security-policy")).toBe("script-src 'nonce-NONCE-1'");
  });

  it("never calls next(): the response is always a rewrite, a redirect, never a pass-through", async () => {
    for (const status of [200, 410, 404, 429, 500]) {
      fetchMock.mockResolvedValueOnce(answer(status));
      const res = await route(request(`/jobb/${ID}`, { headers: { "next-router-prefetch": "1", "next-url": "/jobb" } }));
      expect(res?.headers.get("x-middleware-rewrite")).not.toBeNull();
      expect(res?.headers.get("x-middleware-next")).toBeNull();
    }
  });
});

describe("publicJobAdRoute — what the probe sends to the API", () => {
  it("relays the client address and nothing the visitor sent: no cookie, no Authorization, no other header", async () => {
    fetchMock.mockResolvedValue(answer(200));
    await route(
      request(`/jobb/${ID}`, {
        headers: {
          "x-forwarded-for": "203.0.113.9",
          "x-forwarded-proto": "https",
          "x-forwarded-host": "evil.test",
          authorization: "Bearer stolen",
          cookie: "other=1",
          "user-agent": "Googlebot",
          "next-url": "/jobb",
        },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(`http://backend.test/api/v1/public/job-ads/${ID}`);
    expect(init?.headers).toEqual({ "x-forwarded-for": "203.0.113.9", "x-forwarded-proto": "https" });
    expect(init?.method).toBe("GET");
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.signal).toBeInstanceOf(AbortSignal);
  });

  it("sends no headers at all when the request carried no forwarding header", async () => {
    fetchMock.mockResolvedValue(answer(200));
    await route(request(`/jobb/${ID}`));
    expect(fetchMock.mock.calls[0]?.[1]?.headers).toEqual({});
  });

  it("discards the response body unread", async () => {
    const cancel = vi.fn();
    const res = new Response("secret", { status: 200 });
    Object.defineProperty(res, "body", { value: { cancel } });
    fetchMock.mockResolvedValue(res);

    await route(request(`/jobb/${ID}`));
    expect(cancel).toHaveBeenCalledTimes(1);
  });
});

describe("publicJobAdRoute — everything that is not the anonymous detail", () => {
  it("leaves a request with a session cookie alone, and never asks the API", async () => {
    const res = await route(request(`/jobb/${ID}`, { cookie: true }), true);
    expect(res).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["POST", `/jobb/${ID}`],
    ["PUT", `/jobb/${ID}`],
    ["DELETE", `/jobb/${ID}`],
    ["GET", "/jobb"],
    ["GET", "/jobb/not-a-guid"],
    ["GET", `/jobb/${ID}/extra`],
    ["GET", `/jobb/${ID}/`],
    ["GET", "/oversikt"],
    ["GET", "/cv-granskning"],
    ["GET", "/"],
  ])("leaves %s %s to today's route", async (method, path) => {
    const res = await route(request(path, { method }));
    expect(res).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("publicJobAdRoute — the internal prefix is not a URL", () => {
  it.each([
    "/publik/jobb/" + ID,
    "/publik/borttagen",
    "/publik/overbelastad",
    "/publik",
    "/%70ublik/jobb/" + ID,
    "/%2570ublik/borttagen",
  ])("answers a direct %s with a 404, signed in or not, and without asking the API", async (path) => {
    for (const hasSession of [false, true]) {
      const res = await route(request(path, { cookie: hasSession }), hasSession);
      expect(res?.headers.get("x-middleware-rewrite")).toBe("https://app.test/publik/saknas");
      expect(res?.status).toBe(200);
    }
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers a direct POST to the internal prefix with a 404 too", async () => {
    const res = await route(request(`/publik/jobb/${ID}`, { method: "POST" }));
    expect(res?.headers.get("x-middleware-rewrite")).toBe("https://app.test/publik/saknas");
  });
});
