"use client";

// "use client": each page reads the band's state and keeps its fixtures in memory.
import { useState } from "react";
import { useTranslations } from "next-intl";
import type {
  AdminEmailDelivery as Delivery,
  AdminEmailPeriod,
  AdminErrorLogRow,
  AdminFeedbackAvailability,
  AdminFeedbackItem,
  AdminFeedbackListItem,
  AdminFeedbackListPage,
  AdminFeedbackPageSummary,
  AdminFeedbackStatus,
  AdminFeedbackWindow,
  AdminImportLogRow,
  AdminRegion,
  AdminRegionKind,
  AdminSecurityLogRow,
  AdminValueRegion,
} from "@/lib/admin/view-models";
import { listRegion } from "@/lib/admin/view-models";
import type { AdminFeedbackQuery, AdminFeedbackRefusal } from "@/lib/admin/feedback";
import { AdminOverview } from "@/components/admin/admin-overview";
import { AdminFeedbackView } from "@/components/admin/admin-feedback-view";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminLogsView, type AdminLogView } from "@/components/admin/admin-logs-view";
import type { PreviewOverviewData } from "@/lib/admin-preview/fixtures";
import { usePreviewState } from "./preview-shell.preview";

/** Long enough for the pending state to show. */
const SIMULATED_LATENCY_MS = 400;

const latency = () => new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));

/** A value region in the band's state: "Tom" shows the zero values, since zero is a value. */
function value<T>(kind: AdminRegionKind, loaded: T, zero: T): AdminValueRegion<T> {
  switch (kind) {
    case "loaded":
      return { kind, data: loaded };
    case "empty":
      return { kind: "loaded", data: zero };
    default:
      return { kind };
  }
}

/** A list region in the band's state. */
function list<T>(kind: AdminRegionKind, loaded: ReadonlyArray<T>): AdminRegion<ReadonlyArray<T>> {
  return kind === "loaded" ? listRegion(loaded) : { kind };
}

export function PreviewOverview({
  data,
  zero,
  basePath,
}: {
  readonly data: PreviewOverviewData;
  readonly zero: PreviewOverviewData;
  readonly basePath: string;
}) {
  const { kind } = usePreviewState();
  return (
    <AdminOverview
      basePath={basePath}
      regions={{
        newAccounts: value(kind, data.newAccounts, zero.newAccounts),
        totals: value(kind, data.totals, zero.totals),
        active: value(kind, data.active, zero.active),
        logins: value(kind, data.logins, zero.logins),
        trend: value(kind, data.trend, zero.trend),
        services: list(kind, data.services),
        server: value(kind, data.server, zero.server),
        backup: value(kind, data.backup, zero.backup),
        email: value(kind, data.email, zero.email),
        attention: list(kind, data.attention),
        events: list(kind, data.events),
      }}
    />
  );
}

/** Fewer than the live page's 25 a page, so the pager is seen with the fixtures there are. */
const PREVIEW_FEEDBACK_PAGE_SIZE = 5;

/** The list's excerpt, cut as the backend cuts it. */
const EXCERPT_LENGTH = 90;

function excerptOf(comment: string | null): string | null {
  if (comment === null || comment.length <= EXCERPT_LENGTH) return comment;
  return `${comment.slice(0, EXCERPT_LENGTH).trimEnd()}…`;
}

function toListItem(item: AdminFeedbackItem): AdminFeedbackListItem {
  return {
    id: item.id,
    page: item.page,
    rating: item.rating,
    excerpt: excerptOf(item.comment),
    status: item.status,
    submittedAt: item.submittedAt,
    notice: item.notice?.state ?? null,
  };
}

/** The list the backend would answer for the query: newest first, counted per status inside the page filter. */
function listPage(items: ReadonlyArray<AdminFeedbackItem>, query: AdminFeedbackQuery): AdminFeedbackListPage {
  const onPage = items.filter((item) => query.page === null || item.page === query.page);
  const shown = onPage
    .filter((item) => query.status === null || item.status === query.status)
    .sort((left, right) => right.submittedAt.localeCompare(left.submittedAt));
  const count = (status: AdminFeedbackStatus) => onPage.filter((item) => item.status === status).length;
  const start = (query.pageNumber - 1) * PREVIEW_FEEDBACK_PAGE_SIZE;
  return {
    items: shown.slice(start, start + PREVIEW_FEEDBACK_PAGE_SIZE).map(toListItem),
    page: query.pageNumber,
    totalPages: Math.ceil(shown.length / PREVIEW_FEEDBACK_PAGE_SIZE),
    totalCount: shown.length,
    counts: {
      all: onPage.length,
      new: count("new"),
      inProgress: count("inProgress"),
      resolved: count("resolved"),
      declined: count("declined"),
    },
  };
}

const EMPTY_LIST: AdminFeedbackListPage = {
  items: [],
  page: 1,
  totalPages: 0,
  totalCount: 0,
  counts: { all: 0, new: 0, inProgress: 0, resolved: 0, declined: 0 },
};

/**
 * Feedback in memory, driven by the URL as the live page is: a status change and a requeue change the
 * submission they name, and the band's state applies to every region at once.
 */
export function PreviewFeedback({
  items,
  summaries,
  availability,
  query,
  basePath,
  now,
}: {
  readonly items: ReadonlyArray<AdminFeedbackItem>;
  readonly summaries: Readonly<Record<AdminFeedbackWindow, ReadonlyArray<AdminFeedbackPageSummary>>>;
  readonly availability: AdminFeedbackAvailability;
  readonly query: AdminFeedbackQuery;
  readonly basePath: string;
  readonly now: string;
}) {
  const { kind } = usePreviewState();
  const t = useTranslations("admin.feedback");
  const [submissions, setSubmissions] = useState(items);

  async function onStatus(id: string, status: AdminFeedbackStatus): Promise<AdminFeedbackRefusal> {
    await latency();
    setSubmissions((previous) =>
      previous.map((item) => (item.id === id ? { ...item, status, statusChangedAt: now } : item)),
    );
    return null;
  }

  // The backend's rule: only a failed notice, or one with an unknown outcome and the risk acknowledged.
  async function onRequeue(id: string, acknowledgeDuplicateRisk: boolean): Promise<AdminFeedbackRefusal> {
    await latency();
    const state = submissions.find((item) => item.id === id)?.notice?.state;
    if (state !== "failed" && state !== "unknown") return t("errors.noticeAlreadyQueued");
    if (state === "unknown" && !acknowledgeDuplicateRisk) return t("errors.noticeChanged");
    setSubmissions((previous) =>
      previous.map((item) => (item.id === id ? { ...item, notice: { state: "queued", attempts: 0, nextAttemptAt: now } } : item)),
    );
    return null;
  }

  const open = query.id === null ? undefined : submissions.find((item) => item.id === query.id);
  const opened = query.id !== null;

  switch (kind) {
    case "loaded":
    case "empty": {
      const loaded = kind === "loaded";
      return (
        <AdminFeedbackView
          basePath={basePath}
          query={query}
          availability={{ kind: "loaded", data: availability }}
          list={{ kind: "loaded", data: loaded ? listPage(submissions, query) : EMPTY_LIST }}
          detail={!opened ? null : loaded && open !== undefined ? { kind: "loaded", data: open } : { kind: "empty" }}
          summary={loaded ? listRegion(summaries[query.window]) : { kind: "empty" }}
          onStatus={onStatus}
          onRequeue={onRequeue}
        />
      );
    }
    default:
      return (
        <AdminFeedbackView
          basePath={basePath}
          query={query}
          availability={{ kind }}
          list={{ kind }}
          detail={opened ? { kind } : null}
          summary={{ kind }}
          onStatus={onStatus}
          onRequeue={onRequeue}
        />
      );
  }
}

export function PreviewLogs({
  view,
  basePath,
  security,
  errors,
  imports,
}: {
  readonly view: AdminLogView;
  readonly basePath: string;
  readonly security: ReadonlyArray<AdminSecurityLogRow>;
  readonly errors: ReadonlyArray<AdminErrorLogRow>;
  readonly imports: ReadonlyArray<AdminImportLogRow>;
}) {
  const { kind } = usePreviewState();
  const data =
    view === "security"
      ? ({ view, region: list(kind, security) } as const)
      : view === "errors"
        ? ({ view, region: list(kind, errors) } as const)
        : ({ view, region: list(kind, imports) } as const);
  return (
    <AdminLogsView
      view={view}
      basePath={basePath}
      data={data}
      counts={kind === "loaded" ? { security: security.length, errors: errors.length, imports: imports.length } : undefined}
    />
  );
}

export function PreviewEmail({
  byPeriod,
  zero,
}: {
  readonly byPeriod: Readonly<Record<AdminEmailPeriod, Delivery>>;
  readonly zero: Delivery;
}) {
  const { kind } = usePreviewState();
  const [period, setPeriod] = useState<AdminEmailPeriod>("d7");
  return (
    <AdminEmailDelivery
      region={value(kind, byPeriod[period], zero)}
      period={period}
      onPeriodChange={setPeriod}
    />
  );
}
