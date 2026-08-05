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

  it("does NOT swallow a failure in the relay itself", async () => {
    // The try wraps only `headers()`. A header bag that throws on `get` is a bug in this
    // layer, and it must surface rather than silently degrade to "no client IP" — that is
    // the exact failure mode #1202 was: a missing header nobody noticed.
    headersMock.mockResolvedValue({
      get() {
        throw new Error("broken header bag");
      },
    });

    await expect(forwardedHeaders()).rejects.toThrow("broken header bag");
  });
});
