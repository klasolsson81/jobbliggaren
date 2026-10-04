import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { createServer, request as forward, type Server } from "node:http";
import { createServer as createTlsServer, type Server as TlsServer } from "node:https";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ADMIN, AUDIT_PAGE, FAILED_JOBS, MEMBER, RECURRING_JOBS } from "./fixtures";

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
export const HARNESS_PORTS = { proxy: 3120, next: 3121, backend: 3122 } as const;

export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;
export const SESSION_COOKIE = "__Host-jobbliggaren_session";
export const SESSION_ID = "admin-harness-session";

export type AdminMode = "ok" | "empty" | "forbidden" | "rateLimited" | "error";

export type Harness = {
  who: "admin" | "member";
  mode: AdminMode;
  /** Every backend path the app asked for that the fixtures do not answer. */
  readonly misses: string[];
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

export async function startHarness(): Promise<Harness> {
  const misses: string[] = [];

  const harness: Harness = {
    who: "admin",
    mode: "ok",
    misses,
    reset() {
      misses.length = 0;
      harness.who = "admin";
      harness.mode = "ok";
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
    const url = new URL(request.url ?? "/", `http://localhost:${HARNESS_PORTS.backend}`);
    const route = `${request.method} ${url.pathname}`;

    if (route === "GET /api/v1/me") return json(200, harness.who === "admin" ? ADMIN : MEMBER);
    if (route === "POST /api/v1/auth/refresh") return json(200, { rotated: false, sessionId: null });
    // The start page an ordinary account is sent to reads the landing figures.
    if (route === "GET /api/v1/landing/stats")
      return json(200, { activeCount: 42000, newToday: 62, isStale: false, refreshedAt: "2026-10-03T19:00:00Z" });

    const adminRoutes: Record<string, () => unknown> = {
      "GET /api/v1/admin/audit-log": () =>
        harness.mode === "empty" ? { ...AUDIT_PAGE, items: [], totalCount: 0, totalPages: 0 } : AUDIT_PAGE,
      "GET /api/v1/admin/jobs/recurring": () => (harness.mode === "empty" ? [] : RECURRING_JOBS),
      "GET /api/v1/admin/jobs/failed": () =>
        harness.mode === "empty" ? { totalCount: 0, returned: 0, items: [] } : FAILED_JOBS,
    };
    const answer = adminRoutes[route];
    if (answer !== undefined) {
      switch (harness.mode) {
        case "forbidden":
          return json(403, { title: "Forbidden", status: 403 });
        case "rateLimited":
          return json(429, { title: "Too Many Requests", status: 429 }, { "Retry-After": "30" });
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
      { host: "127.0.0.1", port: HARNESS_PORTS.next, method: request.method, path: request.url, headers: request.headers },
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

  await Promise.all([listen(backend, HARNESS_PORTS.backend), listen(proxy, HARNESS_PORTS.proxy)]);
  return harness;
}
