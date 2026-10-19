"use client";

// "use client": each page reads the band's state and keeps its fixtures in memory.
import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import type {
  AdminEmailAllowance,
  AdminEmailPeriod,
  AdminEmailRead,
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
import { commandRefusal, type AdminFeedbackQuery, type AdminFeedbackRefusal } from "@/lib/admin/feedback";
import { serverRegion, type AdminServerReading } from "@/lib/admin/host-observation";
import { servicesRegion, type AdminServicesReading } from "@/lib/admin/service-observation";
import { AdminOverview } from "@/components/admin/admin-overview";
import { AdminFeedbackView } from "@/components/admin/admin-feedback-view";
import { AdminEmailAllowance as AllowanceRegion, type AdminEmailAllowanceRegion } from "@/components/admin/admin-email-allowance";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { AdminLogsView, type AdminLogView } from "@/components/admin/admin-logs-view";
import type {
  PreviewAllowanceScenario,
  PreviewDelivery,
  PreviewDeliveryScenario,
  PreviewOverviewData,
} from "@/lib/admin-preview/fixtures";
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

/** The Server region in the band's state: "Tom" shows the zero reading, which here is three unmeasured ones. */
function serverValue(kind: AdminRegionKind, loaded: AdminServerReading, zero: AdminServerReading): AdminValueRegion<AdminServerReading> {
  switch (kind) {
    case "loaded":
      return serverRegion(loaded);
    case "empty":
      return serverRegion(zero);
    default:
      return { kind };
  }
}

/** The Tjänster region in the band's state: "Tom" shows the rows with nothing observed. */
function servicesValue(kind: AdminRegionKind, loaded: AdminServicesReading, zero: AdminServicesReading): AdminValueRegion<AdminServicesReading> {
  switch (kind) {
    case "loaded":
      return servicesRegion(loaded);
    case "empty":
      return servicesRegion(zero);
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
        services: servicesValue(kind, data.services, zero.services),
        server: serverValue(kind, data.server, zero.server),
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
    if (state !== "failed" && state !== "unknown") return commandRefusal(t("errors.noticeAlreadyQueued"));
    if (state === "unknown" && !acknowledgeDuplicateRisk) return commandRefusal(t("errors.noticeChanged"));
    setSubmissions((previous) =>
      previous.map((item) => (item.id === id ? { ...item, notice: { state: "queued", attempts: 0, nextAttemptAt: now } } : item)),
    );
    return null;
  }

  // A reply is accepted at once, as the provider answers on a working day; the list shows it like a sent one.
  async function onReply(id: string, replyKey: string, text: string) {
    await latency();
    setSubmissions((previous) =>
      previous.map((item) =>
        item.id === id
          ? {
              ...item,
              replies: [...item.replies, { id: replyKey, text, state: "accepted", createdAt: now, completedAt: now }],
            }
          : item,
      ),
    );
    return { kind: "recorded", state: "accepted" } as const;
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
          onReply={onReply}
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
          onReply={onReply}
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

/**
 * The allowance region in the band's state. The region is built, so "Kommer snart" and "Tom" show the chosen
 * scenario: only a failed or a pending read changes it.
 */
function allowanceRegion(kind: AdminRegionKind, chosen: AdminEmailAllowance): AdminEmailAllowanceRegion {
  return kind === "failed" || kind === "loading" ? { kind } : { kind: "loaded", data: chosen };
}

/** One of Leverans's reads in the band's state: built like the allowance, so only failed and loading change it. */
function deliveryRead<T>(kind: AdminRegionKind, chosen: T): AdminEmailRead<T> {
  return kind === "failed" || kind === "loading" ? { kind } : { kind: "loaded", data: chosen };
}

function isScenario(value: string): value is PreviewAllowanceScenario {
  return SCENARIOS.some((scenario) => scenario === value);
}

function isDeliveryScenario(value: string): value is PreviewDeliveryScenario {
  return DELIVERY_SCENARIOS.some((scenario) => scenario === value);
}

/** The allowance scenarios, in the order the preview's choice lists them. */
const SCENARIOS: ReadonlyArray<PreviewAllowanceScenario> = [
  "withinQuota",
  "nothingSent",
  "atQuota",
  "overQuota",
  "aboveHalf",
  "planDiffers",
  "planUnknown",
  "planFailed",
  "notConfigured",
  "readKeyMissing",
  "noAnswer",
  "rateLimited",
  "keyRefused",
  "refused",
  "malformed",
  "noMonth",
  "webhooks",
  "webhooksUnreadable",
];

/** The Leverans scenarios, in the order the preview's second choice lists them. */
const DELIVERY_SCENARIOS: ReadonlyArray<PreviewDeliveryScenario> = [
  "observed",
  "capped",
  "noneFailed",
  "nothingSent",
  "listUnjudged",
  "notConfigured",
  "notObservable",
  "readKeyMissing",
  "noAnswer",
  "rateLimited",
  "keyRefused",
  "refused",
  "malformed",
];

/**
 * The e-post page over fictional reads. The allowance and Leverans each take a scenario of their own, and Leverans's
 * period is the URL's, as on the live page: its links navigate, and the chosen scenarios stay.
 */
export function PreviewEmail({
  allowances,
  deliveries,
  period,
  pagePath,
}: {
  readonly allowances: Readonly<Record<PreviewAllowanceScenario, AdminEmailAllowance>>;
  readonly deliveries: Readonly<Record<PreviewDeliveryScenario, Readonly<Record<AdminEmailPeriod, PreviewDelivery>>>>;
  readonly period: AdminEmailPeriod;
  readonly pagePath: string;
}) {
  const { kind } = usePreviewState();
  const t = useTranslations("admin");
  const preview = useTranslations("admin-preview.email");
  const selectId = useId();
  const deliverySelectId = useId();
  const [scenario, setScenario] = useState<PreviewAllowanceScenario>("withinQuota");
  const [deliveryScenario, setDeliveryScenario] = useState<PreviewDeliveryScenario>("observed");
  const delivery = deliveries[deliveryScenario][period];
  return (
    <div className="flex flex-col gap-8">
      <AdminPageHeader title={t("email.heading")} lede={t("email.lede")} />
      <div className="jp-adminpreview-scenario">
        <label htmlFor={selectId}>{preview("scenarioLabel")}</label>
        <select
          id={selectId}
          value={scenario}
          onChange={(event) => {
            const chosen = event.target.value;
            if (isScenario(chosen)) setScenario(chosen);
          }}
        >
          {SCENARIOS.map((option) => (
            <option key={option} value={option}>
              {preview(`scenarios.${option}`)}
            </option>
          ))}
        </select>
      </div>
      <AllowanceRegion region={allowanceRegion(kind, allowances[scenario])} />
      <div className="jp-adminpreview-scenario">
        <label htmlFor={deliverySelectId}>{preview("deliveryLabel")}</label>
        <select
          id={deliverySelectId}
          value={deliveryScenario}
          onChange={(event) => {
            const chosen = event.target.value;
            if (isDeliveryScenario(chosen)) setDeliveryScenario(chosen);
          }}
        >
          {DELIVERY_SCENARIOS.map((option) => (
            <option key={option} value={option}>
              {preview(`deliveryScenarios.${option}`)}
            </option>
          ))}
        </select>
      </div>
      <AdminEmailDelivery
        pagePath={pagePath}
        period={period}
        summary={deliveryRead(kind, delivery.summary)}
        failures={deliveryRead(kind, delivery.failures)}
      />
    </div>
  );
}
