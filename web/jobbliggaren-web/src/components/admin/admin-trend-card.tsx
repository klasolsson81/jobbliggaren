"use client";

// "use client": the card holds the chosen period.
import { useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import type { AdminTrendDay, AdminValueRegion } from "@/lib/admin/view-models";
import {
  ADMIN_TREND_PERIODS,
  summarizeTrend,
  trendWindow,
  type AdminTrendPeriod,
} from "@/lib/admin/trend";
import { AdminCard } from "./admin-card";
import { AdminRegionLine } from "./admin-region-line";
import { AdminSegment } from "./admin-segment";

const SOON_ID = "admin-overview-trend-soon";
const PLOT_HEIGHT = 200;
const HEADROOM = 12;
const SLOT = 10;

function Guides() {
  return (
    <>
      <span className="jp-admintrend__guide" aria-hidden="true" />
      <span className="jp-admintrend__guide" aria-hidden="true" />
      <span className="jp-admintrend__guide" aria-hidden="true" />
    </>
  );
}

/**
 * New accounts and logins per day (ADR 0150 D1): hand-built SVG, no chart library. The drawing is
 * decorative; the sentence under it carries what it shows, on each series' own scale. The period
 * switches only while the series is loaded.
 */
export function AdminTrendCard({
  region,
}: {
  readonly region: AdminValueRegion<ReadonlyArray<AdminTrendDay>>;
}) {
  const t = useTranslations("admin.overview.trend");
  const [period, setPeriod] = useState<AdminTrendPeriod>("d30");
  const loaded = region.kind === "loaded";

  return (
    <AdminCard
      id="admin-overview-trend"
      title={t("title")}
      span={8}
      list
      aside={
        <AdminSegment
          label={t("periodLabel")}
          options={ADMIN_TREND_PERIODS.map((value) => ({ value, label: t(`periods.${value}`) }))}
          value={period}
          onChange={loaded ? setPeriod : undefined}
          describedBy={region.kind === "unavailable" ? SOON_ID : undefined}
        />
      }
    >
      {region.kind === "loaded" ? (
        <TrendPlot days={trendWindow(region.data, period)} />
      ) : (
        <div className="jp-admintrend__plot">
          <Guides />
          <AdminRegionLine kind={region.kind} soonId={SOON_ID} quiet />
        </div>
      )}
    </AdminCard>
  );
}

function TrendPlot({ days }: { readonly days: ReadonlyArray<AdminTrendDay> }) {
  const t = useTranslations("admin.overview.trend");
  const format = useFormatter();
  const summary = summarizeTrend(days);

  const maxNew = Math.max(1, ...days.map((day) => day.newAccounts));
  const maxLogins = Math.max(1, ...days.map((day) => day.logins));
  const height = (value: number, max: number) => (value / max) * (PLOT_HEIGHT - HEADROOM);
  const gap = days.length > 30 ? 1 : 2;
  const line = days
    .map((day, index) => `${index * SLOT + SLOT / 2},${PLOT_HEIGHT - height(day.logins, maxLogins)}`)
    .join(" ");
  const label = (date: string) =>
    format.dateTime(new Date(`${date}T12:00:00Z`), { day: "numeric", month: "short" });
  // The sentence ends on its date, so the month is written out: "sep." would end it twice.
  const longLabel = (date: string) =>
    format.dateTime(new Date(`${date}T12:00:00Z`), { day: "numeric", month: "long" });
  const first = days[0];
  const middle = days[Math.floor((days.length - 1) / 2)];

  return (
    <>
      <ul className="jp-admintrend__legend" aria-hidden="true">
        <li>
          <span className="jp-admintrend__swatch jp-admintrend__swatch--bar" aria-hidden="true" />
          {t("legend.newAccounts")}
        </li>
        <li>
          <span className="jp-admintrend__swatch jp-admintrend__swatch--line" aria-hidden="true" />
          {t("legend.logins")}
        </li>
      </ul>
      <div className="jp-admintrend__plot">
        <Guides />
        <svg
          className="jp-admintrend__svg"
          viewBox={`0 0 ${Math.max(days.length, 1) * SLOT} ${PLOT_HEIGHT}`}
          preserveAspectRatio="none"
          aria-hidden="true"
          focusable="false"
        >
          {days.map((day, index) => {
            const barHeight = height(day.newAccounts, maxNew);
            return (
              <rect
                key={day.date}
                className="jp-admintrend__bar"
                x={index * SLOT + gap}
                y={PLOT_HEIGHT - barHeight}
                width={SLOT - gap * 2}
                height={barHeight}
              />
            );
          })}
          <polyline className="jp-admintrend__line" points={line} vectorEffect="non-scaling-stroke" />
        </svg>
      </div>
      {first === undefined || middle === undefined ? null : (
        <div className="jp-admintrend__axis" aria-hidden="true">
          <span>{label(first.date)}</span>
          <span>{label(middle.date)}</span>
          <span>{t("today")}</span>
        </div>
      )}
      <p className="jp-admintrend__summary">
        {t("summary", { days: summary.days, newAccounts: summary.newAccounts, logins: summary.logins })}
        {summary.newAccountsPeak.count === 0
          ? null
          : summary.newAccountsPeak.date === null
            ? ` ${t("peakNewAccountsShared", { count: summary.newAccountsPeak.count })}`
            : ` ${t("peakNewAccounts", {
                count: summary.newAccountsPeak.count,
                date: longLabel(summary.newAccountsPeak.date),
              })}`}
        {summary.loginsPeak.count === 0
          ? null
          : summary.loginsPeak.date === null
            ? ` ${t("peakLoginsShared", { count: summary.loginsPeak.count })}`
            : ` ${t("peakLogins", { count: summary.loginsPeak.count, date: longLabel(summary.loginsPeak.date) })}`}
      </p>
    </>
  );
}
