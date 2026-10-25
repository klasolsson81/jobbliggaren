import type { DmarcIngestion, DmarcLatestFailure, DmarcSource, EmailDmarcDto } from "@/lib/dto/admin-email";

/**
 * The DMARC read as the API sends it (#1981 PR 4b, ADR 0185 D9): every key present, a value a state has none of written
 * as null, instants as System.Text.Json writes a DateTimeOffset in UTC (seven decimals from the clock, six from the
 * database). Addresses are from the documentation ranges only (RFC 5737, RFC 3849).
 */
export const DMARC_TO = "2026-10-25T09:00:00.1234567+00:00";
export const DMARC_FROM = "2026-10-18T09:00:00.1234567+00:00";
export const DMARC_LAST_RUN = "2026-10-25T08:23:09.123456+00:00";

/** The longest canonical form a documentation-range address has without a dotted tail: 38 characters. */
export const LONG_IPV6 = "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff";

export function dmarcIngestion(overrides: Partial<DmarcIngestion> = {}): DmarcIngestion {
  return {
    lastAttemptAt: DMARC_LAST_RUN,
    lastSuccessAt: DMARC_LAST_RUN,
    latestFailure: null,
    signInResumesAt: null,
    stale: false,
    ...overrides,
  };
}

/** A latest run that failed after an earlier success, or before any when `succeeded` is null. */
export function dmarcFailure(
  latestFailure: DmarcLatestFailure,
  succeeded: string | null = "2026-10-25T06:23:09.123456+00:00",
): DmarcIngestion {
  const backsOff = latestFailure === "AuthRefused" || latestFailure === "AuthUnconfirmed";
  return dmarcIngestion({
    lastSuccessAt: succeeded,
    latestFailure,
    signInResumesAt: backsOff ? "2026-10-26T08:23:09.123456+00:00" : null,
    stale: true,
  });
}

export function dmarcSource(
  address: string,
  headerFrom: string,
  messages: number,
  dkimAligned: number,
  spfAligned: number,
  failingDmarc: number,
): DmarcSource {
  return { address, headerFrom, messages, dkimAligned, spfAligned, failingDmarc, flagged: failingDmarc > 0 };
}

/** Three sources in the API's order: a failing IPv4 source, one passing on DKIM only, one on SPF only. */
export const DMARC_SOURCES: ReadonlyArray<DmarcSource> = [
  dmarcSource("198.51.100.7", "jobbliggaren.se", 3, 0, 0, 3),
  dmarcSource("192.0.2.10", "mail.jobbliggaren.se", 40, 40, 0, 0),
  dmarcSource(LONG_IPV6, "jobbliggaren.se", 8, 0, 8, 0),
];

type Observed = Extract<EmailDmarcDto, { status: "Observed" }>;
type NoReports = Extract<EmailDmarcDto, { status: "NoReports" }>;

export function observedDmarc(overrides: Partial<Observed> = {}): Observed {
  return {
    status: "Observed",
    ingestion: dmarcIngestion(),
    from: DMARC_FROM,
    to: DMARC_TO,
    coveredFrom: "2026-10-23T00:00:00+00:00",
    coveredTo: "2026-10-24T00:00:00+00:00",
    conflictingReports: 0,
    mailbox: { skipped: 0, rejected: 0, volumeCapped: 0, abandoned: 0, retryPending: 0 },
    reports: 2,
    reporters: 1,
    messages: 51,
    failingMessages: 3,
    sourceCount: 3,
    sources: [...DMARC_SOURCES],
    ...overrides,
  };
}

export function noReportsDmarc(overrides: Partial<NoReports> = {}): NoReports {
  return {
    status: "NoReports",
    ingestion: dmarcIngestion(),
    from: DMARC_FROM,
    to: DMARC_TO,
    coveredFrom: null,
    coveredTo: null,
    conflictingReports: 0,
    mailbox: { skipped: 0, rejected: 0, volumeCapped: 0, abandoned: 0, retryPending: 0 },
    reports: null,
    reporters: null,
    messages: null,
    failingMessages: null,
    sourceCount: null,
    sources: null,
    ...overrides,
  };
}

const NO_READ = {
  ingestion: null,
  from: null,
  to: null,
  coveredFrom: null,
  coveredTo: null,
  conflictingReports: null,
  mailbox: null,
  reports: null,
  reporters: null,
  messages: null,
  failingMessages: null,
  sourceCount: null,
  sources: null,
} as const;

export function unreadDmarc(status: "NotObservable" | "NotConfigured" | "Misconfigured"): EmailDmarcDto {
  return { status, ...NO_READ };
}

/** A mailbox never read successfully; the latest run's outcome says why. */
export function failedDmarc(latestFailure: DmarcLatestFailure): EmailDmarcDto {
  return { status: "Failed", ...NO_READ, ingestion: dmarcFailure(latestFailure, null) };
}

/** The cap: 25 of `total` sources, the shown ones failing first, every address at IPv6 length. */
export function cappedDmarc(total = 31): Observed {
  const sources = Array.from({ length: 25 }, (_, index) => {
    const failing = index < 3 ? 3 - index : 0;
    return dmarcSource(`2001:db8:ffff:ffff:ffff:ffff:ffff:ff${String(index + 10).padStart(2, "0")}`, "jobbliggaren.se", 100 - index, 100 - index - failing, 0, failing);
  });
  const shownMessages = sources.reduce((sum, source) => sum + source.messages, 0);
  return observedDmarc({
    sources,
    sourceCount: total,
    messages: shownMessages + (total - 25),
    failingMessages: 6,
    reports: 9,
    reporters: 3,
  });
}
