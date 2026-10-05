import type { ReactNode } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import {
  Activity,
  AlertTriangle,
  HardDriveDownload,
  LogIn,
  Mail,
  Server,
  UserPlus,
  Users,
  type LucideIcon,
} from "lucide-react";
import { StatusDot, type StatusTone } from "@/components/ui/status-dot";
import { formatDateTime } from "@/lib/i18n/format";
import {
  ADMIN_OVERVIEW_UNAVAILABLE,
  type AdminAttentionItem,
  type AdminAttentionKind,
  type AdminBackupStatus,
  type AdminEventKind,
  type AdminOverviewRegions,
  type AdminRecentEvent,
  type AdminRegion,
  type AdminServerReading,
  type AdminServiceStatus,
  type AdminValueRegion,
} from "@/lib/admin/view-models";
import { AdminCard, AdminCardLink, type AdminCardSpan } from "./admin-card";
import { AdminPageHeader } from "./admin-page-header";
import { AdminRegionLine } from "./admin-region-line";
import { AdminTrendCard } from "./admin-trend-card";
import { AdminUnknown } from "./admin-unknown";

const EVENT_TONE: Readonly<Record<AdminEventKind, StatusTone>> = {
  accountCreated: "success",
  accountSuspended: "danger",
  accountReinstated: "info",
  deletionScheduled: "warning",
  emailChangeRequested: "info",
  jobFailed: "danger",
};

const ATTENTION_PATH: Readonly<Record<AdminAttentionKind, string>> = {
  failedJobs: "/jobb",
  pendingDeletions: "/anvandare",
  failedEmails: "/e-post",
};

/**
 * The admin overview, variant A (ADR 0150 D1), shared by `/admin` and the local preview.
 *
 * Every card renders exactly one state of its region (D2). While no source exists the card keeps
 * its designed structure and says so: an unknown number is an en-dash without its unit, an
 * unknown list is one "Kommer snart" line, and the attention edge stays neutral. A count is a
 * value, so a value card shows zero rather than an empty state.
 */
export function AdminOverview({
  basePath = "/admin",
  regions = ADMIN_OVERVIEW_UNAVAILABLE,
}: {
  readonly basePath?: string;
  readonly regions?: AdminOverviewRegions;
}) {
  const t = useTranslations("admin.overview");
  const shared = useTranslations("admin.regions");
  const format = useFormatter();
  const kinds = new Set(Object.values(regions).map((region) => region.kind));

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      {kinds.has("failed") ? (
        <p className="sr-only" role="alert">
          {shared("failed")}
        </p>
      ) : null}
      {kinds.has("loading") ? (
        <p className="sr-only" role="status">
          {shared("loading")}
        </p>
      ) : null}

      <div className="jp-admingrid">
        <ValueCard
          id="admin-overview-new-users"
          title={t("kpi.newUsers")}
          icon={UserPlus}
          span={3}
          region={regions.newAccounts}
          render={(data) => ({
            value: data.today,
            unit: t("kpi.unit.today"),
            sub: t("kpi.newUsersSub", {
              yesterday: data.yesterday,
              last7Days: data.last7Days,
              last30Days: data.last30Days,
            }),
          })}
        />
        <ValueCard
          id="admin-overview-total"
          title={t("kpi.total")}
          icon={Users}
          span={3}
          region={regions.totals}
          render={(data) => ({
            value: data.total,
            unit: t("kpi.unit.accounts"),
            sub: t("kpi.totalSub", { suspended: data.suspended, pendingDeletion: data.pendingDeletion }),
          })}
        />
        <ValueCard
          id="admin-overview-active"
          title={t("kpi.active")}
          icon={Activity}
          span={3}
          region={regions.active}
          render={(data) => ({
            value: data.last30Days,
            unit: t("kpi.unit.last30Days"),
            sub: t("kpi.activeSub", { today: data.today, last7Days: data.last7Days }),
          })}
        />
        <ValueCard
          id="admin-overview-logins"
          title={t("kpi.logins")}
          icon={LogIn}
          span={3}
          region={regions.logins}
          render={(data) => ({
            value: data.today,
            unit: t("kpi.unit.today"),
            sub: t("kpi.loginsSub", { yesterday: data.yesterday, failed: data.failed, locked: data.locked }),
          })}
        />

        <AdminTrendCard region={regions.trend} />

        <AdminCard
          id="admin-overview-services"
          title={t("services.title")}
          span={4}
          list
          aside={<AdminCardLink href={`${basePath}/loggar`} label={t("services.link")} />}
        >
          <ServicesBody region={regions.services} />
        </AdminCard>

        <AdminCard id="admin-overview-server" title={t("server.title")} icon={Server} span={4}>
          <ServerBody region={regions.server} />
        </AdminCard>

        <AdminCard
          id="admin-overview-backup"
          title={t("backup.title")}
          icon={HardDriveDownload}
          span={4}
        >
          <BackupBody region={regions.backup} />
        </AdminCard>

        <ValueCard
          id="admin-overview-email"
          title={t("email.title")}
          icon={Mail}
          span={4}
          aside={<AdminCardLink href={`${basePath}/e-post`} label={t("email.link")} />}
          region={regions.email}
          render={(data) => ({
            value: data.sent,
            unit: t("email.unit"),
            sub: (
              <>
                <span className={data.failed > 0 ? "jp-admin-danger" : undefined}>
                  {t("email.failed", { count: data.failed })}
                </span>
                {` · ${t("email.noRecipient", { count: data.noRecipient })}`}
              </>
            ),
            extra:
              data.topTypes.length === 0 ? null : (
                <ol className="jp-admintoplist" aria-label={t("email.topTypes")}>
                  {data.topTypes.map((row) => (
                    <li key={row.type}>
                      <code>{row.type}</code>
                      <span>{format.number(row.sent)}</span>
                    </li>
                  ))}
                </ol>
              ),
          })}
        />

        <AdminCard
          id="admin-overview-attention"
          title={t("attention.title")}
          span={5}
          list
          attention={attentionState(regions.attention)}
        >
          <AttentionBody region={regions.attention} basePath={basePath} />
        </AdminCard>

        <AdminCard
          id="admin-overview-events"
          title={t("events.title")}
          span={7}
          list
          aside={<AdminCardLink href={`${basePath}/granskning`} label={t("events.link")} />}
        >
          <EventsBody region={regions.events} />
        </AdminCard>
      </div>
    </div>
  );
}

function attentionState(region: AdminRegion<ReadonlyArray<unknown>>): "unknown" | "clear" | "raised" {
  if (region.kind === "empty") return "clear";
  if (region.kind !== "loaded") return "unknown";
  return region.data.length === 0 ? "clear" : "raised";
}

interface ShownValue {
  readonly value: number;
  readonly unit: string;
  readonly sub: ReactNode;
  readonly extra?: ReactNode;
}

/** A card around one big number: the number and its unit, or an en-dash with the unit hidden. */
function ValueCard<T>({
  id,
  title,
  icon,
  span,
  aside,
  region,
  render,
}: {
  readonly id: string;
  readonly title: string;
  readonly icon: LucideIcon;
  readonly span: AdminCardSpan;
  readonly aside?: ReactNode;
  readonly region: AdminValueRegion<T>;
  readonly render: (data: T) => ShownValue;
}) {
  const format = useFormatter();

  if (region.kind !== "loaded") {
    return (
      <AdminCard id={id} title={title} icon={icon} span={span} aside={aside}>
        <p className="jp-adminkpi">
          <span className="jp-adminkpi__value"><AdminUnknown /></span>
        </p>
        <AdminRegionLine quiet kind={region.kind} className="jp-adminkpi__sub" />
      </AdminCard>
    );
  }

  const shown = render(region.data);
  return (
    <AdminCard id={id} title={title} icon={icon} span={span} aside={aside}>
      <p className="jp-adminkpi">
        <span className="jp-adminkpi__value">{format.number(shown.value)}</span>
        <span className="jp-adminkpi__unit">{shown.unit}</span>
      </p>
      <p className="jp-adminkpi__sub">{shown.sub}</p>
      {shown.extra}
    </AdminCard>
  );
}

function ServicesBody({ region }: { readonly region: AdminRegion<ReadonlyArray<AdminServiceStatus>> }) {
  const t = useTranslations("admin.overview.services");
  if (region.kind !== "loaded") return <AdminRegionLine quiet kind={region.kind} empty={t("empty")} region />;
  if (region.data.length === 0) return <AdminRegionLine quiet kind="empty" empty={t("empty")} region />;
  return (
    <ul className="jp-adminservices">
      {region.data.map((service) => (
        <li key={service.id}>
          <StatusDot tone={service.state === "ok" ? "success" : "warning"}>{service.label}</StatusDot>
          <span className="jp-adminservices__state" data-state={service.state}>
            {t(`state.${service.state}`)}
          </span>
          <span className="jp-adminservices__detail">{service.detail}</span>
        </li>
      ))}
    </ul>
  );
}

const METERS = ["cpu", "memory", "disk"] as const;

function ServerBody({ region }: { readonly region: AdminValueRegion<AdminServerReading> }) {
  const t = useTranslations("admin.overview.server");
  const format = useFormatter();

  return (
    <div className="jp-admincard__body">
      <dl className="jp-admindl">
        {METERS.map((meter) => (
          <Row key={meter} label={t(meter)}>
            {region.kind === "loaded" ? (
              <span className="jp-adminmeter">
                <span>{format.number(region.data[meter] / 100, { style: "percent" })}</span>
                <span className="jp-adminmeter__track" aria-hidden="true">
                  <span className="jp-adminmeter__fill" style={{ inlineSize: `${Math.min(100, Math.max(0, region.data[meter]))}%` }} />
                </span>
              </span>
            ) : (
              <AdminUnknown />
            )}
          </Row>
        ))}
      </dl>
      {region.kind === "loaded" ? null : <AdminRegionLine quiet kind={region.kind} />}
    </div>
  );
}

function BackupBody({ region }: { readonly region: AdminValueRegion<AdminBackupStatus> }) {
  const t = useTranslations("admin.overview.backup");
  const format = useFormatter();
  const data = region.kind === "loaded" ? region.data : null;

  return (
    <div className="jp-admincard__body">
      <dl className="jp-admindl">
        <Row label={t("latest")}>{data === null ? <AdminUnknown /> : (formatDateTime(format, data.latestAt) ?? <AdminUnknown />)}</Row>
        <Row label={t("offsite")}>{data === null ? <AdminUnknown /> : (formatDateTime(format, data.offsiteAt) ?? <AdminUnknown />)}</Row>
        <Row label={t("next")}>{data === null ? <AdminUnknown /> : (formatDateTime(format, data.nextAt) ?? <AdminUnknown />)}</Row>
        <Row label={t("retention")}>{data === null ? <AdminUnknown /> : t("retentionDays", { days: data.retentionDays })}</Row>
      </dl>
      {region.kind === "loaded" ? null : <AdminRegionLine quiet kind={region.kind} />}
    </div>
  );
}

function Row({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  );
}

function AttentionBody({
  region,
  basePath,
}: {
  readonly region: AdminRegion<ReadonlyArray<AdminAttentionItem>>;
  readonly basePath: string;
}) {
  const t = useTranslations("admin.overview.attention");
  if (region.kind !== "loaded") return <AdminRegionLine quiet kind={region.kind} empty={t("empty")} region />;
  if (region.data.length === 0) return <AdminRegionLine quiet kind="empty" empty={t("empty")} region />;
  return (
    <ul className="jp-adminattention">
      {region.data.map((item) => (
        <li key={item.kind}>
          <AlertTriangle size={18} aria-hidden="true" />
          <Link href={basePath + ATTENTION_PATH[item.kind]}>{t(item.kind, { count: item.count })}</Link>
        </li>
      ))}
    </ul>
  );
}

function EventsBody({ region }: { readonly region: AdminRegion<ReadonlyArray<AdminRecentEvent>> }) {
  const t = useTranslations("admin.overview.events");
  const format = useFormatter();
  if (region.kind !== "loaded") return <AdminRegionLine quiet kind={region.kind} empty={t("empty")} region />;
  if (region.data.length === 0) return <AdminRegionLine quiet kind="empty" empty={t("empty")} region />;
  return (
    <ol className="jp-adminevents">
      {region.data.map((event) => (
        <li key={event.id}>
          <time dateTime={event.occurredAt}>{formatDateTime(format, event.occurredAt) ?? <AdminUnknown />}</time>
          <span className={`jp-pill jp-pill--${EVENT_TONE[event.kind]}`}>{t(`kind.${event.kind}`)}</span>
          <span className="jp-adminevents__subject">{event.subject}</span>
        </li>
      ))}
    </ol>
  );
}
