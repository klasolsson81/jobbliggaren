import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { decodeLoginFlow } from "@/lib/auth/login-flow";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

import { GET } from "./route";

const STATE = "k3Qm9xZ0aB1cD2eF3gH4iJ5kL6mN7oP8qR9sT0uV1wX";
const AUTHORIZE = `https://accounts.google.com/o/oauth2/v2/auth?client_id=x&state=${STATE}&code_challenge=c&code_challenge_method=S256`;

function start(query = "", { provider = "google", headers = {} as Record<string, string> } = {}) {
  const request = new NextRequest(`http://localhost/api/auth/oauth/${provider}/start${query}`, {
    headers: { "x-forwarded-for": "203.0.113.9", ...headers },
  });
  return GET(request, { params: Promise.resolve({ provider }) });
}

function backendAnswers(status: number, body: unknown) {
  const fetchMock = vi.fn<typeof fetch>(async () =>
    new Response(typeof body === "string" ? body : JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    })
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function setCookie(response: Response, name: string): string | undefined {
  return response.headers.getSetCookie().find((c) => c.startsWith(`${name}=`));
}

function noticeOf(response: Response) {
  const raw = setCookie(response, "__Host-jobbliggaren_login");
  return raw ? decodeLoginFlow(raw.split(";")[0]!.split("=").slice(1).join("=")) : null;
}

beforeEach(() => vi.unstubAllGlobals());
afterEach(() => vi.unstubAllGlobals());

describe("the external login start", () => {
  it("sends the browser to Google and binds it to the flow with a Lax state cookie", async () => {
    backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

    const response = await start("?next=%2Fcv");

    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe(AUTHORIZE);
    expect(response.headers.get("cache-control")).toBe("no-store");
    const cookie = setCookie(response, "__Host-jobbliggaren_oauth") ?? "";
    expect(cookie.startsWith(`__Host-jobbliggaren_oauth=${STATE};`)).toBe(true);
    expect(cookie).toMatch(/Max-Age=600/);
    expect(cookie).toMatch(/Path=\//);
    expect(cookie).toMatch(/HttpOnly/);
    expect(cookie).toMatch(/Secure/);
    expect(cookie).toMatch(/SameSite=lax/i);
    expect(cookie).not.toMatch(/Domain=/i);
  });

  it("relays the guarded path and the client's address to the api", async () => {
    const fetchMock = backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

    await start("?next=%2Fansokningar%2Fabc-123");

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("http://test-backend/api/v1/auth/oauth/google/start");
    expect(JSON.parse(String(init?.body))).toEqual({ next: "/ansokningar/abc-123" });
    expect((init?.headers as Record<string, string>)["x-forwarded-for"]).toBe("203.0.113.9");
  });

  it.each([
    ["no path", "", ""],
    ["a protocol-relative path", "?next=%2F%2Fevil.example", "/oversikt"],
    ["a path with a tab", "?next=%2F%09%2Fevil.example", "/oversikt"],
  ])("never relays an unguarded path (%s)", async (_, query, relayed) => {
    const fetchMock = backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

    await start(query);

    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]?.body))).toEqual({ next: relayed });
  });

  it.each(["..", "../challenge", "google/../x", "GOOGLE", "evil", "linkedin"])(
    "sends the segment %j back to the login page without a request or a cookie",
    async (provider) => {
      const fetchMock = backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

      const response = await start("", { provider });

      expect(fetchMock).not.toHaveBeenCalled();
      expect(response.status).toBe(302);
      expect(response.headers.get("location")).toBe("/logga-in");
      expect(response.headers.getSetCookie()).toEqual([]);
    }
  );

  it.each([
    ["Next-Router-Prefetch", { "next-router-prefetch": "1" }],
    ["Sec-Purpose", { "sec-purpose": "prefetch;prerender" }],
    ["Purpose", { purpose: "prefetch" }],
    ["an image on another page", { "sec-fetch-mode": "no-cors", "sec-fetch-dest": "image" }],
    ["a frame", { "sec-fetch-mode": "navigate", "sec-fetch-dest": "iframe" }],
    ["a fetch", { "sec-fetch-mode": "cors", "sec-fetch-dest": "empty" }],
  ])("mints nothing for a request that is not a click on the row (%s)", async (_, headers) => {
    const fetchMock = backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

    const response = await start("", { headers });

    expect(fetchMock).not.toHaveBeenCalled();
    expect(response.status).toBe(204);
    expect(response.headers.getSetCookie()).toEqual([]);
  });

  it("starts for a top-level navigation that says it is one", async () => {
    const fetchMock = backendAnswers(200, { authorizeUrl: AUTHORIZE, state: STATE });

    const response = await start("", { headers: { "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" } });

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(response.status).toBe(302);
  });

  it.each([
    ["an unregistered provider", 404, { title: "Auth.ExternalProviderUnknown" }],
    ["a refused path", 400, { title: "validation" }],
    ["a rate limit", 429, {}],
    ["an unavailable api", 503, {}],
    ["a body that is not json", 200, "<html>"],
    ["an answer with another member", 200, { authorizeUrl: AUTHORIZE, state: STATE, verifier: "x" }],
    ["an authorization request to another host", 200, { authorizeUrl: AUTHORIZE.replace("accounts.google.com", "evil.example"), state: STATE }],
    ["an authorization request for another state", 200, { authorizeUrl: AUTHORIZE, state: `${STATE.slice(0, -1)}Y` }],
  ])("sends the browser back with a not-completed notice and no state cookie on %s", async (_, status, body) => {
    backendAnswers(status, body);

    const response = await start();

    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe("/logga-in");
    expect(setCookie(response, "__Host-jobbliggaren_oauth")).toBeUndefined();
    expect(noticeOf(response)).toEqual({ phase: "notice", notice: "externalNotCompleted", provider: "google" });
  });

  it("sends the browser back with a notice when the api cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Promise.reject(new TypeError("fetch failed"))));

    const response = await start();

    expect(response.headers.get("location")).toBe("/logga-in");
    expect(noticeOf(response)).toEqual({ phase: "notice", notice: "externalNotCompleted", provider: "google" });
  });
});
