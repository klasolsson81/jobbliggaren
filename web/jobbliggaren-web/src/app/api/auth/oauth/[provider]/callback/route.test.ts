import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { decodeLoginFlow, type LoginFlow } from "@/lib/auth/login-flow";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

vi.mock("next-intl/server", async () => {
  const { createTranslator } = await import("next-intl");
  const sv = (await import("../../../../../../../messages/sv")).default;
  return {
    getLocale: async () => "sv",
    getTranslations: async ({ namespace }: { locale: string; namespace: "pages" | "metadata" }) =>
      createTranslator({ locale: "sv", messages: sv, namespace }),
  };
});

import { GET } from "./route";

const STATE = "k3Qm9xZ0aB1cD2eF3gH4iJ5kL6mN7oP8qR9sT0uV1wX";
const CODE = "4/0AVGzR1scripted-code";
const GRANT = "grant-token-that-must-stay-in-a-strict-cookie";
const SESSION = "session-id-that-must-stay-in-a-strict-cookie";

function callback(
  query: Record<string, string>,
  { provider = "google", cookie = STATE }: { provider?: string; cookie?: string | null } = {}
) {
  const url = new URL(`http://localhost/api/auth/oauth/${provider}/callback`);
  for (const [key, value] of Object.entries(query)) url.searchParams.set(key, value);
  const headers: Record<string, string> = { "x-forwarded-for": "203.0.113.9", "x-forwarded-proto": "https" };
  if (cookie !== null) headers.cookie = `__Host-jobbliggaren_oauth=${cookie}`;
  return GET(new NextRequest(url, { headers }), { params: Promise.resolve({ provider }) });
}

const ok = { code: CODE, state: STATE };

function backendAnswers(status: number, body: unknown) {
  const fetchMock = vi.fn<typeof fetch>(async () =>
    new Response(typeof body === "string" ? body : JSON.stringify(body), {
      status,
      headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json" },
    })
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function setCookie(response: Response, name: string): string | undefined {
  return response.headers.getSetCookie().find((c) => c.startsWith(`${name}=`));
}

function flowOf(response: Response): LoginFlow | null {
  const raw = setCookie(response, "__Host-jobbliggaren_login");
  return raw ? decodeLoginFlow(raw.split(";")[0]!.split("=").slice(1).join("=")) : null;
}

async function targetOf(response: Response): Promise<string | null> {
  const doc = new DOMParser().parseFromString(await response.text(), "text/html");
  return doc.querySelector("a[href]")?.getAttribute("href") ?? null;
}

beforeEach(() => vi.unstubAllGlobals());
afterEach(() => vi.unstubAllGlobals());

describe("the external login callback", () => {
  describe("on every branch", () => {
    it.each([
      ["signed in", () => backendAnswers(200, { outcome: "signedIn", sessionId: SESSION, next: "/cv" })],
      ["refused by the api", () => backendAnswers(410, { title: "Auth.ExternalLoginUnusable" })],
    ])("answers 200 with a document, never a redirect, and clears the state cookie in full (%s)", async (_, arrange) => {
      arrange();

      const response = await callback(ok);

      expect(response.status).toBe(200);
      expect(response.headers.get("location")).toBeNull();
      expect(response.headers.get("content-type")).toBe("text/html; charset=utf-8");
      expect(response.headers.get("cache-control")).toBe("no-store");
      expect(response.headers.get("referrer-policy")).toBe("no-referrer");
      const cleared = setCookie(response, "__Host-jobbliggaren_oauth") ?? "";
      expect(cleared).toMatch(/Max-Age=0/);
      expect(cleared).toMatch(/Path=\//);
      expect(cleared).toMatch(/HttpOnly/);
      expect(cleared).toMatch(/Secure/);
      expect(cleared).toMatch(/SameSite=lax/i);
    });

    it("never writes the code, the state, the grant or the session id into the document", async () => {
      backendAnswers(200, { outcome: "consentRequired", grantToken: GRANT, next: "/cv" });
      const consent = await (await callback(ok)).text();
      backendAnswers(200, { outcome: "signedIn", sessionId: SESSION, next: "/cv" });
      const signedIn = await (await callback(ok)).text();

      for (const html of [consent, signedIn]) {
        for (const secret of [CODE, STATE, GRANT, SESSION]) expect(html).not.toContain(secret);
      }
    });
  });

  describe("with a bound state", () => {
    it("relays the code, the state and the client's address to the api", async () => {
      const fetchMock = backendAnswers(200, { outcome: "signedIn", sessionId: SESSION });

      await callback(ok);

      const [url, init] = fetchMock.mock.calls[0]!;
      expect(url).toBe("http://test-backend/api/v1/auth/oauth/google/callback");
      expect(JSON.parse(String(init?.body))).toEqual({ code: CODE, state: STATE });
      expect((init?.headers as Record<string, string>)["x-forwarded-for"]).toBe("203.0.113.9");
    });

    it("signs in with a persistent strict session cookie on this response, clears the flow, and continues to the path", async () => {
      backendAnswers(200, { outcome: "signedIn", sessionId: SESSION, next: "/ansokningar/abc-123" });

      const response = await callback(ok);

      const session = setCookie(response, "__Host-jobbliggaren_session") ?? "";
      expect(session.startsWith(`__Host-jobbliggaren_session=${SESSION};`)).toBe(true);
      expect(session).toMatch(/Max-Age=15552000/);
      expect(session).toMatch(/SameSite=strict/i);
      expect(session).toMatch(/HttpOnly/);
      expect(session).toMatch(/Secure/);
      expect(setCookie(response, "__Host-jobbliggaren_login")).toMatch(/Max-Age=0/);
      expect(await targetOf(response)).toBe("/ansokningar/abc-123");
    });

    it("continues to the default when the echoed path is not a same-site path", async () => {
      backendAnswers(200, { outcome: "signedIn", sessionId: SESSION, next: "//evil.example/" });

      expect(await targetOf(await callback(ok))).toBe("/oversikt");
    });

    it("keeps the grant in the strict flow cookie, says the step was reached through Google, and continues to the terms", async () => {
      backendAnswers(200, { outcome: "consentRequired", grantToken: GRANT, next: "/cv" });

      const response = await callback(ok);

      expect(flowOf(response)).toEqual({ phase: "consent", grantToken: GRANT, next: "/cv", via: "google" });
      expect(setCookie(response, "__Host-jobbliggaren_login")).toMatch(/SameSite=strict/i);
      expect(setCookie(response, "__Host-jobbliggaren_session")).toBeUndefined();
      expect(await targetOf(response)).toBe("/logga-in/villkor");
    });

    it.each([
      [{ outcome: "registrationClosed" }],
      [{ outcome: "accountUnavailable" }],
      [{ outcome: "pendingDeletion", permanentDeletionDate: "2026-10-25" }],
    ])("shows an outcome on the code step, reached through Google (%o)", async (result) => {
      backendAnswers(200, result);

      const response = await callback(ok);

      expect(flowOf(response)).toEqual({ phase: "outcome", result, via: "google" });
      expect(await targetOf(response)).toBe("/logga-in/kod");
    });

    it("says Google cannot vouch for the address when the api refuses it as unverified", async () => {
      backendAnswers(400, { title: "Auth.ExternalEmailUnverified" });

      const response = await callback(ok);

      expect(flowOf(response)).toEqual({ phase: "notice", notice: "externalUnverified", provider: "google" });
      expect(await targetOf(response)).toBe("/logga-in");
    });

    it.each([
      ["a spent flow", 410, { title: "Auth.ExternalLoginUnusable" }],
      ["an unregistered provider", 404, { title: "Auth.ExternalProviderUnknown" }],
      ["another 400", 400, { title: "Auth.SomethingElse" }],
      ["a rate limit", 429, {}],
      ["an unavailable api", 503, {}],
      ["a body that is not json", 200, "<html>"],
      ["an outcome this build does not know", 200, { outcome: "somethingNew" }],
    ])("says the login was not completed on %s", async (_, status, body) => {
      backendAnswers(status, body);

      const response = await callback(ok);

      expect(response.status).toBe(200);
      expect(flowOf(response)).toEqual({ phase: "notice", notice: "externalNotCompleted", provider: "google" });
      expect(await targetOf(response)).toBe("/logga-in");
    });

    it("says the login was not completed when the api cannot be reached", async () => {
      vi.stubGlobal("fetch", vi.fn(async () => Promise.reject(new TypeError("fetch failed"))));

      const response = await callback(ok);

      expect(response.status).toBe(200);
      expect(flowOf(response)).toEqual({ phase: "notice", notice: "externalNotCompleted", provider: "google" });
    });
  });

  describe("reaches no api", () => {
    it.each([
      ["no state cookie", ok, null],
      ["no state in the query", { code: CODE }, STATE],
      ["no code in the query", { state: STATE }, STATE],
      ["another flow's state", { code: CODE, state: `${STATE.slice(0, -1)}Y` }, STATE],
      ["a state of another length", { code: CODE, state: "short" }, STATE],
      ["neither a cookie nor a state", { code: CODE }, null],
    ])("with %s", async (_, query, cookie) => {
      const fetchMock = backendAnswers(200, { outcome: "signedIn", sessionId: SESSION });

      const response = await callback(query, { cookie });

      expect(fetchMock).not.toHaveBeenCalled();
      expect(response.status).toBe(200);
      expect(flowOf(response)).toEqual({ phase: "notice", notice: "externalNotCompleted", provider: "google" });
      expect(setCookie(response, "__Host-jobbliggaren_session")).toBeUndefined();
    });

    it("when the provider answers with an error, and shows nothing of it", async () => {
      const fetchMock = backendAnswers(200, { outcome: "signedIn", sessionId: SESSION });

      const response = await callback({ error: "access_denied", error_description: "<b>nope</b>", state: STATE });
      const html = await response.text();

      expect(fetchMock).not.toHaveBeenCalled();
      expect(html).not.toContain("access_denied");
      expect(html).not.toContain("nope");
    });

    it.each(["..", "../challenge", "google/../x", "GOOGLE", "evil", "linkedin"])(
      "for the segment %j, which is not a known key",
      async (provider) => {
        const fetchMock = backendAnswers(200, { outcome: "signedIn", sessionId: SESSION });

        const response = await callback(ok, { provider });

        expect(fetchMock).not.toHaveBeenCalled();
        expect(response.status).toBe(200);
        expect(flowOf(response)).toBeNull();
        expect(await targetOf(response)).toBe("/logga-in");
      }
    );
  });
});
