import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import {
  Activity,
  HardDriveDownload,
  LogIn,
  Mail,
  Server,
  UserPlus,
  Users,
  type LucideIcon,
} from "lucide-react";
import { AdminCard, AdminCardLink } from "@/components/admin/admin-card";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { AdminSegmented } from "@/components/admin/admin-segmented";
import { ComingSoon } from "@/components/admin/coming-soon";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.overview");
  return { title: t("meta.title") };
}

const TREND_SOON_ID = "admin-overview-trend-soon";

/**
 * `/admin` — the admin overview, variant A (ADR 0150 D1). Its route group keeps a later
 * `loading.tsx` from wrapping the other admin pages.
 *
 * No source behind it exists yet, so every card renders its designed structure and says so
 * (ADR 0150 D2): an unknown number is an en-dash without its unit, an unknown list is one
 * "Kommer snart" line, and the attention edge stays neutral.
 */
export default async function AdminOverviewPage() {
  const t = await getTranslations("admin.overview");
  const unavailable = await getTranslations("admin.unavailable");
  const dash = unavailable("unknownValue");

  const kpis: ReadonlyArray<{ id: string; title: string; icon: LucideIcon }> = [
    { id: "admin-overview-new-users", title: t("kpi.newUsers"), icon: UserPlus },
    { id: "admin-overview-total", title: t("kpi.total"), icon: Users },
    { id: "admin-overview-active", title: t("kpi.active"), icon: Activity },
    { id: "admin-overview-logins", title: t("kpi.logins"), icon: LogIn },
  ];

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />

      <div className="jp-admingrid">
        {kpis.map((kpi) => (
          <AdminCard key={kpi.id} id={kpi.id} title={kpi.title} icon={kpi.icon} span={3}>
            <p className="jp-adminkpi">
              <span className="jp-adminkpi__value">{dash}</span>
            </p>
            <p className="jp-adminkpi__sub">{unavailable("comingSoon")}</p>
          </AdminCard>
        ))}

        <AdminCard
          id="admin-overview-trend"
          title={t("trend.title")}
          span={8}
          list
          aside={
            <AdminSegmented
              label={t("trend.periodLabel")}
              options={[
                { key: "d7", label: t("trend.periods.d7") },
                { key: "d30", label: t("trend.periods.d30") },
                { key: "d90", label: t("trend.periods.d90") },
              ]}
              selected="d30"
              disabled
              describedBy={TREND_SOON_ID}
            />
          }
        >
          <div className="jp-admintrend__plot">
            <span className="jp-admintrend__guide" aria-hidden="true" />
            <span className="jp-admintrend__guide" aria-hidden="true" />
            <span className="jp-admintrend__guide" aria-hidden="true" />
            <ComingSoon id={TREND_SOON_ID} />
          </div>
        </AdminCard>

        <AdminCard
          id="admin-overview-services"
          title={t("services.title")}
          span={4}
          list
          aside={<AdminCardLink href="/admin/loggar" label={t("services.link")} />}
        >
          <ComingSoon region />
        </AdminCard>

        <AdminCard id="admin-overview-server" title={t("server.title")} icon={Server} span={4}>
          <div className="jp-admincard__body">
            <dl className="jp-admindl">
              <dt>{t("server.cpu")}</dt>
              <dd>{dash}</dd>
              <dt>{t("server.memory")}</dt>
              <dd>{dash}</dd>
              <dt>{t("server.disk")}</dt>
              <dd>{dash}</dd>
            </dl>
            <ComingSoon />
          </div>
        </AdminCard>

        <AdminCard
          id="admin-overview-backup"
          title={t("backup.title")}
          icon={HardDriveDownload}
          span={4}
        >
          <div className="jp-admincard__body">
            <dl className="jp-admindl">
              <dt>{t("backup.latest")}</dt>
              <dd>{dash}</dd>
              <dt>{t("backup.offsite")}</dt>
              <dd>{dash}</dd>
              <dt>{t("backup.next")}</dt>
              <dd>{dash}</dd>
              <dt>{t("backup.retention")}</dt>
              <dd>{dash}</dd>
            </dl>
            <ComingSoon />
          </div>
        </AdminCard>

        <AdminCard
          id="admin-overview-email"
          title={t("email.title")}
          icon={Mail}
          span={4}
          aside={<AdminCardLink href="/admin/e-post" label={t("email.link")} />}
        >
          <p className="jp-adminkpi">
            <span className="jp-adminkpi__value">{dash}</span>
          </p>
          <p className="jp-adminkpi__sub">{unavailable("comingSoon")}</p>
        </AdminCard>

        <AdminCard
          id="admin-overview-attention"
          title={t("attention.title")}
          span={5}
          list
          attention="unknown"
        >
          <ComingSoon region />
        </AdminCard>

        <AdminCard
          id="admin-overview-events"
          title={t("events.title")}
          span={7}
          list
          aside={<AdminCardLink href="/admin/granskning" label={t("events.link")} />}
        >
          <ComingSoon region />
        </AdminCard>
      </div>
    </div>
  );
}
