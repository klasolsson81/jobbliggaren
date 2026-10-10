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

const BACKUP = "/api/v1/admin/overview/backup";
const HOST_TIME = "2026-10-08T09:58:02Z";
const backupObserved = {
  status: "Observed", reason: null, observedAt: HOST_TIME, stale: false,
  lastSuccess: { state: "Recorded", completedAt: "2026-10-08T00:19:07Z", overdue: false },
  timer: { state: "Scheduled", nextRunAt: "2026-10-09T00:17:55Z" },
};
const backupNotObserved = { status: "NotObserved", reason: "NotSampledYet", observedAt: null, stale: null, lastSuccess: null, timer: null };

function response(data: unknown, sampledAt: string | null = OVERVIEW_TIME) {
  return new Response(JSON.stringify(data), { headers: sampledAt ? { "X-Admin-Sampled-At": sampledAt } : {} });
}
function success(_session: string, path: string) {
  return Promise.resolve(response(
    path === BACKUP ? backupObserved
      : path.includes("overview") ? accountOverviewFixture() : path.includes("audit") ? audit : jobs));
}
beforeEach(() => { vi.clearAllMocks(); session.mockResolvedValue("synthetic-session"); transport.mockImplementation(success); });
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });

describe("admin overview source reads", () => {
  it("bounds and projects independent sources before they cross the browser boundary", async () => {
    const result = await loadAdminOverview();
    expect(result.kind).toBe("ok");
    expect(transport.mock.calls.map((call) => call[1])).toEqual([
      "/api/v1/admin/overview/accounts", "/api/v1/admin/audit-log?page=1&pageSize=5", "/api/v1/admin/jobs/failed", BACKUP,
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
    expect(await pending).toMatchObject({
      kind: "ok",
      data: { accounts: { kind: "failed" }, audit: { kind: "failed" }, jobs: { kind: "failed" }, backup: { kind: "failed" } },
    });
    expect(deadlines).toEqual([10_000, 10_000, 10_000]);
  });
});

describe("admin overview Backup source (#1982)", () => {
  const backupAnswers = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === BACKUP ? Promise.resolve(response(body, stamp)) : success(_session, path);

  it("asks the host only after an admin read has succeeded, and beside the slower reads rather than after them (#2064)", async () => {
    let succeeded = 0;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === BACKUP) {
        expect(succeeded).toBeGreaterThanOrEqual(1);
        return success(_session, path);
      }
      const result = await success(_session, path);
      succeeded++;
      return result;
    });
    await loadAdminOverview();
    expect(transport.mock.calls.map((call) => call[1])).toContain(BACKUP);
  });
  it("starts the host read before a slow first-step read has finished", async () => {
    let releaseAudit: () => void = () => undefined;
    let backupAsked = false;
    transport.mockImplementation((_session: string, path: string) => {
      if (path === BACKUP) backupAsked = true;
      if (path.includes("audit")) return new Promise<Response>((resolve) => { releaseAudit = () => resolve(response(audit)); });
      return success(_session, path);
    });
    const pending = loadAdminOverview();
    await vi.waitFor(() => expect(backupAsked).toBe(true));
    releaseAudit();
    expect((await pending).kind).toBe("ok");
  });
  it.each([401, 403])("never asks the host for anything when every first-step source answers %i", async (status) => {
    transport.mockImplementation(() => Promise.resolve(new Response(null, { status })));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(BACKUP);
  });
  it("never asks the host for anything when no first-step source succeeded, and reads the card as failed", async () => {
    transport.mockImplementation(() => Promise.resolve(new Response(null, { status: 502 })));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup).toEqual({ kind: "failed" });
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(BACKUP);
  });
  it.each([401, 403])("answers %i when one first-step source does, wherever the host read stands (a role revoked in flight)", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path.includes("audit") ? new Response(null, { status }) : success(_session, path));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
  it.each([401, 403])("clears everything when the Backup source itself answers %i (a role revoked between the steps)", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === BACKUP ? new Response(null, { status }) : success(_session, path));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
  it("takes the observation's time from the host, never from the response header", async () => {
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected observed sources");
    expect(result.data.backup).toEqual({
      kind: "loaded", sampledAt: HOST_TIME, refreshFailed: false,
      data: {
        stale: false,
        lastSuccess: { state: "recorded", completedAt: "2026-10-08T00:19:07Z", overdue: false },
        timer: { state: "scheduled", nextRunAt: "2026-10-09T00:17:55Z" },
      },
    });
  });
  it("keeps an old host sample old however fresh the response is", async () => {
    transport.mockImplementation(backupAnswers({ ...backupObserved, observedAt: "2026-10-05T09:58:02Z", stale: true }, OVERVIEW_TIME));
    const result = await loadAdminOverview();
    if (result.kind !== "ok" || result.data.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    expect(result.data.backup.sampledAt).toBe("2026-10-05T09:58:02Z");
    expect(result.data.backup.data.stale).toBe(true);
  });
  it("reports a host that has not reported as awaiting: not a failure, and not unavailable", async () => {
    transport.mockImplementation(backupAnswers(backupNotObserved));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected observed sources");
    expect(result.data.backup).toEqual({ kind: "awaiting" });
  });
  it("maps the states the box is in until the backup is switched on", async () => {
    transport.mockImplementation(backupAnswers({
      ...backupObserved,
      lastSuccess: { state: "Missing", completedAt: null, overdue: null },
      timer: { state: "Inactive", nextRunAt: null },
    }));
    const result = await loadAdminOverview();
    if (result.kind !== "ok" || result.data.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    expect(result.data.backup.data).toEqual({ stale: false, lastSuccess: { state: "missing" }, timer: { state: "inactive" } });
  });
  it.each([
    ["a refused file", { status: "Failed", reason: "InvalidFormat", observedAt: null, stale: null, lastSuccess: null, timer: null }],
    ["a state outside the contract", { ...backupObserved, timer: { state: "Stopped", nextRunAt: null } }],
    ["an extra property", { ...backupObserved, path: "/run/observations/backup.json" }],
    ["a time that is not an instant", { ...backupObserved, observedAt: "yesterday" }],
    ["a run without a time", { ...backupObserved, lastSuccess: { state: "Recorded", completedAt: null, overdue: null } }],
  ])("fails only the Backup source for %s", async (_name, body) => {
    transport.mockImplementation(backupAnswers(body));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup).toEqual({ kind: "failed" });
    expect(result.data.accounts.kind).toBe("loaded");
    expect(result.data.jobs.kind).toBe("loaded");
  });
  it.each([null, "invalid"])("fails the Backup source whose response header is %s", async (stamp) => {
    transport.mockImplementation(backupAnswers(backupObserved, stamp));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup.kind).toBe("failed");
  });
  it("sends the browser the states and the times and nothing the host or the API said besides", async () => {
    const result = await loadAdminOverview();
    expect(JSON.stringify(result)).not.toMatch(/reason|NotSampledYet|startedAt|\/run\/|backup\.json|jobbliggaren/);
  });
});
