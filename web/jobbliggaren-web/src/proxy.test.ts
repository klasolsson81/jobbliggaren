import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";
import {
  SESSION_COOKIE_NAME,
  REFRESH_AFTER_COOKIE_NAME,
  PERSISTENT_MAX_AGE_SECONDS,
} from "@/lib/auth/cookie-names";
import { ADMIN_RETURN_HEADER } from "@/lib/auth/admin-return";

// The refresh driver (PR2b-3b, epic #481) is security-critical: it rotates the
// session id, re-sets the cookie, and forwards the new id to the downstream render.
// These tests pin every branch the CTO/security gate cares about.

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

// isProtectedPath is exercised for real (pure string logic) — /oversikt is protected,
// /cv-granskning is a public sibling.
import { proxy } from "./proxy";

const fetchMock = vi.fn<typeof fetch>();

function makeRequest(
  path: string,
  opts?: { cookies?: Record<string, string>; headers?: Record<string, string>; method?: string }
): NextRequest {
  const headers = new Headers(opts?.headers);
  if (opts?.cookies) {
    const cookieStr = Object.entries(opts.cookies)
      .map(([k, v]) => `${k}=${v}`)
      .join("; ");
    headers.set("cookie", cookieStr);
  }
  return new NextRequest(`https://app.test${path}`, { headers, method: opts?.method ?? "GET" });
}

function refreshResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

const nowSeconds = () => Math.floor(Date.now() / 1000);

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("proxy refresh driver (PR2b-3b)", () => {
  it("rotated:true — re-sets the session cookie to the new id with a finite Max-Age, rewrites the request cookie, sets no-store, advances the throttle", async () => {
    fetchMock.mockResolvedValue(
      refreshResponse({ rotated: true, sessionId: "NEWID", expiresAt: "2026-12-31T00:00:00Z" })
    );

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID", foo: "bar" },
      })
    );

    // Refresh called once, with the OLD id as the bearer token.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://backend.test/api/v1/auth/refresh",
      expect.objectContaining({
        method: "POST",
        cache: "no-store",
        headers: { Authorization: "Bearer OLDID" },
      })
    );

    // Response Set-Cookie carries the NEW id with the persistent (finite) Max-Age.
    const sessionCookie = res.cookies.get(SESSION_COOKIE_NAME);
    expect(sessionCookie?.value).toBe("NEWID");
    expect(sessionCookie?.maxAge).toBe(PERSISTENT_MAX_AGE_SECONDS);
    expect(sessionCookie?.httpOnly).toBe(true);
    expect(sessionCookie?.secure).toBe(true);
    expect(sessionCookie?.sameSite).toBe("strict");
    expect(sessionCookie?.path).toBe("/");

    // Downstream request cookie is rewritten so the SC render reads the NEW id,
    // while other cookies survive.
    expect(res.headers.get("x-middleware-override-headers")).toContain("cookie");
    const rewritten = res.headers.get("x-middleware-request-cookie");
    expect(rewritten).toContain(`${SESSION_COOKIE_NAME}=NEWID`);
    expect(rewritten).toContain("foo=bar");
    expect(rewritten).not.toContain("OLDID");

    // A rotated Set-Cookie must never be cached.
    expect(res.headers.get("cache-control")).toBe("no-store");

    // Throttle advanced to a future epoch.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value)).toBeGreaterThan(nowSeconds());
    expect(throttle?.maxAge).toBeGreaterThan(0);
  });

  it("rotated:false — leaves the session cookie untouched, advances the throttle, sets no-store", async () => {
    fetchMock.mockResolvedValue(
      refreshResponse({ rotated: false, sessionId: null, expiresAt: "2026-12-31T00:00:00Z" })
    );

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    // No Set-Cookie for the session, no request-cookie rewrite.
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.headers.get("x-middleware-override-headers")).toBeNull();
    // Throttle advanced, response uncacheable.
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBeDefined();
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("throttle not due — no backend call, plain passthrough", async () => {
    const future = String(nowSeconds() + 999);

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: future,
        },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)).toBeUndefined();
  });

  it("prefetch (sec-purpose: prefetch) — no backend call, plain passthrough", async () => {
    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID" },
        headers: { "sec-purpose": "prefetch" },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("prefetch (purpose: prefetch) — no backend call, plain passthrough", async () => {
    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID" },
        headers: { purpose: "prefetch" },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("refresh 401 on a GET (#1893) — the server has forgotten the session: login with next, both cookies cleared, no-store", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

    const res = await proxy(
      makeRequest("/mina-sidor/notiser", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID", [REFRESH_AFTER_COOKIE_NAME]: "0" },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.status).toBe(307);
    const location = new URL(res.headers.get("location") ?? "", "https://app.test");
    expect(location.pathname).toBe("/logga-in");
    expect([...location.searchParams.entries()]).toEqual([["next", "/mina-sidor/notiser"]]);
    for (const name of [SESSION_COOKIE_NAME, REFRESH_AFTER_COOKIE_NAME]) {
      const cleared = res.cookies.get(name);
      expect(cleared?.value).toBe("");
      expect(cleared?.maxAge).toBe(0);
      // A __Host- cookie is only overwritten by a Set-Cookie that still satisfies the prefix.
      expect(cleared?.secure).toBe(true);
      expect(cleared?.path).toBe("/");
      expect(cleared?.httpOnly).toBe(true);
      expect(cleared?.sameSite).toBe("strict");
    }
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("refresh 401 on a POST — passes through: a Server Action is never answered with a redirect", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" }, method: "POST" })
    );

    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
  });

  it.each([429, 500, 503])("refresh %i — swallowed, navigation not broken, session cookie untouched, throttle backed off briefly", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    // On failure the throttle is advanced by a SHORT backoff (not the full window) so a
    // degraded backend is not re-hit on every navigation, while recovery stays prompt.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value) - nowSeconds()).toBeGreaterThan(0);
    expect(Number(throttle!.value) - nowSeconds()).toBeLessThanOrEqual(30);
    expect(throttle?.maxAge).toBeLessThanOrEqual(30);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("refresh network error — swallowed, navigation not broken, throttle backed off briefly", async () => {
    fetchMock.mockRejectedValue(new Error("network down"));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value) - nowSeconds()).toBeLessThanOrEqual(30);
  });

  it("rotated:true but sessionId missing — falls to the slide branch, session cookie untouched, throttle advanced", async () => {
    // Defensive: a malformed rotated:true without an id must NOT clear or corrupt the
    // cookie — it is treated as a plain slide.
    fetchMock.mockResolvedValue(refreshResponse({ rotated: true, sessionId: null }));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.headers.get("x-middleware-override-headers")).toBeNull();
    // Full-window throttle (a successful, non-rotating refresh), not the error backoff.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(Number(throttle!.value) - nowSeconds()).toBeGreaterThan(60);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("throttle elapsed (past epoch) — refresh is due, backend called", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false, sessionId: null }));

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: String(nowSeconds() - 5),
        },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBeDefined();
  });

  it("throttle cookie is junk (non-numeric) — treated as due, backend called", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false, sessionId: null }));

    await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: "not-a-number",
        },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("unauthenticated on a protected path — redirects to /logga-in, no backend call", async () => {
    const res = await proxy(makeRequest("/oversikt"));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.status).toBe(307);
    const location = res.headers.get("location");
    expect(location).toContain("/logga-in");
    expect(location).toContain("next=%2Foversikt");
  });

  it("non-protected path — plain passthrough, no backend call even when authenticated", async () => {
    const res = await proxy(
      makeRequest("/cv-granskning", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });
});

/**
 * ADR 0087 D8(c) — the org.nr wash at the proxy layer.
 *
 * The same rule runs in `foretag/sok/page.tsx`, but a page-level `redirect()` cannot produce a 3xx:
 * measured 2026-07-26, the `(app)` layout has already begun streaming, so Next answers 200 and
 * SERVES A DOCUMENT with a meta refresh — which loads subresources that carry the refused URL in
 * their `Referer`. Here the answer is a real redirect and no document is served at all. These tests
 * pin that it is this layer, on a genuine `NextRequest`.
 */
describe("proxy — the org.nr wash on /foretag/sok (D8(c))", () => {
  const authed = { cookies: { [SESSION_COOKIE_NAME]: "sess-1" } };

  it("redirects a ten-digit ?namn= to the washed URL, without calling the backend", async () => {
    const res = await proxy(makeRequest("/foretag/sok?namn=1010101010", authed));

    expect(res.status).toBe(307);
    const location = res.headers.get("location") ?? "";
    expect(location).toContain("/foretag/sok?avvisat=orgnr");
    expect(location).not.toContain("1010101010");
    // No document is served and no refresh round-trip is spent on a refused URL.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("washes a repeated param where the value is NOT in first position", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=&namn=1010101010", authed),
    );
    expect(res.status).toBe(307);
    expect(res.headers.get("location") ?? "").not.toContain("1010101010");
  });

  it("preserves the filter axes and drops sida when it washes", async () => {
    const res = await proxy(
      makeRequest(
        "/foretag/sok?namn=101010-1010&sni=62020&sni=62010&kommun=0180&sida=4",
        authed,
      ),
    );
    const location = res.headers.get("location") ?? "";
    // The wash re-serialises through the SAME builder as every other href, so it emits the joined
    // form — while still accepting the repeated form on the way in (this request sends
    // "sni=62020&sni=62010", which is what a link shared before 2026-07-29 carries).
    expect(location).toContain("sni=62010-62020");
    expect(location).toContain("kommun=0180");
    expect(location).toContain("avvisat=orgnr");
    expect(location).not.toContain("sida");
    expect(location).not.toContain("namn");
  });

  /**
   * #1075 — the twelve-digit century form. Assert on the BARE digit runs, both the written value and
   * the century-stripped one: `URLSearchParams` leaves digits unencoded, so an assertion against an
   * encoded form would pass vacuously and prove nothing.
   */
  it("washes the twelve-digit century form at the primary gate, no-store, value gone", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=195601257901", authed),
    );

    expect(res.status).toBe(307);
    const location = res.headers.get("location") ?? "";
    expect(location).toContain("/foretag/sok?avvisat=orgnr");
    expect(location).not.toContain("195601257901");
    expect(location).not.toContain("5601257901");
    expect(res.headers.get("cache-control")).toBe("no-store");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("washes a hyphenated century form in a repeated, non-first position", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=volvo&namn=19560125-7901", authed),
    );
    expect(res.status).toBe(307);
    const location = res.headers.get("location") ?? "";
    expect(location).not.toContain("7901");
    expect(location).not.toContain("namn");
  });

  it("washes a speculative PREFETCH too — it carries the value like any other request", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=1010101010", {
        ...authed,
        headers: { "next-router-prefetch": "1" },
      }),
    );
    expect(res.status).toBe(307);
    expect(res.headers.get("location") ?? "").not.toContain("1010101010");
  });

  /** The no-loop pin at this layer: the wash target must pass through, not redirect again. */
  it("passes the wash target through instead of redirecting again", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false }));
    const res = await proxy(makeRequest("/foretag/sok?avvisat=orgnr", authed));
    expect(res.status).not.toBe(307);
  });

  it("leaves an ordinary name search alone", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false }));
    const res = await proxy(makeRequest("/foretag/sok?namn=Volvo", authed));
    expect(res.status).not.toBe(307);
  });

  it("does not gate other routes", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false }));
    const res = await proxy(makeRequest("/jobb?namn=1010101010", authed));
    expect(res.status).not.toBe(307);
  });
});

/**
 * The ordering property, pinned in BOTH directions.
 *
 * The wash sits BELOW the auth branch, which is only safe because the login redirect builds `next`
 * from `pathname` alone and so discards the whole query string — a stronger wash than the gate's
 * own, since it drops every axis rather than just `namn`. That is a property of how `next` is
 * built, not of the gate, so it needs its own pin: preserving the query so a user returns to their
 * search after login is an ordinary, reasonable change, and it would put the value into `?next=`
 * and from there into the login page's DOM. `code-reviewer` raised exactly that mutation.
 */
describe("proxy — the wash ordering, no-store and axis marshalling", () => {
  it("drops the query string when redirecting an unauthenticated request to /logga-in", async () => {
    const res = await proxy(makeRequest("/foretag/sok?namn=1010101010&sni=62010"));

    expect(res.status).toBe(307);
    const location = res.headers.get("location") ?? "";
    expect(location).toContain("/logga-in");
    expect(location).toContain("next=%2Fforetag%2Fsok");
    // Neither raw nor percent-encoded, and no other axis either.
    expect(location).not.toContain("1010101010");
    expect(location).not.toContain("namn");
    expect(location).not.toContain("62010");
    // Not washed via the gate — the auth branch returned first, which is the point.
    expect(location).not.toContain("avvisat");
  });

  it("washes an AUTHENTICATED request instead of sending it to /logga-in", async () => {
    // The other direction, stated for what it actually pins: that a session takes the gate branch,
    // not the login branch. It does NOT kill "delete the auth branch" — an authenticated request
    // never enters that branch, so this test stays green under that mutation; the test above is the
    // one that goes red.
    const res = await proxy(
      makeRequest("/foretag/sok?namn=1010101010", {
        cookies: { [SESSION_COOKIE_NAME]: "sess-1" },
      }),
    );
    const location = res.headers.get("location") ?? "";
    expect(location).toContain("avvisat=orgnr");
    expect(location).not.toContain("logga-in");
  });

  it("marks the wash redirect no-store — a refusal must not be served from a cache", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=1010101010", {
        cookies: { [SESSION_COOKIE_NAME]: "sess-1" },
      }),
    );
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("marshals the axes identically to the page gate (empty repeated values dropped)", async () => {
    const res = await proxy(
      makeRequest("/foretag/sok?namn=1010101010&sni=&sni=62010", {
        cookies: { [SESSION_COOKIE_NAME]: "sess-1" },
      }),
    );
    const location = res.headers.get("location") ?? "";
    expect(location).toContain("sni=62010");
    // An empty repeated value must not survive as `sni=` — that is what `parseCodeAxis` drops, and
    // the page gate has always dropped it. (`/jobb` shares ONE `toStringList` since 2026-08-01,
    // but it splits on a different separator for a different id space — naming it here would
    // still point at the wrong rule.)
    expect(location).not.toMatch(/sni=(&|$)/);
  });
});

describe("proxy — the admin return path (#1979)", () => {
  const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
  const forwarded = (res: Response) => res.headers.get(`x-middleware-request-${ADMIN_RETURN_HEADER}`);

  it("writes the admin pathname into the request header, with no redirect and no backend call", async () => {
    const res = await proxy(makeRequest("/admin/anvandare"));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("location")).toBeNull();
    expect(res.headers.get("x-middleware-override-headers")).toContain(ADMIN_RETURN_HEADER);
    expect(forwarded(res)).toBe("/admin/anvandare");
  });

  it("keeps the one GUID id on /admin/feedback, lower-cased, and drops every other key", async () => {
    const res = await proxy(makeRequest(`/admin/feedback?status=ny&id=${ID.toUpperCase()}&sidnr=2`));

    expect(forwarded(res)).toBe(`/admin/feedback?id=${ID}`);
  });

  it.each([
    ["an id that is not a GUID", "/admin/feedback?id=namn.efternamn%40example.test"],
    ["two ids", `/admin/feedback?id=${ID}&id=${ID}`],
  ])("keeps only the pathname for %s", async (_label, path) => {
    expect(forwarded(await proxy(makeRequest(path)))).toBe("/admin/feedback");
  });

  it("keeps no id on another admin page", async () => {
    expect(forwarded(await proxy(makeRequest(`/admin/anvandare?id=${ID}`)))).toBe("/admin/anvandare");
  });

  it("overwrites a value the client sent", async () => {
    const res = await proxy(
      makeRequest("/admin", { headers: { [ADMIN_RETURN_HEADER]: `/admin/feedback?id=${ID}` } })
    );

    expect(forwarded(res)).toBe("/admin");
  });

  it("writes nothing on a sibling path that only begins with the same letters", async () => {
    const res = await proxy(makeRequest("/administration"));

    expect(res.headers.get("x-middleware-override-headers")).toBeNull();
  });

  it("puts only next on the login redirect, so no other key reaches the request line", async () => {
    const res = await proxy(makeRequest(`/ansokningar/${ID}?flik=anteckningar`));
    const location = new URL(res.headers.get("location") ?? "", "https://app.test");

    expect([...location.searchParams.keys()]).toEqual(["next"]);
    expect(location.searchParams.get("next")).toBe(`/ansokningar/${ID}`);
  });
});

/**
 * #2094 — a link into a gated page that another site opened. The session cookie is Strict, so a click in webmail
 * arrives without it; the proxy answers with the continuation document, whose refresh starts on this origin.
 */
describe("proxy — the Strict-cookie bounce (#2094)", () => {
  const MAIL_CLICK = {
    "sec-fetch-site": "cross-site",
    "sec-fetch-mode": "navigate",
    "sec-fetch-dest": "document",
  } as const;
  const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

  const decode = (value: string) =>
    value
      .replaceAll("&quot;", '"')
      .replaceAll("&#39;", "'")
      .replaceAll("&lt;", "<")
      .replaceAll("&gt;", ">")
      .replaceAll("&amp;", "&");

  /** The refresh's URL as a browser reads the content attribute: after `url=`, the rest of the value. */
  function refreshTarget(html: string): string {
    const content = decode(/<meta http-equiv="refresh" content="([^"]*)">/.exec(html)?.[1] ?? "");
    const afterDelay = content.slice(content.indexOf(";") + 1).trimStart();
    return afterDelay.replace(/^url=/i, "");
  }

  async function bounced(path: string, opts?: Parameters<typeof makeRequest>[1]) {
    const res = await proxy(makeRequest(path, { ...opts, headers: { ...MAIL_CLICK, ...opts?.headers } }));
    return { res, html: res.status === 200 ? await res.text() : "" };
  }

  it.each(["/matchningar", "/mina-sidor/notiser", `/jobb/${ID}`, "/foretag/bevakade/nya"])(
    "answers a mail click on %s with the continuation document, back to the same path",
    async (path) => {
      const { res, html } = await bounced(path);

      expect(fetchMock).not.toHaveBeenCalled();
      expect(res.status).toBe(200);
      expect(res.headers.get("content-type")).toBe("text/html; charset=utf-8");
      expect(res.headers.get("cache-control")).toBe("no-store");
      expect(res.headers.get("referrer-policy")).toBe("no-referrer");
      expect(res.headers.get("x-robots-tag")).toBe("noindex");
      // security-auditor M-1 (#2094 form round): another site's handle on the window must not survive the hop.
      expect(res.headers.get("cross-origin-opener-policy")).toBe("same-origin");
      expect(res.headers.get("set-cookie")).toBeNull();
      expect(refreshTarget(html)).toBe(`${path}?vidare=1`);
      expect(html).toContain(`<a href="${path}?vidare=1" rel="noreferrer">Fortsätt</a>`);
      expect(html).toContain('<html lang="sv">');
      expect(html).toContain("<h1>Jobbliggaren</h1>");
    }
  );

  it("keeps the one GUID id on /admin/feedback, lower-cased, and no other key", async () => {
    const { html } = await bounced(`/admin/feedback?status=ny&id=${ID.toUpperCase()}`);

    expect(refreshTarget(html)).toBe(`/admin/feedback?id=${ID}&vidare=1`);
  });

  it("serves no value from the request's query", async () => {
    const sok = await bounced("/foretag/sok?namn=5560000000&sni=62010");
    expect(refreshTarget(sok.html)).toBe("/foretag/sok?vidare=1");
    for (const value of ["5560000000", "namn", "62010"]) expect(sok.html).not.toContain(value);

    const jobb = await bounced("/jobb?q=utvecklare&commit=true");
    expect(refreshTarget(jobb.html)).toBe("/jobb?vidare=1");
  });

  it("answers in English when the locale cookie says so", async () => {
    const { html } = await bounced("/matchningar", { cookies: { NEXT_LOCALE: "en" } });

    expect(html).toContain('<html lang="en">');
    expect(html).toContain(">Continue</a>");
  });

  it.each(["/jobb/a'b", "/jobb/x;url=https://evil.example", '/jobb/"><script>x</script>'])(
    "keeps the refresh on this origin for the hostile path %j",
    async (path) => {
      const { res, html } = await bounced(path);

      expect(res.status).toBe(200);
      expect(new URL(refreshTarget(html), "https://app.test").origin).toBe("https://app.test");
      expect(html).not.toContain("<script>");
    }
  );

  it.each<[string, Record<string, string>]>([
    ["a same-origin navigation", { "sec-fetch-site": "same-origin" }],
    ["a same-site navigation, which the Strict cookie rides", { "sec-fetch-site": "same-site" }],
    ["an address-bar navigation", { "sec-fetch-site": "none" }],
    ["a fetch", { "sec-fetch-mode": "cors", "sec-fetch-dest": "empty" }],
    ["a frame", { "sec-fetch-dest": "iframe" }],
    ["a Speculation Rules prefetch", { "sec-purpose": "prefetch" }],
    ["a legacy prefetch", { purpose: "prefetch" }],
  ])("does not bounce %s: the login redirect with next = the pathname", async (_label, headers) => {
    const res = await proxy(makeRequest("/matchningar", { headers: { ...MAIL_CLICK, ...headers } }));

    expect(res.status).toBe(307);
    const location = new URL(res.headers.get("location") ?? "", "https://app.test");
    expect([...location.searchParams.entries()]).toEqual([["next", "/matchningar"]]);
  });

  it("does not bounce a path past the length cap", async () => {
    const res = await proxy(makeRequest(`/jobb/${"a".repeat(2050)}`, { headers: MAIL_CLICK }));

    expect(res.status).toBe(307);
  });

  it("does not bounce a client that sends no Fetch Metadata", async () => {
    const res = await proxy(makeRequest("/matchningar"));

    expect(res.status).toBe(307);
  });

  it.each(["HEAD", "POST"])("does not bounce a %s", async (method) => {
    const res = await proxy(makeRequest("/matchningar", { headers: MAIL_CLICK, method }));

    expect(res.status).not.toBe(200);
  });

  it("does not bounce a click that carries the session cookie", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false, sessionId: null }));

    const { res } = await bounced("/matchningar", { cookies: { [SESSION_COOKIE_NAME]: "sess-1" } });

    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("does not bounce a public path", async () => {
    const { res } = await bounced("/cv-granskning");

    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("never bounces its own hop twice: without the cookie it is the login redirect, the marker left behind", async () => {
    const res = await proxy(makeRequest("/matchningar?vidare=1", { headers: MAIL_CLICK }));

    expect(res.status).toBe(307);
    const location = new URL(res.headers.get("location") ?? "", "https://app.test");
    expect([...location.searchParams.entries()]).toEqual([["next", "/matchningar"]]);
  });

  it.each([`/jobb/${ID}`, `/admin/feedback?id=${ID}`])(
    "sends a hop that came back with the cookie on to the clean URL of %s, without asking the backend",
    async (path) => {
      const marked = `${path}${path.includes("?") ? "&" : "?"}vidare=1`;
      const res = await proxy(
        makeRequest(marked, {
          cookies: { [SESSION_COOKIE_NAME]: "sess-1" },
          headers: { "sec-fetch-site": "same-origin", "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" },
        })
      );

      expect(fetchMock).not.toHaveBeenCalled();
      expect(res.status).toBe(307);
      expect(res.headers.get("location")).toBe(`https://app.test${path}`);
      expect(res.headers.get("cache-control")).toBe("no-store");
    }
  );
});
