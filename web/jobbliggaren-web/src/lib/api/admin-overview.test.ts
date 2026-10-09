import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { accountOverviewFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";

const { session, transport } = vi.hoisted(() => ({ session: vi.fn(), transport: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ getSessionId: session }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: transport }));
import { loadAdminOverview } from "./admin-overview";

const audit = {
  items: [{ id: "event-1", occurredAt: OVERVIEW_TIME, correlationId: "correlation", userId: "actor",
    impersonatedBy: null, eventType: "AccountSuspendedEvent", aggregateType: "JobSeeker", aggregateId: "target",
    ipAddress: "192.0.2.1", userAgent: "synthetic-agent" }],
  totalCount: 1, page: 1, pageSize: 5, totalPages: 1,
};
const jobs = { totalCount: 3, returned: 50, items: [{ jobId: "1", jobType: "MatchDigestJob", failedAt: OVERVIEW_TIME, errorCategory: "IOException" }] };
function response(data: unknown, sampledAt: string | null = OVERVIEW_TIME) {
  return new Response(JSON.stringify(data), { headers: sampledAt ? { "X-Admin-Sampled-At": sampledAt } : {} });
}
function success(_session: string, path: string) {
  return Promise.resolve(response(path.includes("overview") ? accountOverviewFixture() : path.includes("audit") ? audit : jobs));
}
beforeEach(() => { vi.clearAllMocks(); session.mockResolvedValue("synthetic-session"); transport.mockImplementation(success); });
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });

describe("admin overview source reads", () => {
  it("bounds and projects independent sources before they cross the browser boundary", async () => {
    const result = await loadAdminOverview();
    expect(result.kind).toBe("ok");
    expect(transport.mock.calls.map((call) => call[1])).toEqual([
      "/api/v1/admin/overview/accounts", "/api/v1/admin/audit-log?page=1&pageSize=5", "/api/v1/admin/jobs/failed",
    ]);
    expect(JSON.stringify(result)).not.toMatch(/ipAddress|userAgent|synthetic-agent|192.0.2.1|correlationId|userId|jobType|errorCategory/);
    if (result.kind !== "ok") throw new Error("Expected observed sources");
    expect(result.data.jobs).toEqual({ kind: "loaded", data: { totalCount: 3 }, sampledAt: OVERVIEW_TIME, refreshFailed: false });
  });
  it.each([null, "invalid"])("fails only the source whose observation header is %s", async (stamp) => {
    transport.mockImplementation((_session: string, path: string) => path.includes("audit") ? response(audit, stamp) : success(_session, path));
    const result = await loadAdminOverview();
    expect(result.kind).toBe("ok");
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.audit.kind).toBe("failed");
    expect(result.data.accounts.kind).toBe("loaded");
    expect(result.data.jobs.kind).toBe("loaded");
  });
  it("keeps real empty audit observations and known zero job counts", async () => {
    transport.mockImplementation((_session: string, path: string) => path.includes("audit")
      ? response({ ...audit, items: [], totalCount: 0, totalPages: 0 })
      : path.includes("jobs") ? response({ ...jobs, items: [], totalCount: 0 }) : success(_session, path));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected empty observation");
    expect(result.data.audit).toEqual({ kind: "empty", data: [], sampledAt: OVERVIEW_TIME, refreshFailed: false });
    expect(result.data.jobs.kind === "loaded" && result.data.jobs.data.totalCount).toBe(0);
  });
  it.each([401, 403])("answers status %i without any retained source data", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path.includes("jobs") ? new Response(null, { status }) : success(_session, path));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
  it("reads nothing without a session", async () => {
    session.mockResolvedValue(null);
    expect(await loadAdminOverview()).toEqual({ kind: "unauthorized" });
    expect(transport).not.toHaveBeenCalled();
  });
  it("gives each source a ten-second deadline and does not turn timeouts into zeros", async () => {
    vi.useFakeTimers();
    const deadlines: number[] = [];
    vi.spyOn(AbortSignal, "timeout").mockImplementation((ms) => {
      deadlines.push(ms);
      const controller = new AbortController();
      setTimeout(() => controller.abort(), ms);
      return controller.signal;
    });
    transport.mockImplementation((_session: string, _path: string, init: { signal: AbortSignal }) =>
      new Promise((_resolve, reject) => init.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true })));
    const pending = loadAdminOverview();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(await pending).toMatchObject({ kind: "ok", data: { accounts: { kind: "failed" }, audit: { kind: "failed" }, jobs: { kind: "failed" } } });
    expect(deadlines).toEqual([10_000, 10_000, 10_000]);
  });
});