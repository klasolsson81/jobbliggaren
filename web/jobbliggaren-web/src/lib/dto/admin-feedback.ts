import { z } from "zod";
import { FEEDBACK_PAGE_KEYS } from "@/lib/admin/feedback";
import type {
  AdminFeedbackAvailability,
  AdminFeedbackBrowser,
  AdminFeedbackDeviceClass,
  AdminFeedbackItem,
  AdminFeedbackListItem,
  AdminFeedbackListPage,
  AdminFeedbackNoticeState,
  AdminFeedbackOs,
  AdminFeedbackPageSummary,
  AdminFeedbackStatus,
  AdminFeedbackTheme,
} from "@/lib/admin/view-models";
import { pagedResultWithTotalPages, readableInstantSchema as instant } from "./_helpers";

/**
 * The admin feedback API's wire shapes (#1979). The backend names its enums the .NET way; this file
 * is the one place they become the view model's.
 *
 * The status and the notice state are read strictly: each decides what the page offers to do. What
 * the browser reported is read leniently, because it is diagnostics: a family this web does not know
 * yet reads as unknown, and never costs the administrator the whole submission.
 */

export const FEEDBACK_WIRE_STATUSES = ["New", "InProgress", "Resolved", "Declined"] as const;
export type FeedbackWireStatus = (typeof FEEDBACK_WIRE_STATUSES)[number];

const statusSchema = z.enum(FEEDBACK_WIRE_STATUSES);
const noticeStateSchema = z.enum(["Queued", "Sending", "Accepted", "Failed", "Unknown"]);
const count = z.number().int().nonnegative();
const rating = z.number().int().min(1).max(5);
const pixels = z.number().int().positive();

/** A known value of a closed set, or null for a value this web does not know yet. */
function lenient<const T extends readonly [string, ...string[]]>(values: T) {
  const known: ReadonlySet<string> = new Set(values);
  const isKnown = (value: string): value is T[number] => known.has(value);
  return z
    .string()
    .nullable()
    .transform((value): T[number] | null => (value !== null && isKnown(value) ? value : null));
}

const listItemSchema = z.object({
  id: z.guid(),
  pageKey: z.string(),
  rating: rating.nullable(),
  excerpt: z.string().nullable(),
  status: statusSchema,
  submittedAt: instant,
  notificationState: noticeStateSchema.nullable(),
});

/** `GET /api/v1/admin/feedback` → 200. */
export const feedbackListResponseSchema = z.object({
  items: pagedResultWithTotalPages(listItemSchema),
  counts: z.object({
    all: count,
    new: count,
    inProgress: count,
    resolved: count,
    declined: count,
  }),
});

/** `GET /api/v1/admin/feedback/{id}` → 200; a 404 means no such submission. */
export const feedbackDetailSchema = z.object({
  id: z.guid(),
  pageKey: z.string(),
  rating: rating.nullable(),
  comment: z.string().nullable(),
  status: statusSchema,
  submittedAt: instant,
  statusChangedAt: instant.nullable(),
  reporterEmail: z.string().nullable(),
  client: z.object({
    viewportWidth: pixels.nullable(),
    viewportHeight: pixels.nullable(),
    screenWidth: pixels.nullable(),
    screenHeight: pixels.nullable(),
    pixelRatio: z.number().positive().nullable(),
    theme: lenient(["Light", "Dark"]),
    deviceClass: lenient(["Mobile", "Tablet", "Desktop"]),
    osFamily: lenient(["Windows", "MacOs", "Ios", "Android", "Linux", "ChromeOs", "Other"]),
    browserFamily: lenient(["Chrome", "Edge", "Firefox", "Safari", "SamsungInternet", "Opera", "Other"]),
  }),
  appVersion: z.string().nullable(),
  notification: z
    .object({
      state: noticeStateSchema,
      attempts: count,
      nextAttemptAt: instant,
    })
    .nullable(),
});

/** `GET /api/v1/admin/feedback/summary?days=` → 200. */
export const feedbackSummarySchema = z.object({
  days: z.number().int(),
  pages: z.array(
    z.object({
      pageKey: z.string(),
      submissions: count,
      raters: count,
      rated1: count,
      rated2: count,
      rated3: count,
      rated4: count,
      rated5: count,
      mean: z.number().nullable(),
    }),
  ),
});

/** `GET /api/v1/admin/feedback/availability` → 200. */
export const feedbackAvailabilitySchema = z.object({
  availability: z.enum(["Open", "Disabled", "NoRecipient", "CannotDeliver"]),
});

export type FeedbackListResponse = z.infer<typeof feedbackListResponseSchema>;
export type FeedbackDetailDto = z.infer<typeof feedbackDetailSchema>;
export type FeedbackSummaryDto = z.infer<typeof feedbackSummarySchema>;
export type FeedbackAvailabilityDto = z.infer<typeof feedbackAvailabilitySchema>;

/** A list read's criteria: a missing status or page reads as every status or every page. */
export interface FeedbackListCriteria {
  readonly status?: FeedbackWireStatus;
  readonly page?: string;
  readonly pageNumber: number;
  readonly pageSize: number;
}

const STATUS: Readonly<Record<FeedbackWireStatus, AdminFeedbackStatus>> = {
  New: "new",
  InProgress: "inProgress",
  Resolved: "resolved",
  Declined: "declined",
};

const WIRE_STATUS: Readonly<Record<AdminFeedbackStatus, FeedbackWireStatus>> = {
  new: "New",
  inProgress: "InProgress",
  resolved: "Resolved",
  declined: "Declined",
};

/** The status the backend names, for a filter or a status change. */
export function wireFeedbackStatus(status: AdminFeedbackStatus): FeedbackWireStatus {
  return WIRE_STATUS[status];
}

const NOTICE_STATE: Readonly<Record<z.infer<typeof noticeStateSchema>, AdminFeedbackNoticeState>> = {
  Queued: "queued",
  Sending: "sending",
  Accepted: "accepted",
  Failed: "failed",
  Unknown: "unknown",
};

const THEME: Readonly<Record<"Light" | "Dark", AdminFeedbackTheme>> = { Light: "light", Dark: "dark" };

const DEVICE_CLASS: Readonly<Record<"Mobile" | "Tablet" | "Desktop", AdminFeedbackDeviceClass>> = {
  Mobile: "mobile",
  Tablet: "tablet",
  Desktop: "desktop",
};

const OS: Readonly<Record<"Windows" | "MacOs" | "Ios" | "Android" | "Linux" | "ChromeOs" | "Other", AdminFeedbackOs>> = {
  Windows: "windows",
  MacOs: "macOs",
  Ios: "ios",
  Android: "android",
  Linux: "linux",
  ChromeOs: "chromeOs",
  Other: "other",
};

const BROWSER: Readonly<
  Record<"Chrome" | "Edge" | "Firefox" | "Safari" | "SamsungInternet" | "Opera" | "Other", AdminFeedbackBrowser>
> = {
  Chrome: "chrome",
  Edge: "edge",
  Firefox: "firefox",
  Safari: "safari",
  SamsungInternet: "samsungInternet",
  Opera: "opera",
  Other: "other",
};

const AVAILABILITY: Readonly<Record<FeedbackAvailabilityDto["availability"], AdminFeedbackAvailability>> = {
  Open: "open",
  Disabled: "disabled",
  NoRecipient: "noRecipient",
  CannotDeliver: "cannotDeliver",
};

function toListItem(item: z.infer<typeof listItemSchema>): AdminFeedbackListItem {
  return {
    id: item.id,
    page: item.pageKey,
    rating: item.rating,
    excerpt: item.excerpt,
    status: STATUS[item.status],
    submittedAt: item.submittedAt,
    notice: item.notificationState === null ? null : NOTICE_STATE[item.notificationState],
  };
}

export function toFeedbackListPage(response: FeedbackListResponse): AdminFeedbackListPage {
  const { items, counts } = response;
  return {
    items: items.items.map(toListItem),
    page: items.page,
    totalPages: items.totalPages,
    totalCount: items.totalCount,
    counts: { ...counts },
  };
}

export function toFeedbackItem(dto: FeedbackDetailDto): AdminFeedbackItem {
  const { client } = dto;
  return {
    id: dto.id,
    page: dto.pageKey,
    rating: dto.rating,
    comment: dto.comment,
    status: STATUS[dto.status],
    submittedAt: dto.submittedAt,
    statusChangedAt: dto.statusChangedAt,
    reporterEmail: dto.reporterEmail,
    client: {
      viewportWidth: client.viewportWidth,
      viewportHeight: client.viewportHeight,
      screenWidth: client.screenWidth,
      screenHeight: client.screenHeight,
      pixelRatio: client.pixelRatio,
      theme: client.theme === null ? null : THEME[client.theme],
      deviceClass: client.deviceClass === null ? null : DEVICE_CLASS[client.deviceClass],
      os: client.osFamily === null ? null : OS[client.osFamily],
      browser: client.browserFamily === null ? null : BROWSER[client.browserFamily],
    },
    appVersion: dto.appVersion,
    notice:
      dto.notification === null
        ? null
        : {
            state: NOTICE_STATE[dto.notification.state],
            attempts: dto.notification.attempts,
            nextAttemptAt: dto.notification.nextAttemptAt,
          },
  };
}

const PAGE_ORDER: ReadonlyMap<string, number> = new Map(FEEDBACK_PAGE_KEYS.map((key, index) => [key, index]));

/** The app's own page order; a key this web does not know yet comes last. */
function pageOrder(key: string): number {
  return PAGE_ORDER.get(key) ?? FEEDBACK_PAGE_KEYS.length;
}

/** The summary's pages in the app's own order, not the backend's alphabetical one. */
export function toFeedbackSummary(dto: FeedbackSummaryDto): ReadonlyArray<AdminFeedbackPageSummary> {
  return [...dto.pages]
    .sort((left, right) => pageOrder(left.pageKey) - pageOrder(right.pageKey) || left.pageKey.localeCompare(right.pageKey))
    .map((row) => ({
      page: row.pageKey,
      submissions: row.submissions,
      raters: row.raters,
      ratings: [row.rated1, row.rated2, row.rated3, row.rated4, row.rated5] as const,
      mean: row.mean,
    }));
}

export function toFeedbackAvailability(dto: FeedbackAvailabilityDto): AdminFeedbackAvailability {
  return AVAILABILITY[dto.availability];
}
