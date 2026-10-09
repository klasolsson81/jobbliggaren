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
export const SYNTHETIC_CV = "%PDF-1.4 synthetic navigation fixture";

/** The code that draws each answer of /adressbyte's completion (#1975); any other code is the one refusal. */
export const ADDRESS_CHANGE_CODES = {
  done: "111111",
  notYet: "222222",
  tooManyAttempts: "333333",
  unavailable: "444444",
  unknown: "555555",
} as const;

/** How the fixture backend answers the next feedback submission (#1979), one per answer the BFF maps. */
export type FeedbackAnswer =
  | "saved"
  | "empty"
  | "screenshot"
  | "closed"
  | "busy"
  | "tooLarge"
  | "rateLimited"
  | "unknown";

/** What a feedback submission carried to the backend: the payload as sent, and the image's shape. */
export type FeedbackReceipt = {
  readonly payload: Record<string, unknown>;
  readonly screenshot: { readonly bytes: number; readonly png: boolean; readonly width: number; readonly height: number } | null;
};

export type Harness = {
  /** Whether the user has stated an occupation; decides the profile and every match detail. */
  occupationStated: boolean;
  jobAdReadGate: Promise<void> | null;
  /** Listed ads erased under Art. 17 after the list rendered: their detail answers 410. */
  readonly erasedAds: Set<string>;
  /** Ads whose listed application answers 404 when it is read. */
  readonly unavailableApplications: Set<string>;
  /** Every backend path the app asked for that the fixtures do not answer. */
  readonly misses: string[];
  readonly requests: string[];
  readonly syntheticUploadReceipts: readonly boolean[];
  rejectNextImport: boolean;
  /** #1979 — closed by default, so every other spec renders its pages without the feedback row. */
  readonly feedback: { open: boolean; answeredPages: string[]; answer: FeedbackAnswer };
  readonly feedbackReceipts: FeedbackReceipt[];
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

// PNG's signature, then IHDR's width and height as big-endian 32-bit integers at bytes 16 and 20.
function screenshotShape(bytes: Uint8Array): NonNullable<FeedbackReceipt["screenshot"]> {
  const png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a].every((b, i) => bytes[i] === b);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return {
    bytes: bytes.byteLength,
    png,
    width: png && bytes.byteLength >= 24 ? view.getUint32(16) : 0,
    height: png && bytes.byteLength >= 24 ? view.getUint32(20) : 0,
  };
}

async function feedbackOf(request: IncomingMessage): Promise<FeedbackReceipt> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(Buffer.from(chunk));
  const form = await new Response(Buffer.concat(chunks), {
    headers: { "Content-Type": request.headers["content-type"] ?? "" },
  }).formData();
  const shot = form.get("screenshot");
  return {
    payload: JSON.parse(String(form.get("payload"))) as Record<string, unknown>,
    screenshot: shot instanceof Blob ? screenshotShape(new Uint8Array(await shot.arrayBuffer())) : null,
  };
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

const application = (applicationId: string, ad: HarnessAd, withContacts = false) => ({
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
  preservedAd: withContacts ? { jobAdId: ad.id, title: ad.title, company: ad.company, location: null, url: summary(ad).url, source: "Platsbanken", publishedAt: PUBLISHED_AT, expiresAt: EXPIRES_AT, description: DESCRIPTION, contacts: [{ name: "Testkontakt", role: "Rekryterare", email: "contact@example.test", phone: null, isDerived: false }], capturedAt: PUBLISHED_AT } : null,
});

export async function startHarness(informationFlows = false, applicationCopies = 0, ports: { proxy: number; next: number; backend: number } = HARNESS_PORTS): Promise<Harness> {
  const misses: string[] = [];
  const requests: string[] = [];
  const syntheticUploadReceipts: boolean[] = [];
  const feedbackReceipts: FeedbackReceipt[] = [];
  const ads = [...ALL_ADS, ...Array.from({ length: applicationCopies }, (_, index) => ({
    ...ALL_ADS[1]!,
    id: `19860000-0000-4000-8000-${String(index + 1000).padStart(12, "0")}`,
    title: `Volume application ${index + 1}`,
    company: `Test company ${String(index + 1).padStart(3, "0")}`,
  }))];
  let applied = new Map<string, string>();
  let saved = new Set<string>();

  const adById = (adId: string) => ads.find((ad) => ad.id === adId);

  const harness: Harness = {
    occupationStated: true,
    jobAdReadGate: null,
    erasedAds: new Set(),
    unavailableApplications: new Set(),
    misses,
    requests,
    syntheticUploadReceipts,
    rejectNextImport: false,
    feedback: { open: false, answeredPages: [], answer: "saved" },
    feedbackReceipts,
    reset() {
      misses.length = 0;
      requests.length = 0;
      syntheticUploadReceipts.length = 0;
      feedbackReceipts.length = 0;
      harness.feedback.open = false;
      harness.feedback.answeredPages = [];
      harness.feedback.answer = "saved";
      harness.rejectNextImport = false;
      harness.occupationStated = true;
      harness.jobAdReadGate = null;
      harness.erasedAds.clear();
      harness.unavailableApplications.clear();
      applied = new Map(ads.filter((ad) => ad.appliedBeforeVisit).map((ad) => [ad.id, randomUUID()]));
      saved = new Set(ads.filter((ad) => ad.saved).map((ad) => ad.id));
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
    const url = new URL(request.url ?? "/", `http://localhost:${ports.backend}`);
    const route = `${request.method} ${url.pathname}`;
    requests.push(route);
    const pathId = url.pathname.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)?.[0] ?? "";
    const ad = adById(pathId);

    if (informationFlows && route === "POST /api/v1/auth/challenge") {
      const body = await bodyOf(request);
      if (body.email === "invalid") return json(400);
      return json(202, { challengeId: "information-harness-challenge" });
    }
    if (informationFlows && route === "POST /api/v1/auth/challenge/verify") {
      await bodyOf(request);
      return json(200, { outcome: "consentRequired", grantToken: "information-harness-grant" });
    }
    // #1975 — /adressbyte's completion, answered by the code typed: each code is one of the route's answers.
    if (informationFlows && route === "POST /api/v1/auth/account-email-change/complete") {
      const { code } = await bodyOf(request);
      if (code === ADDRESS_CHANGE_CODES.done) return json(204);
      if (code === ADDRESS_CHANGE_CODES.notYet) {
        return json(409, { title: "Auth.AccountEmailChangeNotYet", status: 409, completableFrom: "2026-10-08T12:00:00+00:00" });
      }
      if (code === ADDRESS_CHANGE_CODES.tooManyAttempts) return json(429, { title: "Too Many Requests", status: 429 });
      if (code === ADDRESS_CHANGE_CODES.unavailable) return json(503, { title: "Auth.EmailDeliveryUnavailable", status: 503 });
      if (code === ADDRESS_CHANGE_CODES.unknown) return json(500, { status: 500 });
      return json(410, { title: "Auth.AccountEmailChangeUnusable", status: 410 });
    }
    if (informationFlows && route === "POST /api/v1/resumes/import") {
      const chunks: Buffer[] = [];
      for await (const chunk of request) chunks.push(Buffer.from(chunk));
      syntheticUploadReceipts.push(Buffer.concat(chunks).includes(Buffer.from(SYNTHETIC_CV)));
      if (harness.rejectNextImport) {
        harness.rejectNextImport = false;
        return json(503, { title: "Synthetic import unavailable", status: 503 });
      }
      return json(200, { parsedResumeId: "19860000-0000-4000-8000-000000000001", personnummer: { found: true, count: 1, kinds: ["Personnummer"] }, outcome: "LeftPending", resumeId: null, blockReason: "PersonnummerPresent" });
    }
    if (route === "POST /api/v1/me/feedback") {
      const receipt = await feedbackOf(request);
      feedbackReceipts.push(receipt);
      const problem = (status: number, title: string) => json(status, { title, status });
      switch (harness.feedback.answer) {
        case "saved":
          harness.feedback.answeredPages.push(String(receipt.payload.page));
          return json(201, { id: randomUUID(), replayed: false });
        case "empty":
          return problem(400, "Feedback.Empty");
        case "screenshot":
          return problem(400, "Feedback.ScreenshotInvalid");
        case "closed":
          return problem(404, "Feedback.Closed");
        case "busy":
          return problem(409, "Feedback.ScreenshotBusy");
        case "tooLarge":
          return problem(413, "Payload Too Large");
        case "rateLimited":
          response.writeHead(429, { "Content-Type": "application/json", "Retry-After": "540" });
          return response.end(JSON.stringify({ title: "Too Many Requests", status: 429 }));
        case "unknown":
          return problem(500, "Internal Server Error");
      }
    }
    switch (route) {
      case "GET /api/v1/me/feedback/prompt-state":
        return json(200, { open: harness.feedback.open, answeredPages: harness.feedback.answeredPages });
      case "GET /api/v1/auth/oauth/providers":
        return json(200, []);
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
      case "GET /api/v1/job-ads/suggest":
        return json(200, []);
      case "GET /api/v1/me/profile":
        return json(200, profile(harness.occupationStated));
      case "GET /api/v1/job-ads": {
        const items = ads.map(summary);
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
          : ads.map((a) => a.id);
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
              ...application(applicationId, appliedAd, informationFlows),
              lastStatusChangeAt: PUBLISHED_AT,
              lastFollowUpAt: null,
              attentionSignal: "None",
              hasPreservedAdText: informationFlows,
            }] : [];
          }),
        }]);
    }

    if (ad && route === `GET /api/v1/job-ads/${ad.id}`) {
      await harness.jobAdReadGate;
      if (harness.erasedAds.has(ad.id)) return json(410, { title: "Gone", status: 410 });
      return json(200, { ...summary(ad), description: informationFlows ? Array.from({ length: 24 }, () => DESCRIPTION).join("\n\n") : DESCRIPTION, contacts: informationFlows ? [{ name: "Testkontakt", role: "Rekryterare", email: "contact@example.test", phone: null, isDerived: false }] : [] });
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
      if (ownerAd) return json(200, application(pathId, ownerAd, informationFlows));
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
