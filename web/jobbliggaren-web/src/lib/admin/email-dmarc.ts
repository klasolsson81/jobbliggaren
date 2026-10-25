import type { DmarcLatestFailure, EmailDmarcDto } from "@/lib/dto/admin-email";
import type { AdminEmailRead } from "./view-models";

/**
 * #1981 PR 4b (ADR 0185 D9, ADR 0150 Amendment (4)) — what the DMARC region says, from one read. Pure: no React, no
 * clock, no copy. Staleness and the next sign-in are the backend's; the browser never re-derives either.
 */

/** A line under the summary. At most one in any state carries a role; report content never raises one. */
export type DmarcLine =
  | { readonly kind: "loading"; readonly announce: "status" }
  | { readonly kind: "failed"; readonly refusal: string | null; readonly announce: "alert" }
  | { readonly kind: "notObservable"; readonly announce: null }
  | { readonly kind: "notConfigured"; readonly announce: null }
  | { readonly kind: "misconfigured"; readonly announce: "alert" }
  | {
      readonly kind: "failure";
      readonly failure: DmarcLatestFailure;
      /** When the unfinished first run began; only for `Incomplete`. */
      readonly startedAt: string | null;
      /** The earliest next sign-in, exactly as the API gives it. */
      readonly resumesAt: string | null;
      readonly announce: "alert";
    }
  | { readonly kind: "stale"; readonly since: string; readonly announce: "alert" }
  | { readonly kind: "noReports"; readonly announce: null }
  | { readonly kind: "conflicting"; readonly count: number; readonly announce: null }
  | {
      readonly kind: "mailbox";
      readonly which: "unread" | "volumeCapped" | "abandoned" | "retryPending";
      readonly count: number;
      readonly announce: null;
    };

export interface DmarcRow {
  readonly key: string;
  readonly address: string;
  /** The address split after each ":" that does not begin "::", where a line may break; joined, it is the address. */
  readonly addressParts: ReadonlyArray<string>;
  readonly headerFrom: string;
  /** The address heads another shown row too, so the row's name also carries its domain. */
  readonly sharesAddress: boolean;
  readonly messages: number;
  readonly dkimAligned: number;
  readonly spfAligned: number;
  readonly failingDmarc: number;
}

export interface DmarcNote {
  readonly reports: number;
  /** The covered span, each an instant the `<time>` element can carry. */
  readonly from: string;
  readonly to: string;
  /** The latest success, or null where the stale line already says when that was. */
  readonly fetchedAt: string | null;
}

export interface DmarcView {
  readonly figures: { readonly messages: number; readonly failing: number } | null;
  readonly lines: ReadonlyArray<DmarcLine>;
  readonly announced: DmarcLine | null;
  readonly note: DmarcNote | null;
  readonly rows: ReadonlyArray<DmarcRow> | null;
  /** The table's one row when it has none of its own: still loading, or "Uppgift saknas" when a line gives the cause. */
  readonly emptyRow: "loading" | "unknown" | null;
  readonly cap: { readonly shown: number; readonly total: number } | null;
}

const EMPTY = { figures: null, note: null, rows: null, cap: null } as const;

/** An instant in the form `<time dateTime>` takes: the API writes up to seven decimals of a second. */
function instant(value: string): string {
  return new Date(value).toISOString();
}

export function dmarcAddressParts(address: string): ReadonlyArray<string> {
  return address.split(/(?<=:)(?!:)/);
}

export function dmarcView(read: AdminEmailRead<EmailDmarcDto>): DmarcView {
  if (read.kind === "loading") return { ...EMPTY, lines: [LOADING], announced: LOADING, emptyRow: "loading" };
  if (read.kind === "failed") {
    const failed: DmarcLine = { kind: "failed", refusal: read.line ?? null, announce: "alert" };
    return { ...EMPTY, lines: [failed], announced: failed, emptyRow: "unknown" };
  }
  const dto = read.data;
  switch (dto.status) {
    case "NotObservable":
      return quiet({ kind: "notObservable", announce: null });
    case "NotConfigured":
      return quiet({ kind: "notConfigured", announce: null });
    case "Misconfigured":
      return quiet({ kind: "misconfigured", announce: "alert" });
    case "Failed":
      // The schema admits Failed only with a latest failure; anything else reads as our own failed read.
      return quiet(causeOf(dto.ingestion) ?? { kind: "failed", refusal: null, announce: "alert" });
    case "NoReports": {
      const cause = causeOf(dto.ingestion);
      // "Ingen rapport täcker …" only where nothing else explains the absence: a conflict is still a report.
      const lead = cause ?? (dto.conflictingReports > 0 ? null : ({ kind: "noReports", announce: null } as const));
      const lines = [...(lead === null ? [] : [lead]), ...countLines(dto)];
      return { ...EMPTY, lines, announced: cause, emptyRow: "unknown" };
    }
    case "Observed": {
      const cause = causeOf(dto.ingestion);
      const rows = rowsOf(dto.sources);
      return {
        figures: { messages: dto.messages, failing: dto.failingMessages },
        lines: [...(cause === null ? [] : [cause]), ...countLines(dto)],
        announced: cause,
        note: {
          reports: dto.reports,
          from: instant(dto.coveredFrom),
          to: instant(dto.coveredTo),
          fetchedAt: cause?.kind === "stale" || dto.ingestion.lastSuccessAt === null ? null : instant(dto.ingestion.lastSuccessAt),
        },
        rows,
        emptyRow: null,
        cap: dto.sourceCount > rows.length ? { shown: rows.length, total: dto.sourceCount } : null,
      };
    }
  }
}

const LOADING: DmarcLine = { kind: "loading", announce: "status" };

function quiet(line: DmarcLine): DmarcView {
  return { ...EMPTY, lines: [line], announced: line.announce === null ? null : line, emptyRow: "unknown" };
}

/** The read's own cause: the latest run's failure first, then a success older than the backend's threshold. */
function causeOf(ingestion: Extract<EmailDmarcDto, { status: "Failed" }>["ingestion"]): DmarcLine | null {
  if (ingestion.latestFailure !== null) {
    return {
      kind: "failure",
      failure: ingestion.latestFailure,
      startedAt: ingestion.latestFailure === "Incomplete" ? instant(ingestion.lastAttemptAt) : null,
      resumesAt: ingestion.signInResumesAt === null ? null : instant(ingestion.signInResumesAt),
      announce: "alert",
    };
  }
  if (ingestion.stale && ingestion.lastSuccessAt !== null) {
    return { kind: "stale", since: instant(ingestion.lastSuccessAt), announce: "alert" };
  }
  return null;
}

/** What removes reports from the figures before what heals itself (design-reviewer, plan read r2, Minor 4). */
function countLines(dto: Extract<EmailDmarcDto, { status: "Observed" | "NoReports" }>): ReadonlyArray<DmarcLine> {
  const { mailbox } = dto;
  const lines: DmarcLine[] = [];
  if (dto.conflictingReports > 0) lines.push({ kind: "conflicting", count: dto.conflictingReports, announce: null });
  const counts = [
    ["unread", mailbox.skipped + mailbox.rejected],
    ["volumeCapped", mailbox.volumeCapped],
    ["abandoned", mailbox.abandoned],
    ["retryPending", mailbox.retryPending],
  ] as const;
  for (const [which, count] of counts) {
    if (count > 0) lines.push({ kind: "mailbox", which, count, announce: null });
  }
  return lines;
}

function rowsOf(sources: Extract<EmailDmarcDto, { status: "Observed" }>["sources"]): ReadonlyArray<DmarcRow> {
  const seen = new Map<string, number>();
  for (const source of sources) seen.set(source.address, (seen.get(source.address) ?? 0) + 1);
  return sources.map((source) => ({
    key: `${source.address} ${source.headerFrom}`,
    address: source.address,
    addressParts: dmarcAddressParts(source.address),
    headerFrom: source.headerFrom,
    sharesAddress: (seen.get(source.address) ?? 0) > 1,
    messages: source.messages,
    dkimAligned: source.dkimAligned,
    spfAligned: source.spfAligned,
    failingDmarc: source.failingDmarc,
  }));
}
