import { randomBytes } from "node:crypto";
import { createServer, request as forward, type IncomingMessage, type Server } from "node:http";

/**
 * The servers around the app for the Strict-cookie measurement (#1744). The browser only ever talks to
 * the front proxy (`localhost`), which forwards to `next start` and records what each request carried,
 * and to the provider stub on `127.0.0.1`, a different site, so a chain through it is cross-site.
 *
 * A browser follows a 3xx without asking Playwright's routes, so the start's redirect to Google cannot
 * be intercepted in the browser. The proxy rewrites it to the stub instead, and refuses any other
 * redirect off the machine.
 */
export const HARNESS_PORTS = { proxy: 3106, next: 3107, backend: 3108, idp: 3109 } as const;

export const APP_ORIGIN = `http://localhost:${HARNESS_PORTS.proxy}`;
export const IDP_ORIGIN = `http://127.0.0.1:${HARNESS_PORTS.idp}`;
export const AUTHORIZATION_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
export const CALLBACK_PATH = "/api/auth/oauth/google/callback";
export const SESSION_ID = "harness-session";

export const PROBE_COOKIE = "__Host-harness_probe";
export const REFUSED_EXTERNAL_PATH = "/__control/refused-external";

export type Recorded = {
  readonly path: string;
  readonly cookies: Readonly<Record<string, string>>;
  readonly referer: string | null;
  readonly setCookies: readonly string[];
};

function cookiesOf(request: IncomingMessage): Record<string, string> {
  const jar: Record<string, string> = {};
  for (const pair of (request.headers.cookie ?? "").split(";")) {
    const at = pair.indexOf("=");
    if (at > 0) jar[pair.slice(0, at).trim()] = pair.slice(at + 1).trim();
  }
  return jar;
}

function listen(server: Server, port: number, host?: string): Promise<Server> {
  return new Promise((resolve) => server.listen(port, host, () => resolve(server)));
}

function close(server: Server): Promise<void> {
  return new Promise((resolve) => {
    server.closeAllConnections();
    server.close(() => resolve());
  });
}

async function bodyOf(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(chunk as Buffer);
  return JSON.parse(Buffer.concat(chunks).toString("utf8") || "null");
}

/** The provider's consent page: a document on another site whose button leaves through `/approve`. */
function consentPage(to: string): string {
  const href = `${IDP_ORIGIN}/approve?to=${encodeURIComponent(to)}`;
  return `<!doctype html><title>stub</title><a id="approve" href="${href}">approve</a>`;
}

/** Where the stub's consent page for an arbitrary target lives: the control of branch (ii). */
export function consentUrlFor(to: string): string {
  return `${IDP_ORIGIN}/consent?to=${encodeURIComponent(to)}`;
}

export type Harness = {
  /** Every request the browser sent to the app, in order. */
  readonly requests: Recorded[];
  /** Every call that reached the stub api's callback. */
  readonly callbacks: { code: string; state: string }[];
  /** The code the provider hands back, which chooses the stub api's answer: `signedIn`, `consent`, `closed` or `unverified`. */
  code: string;
  /** The mutant of branch (iv): the proxy rewrites the state cookie to `SameSite=Strict`. */
  strictStateCookie: boolean;
  reset(): void;
  stop(): Promise<void>;
};

export async function startHarness(): Promise<Harness> {
  const requests: Recorded[] = [];
  const callbacks: { code: string; state: string }[] = [];
  let issuedState = "";

  const backend = createServer(async (request, response) => {
    const json = (status: number, value: unknown) => {
      response.writeHead(status, { "Content-Type": "application/json" });
      response.end(JSON.stringify(value));
    };
    if (request.method === "GET" && request.url === "/api/v1/auth/oauth/providers") return json(200, ["google"]);
    if (request.method === "POST" && request.url === "/api/v1/auth/oauth/google/start") {
      issuedState = randomBytes(32).toString("base64url");
      const query = new URLSearchParams({
        client_id: "harness-client",
        redirect_uri: `${APP_ORIGIN}${CALLBACK_PATH}`,
        response_type: "code",
        scope: "openid email",
        code_challenge: randomBytes(32).toString("base64url"),
        code_challenge_method: "S256",
        state: issuedState,
      });
      return json(200, { authorizeUrl: `${AUTHORIZATION_ENDPOINT}?${query}`, state: issuedState });
    }
    if (request.method === "POST" && request.url === `/api/v1/auth/oauth/google/callback`) {
      const { code, state } = (await bodyOf(request)) as { code: string; state: string };
      callbacks.push({ code, state });
      if (state !== issuedState) return json(400, { title: "Auth.ExternalLoginStateUnusable" });
      if (code === "unverified") return json(400, { title: "Auth.ExternalEmailUnverified" });
      if (code === "closed") return json(200, { outcome: "registrationClosed" });
      return code === "consent"
        ? json(200, { outcome: "consentRequired", grantToken: "harness-grant" })
        : json(200, { outcome: "signedIn", sessionId: SESSION_ID });
    }
    return json(401, { title: "Unauthorized" });
  });

  const idp = createServer((request, response) => {
    const url = new URL(request.url ?? "/", IDP_ORIGIN);
    const html = (body: string) => {
      response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
      response.end(body);
    };
    if (url.pathname === "/authorize") {
      const state = url.searchParams.get("state") ?? "";
      return html(consentPage(`${APP_ORIGIN}${CALLBACK_PATH}?code=${harness.code}&state=${state}`));
    }
    if (url.pathname === "/consent") return html(consentPage(url.searchParams.get("to") ?? APP_ORIGIN));
    response.writeHead(302, { Location: url.searchParams.get("to") ?? APP_ORIGIN });
    response.end();
  });

  const harness: Harness = {
    requests,
    callbacks,
    code: "signedIn",
    strictStateCookie: false,
    reset() {
      requests.length = 0;
      callbacks.length = 0;
      harness.code = "signedIn";
      harness.strictStateCookie = false;
    },
    async stop() {
      await Promise.all([close(proxy), close(backend), close(idp)]);
    },
  };

  /** The start's redirect goes to the stub; nothing else may leave the machine. */
  function contained(location: string | undefined): string | undefined {
    if (location === undefined || location.startsWith("/")) return location;
    const target = new URL(location);
    if (`${target.origin}${target.pathname}` === AUTHORIZATION_ENDPOINT) return `${IDP_ORIGIN}/authorize${target.search}`;
    return target.origin === APP_ORIGIN ? location : REFUSED_EXTERNAL_PATH;
  }

  const proxy = createServer((request, response) => {
    const path = request.url ?? "/";
    const record = (setCookies: readonly string[]) =>
      requests.push({ path, cookies: cookiesOf(request), referer: request.headers.referer ?? null, setCookies });

    // The controls of branches (ii) and (iii): a Strict cookie set on a 302, and the hop after it.
    if (path === "/__control/same-site-page") {
      record([]);
      response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
      return response.end('<!doctype html><a id="go" href="/__control/redirect">go</a>');
    }
    if (path === "/__control/redirect") {
      const probe = `${PROBE_COOKIE}=1; Path=/; Secure; HttpOnly; SameSite=Strict`;
      record([probe]);
      response.writeHead(302, { Location: "/__control/landing", "Set-Cookie": probe });
      return response.end();
    }
    if (path === "/__control/landing" || path === REFUSED_EXTERNAL_PATH) {
      record([]);
      response.writeHead(200, { "Content-Type": "text/plain" });
      return response.end(path);
    }

    const upstream = forward(
      { host: "127.0.0.1", port: HARNESS_PORTS.next, method: request.method, path, headers: request.headers },
      (answer) => {
        let setCookies = [...(answer.headers["set-cookie"] ?? [])];
        if (harness.strictStateCookie) {
          setCookies = setCookies.map((cookie) =>
            cookie.startsWith("__Host-jobbliggaren_oauth=") ? cookie.replace(/SameSite=Lax/i, "SameSite=Strict") : cookie
          );
        }
        record(setCookies);
        const headers = { ...answer.headers, "set-cookie": setCookies };
        const location = contained(answer.headers.location);
        if (location === undefined) delete headers.location;
        else headers.location = location;
        response.writeHead(answer.statusCode ?? 502, headers);
        answer.pipe(response);
      }
    );
    request.pipe(upstream);
  });

  await Promise.all([
    listen(backend, HARNESS_PORTS.backend),
    listen(idp, HARNESS_PORTS.idp, "127.0.0.1"),
    listen(proxy, HARNESS_PORTS.proxy),
  ]);
  return harness;
}
