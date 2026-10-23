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

/**
 * What the host sampler last published, as the Backup status read answers it (#1982). `switchedOff` is the
 * state the box is in until the backup is: no stamp, a disabled timer.
 */
export type BackupScenario = "observed" | "switchedOff" | "notObserved" | "failed" | "old" | "overdue" | "timerUnknown";

const HOUR = 3_600_000;
function backupAnswer(scenario: BackupScenario): unknown {
  // The API's wire form for a DateTimeOffset in UTC: an offset, never a Z.
  const at = (offset: number) => new Date(Date.now() + offset).toISOString().replace(/\.\d{3}Z$/, "+00:00");
  const nothing = { stale: null, lastSuccess: null, timer: null };
  const recorded = (hoursAgo: number, overdue: boolean) =>
    ({ state: "Recorded", completedAt: at(-hoursAgo * HOUR), overdue });
  const scheduled = { state: "Scheduled", nextRunAt: at(14 * HOUR) };
  const observed = { status: "Observed", reason: null };
  switch (scenario) {
    case "observed": return { ...observed, observedAt: at(-20_000), stale: false, lastSuccess: recorded(10, false), timer: scheduled };
    case "switchedOff": return { ...observed, observedAt: at(-20_000), stale: false,
      lastSuccess: { state: "Missing", completedAt: null, overdue: null }, timer: { state: "Inactive", nextRunAt: null } };
    case "notObserved": return { status: "NotObserved", reason: "NotSampledYet", observedAt: null, ...nothing };
    case "failed": return { status: "Failed", reason: "InvalidFormat", observedAt: null, ...nothing };
    case "old": return { ...observed, observedAt: at(-10 * 60_000), stale: true, lastSuccess: recorded(10, false), timer: scheduled };
    case "overdue": return { ...observed, observedAt: at(-20_000), stale: false, lastSuccess: recorded(30, true), timer: scheduled };
    case "timerUnknown": return { ...observed, observedAt: at(-20_000), stale: false, lastSuccess: recorded(10, false),
      timer: { state: "Unknown", nextRunAt: null } };
  }
}

/**
 * What the Server card's host read answers (#1982), as the API states it. The numbers are fictional (a 200 GiB disk,
 * 8 GiB of memory). `unmeasured` is a /proc that does not describe the host: CPU and memory are not observable
 * here and the disk, which does not depend on /proc, stands.
 */
export type HostScenario = "measured" | "collecting" | "unmeasured" | "failedDisk" | "stale" | "idle" | "saturated";

function hostAnswer(scenario: HostScenario): unknown {
  const at = (offset: number) => new Date(Date.now() + offset).toISOString();
  const sampledAt = scenario === "stale" ? at(-5 * 60_000) : at(-20_000);
  const state = scenario === "stale" ? "Stale" : "Available";
  const none = { sampledAt: null, value: null };
  const reading = (value: unknown) => ({ state, sampledAt, value });
  const memory = scenario === "saturated"
    ? { percent: 100, usedBytes: 8_589_934_592, totalBytes: 8_589_934_592 }
    : { percent: 61.1, usedBytes: 5_244_620_800, totalBytes: 8_589_934_592 };
  const disk = scenario === "saturated"
    ? { percent: 99.9, freeBytes: 214_748_364, totalBytes: 214_748_364_800 }
    : { percent: 38, freeBytes: 133_143_986_176, totalBytes: 214_748_364_800 };
  const cpu = { percent: scenario === "idle" ? 0 : scenario === "saturated" ? 100 : 23.4, windowSeconds: 30 };
  return {
    staleAfterSeconds: 120,
    cpu: scenario === "collecting" ? { state: "Collecting", ...none }
      : scenario === "unmeasured" ? { state: "NotObservable", ...none } : reading(cpu),
    memory: scenario === "unmeasured" ? { state: "NotObservable", ...none } : reading(memory),
    disk: scenario === "failedDisk" ? { state: "Failed", ...none } : reading(disk),
  };
}

/**
 * What the Tjänster card's services read answers (#1982), as the API states it: each row its own state, the instants
 * it rests on and nothing else. `observed` is a live Worker, a store that answers and a clean import run; each other
 * scenario turns one row, and `warnings` turns all three at once (a silent Worker, a store that does not answer and an
 * overdue run with errors). `unknown` is a sampler that could judge nothing.
 */
export type ServicesScenario =
  | "observed" | "silent" | "noServer" | "unreachable" | "overdue" | "noneRecorded" | "errors" | "unknown" | "warnings";

function servicesAnswer(scenario: ServicesScenario): unknown {
  // The API's wire form for a DateTimeOffset in UTC: an offset, never a Z.
  const at = (offset: number) => new Date(Date.now() + offset).toISOString().replace(/\.\d{3}Z$/, "+00:00");
  const sampledAt = at(-20_000);
  const turns = (row: ServicesScenario) =>
    scenario === row || (scenario === "warnings" && (row === "silent" || row === "unreachable" || row === "overdue"));
  const backgroundJobs = scenario === "unknown" ? { state: "Unknown", sampledAt: null, heartbeatAt: null }
    : turns("silent") ? { state: "Silent", sampledAt, heartbeatAt: at(-10 * 60_000) }
    : turns("noServer") ? { state: "NoServer", sampledAt, heartbeatAt: null }
    : { state: "Alive", sampledAt, heartbeatAt: at(-30_000) };
  const volatileStore = scenario === "unknown" ? { state: "Unknown", sampledAt: null }
    : turns("unreachable") ? { state: "Unreachable", sampledAt } : { state: "Reachable", sampledAt };
  const jobImport = scenario === "unknown" ? { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null }
    : turns("overdue") ? { state: "Overdue", sampledAt, lastRunAt: at(-40 * 60_000), errors: scenario === "warnings" ? 2 : 0 }
    : turns("noneRecorded") ? { state: "NoneRecorded", sampledAt, lastRunAt: null, errors: null }
    : { state: "Recorded", sampledAt, lastRunAt: at(-5 * 60_000), errors: scenario === "errors" ? 3 : 0 };
  return {
    staleAfterSeconds: 120, workerAliveWithinSeconds: 120, importOverdueAfterSeconds: 1500,
    backgroundJobs, volatileStore, jobImport,
  };
}

/**
 * What the email allowance read answers (#1981 PR 1, ADR 0178), as the API states it. `observed` is a month within the
 * free allowance under the configured plan; `notConfigured` is a deployment without a provider to ask; `providerFailed`
 * is a count the provider did not answer; `planDiffers` is a count under another plan, so no cost is computed;
 * `readKeyMissing` is a Scaleway deployment whose read key is not installed (#1981 PR 2).
 */
export type EmailScenario = "observed" | "notConfigured" | "providerFailed" | "planDiffers" | "readKeyMissing";

function emailAnswer(scenario: EmailScenario): unknown {
  // The API's wire form for a DateTimeOffset in UTC: an offset, never a Z.
  const at = (offset: number) => new Date(Date.now() + offset).toISOString().replace(/\.\d{3}Z$/, "+00:00");
  const terms = {
    plan: "Essential", includedMonthlyEmails: 300, pricePerThousandExtra: 0.25, currency: "EUR",
    priceReadOn: "2026-10-18", monthlySendLimit: 10000, costCurrency: "SEK", sekPerEur: 11.145, exchangeRateOn: "2026-10-16",
  };
  const noUsage = {
    observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostOre: null,
    aboveHalfOfSendLimit: null, webhooksCount: null,
  };
  const inForce = { status: "Observed", reason: null, active: "Essential", matches: true };
  const usage = (termsApply: boolean) => ({
    status: "Observed", reason: null, observedAt: at(-20_000), month: new Date().toISOString().slice(0, 7), sent: 142,
    billable: termsApply ? 0 : null, estimatedExtraCostOre: termsApply ? 0 : null, aboveHalfOfSendLimit: false, webhooksCount: 0,
  });
  switch (scenario) {
    case "observed": return { terms, usage: usage(true), plan: inForce };
    case "notConfigured": return {
      terms,
      usage: { status: "NotObserved", reason: "NotConfigured", ...noUsage },
      plan: { status: "NotObserved", reason: "NotConfigured", active: null, matches: null },
    };
    case "providerFailed": return { terms, usage: { status: "Failed", reason: "Timeout", ...noUsage }, plan: inForce };
    case "planDiffers": return {
      terms, usage: usage(false), plan: { status: "Observed", reason: null, active: "Scale", matches: false },
    };
    case "readKeyMissing": return {
      terms,
      usage: { status: "ReadKeyMissing", reason: null, ...noUsage },
      plan: { status: "ReadKeyMissing", reason: null, active: null, matches: null },
    };
  }
}

/**
 * What the delivery read answers (#1981 PR 2), as the API states it. `observed` counts failed sends in every period;
 * `noneFailed` counts none, and `nothingSent` no send at all; the three states without figures follow; the rest are
 * the seven reasons a provider read fails for. The failures read answers the same state, so one scenario sets both, unless `emailFailures` says
 * otherwise.
 */
export type DeliveryScenario =
  | "observed" | "noneFailed" | "nothingSent" | "notConfigured" | "notObservable" | "readKeyMissing"
  | "Unauthorized" | "Forbidden" | "RateLimited" | "ProviderError" | "Timeout" | "Unreachable" | "Malformed";

/**
 * What the failures read answers when it differs from the delivery read: `capped` is as many rows as one read carries,
 * one with each of the seven reasons, one at a long domain and one in each recipient form; `unjudged` is a list the API
 * could not judge; `providerId` is the observed list with the provider's id on each row, which is outside the contract.
 */
export type FailuresScenario = DeliveryScenario | "capped" | "unjudged" | "providerId";

/** The id the `providerId` scenario puts on its rows, so a test can look for it in what reached the page. */
export const PROVIDER_EMAIL_ID = "6f2a1d3e-90b4-4c1a-8e5f-1981deadbeef";

const EMAIL_PERIODS = ["h24", "d3", "d7"] as const;
type EmailPeriod = (typeof EMAIL_PERIODS)[number];
const PERIOD_COUNTS: Readonly<Record<EmailPeriod, { readonly sent: number; readonly failed: number }>> = {
  h24: { sent: 57, failed: 2 },
  d3: { sent: 163, failed: 3 },
  d7: { sent: 384, failed: 5 },
};
/** Every address here is masked as the API masks it, on a domain RFC 2606/6761 reserves. */
const LONG_DOMAIN = "post.ett-mycket-langt-underdomannamn-for-smala-skarmar.example.test";

function unansweredEmail(scenario: DeliveryScenario): { status: string; reason: string | null } | null {
  switch (scenario) {
    case "observed":
    case "noneFailed":
    case "nothingSent":
      return null;
    case "notConfigured": return { status: "NotObserved", reason: "NotConfigured" };
    case "notObservable": return { status: "NotObservable", reason: null };
    case "readKeyMissing": return { status: "ReadKeyMissing", reason: null };
    default: return { status: "Failed", reason: scenario };
  }
}

function deliveryAnswer(scenario: DeliveryScenario, period: EmailPeriod): unknown {
  const at = (offset: number) => new Date(Date.now() + offset).toISOString().replace(/\.\d{3}Z$/, "+00:00");
  const unanswered = unansweredEmail(scenario);
  if (unanswered !== null) return { period, ...unanswered, observedAt: null, sent: null, failed: null };
  const counts = PERIOD_COUNTS[period];
  return { period, status: "Observed", reason: null, observedAt: at(-20_000), sent: scenario === "nothingSent" ? 0 : counts.sent,
    failed: scenario === "observed" ? counts.failed : 0 };
}

function failuresAnswer(scenario: FailuresScenario, period: EmailPeriod): unknown {
  const at = (minutesAgo: number) => new Date(Date.now() - minutesAgo * 60_000).toISOString().replace(/\.\d{3}Z$/, "+00:00");
  const observed = (failures: ReadonlyArray<unknown>) =>
    ({ period, status: "Observed", reason: null, observedAt: at(0.3), failures, limit: 10 });
  // Newest first, each inside the periods it is listed for: two in 24 hours, three in 3 days and five in 7 days.
  const rows = [
    { createdAt: at(80), recipient: "k•••@example.com", reason: "MailboxNotFound" },
    { createdAt: at(300), recipient: "•••", reason: "SoftBounce" },
    { createdAt: at(60 * 40), recipient: "a•••@example.org", reason: "HardBounce" },
    { createdAt: at(60 * 100), recipient: `p•••@${LONG_DOMAIN}`, reason: "MailboxFull" },
    { createdAt: at(60 * 150), recipient: "m•••@konto.test", reason: "Blocklisted" },
  ].slice(0, PERIOD_COUNTS[period].failed);
  if (scenario === "unjudged") return { period, status: "NotObservable", reason: null, observedAt: null, failures: null, limit: 10 };
  if (scenario === "providerId") return observed(rows.map((row) => ({ ...row, id: PROVIDER_EMAIL_ID })));
  if (scenario === "capped") {
    const reasons = ["MailboxNotFound", "HardBounce", "MailboxFull", "SoftBounce", "Spam", "Blocklisted", "Other",
      "HardBounce", "MailboxNotFound", "SoftBounce"];
    return observed(reasons.map((reason, index) => ({
      createdAt: at(12 + index * 47),
      recipient: index === 1 ? `p•••@${LONG_DOMAIN}` : index === 7 ? "•••" : index === 9 ? "•••@example.org"
        : `${"abcdefghij"[index]}•••@example.org`,
      reason,
    })));
  }
  const unanswered = unansweredEmail(scenario);
  if (unanswered !== null) return { period, ...unanswered, observedAt: null, failures: null, limit: 10 };
  if (scenario === "noneFailed" || scenario === "nothingSent") return observed([]);
  return observed(rows);
}

/**
 * What the per-kind read answers (#1981 PR 3), as the API states it: our own count of handovers per mail kind.
 * `observed` has failures on three days' worth of kinds, one day with two classes and every class somewhere;
 * `noneFailed` counts no failure, and `nothingSent` no handover; `partial` is a count that began five hours ago;
 * `notConfigured` a deployment without Scaleway, and `notObservable` a count with no sends while the provider sent.
 */
export type KindsScenario = "observed" | "noneFailed" | "nothingSent" | "partial" | "notConfigured" | "notObservable";

const EMAIL_KINDS = [
  "login-challenge", "account-email-change-requested-notification", "email-changed-notification", "match-notification",
  "followed-company-notification", "feedback-received-notification", "feedback-reply",
] as const;
const PERIOD_HOURS: Readonly<Record<EmailPeriod, number>> = { h24: 24, d3: 72, d7: 168 };
const KIND_HANDOVERS = [12, 3, 2, 35, 8, 4, 1] as const;

/** The Swedish civil date an instant falls on, as the API's per-kind read dates its days. */
const swedishDate = (ms: number) => new Intl.DateTimeFormat("sv-SE", { timeZone: "Europe/Stockholm" }).format(ms);

function kindsAnswer(scenario: KindsScenario, period: EmailPeriod): unknown {
  const now = Date.now();
  const from = swedishDate(now - PERIOD_HOURS[period] * 3_600_000);
  const today = swedishDate(now);
  const none = { coverage: null, coveredFrom: null, kinds: null };
  if (scenario === "notConfigured") return { period, status: "NotObserved", from: null, ...none };
  if (scenario === "notObservable") return { period, status: "NotObservable", from, ...none };
  // Each failing kind's latest day lies inside what was counted: today, or the period's first day where the count
  // spans the whole period.
  const earliest = scenario === "partial" ? today : from;
  const failures: Readonly<Record<string, { failed: number; day: string; outcomes: string[] }>> = scenario === "observed" || scenario === "partial"
    ? {
      "login-challenge": { failed: 2, day: today, outcomes: ["Refused", "Timeout"] },
      "email-changed-notification": { failed: 1, day: earliest, outcomes: ["RateLimited"] },
      "match-notification": { failed: 3, day: today, outcomes: ["Unreachable", "ProviderError"] },
      "feedback-received-notification": { failed: 1, day: earliest, outcomes: ["Other"] },
    }
    : {};
  const scale = PERIOD_HOURS[period] / 24;
  const kinds = EMAIL_KINDS.map((kind, index) => {
    const failure = failures[kind];
    return {
      kind,
      accepted: scenario === "nothingSent" ? 0 : (KIND_HANDOVERS[index] ?? 0) * scale,
      failed: failure?.failed ?? 0,
      latestFailure: failure === undefined ? null : { day: failure.day, outcomes: failure.outcomes },
    };
  });
  return scenario === "partial"
    ? { period, status: "Observed", from, coverage: "Partial",
      // A timestamptz as the API writes it: six decimals of a second and an offset.
      coveredFrom: new Date(now - 5 * 3_600_000).toISOString().replace(/Z$/, "456+00:00"), kinds }
    : { period, status: "Observed", from, coverage: "Complete", coveredFrom: null, kinds };
}

/** The four reads the feedback page makes side by side (#1979). */
export type FeedbackRead = "list" | "detail" | "summary" | "availability";
export type FeedbackScreenshotRead = "ok" | "notFound" | "error" | "loading";

const FEEDBACK_READS: ReadonlyArray<FeedbackRead> = ["list", "detail", "summary", "availability"];
export type AccessMode = "ok" | "forbidden" | "rateLimited" | "unauthorized" | "unknown" | "unknownAfterCommit";

export type Harness = {
  /** `stale` answers `/api/v1/me` 401, as a session the backend has ended does (#1979's deep link). */
  who: "admin" | "member" | "stale";
  mode: AdminMode;
  readonly overviewReads: Record<
    "accounts" | "audit" | "jobs" | "backup" | "host" | "services" | "email" | "delivery" | "failures" | "kinds",
    AdminMode
  >;
  overviewBackup: BackupScenario;
  /** What the allowance read answers, for the overview's e-post card and the e-post page alike (#1981). */
  overviewEmail: EmailScenario;
  /**
   * What the delivery read answers, for the overview's attention line and the e-post page alike (#1981 PR 2). Null is
   * the default: failed sends counted, or none while `overviewEmpty` holds, since an empty overview needs no attention.
   */
  emailDelivery: DeliveryScenario | null;
  /** What the failures read answers; null answers the delivery read's own state. */
  emailFailures: FailuresScenario | null;
  /** What the per-kind read answers (#1981 PR 3); our own count, so it never follows the delivery read. */
  emailKinds: KindsScenario;
  /** How long the delivery, failures and per-kind reads take to answer, for a period change seen mid-flight. */
  emailDelay: number;
  /** How long the per-kind read alone takes to answer, beyond `emailDelay`. */
  kindsDelay: number;
  /** Every delivery, failures and per-kind read's path and query string, as the backend received them. */
  readonly emailQueries: string[];
  /** Every body the failures read answered, so a test can look for what must never reach the page. */
  readonly failuresAnswers: unknown[];
  overviewHost: HostScenario;
  overviewServices: ServicesScenario;
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
  /** Every status change's, requeue's and reply's body, as the backend received them. */
  readonly feedbackCommands: {
    readonly id: string;
    readonly command: "status" | "requeue" | "reply";
    readonly body: string;
  }[];
  /** The outcome the provider records for the next replies (#2069). */
  feedbackReplyOutcome: "Accepted" | "Failed" | "Unknown";
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
  const emailQueries: string[] = [];
  const failuresAnswers: unknown[] = [];
  const feedbackCommands: Harness["feedbackCommands"] = [];
  const pendingCodeRequests: (() => void)[] = [];
  const pendingAccessWrites: (() => void)[] = [];
  const pendingAccountDetails: (() => void)[] = [];
  const pendingFeedbackScreenshots: (() => void)[] = [];
  let issuedGrants = 0;

  const harness: Harness = {
    who: "admin",
    mode: "ok",
    overviewReads: {
      accounts: "ok", audit: "ok", jobs: "ok", backup: "ok", host: "ok", services: "ok", email: "ok", delivery: "ok", failures: "ok",
      kinds: "ok",
    },
    overviewBackup: "observed",
    overviewEmail: "observed",
    emailDelivery: null,
    emailFailures: null,
    emailKinds: "observed",
    emailDelay: 0,
    kindsDelay: 0,
    emailQueries,
    failuresAnswers,
    overviewHost: "measured",
    overviewServices: "observed",
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
    feedbackReplyOutcome: "Accepted",
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
      harness.overviewReads.accounts = harness.overviewReads.audit = harness.overviewReads.jobs = harness.overviewReads.backup = "ok";
      harness.overviewReads.host = harness.overviewReads.services = harness.overviewReads.email = "ok";
      harness.overviewReads.delivery = harness.overviewReads.failures = harness.overviewReads.kinds = "ok";
      harness.overviewBackup = "observed";
      harness.overviewEmail = "observed";
      harness.emailDelivery = null;
      harness.emailFailures = null;
      harness.emailKinds = "observed";
      harness.emailDelay = 0;
      harness.kindsDelay = 0;
      emailQueries.length = 0;
      failuresAnswers.length = 0;
      harness.overviewHost = "measured";
      harness.overviewServices = "observed";
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
      harness.feedbackReplyOutcome = "Accepted";
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
    // #1981 PR 2 and PR 3: the three Leverans reads take one of three periods, and the API refuses any other with a 400.
    const isEmailRead = route === "GET /api/v1/admin/email/delivery" || route === "GET /api/v1/admin/email/failures"
      || route === "GET /api/v1/admin/email/kinds";
    if (isEmailRead) emailQueries.push(`${url.pathname}${url.search}`);
    const emailPeriod = EMAIL_PERIODS.find((period) => period === url.searchParams.get("period")) ?? null;
    const emailScenario = (): DeliveryScenario =>
      harness.emailDelivery ?? (harness.overviewEmpty ? "noneFailed" : "observed");
    if (isEmailRead && (emailPeriod === null || url.searchParams.getAll("period").length !== 1)) {
      return json(400, { title: "One or more validation errors occurred.", status: 400, errors: { period: ["Okänd period."] } },
        { "Cache-Control": "private, no-store" });
    }

    if (route === "GET /api/v1/me") {
      if (harness.who === "stale") return json(401, { title: "Unauthorized", status: 401 });
      return json(200, harness.who === "admin" ? ADMIN : MEMBER);
    }
    if (route === "POST /api/v1/auth/refresh") return json(200, { rotated: false, sessionId: null });
    // AuthEndpoints returns an empty provider list when this host has no OAuth keys.
    if (route === "GET /api/v1/auth/oauth/providers") return json(200, []);
    // Every (app) page's layout reads it (#1979); closed, so the pages render without the feedback row.
    if (route === "GET /api/v1/me/feedback/prompt-state") return json(200, { open: false, answeredPages: [] });
    // #1666 — the shell's bell polls its feed on every signed-in page, the admin pages included.
    if (route === "GET /api/v1/me/notification-feed")
      return json(200, {
        items: [],
        sources: [
          { kind: "Match", unreadCount: 0, acknowledgedThrough: null },
          { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: null },
        ],
      });
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
      "GET /api/v1/admin/overview/backup": () => backupAnswer(harness.overviewBackup),
      "GET /api/v1/admin/overview/host": () => hostAnswer(harness.overviewHost),
      "GET /api/v1/admin/overview/services": () => servicesAnswer(harness.overviewServices),
      "GET /api/v1/admin/email/allowance": () => emailAnswer(harness.overviewEmail),
      // The period is checked above, before these answer: an invalid one is the API's 400. The overview's "empty"
      // sources count no failed sends either.
      "GET /api/v1/admin/email/delivery": () => deliveryAnswer(emailScenario(), emailPeriod ?? "d7"),
      "GET /api/v1/admin/email/failures": () => {
        const body = failuresAnswer(harness.emailFailures ?? emailScenario(), emailPeriod ?? "d7");
        failuresAnswers.push(body);
        return body;
      },
      "GET /api/v1/admin/email/kinds": () => kindsAnswer(harness.emailKinds, emailPeriod ?? "d7"),
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
    const feedbackCommand = /^POST \/api\/v1\/admin\/feedback\/([0-9a-f-]{36})\/(status|notification\/requeue|replies)$/.exec(
      route,
    );
    if (feedbackCommand !== null) {
      const feedbackId = feedbackCommand[1] ?? "";
      const command = feedbackCommand[2] === "status" ? "status" : feedbackCommand[2] === "replies" ? "reply" : "requeue";
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
        // SendFeedbackReplyCommandHandler's rules (#2069): a known key replays its outcome, an empty text is refused,
        // ten replies are the most, and every other send is recorded with the provider's outcome.
        if (command === "reply") {
          const { replyKey, text } = JSON.parse(body) as { replyKey?: unknown; text?: unknown };
          const replies = found.replies ?? [];
          const earlier = replies.find((reply) => reply.key === replyKey);
          const outcome = (reply: { id: string; state: string }, replayed: boolean) =>
            json(200, { replyId: reply.id, state: reply.state, replayed }, { "Cache-Control": "private, no-store" });
          if (earlier !== undefined) return outcome(earlier, true);
          if (typeof replyKey !== "string" || typeof text !== "string" || text.trim() === "")
            return problem(400, "Feedback.ReplyEmpty");
          if (replies.length >= 10) return problem(409, "Feedback.ReplyLimitReached");
          const reply = {
            id: `00000000-0000-4000-8000-${String(800 + replies.length).padStart(12, "0")}`,
            key: replyKey,
            text: text.trim(),
            state: harness.feedbackReplyOutcome,
            createdAt: FEEDBACK_NOW,
            completedAt: FEEDBACK_NOW,
          };
          harness.feedback.set(feedbackId, { ...found, replies: [...replies, reply] });
          return outcome(reply, false);
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
        : route.endsWith("/audit-log") ? "audit" : route.endsWith("/jobs/failed") ? "jobs"
        : route.endsWith("/overview/backup") ? "backup"
        : route.endsWith("/overview/host") ? "host"
        : route.endsWith("/overview/services") ? "services"
        : route.endsWith("/email/allowance") ? "email"
        : route.endsWith("/email/delivery") ? "delivery"
        : route.endsWith("/email/failures") ? "failures"
        : route.endsWith("/email/kinds") ? "kinds" : null;
      const mode = harness.mode === "ok" && source !== null ? harness.overviewReads[source] : harness.mode;
      const respond = () => {
        if (mode !== "ok") return refusal(mode);
        return json(200, answer(), {
          "Cache-Control": "private, no-store",
          ...(source !== null && harness.overviewSampledAt !== null ? { "X-Admin-Sampled-At": harness.overviewSampledAt } : {}),
        });
      };
      const emailWait = harness.emailDelay + (source === "kinds" ? harness.kindsDelay : 0);
      const delay = isEmailRead && emailWait > 0 ? emailWait : source !== null ? harness.overviewDelayMs : 0;
      if (delay > 0) {
        const timer = setTimeout(respond, delay);
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
