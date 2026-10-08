import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { createServer, request as forward, type Server } from "node:http";
import { createServer as createTlsServer, type Server as TlsServer } from "node:https";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { FEEDBACK_SCREENSHOT } from "./screenshot-fixture";
import {
  ADMIN,
  AUDIT_PAGE,
  EMAIL_CHANGE_INSTANTS,
  DELETION_TIMING,
  FAILED_JOBS,
  FEEDBACK,
  FEEDBACK_NOW,
  MEMBER,
  RECURRING_JOBS,
  STEP_UP_CHALLENGE,
  STEP_UP_GRANT,
  accountDetails,
  accountOverview,
  accountsPage,
  feedbackDetail,
  feedbackList,
  feedbackSummary,
  type AccountAccessState,
  type AccountDeletionState,
  type FeedbackAvailability,
  type FeedbackRecord,
} from "./fixtures";

/**
 * The servers around the app for the admin harness (#1973): a fixture backend the admin pages read,
 * and a front proxy that serves the app over https, because the session cookie is `__Host-`. The
 * browser only ever talks to the proxy.
 *
 * `who` decides whether `/api/v1/me` answers an administrator or an ordinary account; `mode` decides
 * what the admin endpoints answer, so a page's refused and failed states render from a real
 * response. Anything else answers 404 and is recorded in `misses`, so a page that starts reading
 * something new shows up there rather than as a silent fallback.
 */
export type HarnessPorts = { readonly proxy: number; readonly next: number; readonly backend: number };

const portBase = Number(process.env.ADMIN_HARNESS_PORT_BASE ?? "3120");
if (!Number.isInteger(portBase) || portBase < 1024 || portBase > 65533) throw new Error("Invalid ADMIN_HARNESS_PORT_BASE");
export const HARNESS_PORTS: HarnessPorts = { proxy: portBase, next: portBase + 1, backend: portBase + 2 };

export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;
export const SESSION_COOKIE = "__Host-jobbliggaren_session";
export const SESSION_ID = "admin-harness-session";

export type AdminMode = "ok" | "forbidden" | "error" | "rateLimited" | "unauthorized";

/** The four reads the feedback page makes side by side (#1979). */
export type FeedbackRead = "list" | "detail" | "summary" | "availability";
export type FeedbackScreenshotRead = "ok" | "notFound" | "error" | "loading";

const FEEDBACK_READS: ReadonlyArray<FeedbackRead> = ["list", "detail", "summary", "availability"];
export type AccessMode = "ok" | "forbidden" | "rateLimited" | "unauthorized" | "unknown" | "unknownAfterCommit";

export type Harness = {
  who: "admin" | "member";
  mode: AdminMode;
  readonly overviewReads: Record<"accounts" | "audit" | "jobs", AdminMode>;
  overviewSampledAt: string | null;
  overviewEmpty: boolean;
  overviewDelayMs: number;
  overviewLongEvent: boolean;
  accessMode: AccessMode;
  deletionMode: AccessMode | "alreadyPending" | "lastAdministrator";
  /** The injected server clock's shared AccountDeletionTiming.From result, for preview and commit. */
  deletionTiming: AccountDeletionState;
  codeMode: "ok" | "unauthorized" | "rateLimited";
  holdCodeRequests: boolean;
  holdAccessWrites: boolean;
  holdAccountDetails: boolean;
  readonly accountDetailFailuresAfterDeletion: Set<string>;
  readonly access: Map<string, AccountAccessState>;
  readonly deletions: Map<string, AccountDeletionState>;
  readonly deletionRequests: { accountId: string; body: string }[];
  readonly accessRequests: { accountId: string; operation: "suspend" | "reinstate"; body: string }[];
  readonly reauthVerifications: string[];
  releaseCodeRequests(): void;
  releaseAccessWrites(): void;
  releaseAccountDetails(): void;
  /** The account directory holds thirty more accounts, so its listing has a second page. */
  many: boolean;
  /** Accounts removed since the page was read: they no longer list, and their details answer 404. */
  readonly gone: Set<string>;
  /** Accounts with a pending address change (#1975), as the request left them and the cancel removes them. */
  readonly emailChanges: Set<string>;
  /** Every address change request's body, as the backend received it. */
  readonly emailChangeRequests: string[];
  /** Every backend path the app asked for that the fixtures do not answer. */
  readonly misses: string[];
  /** Every backend path the app asked for, answered or not. */
  readonly requests: string[];
  /** Every account search's request body, as the backend received it. */
  readonly searches: string[];
  /** #1979 — the feedback backend's submissions, as its two commands leave them. */
  readonly feedback: Map<string, FeedbackRecord>;
  /** What each feedback read answers; `mode` refuses them all at once, as it does every admin read. */
  readonly feedbackReads: Record<FeedbackRead, AdminMode>;
  /** Actual normalized PNG rows, independently of the metadata the detail read already returned. */
  readonly feedbackScreenshots: Map<string, typeof FEEDBACK_SCREENSHOT>;
  readonly feedbackScreenshotReads: Map<string, FeedbackScreenshotRead>;
  readonly feedbackScreenshotRequests: string[];
  readonly feedbackScreenshotAnswers: string[];
  releaseFeedbackScreenshots(): void;
  feedbackAvailability: FeedbackAvailability;
  /** Every feedback read's path and query string, as the backend received them. */
  readonly feedbackQueries: string[];
  /** Every status change's and requeue's body, as the backend received them. */
  readonly feedbackCommands: { readonly id: string; readonly command: "status" | "requeue"; readonly body: string }[];
  reset(): void;
  stop(): Promise<void>;
};

function listen(server: Server | TlsServer, port: number): Promise<void> {
  return new Promise((resolve) => server.listen(port, () => resolve()));
}

function close(server: Server | TlsServer): Promise<void> {
  return new Promise((resolve) => {
    server.closeAllConnections();
    server.close(() => resolve());
  });
}

function localhostCertificate(): { key: Buffer; cert: Buffer } {
  const dir = mkdtempSync(join(tmpdir(), "admin-harness-"));
  const [key, cert] = [join(dir, "key.pem"), join(dir, "cert.pem")];
  execFileSync(
    "openssl",
    ["req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1", "-subj", "/CN=localhost",
      "-addext", "subjectAltName=DNS:localhost", "-keyout", key, "-out", cert],
    { stdio: "ignore" }
  );
  return { key: readFileSync(key), cert: readFileSync(cert) };
}

export async function startHarness(ports: HarnessPorts = HARNESS_PORTS): Promise<Harness> {
  const misses: string[] = [];
  const requests: string[] = [];
  const searches: string[] = [];
  const emailChangeRequests: string[] = [];
  const accessRequests: Harness["accessRequests"] = [];
  const deletionRequests: Harness["deletionRequests"] = [];
  const reauthVerifications: string[] = [];
  const feedbackQueries: string[] = [];
  const feedbackCommands: Harness["feedbackCommands"] = [];
  const pendingCodeRequests: (() => void)[] = [];
  const pendingAccessWrites: (() => void)[] = [];
  const pendingAccountDetails: (() => void)[] = [];
  const pendingFeedbackScreenshots: (() => void)[] = [];
  let issuedGrants = 0;

  const harness: Harness = {
    who: "admin",
    mode: "ok",
    overviewReads: { accounts: "ok", audit: "ok", jobs: "ok" },
    overviewSampledAt: new Date().toISOString(),
    overviewEmpty: false,
    overviewDelayMs: 0,
    overviewLongEvent: false,
    accessMode: "ok",
    deletionMode: "ok",
    deletionTiming: DELETION_TIMING,
    codeMode: "ok",
    holdCodeRequests: false,
    holdAccessWrites: false,
    holdAccountDetails: false,
    accountDetailFailuresAfterDeletion: new Set(),
    access: new Map(),
    deletions: new Map(),
    deletionRequests,
    accessRequests,
    reauthVerifications,
    releaseCodeRequests() {
      harness.holdCodeRequests = false;
      for (const answer of pendingCodeRequests.splice(0)) answer();
    },
    releaseAccessWrites() {
      harness.holdAccessWrites = false;
      for (const answer of pendingAccessWrites.splice(0)) answer();
    },
    releaseAccountDetails() {
      harness.holdAccountDetails = false;
      for (const answer of pendingAccountDetails.splice(0)) answer();
    },
    many: false,
    gone: new Set(),
    emailChanges: new Set(),
    emailChangeRequests,
    misses,
    requests,
    searches,
    feedback: new Map(FEEDBACK.map((record) => [record.id, record])),
    feedbackReads: { list: "ok", detail: "ok", summary: "ok", availability: "ok" },
    feedbackScreenshots: new Map(),
    feedbackScreenshotReads: new Map(),
    feedbackScreenshotRequests: [],
    feedbackScreenshotAnswers: [],
    releaseFeedbackScreenshots() {
      for (const [id, mode] of harness.feedbackScreenshotReads)
        if (mode === "loading") harness.feedbackScreenshotReads.set(id, "ok");
      for (const answer of pendingFeedbackScreenshots.splice(0)) answer();
    },
    feedbackAvailability: "Open",
    feedbackQueries,
    feedbackCommands,
    reset() {
      harness.releaseCodeRequests();
      harness.releaseAccessWrites();
      harness.releaseAccountDetails();
      harness.releaseFeedbackScreenshots();
      misses.length = 0;
      requests.length = 0;
      searches.length = 0;
      emailChangeRequests.length = 0;
      accessRequests.length = 0;
      deletionRequests.length = 0;
      reauthVerifications.length = 0;
      issuedGrants = 0;
      harness.who = "admin";
      harness.mode = "ok";
      harness.overviewReads.accounts = harness.overviewReads.audit = harness.overviewReads.jobs = "ok";
      harness.overviewSampledAt = new Date().toISOString();
      harness.overviewEmpty = false;
      harness.overviewDelayMs = 0;
      harness.overviewLongEvent = false;
      harness.accessMode = "ok";
      harness.deletionMode = "ok";
      harness.deletionTiming = DELETION_TIMING;
      harness.codeMode = "ok";
      harness.many = false;
      harness.gone.clear();
      harness.emailChanges.clear();
      harness.access.clear();
      harness.deletions.clear();
      harness.accountDetailFailuresAfterDeletion.clear();
      harness.feedback.clear();
      for (const record of FEEDBACK) harness.feedback.set(record.id, record);
      harness.feedbackScreenshots.clear();
      harness.feedbackScreenshotReads.clear();
      harness.feedbackScreenshotRequests.length = 0;
      harness.feedbackScreenshotAnswers.length = 0;
      for (const read of FEEDBACK_READS) harness.feedbackReads[read] = "ok";
      harness.feedbackAvailability = "Open";
      feedbackQueries.length = 0;
      feedbackCommands.length = 0;
    },
    async stop() {
      harness.releaseAccountDetails();
      harness.releaseFeedbackScreenshots();
      await Promise.all([close(proxy), close(backend)]);
    },
  };

  const backend = createServer((request, response) => {
    const json = (status: number, value?: unknown, headers: Record<string, string> = {}) => {
      response.writeHead(status, { "Content-Type": "application/json", ...headers });
      response.end(JSON.stringify(value ?? null));
    };
    /** A status with no body, as `Results.NotFound()` and `Results.NoContent()` answer. */
    const bare = (status: number) => {
      response.writeHead(status, { "Cache-Control": "private, no-store" });
      response.end();
    };
    const url = new URL(request.url ?? "/", `http://localhost:${ports.backend}`);
    const route = `${request.method} ${url.pathname}`;
    requests.push(route);

    if (route === "GET /api/v1/me") return json(200, harness.who === "admin" ? ADMIN : MEMBER);
    if (route === "POST /api/v1/auth/refresh") return json(200, { rotated: false, sessionId: null });
    // AuthEndpoints returns an empty provider list when this host has no OAuth keys.
    if (route === "GET /api/v1/auth/oauth/providers") return json(200, []);
    // The start page an ordinary account is sent to reads the landing figures.
    if (route === "GET /api/v1/landing/stats")
      return json(200, { activeCount: 42000, newToday: 62, isStale: false, refreshedAt: "2026-10-03T19:00:00Z" });

    const adminRoutes: Record<string, () => unknown> = {
      "GET /api/v1/admin/overview/accounts": () => accountOverview({
        many: harness.many, gone: harness.overviewEmpty ? new Set(accountsPage(undefined).accounts.items.map(row => row.id)) : harness.gone,
        access: harness.access,
      }),
      "GET /api/v1/admin/audit-log": () => harness.overviewEmpty ? { ...AUDIT_PAGE, items: [], totalCount: 0 }
        : harness.overviewLongEvent ? { ...AUDIT_PAGE, items: AUDIT_PAGE.items.map(row => ({ ...row,
          eventType: "JobSeeker.FollowedCompanyNotificationConsentUpdated", aggregateType: "JobSeeker" })) } : AUDIT_PAGE,
      "GET /api/v1/admin/jobs/recurring": () => RECURRING_JOBS,
      "GET /api/v1/admin/jobs/failed": () => harness.overviewEmpty ? { ...FAILED_JOBS, items: [], totalCount: 0 } : FAILED_JOBS,
    };
    const refusal = (mode: AdminMode = harness.mode) => {
      switch (mode) {
        case "forbidden":
          return json(403, { title: "Forbidden", status: 403 });
        case "rateLimited":
          return json(429, { title: "Too Many Requests", status: 429 }, { "Retry-After": "6" });
        case "unauthorized":
          return json(401, { title: "Unauthorized", status: 401 });
        default:
          return json(500, { title: "Internal Server Error", status: 500 });
      }
    };

    if (route === "POST /api/v1/admin/accounts/search") {
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        searches.push(body);
        if (harness.mode !== "ok") return refusal();
        const { address, status, page, pageSize, registeredFrom, registeredBefore } = JSON.parse(body) as {
          address?: string; status?: string; page?: number; pageSize?: number;
          registeredFrom?: string; registeredBefore?: string;
        };
        return json(200, accountsPage(address, { status, page, pageSize, registeredFrom, registeredBefore, many: harness.many,
          gone: harness.gone, access: harness.access, deletions: harness.deletions }));
      });
      return;
    }
    // #1975 — the administrator's own step-up, then an account's address change: its request, cancel and read.
    if (route === "POST /api/v1/auth/reauth") {
      request.resume();
      const answer = () => json(202, { challengeId: STEP_UP_CHALLENGE }, { "Cache-Control": "private, no-store" });
      if (harness.holdCodeRequests) pendingCodeRequests.push(answer);
      else answer();
      return;
    }
    if (route === "POST /api/v1/auth/reauth/verify") {
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        reauthVerifications.push(body);
        if (harness.codeMode === "unauthorized") return json(401, { title: "Unauthorized", status: 401 });
        if (harness.codeMode === "rateLimited")
          return json(429, { title: "Too Many Requests", status: 429 }, { "Retry-After": "6" });
        const proof = JSON.parse(body) as { challengeId?: unknown; code?: unknown };
        if (proof.challengeId !== STEP_UP_CHALLENGE || proof.code !== "123456")
          return json(400, { title: "Auth.LoginCodeWrong", status: 400 });
        issuedGrants++;
        return json(200, { reauthGrant: STEP_UP_GRANT }, { "Cache-Control": "private, no-store" });
      });
      return;
    }

    const accessCommand = /^POST \/api\/v1\/admin\/accounts\/([0-9a-f-]{36})\/(suspend|reinstate)$/.exec(route);
    if (accessCommand !== null) {
      const accountId = accessCommand[1] ?? "";
      const operation = accessCommand[2] === "suspend" ? "suspend" : "reinstate";
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        accessRequests.push({ accountId, operation, body });
        const reply = (status: number, value?: unknown, headers: Record<string, string> = {}) =>
          json(status, value, { "Cache-Control": "private, no-store", ...headers });
        const input = JSON.parse(body) as { reauthGrant?: unknown };
        if (harness.who !== "admin") return reply(403, { title: "Forbidden", status: 403 });
        if (input.reauthGrant !== STEP_UP_GRANT || issuedGrants === 0)
          return reply(401, { title: "Auth.InvalidCredentials", status: 401 });
        issuedGrants--;
        const answer = () => {
          if (harness.accessMode === "forbidden") return reply(403, { title: "Forbidden", status: 403 });
          if (harness.accessMode === "rateLimited")
            return reply(429, { title: "Too Many Requests", status: 429 }, { "Retry-After": "6" });
          if (harness.accessMode === "unauthorized") return reply(401, { title: "Unauthorized", status: 401 });
          if (harness.accessMode === "unknown") return reply(503, { title: "Unavailable", status: 503 });
          const found = accountDetails(accountId, harness.gone, harness.access, harness.deletions);
          if (found === undefined) return reply(404, { title: "Admin.AccountNotFound", status: 404 });
          if (found.status === "ProfileMissing")
            return reply(410, { title: "Admin.ProfileUnavailable", status: 410 });
          const suspended = operation === "suspend";
          if (found.isSuspended === suspended)
            return reply(409, { title: suspended ? "Admin.AccountAlreadySuspended" : "Admin.AccountAlreadyReinstated", status: 409 });
          const accessRevision = (harness.access.get(accountId)?.accessRevision ?? 0) + 1;
          harness.access.set(accountId, { isSuspended: suspended, accessRevision });
          if (suspended) harness.emailChanges.delete(accountId);
          if (harness.accessMode === "unknownAfterCommit") return reply(503, { title: "Unavailable", status: 503 });
          return reply(200, { userId: accountId, isSuspended: suspended, accessRevision,
            pendingDeletion: found.status === "PendingDeletion" });
        };
        if (harness.holdAccessWrites) pendingAccessWrites.push(answer);
        else answer();
      });
      return;
    }
    const deletionCommand = /^POST \/api\/v1\/admin\/accounts\/([0-9a-f-]{36})\/deletion$/.exec(route);
    if (deletionCommand !== null) {
      const accountId = deletionCommand[1] ?? "";
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        deletionRequests.push({ accountId, body });
        const reply = (status: number, value: unknown) => json(status, value, { "Cache-Control": "private, no-store" });
        const input = JSON.parse(body) as { reauthGrant?: unknown };
        if (harness.who !== "admin") return reply(403, { title: "Forbidden", status: 403 });
        if (input.reauthGrant !== STEP_UP_GRANT || issuedGrants === 0)
          return reply(401, { title: "Auth.InvalidCredentials", status: 401 });
        issuedGrants--;
        const answer = () => {
          const mode = harness.deletionMode;
          if (mode === "unknown") return reply(503, { title: "Unavailable", status: 503 });
          if (mode === "forbidden" || mode === "unauthorized" || mode === "rateLimited")
            return refusal(mode);
          const found = accountDetails(accountId, harness.gone, harness.access, harness.deletions);
          if (found === undefined) return reply(404, { title: "Admin.AccountNotFound", status: 404 });
          if (found.status === "ProfileMissing") return reply(410, { title: "Admin.ProfileUnavailable", status: 410 });
          if (found.status === "PendingDeletion" || mode === "alreadyPending")
            return reply(409, { title: "Admin.AccountAlreadyPendingDeletion", status: 409 });
          if (accountId === ADMIN.userId) return reply(409, { title: "Admin.SelfDeletion", status: 409 });
          if (mode === "lastAdministrator") return reply(409, { title: "Admin.LastAdministrator", status: 409 });
          harness.deletions.set(accountId, harness.deletionTiming);
          harness.emailChanges.delete(accountId);
          if (mode === "unknownAfterCommit") return reply(503, { title: "Unavailable", status: 503 });
          return reply(202, { userId: accountId, ...harness.deletionTiming });
        };
        if (harness.holdAccessWrites) pendingAccessWrites.push(answer);
        else answer();
      });
      return;
    }
    const emailChange = /^(GET|POST|DELETE) \/api\/v1\/admin\/accounts\/([0-9a-f-]{36})\/email-change$/.exec(route);
    if (emailChange !== null) {
      const [, method, accountId = ""] = emailChange;
      if (method === "GET") {
        return harness.emailChanges.has(accountId) ? json(200, { state: "Pending", ...EMAIL_CHANGE_INSTANTS }) : json(204);
      }
      if (method === "DELETE") {
        if (!harness.emailChanges.delete(accountId)) {
          return json(410, { title: "Auth.AccountEmailChangeNothingPending", status: 410 });
        }
        return json(204);
      }
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        emailChangeRequests.push(body);
        harness.emailChanges.add(accountId);
        json(202, EMAIL_CHANGE_INSTANTS);
      });
      return;
    }

    // #1979 — feedback: its list, one submission, the summary and the availability, read side by side, and
    // the two commands. `mode` refuses every read at once; `feedbackReads` refuses one while the rest answer.
    const feedbackRead = (read: FeedbackRead, answer: () => { readonly status: number; readonly value?: unknown }) => {
      feedbackQueries.push(`${url.pathname}${url.search}`);
      const mode = harness.mode === "ok" ? harness.feedbackReads[read] : harness.mode;
      if (mode !== "ok") return refusal(mode);
      const { status, value } = answer();
      return value === undefined ? bare(status) : json(status, value, { "Cache-Control": "private, no-store" });
    };
    const submissions = () => [...harness.feedback.values()];
    if (route === "GET /api/v1/admin/feedback") {
      return feedbackRead("list", () => ({
        status: 200,
        value: feedbackList(submissions(), {
          status: url.searchParams.get("status") ?? undefined,
          pageKey: url.searchParams.get("page") ?? undefined,
          pageNumber: Number(url.searchParams.get("pageNumber") ?? "1"),
          pageSize: Number(url.searchParams.get("pageSize") ?? "25"),
        }),
      }));
    }
    if (route === "GET /api/v1/admin/feedback/summary") {
      const days = Number(url.searchParams.get("days") ?? "30");
      return feedbackRead("summary", () =>
        [7, 30, 90].includes(days)
          ? { status: 200, value: feedbackSummary(submissions(), days) }
          : { status: 400, value: { errors: { Days: ["Välj 7, 30 eller 90 dagar."] } } });
    }
    if (route === "GET /api/v1/admin/feedback/availability") {
      return feedbackRead("availability", () => ({ status: 200, value: { availability: harness.feedbackAvailability } }));
    }
    const feedbackScreenshot = /^GET \/api\/v1\/admin\/feedback\/([0-9a-f-]{36})\/screenshot$/.exec(route);
    if (feedbackScreenshot !== null) {
      const id = feedbackScreenshot[1] ?? "";
      harness.feedbackScreenshotRequests.push(id);
      const headers = { "Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff" };
      const problem = (status: number, title: string, extra: Record<string, string> = {}) =>
        json(status, { status, title }, { ...headers, ...extra });
      if (harness.mode !== "ok") {
        const status = { forbidden: 403, unauthorized: 401, error: 500, rateLimited: 429 }[harness.mode];
        return problem(status, "Screenshot read refused", status === 429 ? { "Retry-After": "6" } : {});
      }
      const mode = harness.feedbackScreenshotReads.get(id) ?? "ok";
      const answer = () => {
        harness.feedbackScreenshotAnswers.push(id);
        if (response.destroyed) return;
        const found = harness.feedbackScreenshots.get(id);
        if (mode === "notFound" || found === undefined || harness.feedback.get(id)?.screenshot == null)
          return problem(404, "Feedback.ScreenshotNotFound");
        if (mode === "error") return problem(500, "Internal Server Error");
        response.writeHead(200, {
          ...headers, "Content-Type": "image/png", "Content-Length": String(found.content.length),
        });
        response.end(found.content);
      };
      if (mode === "loading") pendingFeedbackScreenshots.push(answer);
      else answer();
      return;
    }
    const feedbackItem = /^GET \/api\/v1\/admin\/feedback\/([0-9a-f-]{36})$/.exec(route);
    if (feedbackItem !== null) {
      const found = harness.feedback.get(feedbackItem[1] ?? "");
      // The real endpoint answers an id it does not know with a bare 404.
      return feedbackRead("detail", () => (found === undefined ? { status: 404 } : { status: 200, value: feedbackDetail(found) }));
    }
    const feedbackCommand = /^POST \/api\/v1\/admin\/feedback\/([0-9a-f-]{36})\/(status|notification\/requeue)$/.exec(route);
    if (feedbackCommand !== null) {
      const feedbackId = feedbackCommand[1] ?? "";
      const command = feedbackCommand[2] === "status" ? "status" : "requeue";
      let body = "";
      request.setEncoding("utf8");
      request.on("data", (chunk: string) => (body += chunk));
      request.on("end", () => {
        feedbackCommands.push({ id: feedbackId, command, body });
        const problem = (status: number, title: string) =>
          json(status, { title, status }, { "Cache-Control": "private, no-store" });
        if (harness.mode !== "ok") return refusal();
        const found = harness.feedback.get(feedbackId);
        if (found === undefined) return problem(404, "Feedback.NotFound");
        if (command === "status") {
          const { status } = JSON.parse(body) as { status?: unknown };
          if (status !== "New" && status !== "InProgress" && status !== "Resolved" && status !== "Declined")
            return json(400, { errors: { Status: ["Okänd status."] } });
          if (status === found.status) return problem(400, "Feedback.StatusUnchanged");
          harness.feedback.set(feedbackId, { ...found, status, statusChangedAt: FEEDBACK_NOW });
          return bare(204);
        }
        // The domain's rule (`FeedbackNotification.Requeue`): only a failed notice, or one whose outcome is
        // unknown and whose risk of a duplicate is acknowledged, is sent again.
        const notice = found.notification;
        if (notice === null) return problem(404, "Feedback.NotFound");
        const { acknowledgeDuplicateRisk } = JSON.parse(body) as { acknowledgeDuplicateRisk?: unknown };
        if (notice.state !== "Failed" && notice.state !== "Unknown")
          return problem(409, "Feedback.NotificationNotRequeueable");
        if (notice.state === "Unknown" && acknowledgeDuplicateRisk !== true)
          return problem(409, "Feedback.DuplicateRiskNotAcknowledged");
        harness.feedback.set(feedbackId, {
          ...found,
          notification: { ...notice, state: "Queued", attempts: 0, nextAttemptAt: FEEDBACK_NOW, stateChangedAt: FEEDBACK_NOW },
        });
        return bare(204);
      });
      return;
    }

    const detail = /^GET \/api\/v1\/admin\/accounts\/([0-9a-f-]{36})$/.exec(route);
    if (detail !== null) {
      const accountId = detail[1] ?? "";
      const mode = harness.accountDetailFailuresAfterDeletion.has(accountId) && harness.deletions.has(accountId)
        ? "error" : harness.mode;
      const found = accountDetails(accountId, harness.gone, harness.access, harness.deletions, harness.deletionTiming);
      const answer = () => {
        if (mode !== "ok") return refusal(mode);
        return found === undefined ? json(404, { title: "Not Found", status: 404 }) : json(200, found);
      };
      if (harness.holdAccountDetails) pendingAccountDetails.push(answer);
      else answer();
      return;
    }

    const answer = adminRoutes[route];
    if (answer !== undefined) {
      const source = route.endsWith("/overview/accounts") ? "accounts"
        : route.endsWith("/audit-log") ? "audit" : route.endsWith("/jobs/failed") ? "jobs" : null;
      const mode = harness.mode === "ok" && source !== null ? harness.overviewReads[source] : harness.mode;
      const respond = () => {
        if (mode !== "ok") return refusal(mode);
        return json(200, answer(), {
          "Cache-Control": "private, no-store",
          ...(source !== null && harness.overviewSampledAt !== null ? { "X-Admin-Sampled-At": harness.overviewSampledAt } : {}),
        });
      };
      if (source !== null && harness.overviewDelayMs > 0) {
        const timer = setTimeout(respond, harness.overviewDelayMs);
        response.once("close", () => clearTimeout(timer));
        return;
      }
      return respond();
    }

    misses.push(route);
    return json(404, { title: "No fixture", status: 404 });
  });

  const proxy = createTlsServer(localhostCertificate(), (request, response) => {
    const upstream = forward(
      { host: "127.0.0.1", port: ports.next, method: request.method, path: request.url, headers: request.headers },
      (answer) => {
        response.writeHead(answer.statusCode ?? 502, answer.headers);
        answer.pipe(response);
      }
    );
    upstream.on("error", () => {
      response.writeHead(502);
      response.end();
    });
    request.pipe(upstream);
  });

  await Promise.all([listen(backend, ports.backend), listen(proxy, ports.proxy)]);
  return harness;
}
