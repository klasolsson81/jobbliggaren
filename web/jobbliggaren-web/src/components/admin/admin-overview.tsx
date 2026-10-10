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
  type AdminServiceStatus,
  type AdminValueRegion,
} from "@/lib/admin/view-models";
import { accountsHref, type AdminOverviewSnapshot } from "@/lib/admin/overview";
import type { RegistrationPeriod } from "@/lib/dto/admin-overview";
import { AdminObservationNote } from "./admin-observation-note";
import { AdminCard, AdminCardLink, type AdminCardSpan } from "./admin-card";
import { AdminPageHeader } from "./admin-page-header";
import { AdminRegionLine } from "./admin-region-line";
import { AdminServerBody } from "./admin-server-body";
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
  regions: suppliedRegions = ADMIN_OVERVIEW_UNAVAILABLE,
  observations,
  now = 0,
}: {
  readonly basePath?: string;
  readonly regions?: AdminOverviewRegions;
  readonly observations?: AdminOverviewSnapshot;
  readonly now?: number;
}) {
  const t = useTranslations("admin.overview");
  const shared = useTranslations("admin.regions");
  const format = useFormatter();
  const regions = observations ? overviewRegions(observations) : suppliedRegions;
  const accountData = observations?.accounts.kind === "loaded" ? observations.accounts.data : null;
  const kinds = new Set(Object.values(observations ?? regions).map((region) => region.kind));

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
          note={observations ? <><p className="jp-adminkpi__sub">{t("population")}</p><AdminObservationNote observation={observations.accounts} now={now} /></> : undefined}
          region={regions.newAccounts}
          render={(data) => ({
            value: data.today,
            href: accountData ? accountsHref(basePath, accountData.newAccounts.today) : undefined,
            unit: t("kpi.unit.today"),
            sub: accountData ? <RegistrationLinks basePath={basePath} periods={accountData.newAccounts} /> : t("kpi.newUsersSub", {
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
          note={observations ? <AdminObservationNote observation={observations.accounts} now={now} /> : undefined}
          region={regions.totals}
          render={(data) => ({
            value: data.total,
            href: observations ? accountsHref(basePath) : undefined,
            unit: t("kpi.unit.accounts"),
            sub: accountData ? <AccountStatusLinks basePath={basePath} counts={accountData.counts} />
              : t("kpi.totalSub", { suspended: data.suspended, pendingDeletion: data.pendingDeletion }),
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

        <AdminTrendCard region={regions.trend} note={observations ? <AdminObservationNote observation={observations.accounts} now={now} /> : undefined} />

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
          <AdminServerBody region={regions.server} />
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
          attention={observations ? overviewAttentionState(observations) : attentionState(regions.attention)}
        >
          {observations ? <ObservedAttention observations={observations} now={now} basePath={basePath} />
            : <AttentionBody region={regions.attention} basePath={basePath} />}
        </AdminCard>

        <AdminCard
          id="admin-overview-events"
          title={t("events.title")}
          span={7}
          list
          aside={<AdminCardLink href={`${basePath}/granskning`} label={t("events.link")} />}
        >
          {observations ? <ObservedEvents observations={observations} now={now} /> : <EventsBody region={regions.events} />}
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
  readonly href?: string;
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
  note,
}: {
  readonly id: string;
  readonly title: string;
  readonly icon: LucideIcon;
  readonly span: AdminCardSpan;
  readonly aside?: ReactNode;
  readonly region: AdminValueRegion<T>;
  readonly render: (data: T) => ShownValue;
  readonly note?: ReactNode;
}) {
  const format = useFormatter();

  if (region.kind !== "loaded") {
    return (
      <AdminCard id={id} title={title} icon={icon} span={span} aside={aside}>
        <p className="jp-adminkpi">
          <span className="jp-adminkpi__value"><AdminUnknown /></span>
        </p>
        <AdminRegionLine quiet kind={region.kind} className="jp-adminkpi__sub" />
        {note}
      </AdminCard>
    );
  }

  const shown = render(region.data);
  return (
    <AdminCard id={id} title={title} icon={icon} span={span} aside={aside}>
      <p className="jp-adminkpi">
        <span className="jp-adminkpi__value">{shown.href ? <Link href={shown.href} className="jp-adminoverview__link">{format.number(shown.value)}</Link> : format.number(shown.value)}</span>
        <span className="jp-adminkpi__unit">{shown.unit}</span>
      </p>
      <p className="jp-adminkpi__sub">{shown.sub}</p>
      {shown.extra}
      {note}
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

function overviewRegions(snapshot: AdminOverviewSnapshot): AdminOverviewRegions {
  const account = snapshot.accounts;
  const loaded = account.kind === "loaded" || account.kind === "empty";
  const failedOrLoading = loaded ? "failed" : account.kind;
  return {
    ...ADMIN_OVERVIEW_UNAVAILABLE,
    newAccounts: loaded ? { kind: "loaded", data: {
      today: account.data.newAccounts.today.count, yesterday: account.data.newAccounts.yesterday.count,
      last7Days: account.data.newAccounts.last7Days.count, last30Days: account.data.newAccounts.last30Days.count,
    } } : { kind: failedOrLoading },
    totals: loaded ? { kind: "loaded", data: account.data.counts } : { kind: failedOrLoading },
    trend: loaded ? { kind: "loaded", data: account.data.days.map((day) => ({ ...day, logins: null })) } : { kind: failedOrLoading },
    events: { kind: snapshot.audit.kind === "loaded" ? "empty" : snapshot.audit.kind },
    attention: { kind: snapshot.jobs.kind === "failed" || account.kind === "failed" ? "failed" : "loading" },
  };
}

function RegistrationLinks({ basePath, periods }: {
  readonly basePath: string;
  readonly periods: Readonly<Record<"today" | "yesterday" | "last7Days" | "last30Days", RegistrationPeriod>>;
}) {
  const t = useTranslations("admin.overview.registrationPeriods");
  return <>{(["yesterday", "last7Days", "last30Days"] as const).map((key, index) =>
    <span key={key}>{index ? " · " : ""}<Link href={accountsHref(basePath, periods[key])} className="jp-adminoverview__link">{t(key, { count: periods[key].count })}</Link></span>)}</>;
}

function AccountStatusLinks({ basePath, counts }: {
  readonly basePath: string;
  readonly counts: Readonly<Record<"active" | "pendingDeletion" | "profileMissing" | "suspended", number>>;
}) {
  const t = useTranslations("admin.overview.accountStatus");
  const statuses = { active: "Active", pendingDeletion: "PendingDeletion", profileMissing: "ProfileMissing", suspended: "Suspended" } as const;
  return <>{(Object.keys(statuses) as Array<keyof typeof statuses>).map((key, index) =>
    <span key={key}>{index ? " · " : ""}<Link href={accountsHref(basePath, undefined, statuses[key])} className="jp-adminoverview__link">{t(key, { count: counts[key] })}</Link></span>)}</>;
}

function overviewAttentionState(snapshot: AdminOverviewSnapshot): "unknown" | "raised" {
  const pending = snapshot.accounts.kind === "loaded" && snapshot.accounts.data.counts.pendingDeletion > 0;
  const failed = snapshot.jobs.kind === "loaded" && snapshot.jobs.data.totalCount > 0;
  return pending || failed ? "raised" : "unknown";
}

function ObservedAttention({ observations, now, basePath }: {
  readonly observations: AdminOverviewSnapshot;
  readonly now: number;
  readonly basePath: string;
}) {
  const t = useTranslations("admin.overview.attention");
  const { accounts, jobs } = observations;
  return <div className="jp-admincard__body">
    {jobs.kind === "loaded" || jobs.kind === "empty" ? <p>{jobs.data.totalCount > 0
      ? <Link href={`${basePath}/jobb#failed-jobs`} className="jp-adminoverview__link">{t("failedJobs", { count: jobs.data.totalCount })}</Link>
      : t("noFailedJobs")}</p> : <div><p>{t("jobsSource")}</p><AdminRegionLine kind={jobs.kind} quiet /></div>}
    <AdminObservationNote observation={jobs} now={now} />
    {accounts.kind === "loaded" || accounts.kind === "empty" ? <p>{accounts.data.counts.pendingDeletion > 0
      ? <Link href={accountsHref(basePath, undefined, "PendingDeletion")} className="jp-adminoverview__link">{t("pendingDeletions", { count: accounts.data.counts.pendingDeletion })}</Link>
      : t("noPendingDeletions")}</p> : <div><p>{t("deletionsSource")}</p><AdminRegionLine kind={accounts.kind} quiet /></div>}
    <AdminObservationNote observation={accounts} now={now} />
    <p>{t("emailUnknown")}</p>
  </div>;
}

function ObservedEvents({ observations, now }: { readonly observations: AdminOverviewSnapshot; readonly now: number }) {
  const t = useTranslations("admin.overview.events");
  const format = useFormatter();
  const audit = observations.audit;
  if (audit.kind === "failed" || audit.kind === "loading") return <AdminRegionLine quiet kind={audit.kind} region />;
  return <>
    {audit.data.length === 0 ? <AdminRegionLine quiet kind="empty" empty={t("empty")} region /> : <ol className="jp-adminevents jp-adminevents--observed">
      {audit.data.map((event) => <li key={event.id}>
        <time dateTime={event.occurredAt}>{formatDateTime(format, event.occurredAt) ?? <AdminUnknown />}</time>
        <code className="jp-adminevents__subject">{event.eventType}</code>
        <span className="jp-adminevents__subject">{event.aggregateType} · {event.aggregateId}</span>
      </li>)}
    </ol>}
    <AdminObservationNote observation={audit} now={now} />
  </>;
}
