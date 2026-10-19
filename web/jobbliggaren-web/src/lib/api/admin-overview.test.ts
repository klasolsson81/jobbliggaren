import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  accountOverviewFixture, emailAllowanceFixture, emailDeliveryFixture, hostFixture, OVERVIEW_TIME, servicesFixture,
} from "@/test/fixtures/admin-overview";

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
const HOST = "/api/v1/admin/overview/host";
const SERVICES = "/api/v1/admin/overview/services";
const EMAIL = "/api/v1/admin/email/allowance";
const DELIVERY = "/api/v1/admin/email/delivery?period=h24";
// The wire form of System.Text.Json for a DateTimeOffset in UTC: an offset, never a Z.
const HOST_TIME = "2026-10-08T09:58:02+00:00";
const backupObserved = {
  status: "Observed", reason: null, observedAt: HOST_TIME, stale: false,
  lastSuccess: { state: "Recorded", completedAt: "2026-10-08T00:19:07+00:00", overdue: false },
  timer: { state: "Scheduled", nextRunAt: "2026-10-09T00:17:55+00:00" },
};
const backupNotObserved = { status: "NotObserved", reason: "NotSampledYet", observedAt: null, stale: null, lastSuccess: null, timer: null };

function response(data: unknown, sampledAt: string | null = OVERVIEW_TIME) {
  return new Response(JSON.stringify(data), { headers: sampledAt ? { "X-Admin-Sampled-At": sampledAt } : {} });
}
function success(_session: string, path: string) {
  return Promise.resolve(response(
    path === BACKUP ? backupObserved
      : path === HOST ? hostFixture()
      : path === SERVICES ? servicesFixture()
      : path === EMAIL ? emailAllowanceFixture()
      : path === DELIVERY ? emailDeliveryFixture(2)
      : path.includes("overview") ? accountOverviewFixture() : path.includes("audit") ? audit : jobs));
}
beforeEach(() => { vi.clearAllMocks(); session.mockResolvedValue("synthetic-session"); transport.mockImplementation(success); });
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });

describe("admin overview source reads", () => {
  it("bounds and projects independent sources before they cross the browser boundary", async () => {
    const result = await loadAdminOverview();
    expect(result.kind).toBe("ok");
    expect(transport.mock.calls.map((call) => call[1])).toEqual([
      "/api/v1/admin/overview/accounts", "/api/v1/admin/audit-log?page=1&pageSize=5", "/api/v1/admin/jobs/failed", BACKUP, HOST, SERVICES,
      EMAIL, DELIVERY,
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
      data: {
        accounts: { kind: "failed" }, audit: { kind: "failed" }, jobs: { kind: "failed" }, backup: { kind: "failed" }, host: { kind: "failed" },
        services: { kind: "failed" }, email: { kind: "failed" },
      },
    });
    expect(deadlines).toEqual([10_000, 10_000, 10_000]);
  });
});

describe("admin overview Backup source (#1982)", () => {
  const backupAnswers = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === BACKUP ? Promise.resolve(response(body, stamp)) : success(_session, path);

  it("asks the host only after an admin read has succeeded (#2064)", async () => {
    const asked = { succeededBefore: -1 };
    let succeeded = 0;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === BACKUP) {
        asked.succeededBefore = succeeded;
        return success(_session, path);
      }
      const result = await success(_session, path);
      succeeded++;
      return result;
    });
    await loadAdminOverview();
    expect(asked.succeededBefore).toBeGreaterThanOrEqual(1);
  });
  it.each(["overview/accounts", "audit-log", "jobs/failed"])("asks the host when only %s of the three succeeds", async (only) => {
    transport.mockImplementation((_session: string, path: string) =>
      path === BACKUP || path.includes(only) ? success(_session, path) : Promise.resolve(new Response(null, { status: 502 })));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup.kind).toBe("loaded");
  });
  it("is not decided by the first read to settle: two failures, then a late success, and the host is still asked", async () => {
    let releaseAudit: () => void = () => undefined;
    transport.mockImplementation((_session: string, path: string) => {
      if (path === BACKUP) return success(_session, path);
      if (path.includes("audit")) return new Promise<Response>((resolve) => { releaseAudit = () => resolve(response(audit)); });
      return Promise.resolve(new Response(null, { status: 502 }));
    });
    const pending = loadAdminOverview();
    await new Promise((resolve) => setTimeout(resolve, 0));
    releaseAudit();
    const result = await pending;
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup.kind).toBe("loaded");
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
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(HOST);
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(SERVICES);
  });
  it("never asks the host for anything when no first-step source succeeded, and reads the card as failed", async () => {
    transport.mockImplementation(() => Promise.resolve(new Response(null, { status: 502 })));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.backup).toEqual({ kind: "failed" });
    expect(result.data.host).toEqual({ kind: "failed" });
    expect(result.data.services).toEqual({ kind: "failed" });
    expect(result.data.email).toEqual({ kind: "failed" });
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(BACKUP);
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(HOST);
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(SERVICES);
    expect(transport.mock.calls.map((call) => call[1])).not.toContain(EMAIL);
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
        lastSuccess: { state: "recorded", completedAt: "2026-10-08T00:19:07+00:00", overdue: false },
        timer: { state: "scheduled", nextRunAt: "2026-10-09T00:17:55+00:00" },
      },
    });
  });
  it("keeps an old host sample old however fresh the response is", async () => {
    transport.mockImplementation(backupAnswers({ ...backupObserved, observedAt: "2026-10-05T09:58:02+00:00", stale: true }, OVERVIEW_TIME));
    const result = await loadAdminOverview();
    if (result.kind !== "ok" || result.data.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    expect(result.data.backup.sampledAt).toBe("2026-10-05T09:58:02+00:00");
    expect(result.data.backup.data).not.toHaveProperty("stale");
  });
  it("reports a host that has not reported as awaiting: not a failure, and not unavailable", async () => {
    transport.mockImplementation(backupAnswers(backupNotObserved));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected observed sources");
    expect(result.data.backup).toEqual({ kind: "awaiting" });
  });
  it.each([
    ["Missing", "missing"],
    ["Unreadable", "unreadable"],
    ["Invalid", "invalid"],
  ])("maps the stamp state %s", async (token, state) => {
    transport.mockImplementation(backupAnswers({ ...backupObserved, lastSuccess: { state: token, completedAt: null, overdue: null } }));
    const result = await loadAdminOverview();
    if (result.kind !== "ok" || result.data.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    expect(result.data.backup.data.lastSuccess).toEqual({ state });
  });
  it.each([
    ["Inactive", "inactive"],
    ["NotInstalled", "notInstalled"],
    ["Unknown", "unknown"],
  ])("maps the timer state %s", async (token, state) => {
    transport.mockImplementation(backupAnswers({ ...backupObserved, timer: { state: token, nextRunAt: null } }));
    const result = await loadAdminOverview();
    if (result.kind !== "ok" || result.data.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    expect(result.data.backup.data.timer).toEqual({ state });
  });
  it.each([
    ["a refused file", { status: "Failed", reason: "InvalidFormat", observedAt: null, stale: null, lastSuccess: null, timer: null }],
    ["a state outside the contract", { ...backupObserved, timer: { state: "Stopped", nextRunAt: null } }],
    ["an extra property", { ...backupObserved, path: "/run/observations/backup.json" }],
    ["a time that is not an instant", { ...backupObserved, observedAt: "yesterday" }],
    ["a contract violation: a recorded run without its time", { ...backupObserved, lastSuccess: { state: "Recorded", completedAt: null, overdue: null } }],
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
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    // The Backup projection only: the Server source legitimately carries `staleAfterSeconds`.
    expect(JSON.stringify(result.data.backup)).not.toMatch(/reason|stale|NotSampledYet|startedAt|\/run\/|backup\.json|jobbliggaren/);
  });
});

describe("admin overview Server source (#1982)", () => {
  const hostAnswers = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === HOST ? Promise.resolve(response(body, stamp)) : success(_session, path);
  const observedHost = async () => {
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    return result.data.host;
  };

  it("asks the host only after an admin read has succeeded, beside the slower reads (#2064)", async () => {
    let succeeded = 0;
    let hostAsked = false;
    let releaseAudit: () => void = () => undefined;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === HOST) {
        expect(succeeded).toBeGreaterThanOrEqual(1);
        hostAsked = true;
        return success(_session, path);
      }
      if (path.includes("audit")) return new Promise<Response>((resolve) => { releaseAudit = () => resolve(response(audit)); });
      const result = await success(_session, path);
      succeeded++;
      return result;
    });
    const pending = loadAdminOverview();
    await vi.waitFor(() => expect(hostAsked).toBe(true));
    releaseAudit();
    expect((await pending).kind).toBe("ok");
  });
  it("dates the observation by the host's own newest sample, not by the time of the read", async () => {
    const old = "2026-10-08T09:40:00Z";
    transport.mockImplementation(hostAnswers({
      ...hostFixture(),
      cpu: { ...hostFixture().cpu, sampledAt: old },
      memory: { ...hostFixture().memory, sampledAt: "2026-10-08T09:41:00Z" },
      disk: { ...hostFixture().disk, sampledAt: "2026-10-08T09:39:00Z" },
    }, OVERVIEW_TIME));

    const host = await observedHost();

    expect(host).toMatchObject({ kind: "loaded", sampledAt: "2026-10-08T09:41:00Z", refreshFailed: false });
  });
  it("dates it by the API read instant when no reading has a value, since there is no sample to age", async () => {
    const none = { sampledAt: null, value: null };
    transport.mockImplementation(hostAnswers({
      ...hostFixture(),
      cpu: { state: "Collecting", ...none }, memory: { state: "NotObservable", ...none }, disk: { state: "Failed", ...none },
    }));

    expect(await observedHost()).toMatchObject({ kind: "loaded", sampledAt: OVERVIEW_TIME });
  });
  it("keeps a failing or unreadable reading as a state of the loaded source and not as a failed source", async () => {
    transport.mockImplementation(hostAnswers({
      ...hostFixture(), cpu: { state: "Collecting", sampledAt: null, value: null },
    }));

    const host = await observedHost();

    if (host.kind !== "loaded") throw new Error("Expected a loaded host");
    expect(host.data.cpu.state).toBe("Collecting");
    expect(host.data.memory.state).toBe("Available");
  });
  it("fails only the host when it answers with a reading that carries a value in a state that has none", async () => {
    transport.mockImplementation(hostAnswers({
      ...hostFixture(), disk: { state: "Failed", sampledAt: OVERVIEW_TIME, value: { percent: 1, freeBytes: 1, totalBytes: 2 } },
    }));

    const result = await loadAdminOverview();

    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.host).toEqual({ kind: "failed" });
    expect(result.data.accounts.kind).toBe("loaded");
    expect(result.data.backup.kind).toBe("loaded");
  });
  it.each([500, 502, 503])("reads the card as failed, and nothing else, when the host answers %i", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === HOST ? new Response(null, { status }) : success(_session, path));

    const result = await loadAdminOverview();

    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.host).toEqual({ kind: "failed" });
    expect(result.data.jobs.kind).toBe("loaded");
  });
  it.each([null, "invalid"])("fails the host when its observation header is %s", async (stamp) => {
    transport.mockImplementation(hostAnswers(hostFixture(), stamp));

    expect((await observedHost()).kind).toBe("failed");
  });
  it.each([401, 403])("answers %i when the host does, wherever the other reads stand", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === HOST ? new Response(null, { status }) : success(_session, path));

    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
  it("lets only the schema's fields cross to the browser", async () => {
    transport.mockImplementation(hostAnswers({
      ...hostFixture(), mount: "/", device: "vda4", cpu: { ...hostFixture().cpu, reason: "ReadFailed" },
    }));

    const host = await observedHost();

    expect(JSON.stringify(host)).not.toMatch(/mount|vda4|reason|ReadFailed|"\/"/);
    if (host.kind !== "loaded") throw new Error("Expected a loaded host");
    expect(Object.keys(host.data).sort()).toEqual(["cpu", "disk", "memory", "staleAfterSeconds"]);
  });
});

describe("admin overview Services source (#1982)", () => {
  const servicesAnswer = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === SERVICES ? Promise.resolve(response(body, stamp)) : success(_session, path);
  const loaded = async () => {
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    return result.data;
  };

  it("asks for the services only after an admin read has succeeded, beside the slower reads (#2064)", async () => {
    let succeeded = 0;
    let servicesAsked = false;
    let releaseAudit: () => void = () => undefined;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === SERVICES) {
        expect(succeeded).toBeGreaterThanOrEqual(1);
        servicesAsked = true;
        return success(_session, path);
      }
      if (path.includes("audit")) return new Promise<Response>((resolve) => { releaseAudit = () => resolve(response(audit)); });
      const result = await success(_session, path);
      succeeded++;
      return result;
    });
    const pending = loadAdminOverview();
    await vi.waitFor(() => expect(servicesAsked).toBe(true));
    releaseAudit();
    expect((await pending).kind).toBe("ok");
  });
  it("dates the observation by the newest row's own sample, not by the time of the read", async () => {
    const fixture = servicesFixture();
    transport.mockImplementation(servicesAnswer({
      ...fixture,
      backgroundJobs: { ...fixture.backgroundJobs, sampledAt: "2026-10-08T09:40:00Z" },
      volatileStore: { ...fixture.volatileStore, sampledAt: "2026-10-08T09:41:00Z" },
      jobImport: { ...fixture.jobImport, sampledAt: "2026-10-08T09:39:00Z" },
    }));

    expect((await loaded()).services).toMatchObject({ kind: "loaded", sampledAt: "2026-10-08T09:41:00Z", refreshFailed: false });
  });
  it("dates it by the API read instant when every row is unknown, since there is no sample to age", async () => {
    transport.mockImplementation(servicesAnswer({
      ...servicesFixture(),
      backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
      volatileStore: { state: "Unknown", sampledAt: null },
      jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
    }));

    expect((await loaded()).services).toMatchObject({ kind: "loaded", sampledAt: OVERVIEW_TIME });
  });
  it("keeps an undetermined row as a state of the loaded source and not as a failed source", async () => {
    transport.mockImplementation(servicesAnswer({ ...servicesFixture(), volatileStore: { state: "Unknown", sampledAt: null } }));

    const services = (await loaded()).services;

    if (services.kind !== "loaded") throw new Error("Expected loaded services");
    expect(services.data.volatileStore.state).toBe("Unknown");
    expect(services.data.backgroundJobs.state).toBe("Alive");
  });
  it.each([
    ["a row whose fields break its state", { ...servicesFixture(), backgroundJobs: { state: "NoServer", sampledAt: OVERVIEW_TIME, heartbeatAt: OVERVIEW_TIME } }],
    ["a state outside the contract", { ...servicesFixture(), volatileStore: { state: "Healthy", sampledAt: OVERVIEW_TIME } }],
    ["an extra property", { ...servicesFixture(), serverName: "worker-1" }],
    ["a limit that is not a whole second", { ...servicesFixture(), staleAfterSeconds: 0.5 }],
  ])("fails only the Services source for %s", async (_name, body) => {
    transport.mockImplementation(servicesAnswer(body));

    const data = await loaded();

    expect(data.services).toEqual({ kind: "failed" });
    expect(data.accounts.kind).toBe("loaded");
    expect(data.backup.kind).toBe("loaded");
    expect(data.host.kind).toBe("loaded");
  });
  it.each([500, 502, 503])("reads the card as failed, and nothing else, when the services read answers %i", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === SERVICES ? new Response(null, { status }) : success(_session, path));

    const data = await loaded();

    expect(data.services).toEqual({ kind: "failed" });
    expect(data.jobs.kind).toBe("loaded");
    expect(data.host.kind).toBe("loaded");
  });
  it.each([null, "invalid"])("fails the Services source when its observation header is %s", async (stamp) => {
    transport.mockImplementation(servicesAnswer(servicesFixture(), stamp));

    expect((await loaded()).services.kind).toBe("failed");
  });
  it.each([401, 403])("answers %i when the services read does, wherever the other reads stand", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === SERVICES ? new Response(null, { status }) : success(_session, path));

    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
  it("lets the states, instants, error count and limits cross to the browser and nothing else", async () => {
    const services = (await loaded()).services;

    if (services.kind !== "loaded") throw new Error("Expected loaded services");
    expect(Object.keys(services.data).sort()).toEqual([
      "backgroundJobs", "importOverdueAfterSeconds", "jobImport", "staleAfterSeconds", "volatileStore", "workerAliveWithinSeconds",
    ]);
    expect(services.data).toEqual(servicesFixture());
  });
});

describe("admin overview e-post source (#1981 PR 1)", () => {
  const emailAnswer = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === EMAIL ? Promise.resolve(response(body, stamp)) : success(_session, path);
  const observedEmail = async () => {
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    return result.data.email;
  };
  const failedUsage = { status: "Failed", reason: "Timeout", observedAt: null, month: null, sent: null, billable: null,
    estimatedExtraCostOre: null, aboveHalfOfSendLimit: null, webhooksCount: null };

  it("asks for the allowance only after an admin read has succeeded (#2064)", async () => {
    let succeeded = 0;
    let emailAsked = false;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === EMAIL) {
        expect(succeeded).toBeGreaterThanOrEqual(1);
        emailAsked = true;
        return success(_session, path);
      }
      const result = await success(_session, path);
      succeeded++;
      return result;
    });
    await loadAdminOverview();
    expect(emailAsked).toBe(true);
  });
  it("dates an observed count by when the provider answered, never by the time of the read", async () => {
    expect(await observedEmail()).toEqual({
      kind: "loaded", data: emailAllowanceFixture(), sampledAt: "2026-10-08T09:59:30+00:00", refreshFailed: false,
    });
  });
  it("dates a provider failure by the API read instant: a loaded source whose usage failed, not a failed source", async () => {
    transport.mockImplementation(emailAnswer({ ...emailAllowanceFixture(), usage: failedUsage }));
    expect(await observedEmail()).toMatchObject({ kind: "loaded", sampledAt: OVERVIEW_TIME, data: { usage: { status: "Failed" } } });
  });
  it.each([
    ["an extra property", { ...emailAllowanceFixture(), projectId: "p" }],
    ["a cost without the plan in force", { ...emailAllowanceFixture(), plan: { status: "Observed", reason: null, active: "Scale", matches: false } }],
    ["NotConfigured given as a failure", { ...emailAllowanceFixture(), usage: { ...failedUsage, reason: "NotConfigured" } }],
  ])("fails only the e-post source for %s", async (_name, body) => {
    transport.mockImplementation(emailAnswer(body));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.email).toEqual({ kind: "failed" });
    expect(result.data.accounts.kind).toBe("loaded");
  });
  it.each([null, "invalid"])("fails the e-post source whose response header is %s", async (stamp) => {
    transport.mockImplementation(emailAnswer(emailAllowanceFixture(), stamp));
    expect((await observedEmail()).kind).toBe("failed");
  });
  it.each([401, 403])("answers %i when the allowance read does, wherever the other reads stand", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === EMAIL ? new Response(null, { status }) : success(_session, path));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });
});

describe("admin overview failed-sends source (#1981 PR 2)", () => {
  const deliveryAnswer = (body: unknown, stamp: string | null = OVERVIEW_TIME) =>
    (_session: string, path: string) => path === DELIVERY ? Promise.resolve(response(body, stamp)) : success(_session, path);
  const observedDelivery = async () => {
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    return result.data.delivery;
  };
  const unjudged = { period: "h24", status: "NotObservable", reason: null, observedAt: null, sent: null, failed: null };

  it("reads the provider's counts for the last 24 hours, and never the failures list", async () => {
    await loadAdminOverview();
    const paths = transport.mock.calls.map((call) => String(call[1]));
    expect(paths).toContain(DELIVERY);
    expect(paths.filter((path) => path.includes("/email/failures"))).toEqual([]);
  });

  it("asks only after an admin read has succeeded (#2064)", async () => {
    let succeeded = 0;
    transport.mockImplementation(async (_session: string, path: string) => {
      if (path === DELIVERY) expect(succeeded).toBeGreaterThanOrEqual(1);
      const result = await success(_session, path);
      if (path !== DELIVERY && path !== EMAIL) succeeded++;
      return result;
    });
    expect((await observedDelivery()).kind).toBe("loaded");
  });

  it("dates an observed count by when we asked the provider, never by the time of the read", async () => {
    expect(await observedDelivery()).toEqual({
      kind: "loaded", data: emailDeliveryFixture(2), sampledAt: "2026-10-08T09:59:30+00:00", refreshFailed: false,
    });
  });

  it("dates an unjudged read by the API read instant: a loaded source in its own state, never a zero", async () => {
    transport.mockImplementation(deliveryAnswer(unjudged));
    expect(await observedDelivery()).toEqual({ kind: "loaded", data: unjudged, sampledAt: OVERVIEW_TIME, refreshFailed: false });
  });

  it.each([
    ["another period's answer", { ...emailDeliveryFixture(2), period: "d7" }],
    ["an extra property", { ...emailDeliveryFixture(2), canceled: 0 }],
    ["a zero where nothing was observed", { ...unjudged, failed: 0 }],
  ])("fails only the failed-sends source for %s", async (_name, body) => {
    transport.mockImplementation(deliveryAnswer(body));
    const result = await loadAdminOverview();
    if (result.kind !== "ok") throw new Error("Expected partial observation");
    expect(result.data.delivery).toEqual({ kind: "failed" });
    expect(result.data.email.kind).toBe("loaded");
  });

  it.each([null, "invalid"])("fails the failed-sends source whose response header is %s", async (stamp) => {
    transport.mockImplementation(deliveryAnswer(emailDeliveryFixture(2), stamp));
    expect((await observedDelivery()).kind).toBe("failed");
  });

  it.each([401, 403])("answers %i when the delivery read does", async (status) => {
    transport.mockImplementation((_session: string, path: string) => path === DELIVERY ? new Response(null, { status }) : success(_session, path));
    expect(await loadAdminOverview()).toEqual({ kind: status === 401 ? "unauthorized" : "forbidden" });
  });

  it("lets the period, the state and the two counts cross to the browser, and nothing else", async () => {
    const delivery = await observedDelivery();
    if (delivery.kind !== "loaded") throw new Error("Expected a loaded source");
    expect(Object.keys(delivery.data).sort()).toEqual(["failed", "observedAt", "period", "reason", "sent", "status"]);
  });
});
