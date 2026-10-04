import { execFileSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { mkdtempSync, readFileSync } from "node:fs";
import { createServer, request as forward, type IncomingMessage, type Server } from "node:http";
import { createServer as createTlsServer, type Server as TlsServer } from "node:https";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  ALL_ADS,
  DESCRIPTION,
  EXPIRES_AT,
  MATCHES,
  PUBLISHED_AT,
  TAXONOMY,
  USER,
  matchDetail,
  profile,
  type HarnessAd,
} from "./fixtures";

/**
 * The servers around the app for the job modal's navigation harness (#1963): a fixture backend the
 * app's server components read, and a front proxy that serves the app over https, because the session
 * cookie is `__Host-` and the security headers ask for https. The browser only ever talks to the proxy.
 *
 * The backend answers what the flows' pages read, with state: marking an ad applied creates an
 * application, and what the user has stated decides the profile and every match detail together.
 * Anything else answers 404 and is recorded in `misses`, so a page that starts reading something new
 * shows up there rather than as a silent fallback.
 */
export const HARNESS_PORTS = { proxy: 3110, next: 3111, backend: 3112 } as const;

export const APP_ORIGIN = `https://localhost:${HARNESS_PORTS.proxy}`;
export const SESSION_COOKIE = "__Host-jobbliggaren_session";
export const SESSION_ID = "job-modal-harness-session";

export type Harness = {
  /** Whether the user has stated an occupation; decides the profile and every match detail. */
  occupationStated: boolean;
  /** Listed ads whose detail answers this status: 404, or 410 for an ad erased under Art. 17. */
  readonly unavailableAds: Map<string, 404 | 410>;
  /** Ads whose listed application answers 404 when it is read. */
  readonly unavailableApplications: Set<string>;
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
  const dir = mkdtempSync(join(tmpdir(), "job-modal-"));
  const [key, cert] = [join(dir, "key.pem"), join(dir, "cert.pem")];
  execFileSync(
    "openssl",
    ["req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1", "-subj", "/CN=localhost",
      "-addext", "subjectAltName=DNS:localhost", "-keyout", key, "-out", cert],
    { stdio: "ignore" }
  );
  return { key: readFileSync(key), cert: readFileSync(cert) };
}

async function bodyOf(request: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(chunk as Buffer);
  const text = Buffer.concat(chunks).toString("utf8");
  return text ? (JSON.parse(text) as Record<string, unknown>) : {};
}

const summary = (ad: HarnessAd) => ({
  id: ad.id,
  title: ad.title,
  companyName: ad.company,
  url: `https://example.test/annons/${ad.id.slice(-3)}`,
  source: "Platsbanken",
  status: "Active",
  publishedAt: PUBLISHED_AT,
  expiresAt: EXPIRES_AT,
  createdAt: PUBLISHED_AT,
});

const jobAdOf = (ad: HarnessAd) => {
  const { id, title, companyName, url, source, publishedAt, expiresAt, status } = summary(ad);
  return { jobAdId: id, title, company: companyName, url, source, publishedAt, expiresAt, status };
};

const application = (applicationId: string, ad: HarnessAd) => ({
  id: applicationId,
  jobSeekerId: USER.userId,
  jobAdId: ad.id,
  status: "Submitted",
  createdAt: PUBLISHED_AT,
  updatedAt: PUBLISHED_AT,
  appliedAt: PUBLISHED_AT,
  jobAd: jobAdOf(ad),
  coverLetter: null,
  followUps: [],
  notes: [],
  statusChanges: [{ from: "Draft", to: "Submitted", changedAt: PUBLISHED_AT }],
  preservedAd: null,
});

export async function startHarness(): Promise<Harness> {
  const misses: string[] = [];
  let applied = new Map<string, string>();
  let saved = new Set<string>();

  const adById = (adId: string) => ALL_ADS.find((ad) => ad.id === adId);

  const harness: Harness = {
    occupationStated: true,
    unavailableAds: new Map(),
    unavailableApplications: new Set(),
    misses,
    reset() {
      misses.length = 0;
      harness.occupationStated = true;
      harness.unavailableAds.clear();
      harness.unavailableApplications.clear();
      applied = new Map(ALL_ADS.filter((ad) => ad.appliedBeforeVisit).map((ad) => [ad.id, randomUUID()]));
      saved = new Set(ALL_ADS.filter((ad) => ad.saved).map((ad) => ad.id));
    },
    async stop() {
      await Promise.all([close(proxy), close(backend)]);
    },
  };
  harness.reset();

  const backend = createServer(async (request, response) => {
    const json = (status: number, value?: unknown) => {
      response.writeHead(status, { "Content-Type": "application/json" });
      response.end(status === 204 ? "" : JSON.stringify(value ?? null));
    };
    const url = new URL(request.url ?? "/", `http://localhost:${HARNESS_PORTS.backend}`);
    const route = `${request.method} ${url.pathname}`;
    const pathId = url.pathname.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)?.[0] ?? "";
    const ad = adById(pathId);

    switch (route) {
      case "GET /api/v1/me":
        return json(200, USER);
      case "POST /api/v1/auth/refresh":
        return json(200, { rotated: false, sessionId: null });
      case "GET /api/v1/landing/stats":
        return json(200, { activeCount: 42000, newToday: 62, isStale: false, refreshedAt: PUBLISHED_AT });
      case "GET /api/v1/me/jobs/watermark":
        return json(200, { lastSeenJobsAt: PUBLISHED_AT });
      case "POST /api/v1/me/jobs/seen":
        return json(204);
      case "GET /api/v1/me/recent-searches":
        return json(200, []);
      case "GET /api/v1/job-ads/taxonomy":
        return json(200, TAXONOMY);
      case "GET /api/v1/job-ads/taxonomy/skills/labels":
        return json(200, []);
      case "GET /api/v1/me/profile":
        return json(200, profile(harness.occupationStated));
      case "GET /api/v1/job-ads": {
        const items = ALL_ADS.map(summary);
        return json(200, { items, totalCount: items.length, page: 1, pageSize: 20 });
      }
      case "POST /api/v1/me/job-ad-match-tags": {
        const { jobAdIds = [] } = (await bodyOf(request)) as { jobAdIds?: string[] };
        const detail = matchDetail(harness.occupationStated);
        const entries = detail.grade
          ? Object.fromEntries(jobAdIds.filter(adById).map((adId) => [adId, {
              grade: detail.grade,
              ssykOverlap: detail.ssykOverlap.verdict,
              titleSimilarity: detail.titleSimilarity.verdict,
              regionFit: detail.regionFit.verdict,
              employmentFit: detail.employmentFit.verdict,
            }]))
          : {};
        return json(200, { entries });
      }
      case "GET /api/v1/me/job-ad-status":
      case "POST /api/v1/me/job-ad-status": {
        const ids = request.method === "POST"
          ? (((await bodyOf(request)) as { jobAdIds?: string[] }).jobAdIds ?? [])
          : ALL_ADS.map((a) => a.id);
        return json(200, { savedIds: ids.filter((i) => saved.has(i)), appliedIds: ids.filter((i) => applied.has(i)) });
      }
      case "POST /api/v1/me/company-watches/status": {
        const { jobAdIds = [] } = (await bodyOf(request)) as { jobAdIds?: string[] };
        return json(200, { statuses: jobAdIds.map((jobAdId) => ({ jobAdId, companyWatchId: null, followable: true })) });
      }
      case "POST /api/v1/me/application-history/counts":
        return json(200, { countsByJobAdId: {} });
      case "GET /api/v1/me/matches":
        return json(200, MATCHES);
      case "POST /api/v1/me/matches/seen":
        return json(204);
      case "GET /api/v1/me/saved-job-ads":
        return json(200, [...saved].flatMap((adId) => {
          const savedAd = adById(adId);
          return savedAd ? [{ id: `5a7e0000${adId.slice(8)}`, jobAdId: adId, savedAt: PUBLISHED_AT, jobAd: jobAdOf(savedAd) }] : [];
        }));
      case "GET /api/v1/applications/pipeline":
        return json(200, [{
          status: "Submitted",
          count: applied.size,
          applications: [...applied].flatMap(([adId, applicationId]) => {
            const appliedAd = adById(adId);
            return appliedAd ? [{
              ...application(applicationId, appliedAd),
              lastStatusChangeAt: PUBLISHED_AT,
              lastFollowUpAt: null,
              attentionSignal: "None",
              hasPreservedAdText: false,
            }] : [];
          }),
        }]);
    }

    if (ad && route === `GET /api/v1/job-ads/${ad.id}`) {
      const gone = harness.unavailableAds.get(ad.id);
      if (gone) return json(gone, { title: gone === 410 ? "Gone" : "Not Found", status: gone });
      return json(200, { ...summary(ad), description: DESCRIPTION, contacts: [] });
    }
    if (ad && route === `GET /api/v1/me/job-ad-match-tags/${ad.id}`)
      return json(200, matchDetail(harness.occupationStated));
    if (ad && route === `GET /api/v1/me/applications/has-applied/${ad.id}`)
      return json(200, { hasApplied: applied.has(ad.id) });
    if (ad && route === `POST /api/v1/applications/from-job-ad/${ad.id}`) {
      const applicationId = randomUUID();
      applied.set(ad.id, applicationId);
      return json(201, { id: applicationId });
    }
    if (ad && route === `POST /api/v1/me/company-watches/ad-hits/${ad.id}/seen`) return json(204);
    if (route === `GET /api/v1/applications/${pathId}`) {
      const owner = [...applied].find(([, applicationId]) => applicationId === pathId);
      const ownerAd = owner && adById(owner[0]);
      if (ownerAd && harness.unavailableApplications.has(ownerAd.id))
        return json(404, { title: "Not Found", status: 404 });
      if (ownerAd) return json(200, application(pathId, ownerAd));
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
