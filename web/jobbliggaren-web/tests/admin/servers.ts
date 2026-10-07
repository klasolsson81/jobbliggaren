import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { createServer, request as forward, type Server } from "node:http";
import { createServer as createTlsServer, type Server as TlsServer } from "node:https";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  ADMIN,
  AUDIT_PAGE,
  EMAIL_CHANGE_INSTANTS,
  FAILED_JOBS,
  MEMBER,
  RECURRING_JOBS,
  STEP_UP_CHALLENGE,
  STEP_UP_GRANT,
  accountDetails,
  accountsPage,
  type AccountAccessState,
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

export const HARNESS_PORTS: HarnessPorts = { proxy: 3120, next: 3121, backend: 3122 };

export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;
export const SESSION_COOKIE = "__Host-jobbliggaren_session";
export const SESSION_ID = "admin-harness-session";

export type AdminMode = "ok" | "forbidden" | "error" | "rateLimited" | "unauthorized";
export type AccessMode = "ok" | "forbidden" | "rateLimited" | "unauthorized" | "unknown" | "unknownAfterCommit";

export type Harness = {
  who: "admin" | "member";
  mode: AdminMode;
  accessMode: AccessMode;
  codeMode: "ok" | "unauthorized" | "rateLimited";
  holdCodeRequests: boolean;
  holdAccessWrites: boolean;
  readonly access: Map<string, AccountAccessState>;
  readonly accessRequests: { accountId: string; operation: "suspend" | "reinstate"; body: string }[];
  readonly reauthVerifications: string[];
  releaseCodeRequests(): void;
  releaseAccessWrites(): void;
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
  const reauthVerifications: string[] = [];
  const pendingCodeRequests: (() => void)[] = [];
  const pendingAccessWrites: (() => void)[] = [];
  let issuedGrants = 0;

  const harness: Harness = {
    who: "admin",
    mode: "ok",
    accessMode: "ok",
    codeMode: "ok",
    holdCodeRequests: false,
    holdAccessWrites: false,
    access: new Map(),
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
    many: false,
    gone: new Set(),
    emailChanges: new Set(),
    emailChangeRequests,
    misses,
    requests,
    searches,
    reset() {
      harness.releaseCodeRequests();
      harness.releaseAccessWrites();
      misses.length = 0;
      requests.length = 0;
      searches.length = 0;
      emailChangeRequests.length = 0;
      accessRequests.length = 0;
      reauthVerifications.length = 0;
      issuedGrants = 0;
      harness.who = "admin";
      harness.mode = "ok";
      harness.accessMode = "ok";
      harness.codeMode = "ok";
      harness.many = false;
      harness.gone.clear();
      harness.emailChanges.clear();
      harness.access.clear();
    },
    async stop() {
      await Promise.all([close(proxy), close(backend)]);
    },
  };

  const backend = createServer((request, response) => {
    const json = (status: number, value?: unknown, headers: Record<string, string> = {}) => {
      response.writeHead(status, { "Content-Type": "application/json", ...headers });
      response.end(JSON.stringify(value ?? null));
    };
    const url = new URL(request.url ?? "/", `http://localhost:${ports.backend}`);
    const route = `${request.method} ${url.pathname}`;
    requests.push(route);

    if (route === "GET /api/v1/me") return json(200, harness.who === "admin" ? ADMIN : MEMBER);
    if (route === "POST /api/v1/auth/refresh") return json(200, { rotated: false, sessionId: null });
    // The start page an ordinary account is sent to reads the landing figures.
    if (route === "GET /api/v1/landing/stats")
      return json(200, { activeCount: 42000, newToday: 62, isStale: false, refreshedAt: "2026-10-03T19:00:00Z" });

    const adminRoutes: Record<string, () => unknown> = {
      "GET /api/v1/admin/audit-log": () => AUDIT_PAGE,
      "GET /api/v1/admin/jobs/recurring": () => RECURRING_JOBS,
      "GET /api/v1/admin/jobs/failed": () => FAILED_JOBS,
    };
    const refusal = () => {
      switch (harness.mode) {
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
        const { address, status, page, pageSize } = JSON.parse(body) as {
          address?: string; status?: string; page?: number; pageSize?: number;
        };
        return json(200, accountsPage(address, { status, page, pageSize, many: harness.many,
          gone: harness.gone, access: harness.access }));
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
          const found = accountDetails(accountId, harness.gone, harness.access);
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

    const detail = /^GET \/api\/v1\/admin\/accounts\/([0-9a-f-]{36})$/.exec(route);
    if (detail !== null) {
      if (harness.mode !== "ok") return refusal();
      const found = accountDetails(detail[1] ?? "", harness.gone, harness.access);
      return found === undefined ? json(404, { title: "Not Found", status: 404 }) : json(200, found);
    }

    const answer = adminRoutes[route];
    if (answer !== undefined) {
      switch (harness.mode) {
        case "forbidden":
          return json(403, { title: "Forbidden", status: 403 });
        case "error":
          return json(500, { title: "Internal Server Error", status: 500 });
        default:
          return json(200, answer());
      }
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
