"use client";

import { useState } from "react";
import type {
  AdminEmailDelivery as Delivery,
  AdminEmailPeriod,
  AdminErrorLogRow,
  AdminFeedbackItem,
  AdminImportLogRow,
  AdminRegion,
  AdminRegionKind,
  AdminSecurityLogRow,
  AdminValueRegion,
} from "@/lib/admin/view-models";
import { AdminOverview } from "@/components/admin/admin-overview";
import { AdminFeedbackView } from "@/components/admin/admin-feedback-view";
import { AdminEmailDelivery } from "@/components/admin/admin-email-delivery";
import { AdminLogsView, type AdminLogView } from "@/components/admin/admin-logs-view";
import type { PreviewOverviewData } from "@/lib/admin-preview/fixtures";
import { usePreviewState } from "./preview-shell.preview";

/** Long enough for the pending state to show. */
const SIMULATED_LATENCY_MS = 400;

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
  return kind === "loaded" ? { kind, data: loaded } : { kind };
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

/** The reports in memory: a reply is added to its report and moves a new report to Pågår. */
export function PreviewFeedback({ items }: { readonly items: ReadonlyArray<AdminFeedbackItem> }) {
  const { kind } = usePreviewState();
  const [reports, setReports] = useState(items);

  async function reply(item: AdminFeedbackItem, text: string): Promise<string | null> {
    await new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
    setReports((previous) =>
      previous.map((report) =>
        report.id === item.id
          ? {
              ...report,
              status: report.status === "new" ? "inProgress" : report.status,
              replies: [...report.replies, { id: `${report.id}-${report.replies.length + 1}`, sentAt: new Date().toISOString(), text }],
            }
          : report,
      ),
    );
    return null;
  }

  return (
    <AdminFeedbackView
      key={kind}
      region={list(kind, reports)}
      onReply={reply}
      onStatus={(item, status) =>
        setReports((previous) => previous.map((report) => (report.id === item.id ? { ...report, status } : report)))
      }
    />
  );
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
