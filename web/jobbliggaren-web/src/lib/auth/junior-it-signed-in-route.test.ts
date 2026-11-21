import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { REFRESH_AFTER_COOKIE_NAME, SESSION_COOKIE_NAME } from "./cookie-names";
import { juniorItSignedInRoute, type BoundSessionRefresh } from "./junior-it-signed-in-route";
import type { SessionRefresh } from "./session-refresh";

const GOTEBORG = "PVZL_BQT_XtL";
const CROSS_SITE = { "sec-fetch-site": "cross-site", "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" } as const;
const SAME_ORIGIN = { ...CROSS_SITE, "sec-fetch-site": "same-origin" } as const;

function request(path: string, opts?: { headers?: Record<string, string>; method?: string; cookie?: string }) {
  const headers = new Headers(opts?.headers);
  if (opts?.cookie) headers.set("cookie", opts.cookie);
  return new NextRequest(`https://app.test${path}`, { headers, method: opts?.method ?? "GET" });
}

/** What the proxy forwards: the request's own headers plus the nonce pair. */
function forwardedFor(req: NextRequest): Headers {
  const forwarded = new Headers(req.headers);
  forwarded.set("x-nonce", "n0nce");
  return forwarded;
}

const route = (req: NextRequest, refresh: BoundSessionRefresh | null) => juniorItSignedInRoute(req, forwardedFor(req), refresh);
const refreshing = (outcome: SessionRefresh) => vi.fn<BoundSessionRefresh>().mockResolvedValue(outcome);

const decode = (value: string) => value.replaceAll("&amp;", "&");
const refreshTarget = (html: string) =>
  decode(/<meta http-equiv="refresh" content="0;url=([^"]*)">/.exec(html)?.[1] ?? "");

describe("juniorItSignedInRoute — without the session cookie", () => {
  it("answers a cross-site navigation with the continuation document, its hop the list's validated address", async () => {
    const res = await route(request(`/junior?municipality=${GOTEBORG}&sortBy=ExpiresAtAsc&page=2`, { headers: CROSS_SITE }), null);

    expect(res?.status).toBe(200);
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("referrer-policy")).toBe("no-referrer");
    expect(res?.headers.get("x-robots-tag")).toBe("noindex");
    expect(res?.headers.get("cross-origin-opener-policy")).toBe("same-origin");
    expect(res?.headers.get("set-cookie")).toBeNull();
    expect(refreshTarget((await res?.text()) ?? "")).toBe(
      `/junior?municipality=${GOTEBORG}&sortBy=ExpiresAtAsc&page=2&vidare=1`
    );
  });

  it("serves no value the page does not read: a search, a commit flag, an employer or a click id stay behind", async () => {
    const res = await route(
      request("/junior?q=5560000000&commit=true&employer=5560000000&fbclid=IwAR0abc&sortBy=ExpiresAtAsc", { headers: CROSS_SITE }),
      null
    );
    const html = (await res?.text()) ?? "";

    expect(refreshTarget(html)).toBe("/junior?sortBy=ExpiresAtAsc&vidare=1");
    for (const value of ["5560000000", "commit", "employer", "IwAR0abc", "q="]) expect(html).not.toContain(value);
  });

  it.each([
    ["same-origin", SAME_ORIGIN],
    ["same-site", { ...CROSS_SITE, "sec-fetch-site": "same-site" }],
    ["none", { ...CROSS_SITE, "sec-fetch-site": "none" }],
  ])("sends the hop that came back %s on to the list's clean address, uncacheable and without a cookie", async (_label, headers) => {
    const res = await route(request(`/junior?vidare=1&municipality=${GOTEBORG}&fbclid=x`, { headers }), null);

    expect(res?.status).toBe(307);
    expect(res?.headers.get("location")).toBe(`https://app.test/junior?municipality=${GOTEBORG}`);
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("set-cookie")).toBeNull();
  });

  it.each<[string, Record<string, string>]>([
    ["a hop that came back cross-site (a sandboxed popup's opaque origin)", CROSS_SITE],
    ["a hop without Fetch Metadata", {}],
    ["a hop that is a prefetch", { ...SAME_ORIGIN, "sec-purpose": "prefetch" }],
    ["a hop that is a fetch", { ...SAME_ORIGIN, "sec-fetch-mode": "cors", "sec-fetch-dest": "empty" }],
  ])("serves %s where it is: never bounced twice", async (_label, headers) => {
    expect(await route(request("/junior?vidare=1", { headers }), null)).toBeNull();
  });

  it.each<[string, Record<string, string>]>([
    ["a same-origin navigation", SAME_ORIGIN],
    ["an address-bar navigation", { ...CROSS_SITE, "sec-fetch-site": "none" }],
    ["a link previewer, which sends no Fetch Metadata", { "user-agent": "LinkedInBot/1.0" }],
    ["a cross-site prefetch", { ...CROSS_SITE, "sec-purpose": "prefetch" }],
    ["a cross-site frame", { ...CROSS_SITE, "sec-fetch-dest": "iframe" }],
  ])("leaves %s to the public page", async (_label, headers) => {
    expect(await route(request("/junior", { headers }), null)).toBeNull();
  });

  it.each(["HEAD", "POST"])("leaves a %s to the public page", async (method) => {
    expect(await route(request("/junior", { headers: CROSS_SITE, method }), null)).toBeNull();
  });
});

describe("juniorItSignedInRoute — with the session cookie", () => {
  const JOBB = `https://app.test/jobb?municipality=${GOTEBORG}&junior=on&sortBy=ExpiresAtAsc`;
  const JUNIOR = `/junior?municipality=${GOTEBORG}&sortBy=ExpiresAtAsc&page=3`;

  it.each<[string, string, Record<string, string>]>([
    ["a same-origin navigation", JUNIOR, SAME_ORIGIN],
    ["an address-bar navigation or a desktop app's link", JUNIOR, { ...CROSS_SITE, "sec-fetch-site": "none" }],
    ["a client without Fetch Metadata", JUNIOR, {}],
    ["the bounce's hop that came back with the cookie", `${JUNIOR}&vidare=1`, SAME_ORIGIN],
  ])("sends %s on to /jobb with the filter, the kommun and the order, and no page", async (_label, path, headers) => {
    const refresh = refreshing({ kind: "notDue" });
    const res = await route(request(path, { headers }), refresh);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(res?.status).toBe(307);
    expect(res?.headers.get("location")).toBe(JOBB);
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("set-cookie")).toBeNull();
  });

  it("answers a HEAD as it answers the GET", async () => {
    const res = await route(request(JUNIOR, { method: "HEAD" }), refreshing({ kind: "notDue" }));

    expect(res?.status).toBe(307);
    expect(res?.headers.get("location")).toBe(JOBB);
  });

  it("puts no search, commit flag or employer on /jobb, whatever the request carried", async () => {
    const res = await route(
      request("/junior?q=sjukskoterska&commit=true&employer=5560000000&municipality=" + GOTEBORG),
      refreshing({ kind: "notDue" })
    );
    const location = new URL(res?.headers.get("location") ?? "");

    expect([...location.searchParams.keys()].sort()).toEqual(["junior", "municipality"]);
  });

  it("carries the advanced throttle when the session slid", async () => {
    const res = await route(request(JUNIOR), refreshing({ kind: "slid" }));

    expect(res?.status).toBe(307);
    expect(res?.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toMatch(/^\d+$/);
    expect(res?.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res?.headers.get("cache-control")).toBe("no-store");
  });

  it("carries a rotated id as the new cookie, and never in the redirect", async () => {
    const res = await route(request(JUNIOR), refreshing({ kind: "rotated", sessionId: "sess-rotated-9" }));

    expect(res?.status).toBe(307);
    expect(res?.cookies.get(SESSION_COOKIE_NAME)?.value).toBe("sess-rotated-9");
    expect(res?.headers.get("set-cookie")).toMatch(/HttpOnly/i);
    expect(res?.headers.get("set-cookie")).toMatch(/SameSite=strict/i);
    expect(res?.headers.get("location")).not.toContain("sess-rotated-9");
    expect(res?.headers.get("cache-control")).toBe("no-store");
  });

  it("still redirects when the refresh failed, which says nothing about the session, with the short backoff", async () => {
    const res = await route(request(JUNIOR), refreshing({ kind: "failed" }));

    expect(res?.status).toBe(307);
    expect(res?.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toMatch(/^\d+$/);
    expect(res?.headers.get("set-cookie")).toMatch(/Max-Age=30/);
    expect(res?.headers.get("cache-control")).toBe("no-store");
  });

  it("on an exact 401 clears both cookies and serves the public page, whose render never sees the dead id", async () => {
    const req = request(JUNIOR, { headers: SAME_ORIGIN, cookie: `${SESSION_COOKIE_NAME}=dead-1; NEXT_LOCALE=sv` });
    const res = await route(req, refreshing({ kind: "unknownSession" }));

    expect(res?.headers.get("x-middleware-next")).toBe("1");
    expect(res?.headers.get("location")).toBeNull();
    expect(res?.cookies.get(SESSION_COOKIE_NAME)?.value).toBe("");
    expect(res?.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBe("");
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("x-middleware-request-cookie")).toBe("NEXT_LOCALE=sv");
    expect(res?.headers.get("x-middleware-request-x-nonce")).toBe("n0nce");
  });

  it("on an exact 401 for the bounce's hop strips the marker as for a visitor without a cookie, and clears both", async () => {
    const res = await route(request(`${JUNIOR}&vidare=1`, { headers: SAME_ORIGIN }), refreshing({ kind: "unknownSession" }));

    expect(res?.status).toBe(307);
    expect(res?.headers.get("location")).toBe(`https://app.test${JUNIOR}`);
    expect(res?.cookies.get(SESSION_COOKIE_NAME)?.value).toBe("");
    expect(res?.headers.get("cache-control")).toBe("no-store");
  });

  it("leaves a POST to the page and never runs the driver for it", async () => {
    const refresh = refreshing({ kind: "notDue" });

    expect(await route(request("/junior", { method: "POST", headers: { "next-action": "4000deadbeef" } }), refresh)).toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("juniorItSignedInRoute — what it leaves alone", () => {
  it.each([
    "/junior?municipality=a&municipality=b",
    "/junior?page=0",
    "/junior?sortBy=PublishedAtAsc",
    "/junior/x",
    "/juniorjobb",
    "/admin/juniorjobb",
    "/jobb",
  ])("ignores %s, with or without the cookie", async (path) => {
    const refresh = refreshing({ kind: "notDue" });

    expect(await route(request(path, { headers: CROSS_SITE }), null)).toBeNull();
    expect(await route(request(path), refresh)).toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("reads no cookie of its own: the proxy binds the driver to the cookie, and the driver writes it", () => {
    const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "junior-it-signed-in-route.ts"), "utf8");

    expect(source).not.toMatch(/\.cookies\b/);
    expect(source).not.toMatch(/get\(\s*["']cookie["']\s*\)/i);
  });
});
