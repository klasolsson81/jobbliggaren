// The bailout assertion below needs no special environment: the relay decides on the error's
// `digest` itself rather than through next/navigation, whose implementation differs between
// the server and browser builds. That import was measured and withdrawn — see the module.
import { describe, it, expect, vi, beforeEach } from "vitest";

// `headers()` is the only thing mocked. The pure relay is exercised directly, so its
// assertions never depend on the mock behaving like a request scope.
const headersMock = vi.fn();
vi.mock("next/headers", () => ({
  headers: () => headersMock(),
}));

import { pickForwardedHeaders, forwardedHeaders } from "./forwarded-headers";

/** A minimal stand-in for `Headers` — the only shape the relay is allowed to require. */
function source(values: Record<string, string>): { get(name: string): string | null } {
  return { get: (name) => values[name.toLowerCase()] ?? null };
}

describe("pickForwardedHeaders", () => {
  it("relays a single client address verbatim", () => {
    expect(pickForwardedHeaders(source({ "x-forwarded-for": "198.51.100.10" }))).toEqual({
      "x-forwarded-for": "198.51.100.10",
    });
  });

  it("relays a multi-entry chain verbatim — it never parses, splits or rewrites", () => {
    // Caddy replaces rather than appends, so this shape should not occur in production.
    // The test exists because the alternative — taking the first or last entry ourselves —
    // would put a second, disagreeing opinion about the client IP in the stack. The API's
    // ForwardLimit decides which entry it trusts; this layer must not pre-empt it.
    const chain = "203.0.113.9, 198.51.100.10";
    expect(pickForwardedHeaders(source({ "x-forwarded-for": chain }))).toEqual({
      "x-forwarded-for": chain,
    });
  });

  it("relays the proto when present", () => {
    expect(
      pickForwardedHeaders(
        source({ "x-forwarded-for": "198.51.100.10", "x-forwarded-proto": "https" }),
      ),
    ).toEqual({ "x-forwarded-for": "198.51.100.10", "x-forwarded-proto": "https" });
  });

  it("omits entirely when nothing arrived — never synthesises a value", () => {
    expect(pickForwardedHeaders(source({}))).toEqual({});
  });

  it("omits an empty header rather than forwarding an empty string", () => {
    expect(pickForwardedHeaders(source({ "x-forwarded-for": "" }))).toEqual({});
  });

  it("relays nothing else — not host, not cookie, not authorization", () => {
    const relayed = pickForwardedHeaders(
      source({
        "x-forwarded-for": "198.51.100.10",
        "x-forwarded-host": "evil.example",
        cookie: "session=secret",
        authorization: "Bearer leaked",
      }),
    );

    expect(relayed).toEqual({ "x-forwarded-for": "198.51.100.10" });
  });
});

describe("forwardedHeaders", () => {
  beforeEach(() => {
    headersMock.mockReset();
  });

  it("relays the ambient request's headers", async () => {
    headersMock.mockResolvedValue(source({ "x-forwarded-for": "198.51.100.10" }));

    await expect(forwardedHeaders()).resolves.toEqual({ "x-forwarded-for": "198.51.100.10" });
  });

  it("returns {} outside a request scope instead of throwing", async () => {
    // What build-time and static rendering actually do: `headers()` throws there by design.
    headersMock.mockRejectedValue(new Error("`headers` was called outside a request scope"));

    await expect(forwardedHeaders()).resolves.toEqual({});
  });

  it("returns {} when next/headers is unavailable synchronously", async () => {
    headersMock.mockImplementation(() => {
      throw new Error("Dynamic server usage");
    });

    await expect(forwardedHeaders()).resolves.toEqual({});
  });

  it("rethrows Next's dynamic bailout instead of swallowing it", async () => {
    // The actor is Next's own DynamicServerError, which `headers()` throws during prerender:
    // its constructor sets `digest = "DYNAMIC_SERVER_USAGE"`, and `unstable_rethrow` decides
    // purely on that digest (hooks-server-context.js `isDynamicServerError`). The shape is
    // reproduced rather than imported because the class lives behind a deep internal path.
    //
    // Swallowing it would let a per-user response be cached statically, which is a worse
    // defect than the missing header this module exists to fix.
    const bailout = Object.assign(new Error("Dynamic server usage: headers"), {
      digest: "DYNAMIC_SERVER_USAGE",
    });
    headersMock.mockRejectedValue(bailout);

    await expect(forwardedHeaders()).rejects.toBe(bailout);
  });

  it("degrades when digest is present but not a string — the guard checks the type too", async () => {
    // Crosses the `typeof digest === "string"` clause, which the two cases above leave
    // unprobed: one carries a string digest, the other none at all. Next's own errors set
    // the digest in a constructor and it is always a string, so this pins the guard's
    // narrowness rather than a production state — an object that merely HAS a digest is not
    // framework control flow, and must not be mistaken for it.
    headersMock.mockRejectedValue(Object.assign(new Error("not a bailout"), { digest: 42 }));

    await expect(forwardedHeaders()).resolves.toEqual({});
  });

  // A test asserting that a header bag whose `get` throws propagates was REMOVED, not
  // relaxed. code-reviewer measured that `headers()` resolves to ReadonlyHeaders over
  // HeadersAdapter, whose `get` goes through a proxy trap that cannot throw — only
  // append/delete/set do. The premise was one production cannot produce (§5 Tests:), and
  // it made the pin true in the impossible case while saying nothing about the reachable
  // ones. Those are pinned above instead: a digest-bearing error propagates, an ordinary
  // one degrades to {}. The errors `headers()` really throws — E839 inside a render-phase
  // after(), E833/E838 outside a cache scope — are ordinary Errors, and degrading is the
  // intended behaviour there, not a defect.

});
