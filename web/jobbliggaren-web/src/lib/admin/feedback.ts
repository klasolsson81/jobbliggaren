import { isFeedbackPageKey, type FeedbackPageKey } from "@/lib/feedback/page-keys";
import type { AdminFeedbackStatus, AdminFeedbackWindow } from "./view-models";

/**
 * `/admin/feedback`'s fixed sets and its URL (#1979). The page is driven by its URL, so the notice
 * mail's link `/admin/feedback?id=<guid>` opens the submission it names, and every filter, page and
 * window is a link. Every value here is from a closed set or a bounded number, and the one id names a
 * submission, never a person.
 */

/** The route; the Server Actions revalidate it. */
export const FEEDBACK_ROUTE = "/admin/feedback";

/** The list's page size. */
export const FEEDBACK_PAGE_SIZE = 25;

export { MAX_FEEDBACK_SCREENSHOT_BYTES } from "@/lib/feedback/limits";

// The closed page set lives with the user surface; a key outside it is shown as it came and never filtered on.
export { FEEDBACK_PAGE_KEYS, isFeedbackPageKey, type FeedbackPageKey } from "@/lib/feedback/page-keys";

/** The statuses in the order the filter and the status choice show them. */
export const FEEDBACK_STATUSES: ReadonlyArray<AdminFeedbackStatus> = ["new", "inProgress", "resolved", "declined"];

const STATUSES: ReadonlySet<string> = new Set(FEEDBACK_STATUSES);

export function isFeedbackStatus(value: unknown): value is AdminFeedbackStatus {
  return typeof value === "string" && STATUSES.has(value);
}

export const FEEDBACK_WINDOWS: ReadonlyArray<AdminFeedbackWindow> = [7, 30, 90];

export const DEFAULT_FEEDBACK_WINDOW: AdminFeedbackWindow = 30;

/** A submission id's shape: checked before an id becomes a path segment. */
const FEEDBACK_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isFeedbackId(value: unknown): value is string {
  return typeof value === "string" && FEEDBACK_ID.test(value);
}

/**
 * What a command on a submission came to: null when it went through, otherwise the refusal in words,
 * shown where the command was asked. `value` refuses the value chosen, the status the submission already
 * has, and marks that field invalid; `command` is every other refusal, which leaves the field as it is.
 */
export type AdminFeedbackRefusal = AdminFeedbackRefused | null;

export interface AdminFeedbackRefused {
  readonly text: string;
  readonly about: "value" | "command";
}

/** A refusal of the command rather than of a value chosen. */
export function commandRefusal(text: string): AdminFeedbackRefused {
  return { text, about: "command" };
}

/** The ProblemDetails titles the feedback commands answer with. */
export const FEEDBACK_ERRORS = {
  notFound: "Feedback.NotFound",
  statusUnchanged: "Feedback.StatusUnchanged",
  duplicateRiskNotAcknowledged: "Feedback.DuplicateRiskNotAcknowledged",
  notificationNotRequeueable: "Feedback.NotificationNotRequeueable",
  reporterUnavailable: "Feedback.ReporterUnavailable",
} as const;

// ── The URL ─────────────────────────────────────────────────────────────────────────────────

/** The URL's status values, in Swedish like the route. */
const STATUS_SLUGS: Readonly<Record<AdminFeedbackStatus, string>> = {
  new: "ny",
  inProgress: "pagar",
  resolved: "atgardad",
  declined: "avstar",
};

/** The highest page number the URL is read as; a larger one reads as the first page. */
const MAX_PAGE_NUMBER = 10_000;

/** What the URL asks for. A value the URL leaves out, or gives in a form not read, is the default. */
export interface AdminFeedbackQuery {
  /** Null for every status. */
  readonly status: AdminFeedbackStatus | null;
  /** Null for every page. */
  readonly page: FeedbackPageKey | null;
  readonly pageNumber: number;
  /**
   * The open submission as the URL names it, or null when none is open. An id of the wrong shape reads
   * as none, so what the URL carried never reaches a link on the page or the backend.
   */
  readonly id: string | null;
  readonly window: AdminFeedbackWindow;
}

export const DEFAULT_FEEDBACK_QUERY: AdminFeedbackQuery = {
  status: null,
  page: null,
  pageNumber: 1,
  id: null,
  window: DEFAULT_FEEDBACK_WINDOW,
};

export type FeedbackSearchParams = Readonly<Record<string, string | string[] | undefined>>;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function statusFromSlug(slug: string | undefined): AdminFeedbackStatus | null {
  return FEEDBACK_STATUSES.find((status) => STATUS_SLUGS[status] === slug) ?? null;
}

function pageNumberFrom(raw: string | undefined): number {
  if (raw === undefined || !/^\d{1,5}$/.test(raw)) return 1;
  const number = Number.parseInt(raw, 10);
  return number >= 1 && number <= MAX_PAGE_NUMBER ? number : 1;
}

function windowFrom(raw: string | undefined): AdminFeedbackWindow {
  return FEEDBACK_WINDOWS.find((days) => String(days) === raw) ?? DEFAULT_FEEDBACK_WINDOW;
}

/** Reads `status`, `sida`, `sidnr`, `id` and `fonster`. */
export function parseFeedbackQuery(params: FeedbackSearchParams): AdminFeedbackQuery {
  const page = first(params.sida);
  const id = first(params.id)?.trim();
  return {
    status: statusFromSlug(first(params.status)),
    page: isFeedbackPageKey(page) ? page : null,
    pageNumber: pageNumberFrom(first(params.sidnr)),
    id: isFeedbackId(id) ? id : null,
    window: windowFrom(first(params.fonster)),
  };
}

/** The URL of the query; a default is left out, so the bare route is the first page of everything. */
export function feedbackHref(basePath: string, query: AdminFeedbackQuery): string {
  const params = new URLSearchParams();
  if (query.status !== null) params.set("status", STATUS_SLUGS[query.status]);
  if (query.page !== null) params.set("sida", query.page);
  if (query.pageNumber > 1) params.set("sidnr", String(query.pageNumber));
  if (query.window !== DEFAULT_FEEDBACK_WINDOW) params.set("fonster", String(query.window));
  if (query.id !== null) params.set("id", query.id);
  const search = params.toString();
  return search === "" ? basePath : `${basePath}?${search}`;
}

/** A status filter starts again from the first page; the open submission stays open. */
export function withStatus(query: AdminFeedbackQuery, status: AdminFeedbackStatus | null): AdminFeedbackQuery {
  return { ...query, status, pageNumber: 1 };
}

/** A page filter starts again from the first page; the open submission stays open. */
export function withPage(query: AdminFeedbackQuery, page: FeedbackPageKey | null): AdminFeedbackQuery {
  return { ...query, page, pageNumber: 1 };
}

export function withPageNumber(query: AdminFeedbackQuery, pageNumber: number): AdminFeedbackQuery {
  return { ...query, pageNumber };
}

export function withId(query: AdminFeedbackQuery, id: string): AdminFeedbackQuery {
  return { ...query, id };
}

/** Back to the list the open submission was opened from: its filters, page and window stay. */
export function withoutId(query: AdminFeedbackQuery): AdminFeedbackQuery {
  return { ...query, id: null };
}

export function withWindow(query: AdminFeedbackQuery, window: AdminFeedbackWindow): AdminFeedbackQuery {
  return { ...query, window };
}
