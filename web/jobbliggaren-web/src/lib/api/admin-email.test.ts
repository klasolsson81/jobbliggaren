import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { emailAllowanceFixture } from "@/test/fixtures/admin-overview";

const { session, transport } = vi.hoisted(() => ({ session: vi.fn(), transport: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ getSessionId: session }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: transport }));
import {
  EMAIL_ALLOWANCE_PATH,
  EMAIL_DELIVERY_PATH,
  EMAIL_FAILURES_PATH,
  loadEmailAllowance,
  loadEmailDelivery,
  loadEmailFailures,
} from "./admin-email";

const answer = (body: unknown, status = 200) =>
  Promise.resolve(new Response(body === null ? null : JSON.stringify(body), { status }));

beforeEach(() => {
  vi.clearAllMocks();
  session.mockResolvedValue("synthetic-session");
  transport.mockImplementation(() => answer(emailAllowanceFixture()));
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe("loadEmailAllowance (#1981 PR 1)", () => {
  it("reads the allowance and answers it as the schema parsed it", async () => {
    expect(await loadEmailAllowance()).toEqual({ kind: "ok", data: emailAllowanceFixture() });
    expect(transport).toHaveBeenCalledWith("synthetic-session", EMAIL_ALLOWANCE_PATH, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("needs no sampling header: when the provider answered is in the body", async () => {
    // The fixture's Response carries no X-Admin-Sampled-At at all.
    expect((await loadEmailAllowance()).kind).toBe("ok");
  });

  it("reads nothing without a session", async () => {
    session.mockResolvedValue(null);
    expect(await loadEmailAllowance()).toEqual({ kind: "unauthorized" });
    expect(transport).not.toHaveBeenCalled();
  });

  it.each([
    [401, "unauthorized"],
    [403, "forbidden"],
    [404, "error"],
    [429, "error"],
    [500, "error"],
    [503, "error"],
  ] as const)("answers status %i as %s", async (status, kind) => {
    transport.mockImplementation(() => answer(null, status));
    expect(await loadEmailAllowance()).toEqual({ kind });
  });

  it("answers a body outside the contract as an error, never as a guess", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    transport.mockImplementation(() => answer({ ...emailAllowanceFixture(), extra: true }));
    expect(await loadEmailAllowance()).toEqual({ kind: "error" });
  });

  it("answers a transport failure as an error and never throws", async () => {
    transport.mockImplementation(() => Promise.reject(new TypeError("fetch failed")));
    await expect(loadEmailAllowance()).resolves.toEqual({ kind: "error" });
  });

  it("gives the read the overview's ten-second source deadline", async () => {
    vi.useFakeTimers();
    const deadlines: number[] = [];
    vi.spyOn(AbortSignal, "timeout").mockImplementation((ms) => {
      deadlines.push(ms);
      const controller = new AbortController();
      setTimeout(() => controller.abort(), ms);
      return controller.signal;
    });
    transport.mockImplementation((_session: string, _path: string, init: { signal: AbortSignal }) =>
      new Promise((_resolve, reject) =>
        init.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true })));
    const pending = loadEmailAllowance();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(await pending).toEqual({ kind: "error" });
    expect(deadlines).toEqual([10_000]);
  });

  it("gives up when the caller's own signal aborts", async () => {
    const caller = new AbortController();
    transport.mockImplementation((_session: string, _path: string, init: { signal: AbortSignal }) =>
      new Promise((_resolve, reject) => {
        const abort = () => reject(new DOMException("Aborted", "AbortError"));
        if (init.signal.aborted) abort();
        else init.signal.addEventListener("abort", abort, { once: true });
      }));
    const pending = loadEmailAllowance(caller.signal);
    caller.abort();
    expect(await pending).toEqual({ kind: "error" });
  });
});

const DELIVERY = {
  period: "h24", status: "Observed", reason: null, observedAt: "2026-10-19T18:59:46+00:00", sent: 57, failed: 2,
} as const;
const FAILURES = {
  period: "h24", status: "Observed", reason: null, observedAt: "2026-10-19T18:59:46+00:00", limit: 10,
  failures: [{ createdAt: "2026-10-19T14:32:51+00:00", recipient: "k•••@example.com", reason: "MailboxNotFound" }],
} as const;

describe.each([
  ["loadEmailDelivery", loadEmailDelivery, EMAIL_DELIVERY_PATH, DELIVERY],
  ["loadEmailFailures", loadEmailFailures, EMAIL_FAILURES_PATH, FAILURES],
] as const)("%s (#1981 PR 2)", (_name, load, path, body) => {
  beforeEach(() => {
    transport.mockImplementation(() => answer(body));
  });

  it("asks for the period alone, and answers the body as the schema parsed it", async () => {
    expect(await load("h24")).toEqual({ kind: "ok", data: body });
    expect(transport).toHaveBeenCalledWith("synthetic-session", `${path}?period=h24`, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("answers another period's body as an error: it is not this page's answer", async () => {
    expect(await load("d7")).toEqual({ kind: "error" });
  });

  it("reads nothing without a session", async () => {
    session.mockResolvedValue(null);
    expect(await load("h24")).toEqual({ kind: "unauthorized" });
    expect(transport).not.toHaveBeenCalled();
  });

  it.each([
    [400, "error"],
    [401, "unauthorized"],
    [403, "forbidden"],
    [429, "error"],
    [500, "error"],
  ] as const)("answers status %i as %s", async (status, kind) => {
    transport.mockImplementation(() => answer(null, status));
    expect(await load("h24")).toEqual({ kind });
  });

  it("answers a body outside the contract as an error, never as a guess", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    transport.mockImplementation(() => answer({ ...body, id: "provider-id" }));
    expect(await load("h24")).toEqual({ kind: "error" });
  });

  it("answers a transport failure as an error and never throws", async () => {
    transport.mockImplementation(() => Promise.reject(new TypeError("fetch failed")));
    await expect(load("h24")).resolves.toEqual({ kind: "error" });
  });
});

describe("loadEmailFailures and the mask (#1981 PR 2)", () => {
  it("answers an address the API did not mask as an error, so the page never shows it", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    transport.mockImplementation(() => answer({ ...FAILURES, failures: [{ ...FAILURES.failures[0], recipient: "konto@example.com" }] }));
    expect(await loadEmailFailures("h24")).toEqual({ kind: "error" });
  });
});
