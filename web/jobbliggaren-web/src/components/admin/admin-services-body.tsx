import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatStaleLimit } from "@/lib/admin/host-observation";
import type { OverviewObservation } from "@/lib/admin/overview";
import {
  servicesRegion,
  toServicesReading,
  type AdminJobImportReading,
  type AdminServicesReading,
  type AdminVolatileStoreReading,
  type AdminWorkerReading,
} from "@/lib/admin/service-observation";
import type { AdminValueRegion } from "@/lib/admin/view-models";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";

/**
 * The Tjänster card's body (#1982, ADR 0150 D2): the Worker, the temporary store and the job ad import, each its own
 * row and its own state. No row is worded as working and none is coloured as healthy, because nothing here verifies
 * function: a positive state is a plain observation with its time, a state that needs attention reads in the
 * warning form with its words carrying it, and an undetermined row is an en-dash with one quiet line.
 *
 * The region carries the newest sample time and whether any row is old (`servicesRegion` builds it, aged to the
 * page's clock); the card prints them once, in the same note as the Server card.
 */
export function AdminServicesBody({ region, refreshFailed = false, now }: {
  readonly region: AdminValueRegion<AdminServicesReading>;
  /** The last refresh failed, so the rows shown are the last good ones (ADR 0150 Amendment 2026-10-08). */
  readonly refreshFailed?: boolean;
  /** The page's clock, for the age of a positive row. The preview has none, and then no age is shown. */
  readonly now?: number;
}) {
  const t = useTranslations("admin.overview.services");
  const observed = useTranslations("admin.overview.observation");
  const format = useFormatter();
  const services = region.kind === "loaded" ? region.data : null;

  const warn = (text: string) => <span className="jp-admin-warning">{text}</span>;
  const undetermined = <><AdminUnknown /> <span className="jp-admindl__aside">{t("unknown")}</span></>;

  /** A row whose sentence carries a time; an instant the formatter cannot read is undetermined, never guessed. */
  function at(instant: string, render: (time: string) => ReactNode): ReactNode {
    const time = formatDateTime(format, instant);
    return time === null ? undetermined : render(time);
  }

  /**
   * How long ago `instant` was, while the sample behind it is young. An old sample keeps its own time and loses its
   * age, which would otherwise read as current; an instant ahead of the page's clock gets none either.
   */
  function age(instant: string, stale: boolean): ReactNode {
    if (now === undefined || now <= 0 || stale || Date.parse(instant) > now) return null;
    return (
      <>
        {" "}
        <span className="jp-admindl__aside jp-admindl__age">({format.relativeTime(new Date(instant), now)})</span>
      </>
    );
  }

  function worker(row: AdminWorkerReading): ReactNode {
    switch (row.kind) {
      case "alive":
        return at(row.heartbeatAt, (time) => <>{t("worker.alive", { time })}{age(row.heartbeatAt, row.stale)}</>);
      case "silent":
        return at(row.heartbeatAt, (time) => warn(t("worker.silent", { time })));
      case "noServer":
        return warn(t("worker.noServer"));
      case "unknown":
        return undetermined;
    }
  }

  function store(row: AdminVolatileStoreReading): ReactNode {
    switch (row.kind) {
      case "reachable":
        return t("store.reachable");
      case "unreachable":
        return warn(t("store.unreachable"));
      case "unknown":
        return undetermined;
    }
  }

  function jobImport(row: AdminJobImportReading, overdueAfterSeconds: number): ReactNode {
    // A run without errors says nothing about them: "0 fel" would read as a verdict on the import.
    const errors = (count: number) => (count > 0 ? <><br />{warn(t("import.errors", { count }))}</> : null);
    switch (row.kind) {
      case "recorded":
        return at(row.lastRunAt, (time) => (
          <>{t("import.recorded", { time })}{age(row.lastRunAt, row.stale)}{errors(row.errors)}</>
        ));
      case "overdue":
        return at(row.lastRunAt, (time) => (
          <>{warn(t("import.overdue", { time, limit: formatStaleLimit(format, overdueAfterSeconds) }))}{errors(row.errors)}</>
        ));
      case "noneRecorded":
        return warn(t("import.noneRecorded"));
      case "unknown":
        return undetermined;
    }
  }

  return (
    <div className="jp-admincard__body">
      <dl className="jp-admindl">
        <Row label={t("backgroundJobs")}>{services ? worker(services.backgroundJobs) : <AdminUnknown />}</Row>
        <Row label={t("volatileStore")}>{services ? store(services.volatileStore) : <AdminUnknown />}</Row>
        <Row label={t("jobImport")}>
          {services ? jobImport(services.jobImport, services.importOverdueAfterSeconds) : <AdminUnknown />}
        </Row>
      </dl>
      {region.kind !== "loaded" ? <AdminRegionLine quiet kind={region.kind} /> : null}
      {region.kind === "loaded" && region.sampledAt ? (
        <p className="jp-adminkpi__sub">
          {observed("sampledAt")} <time dateTime={region.sampledAt}>{formatDateTime(format, region.sampledAt)}</time>
          {refreshFailed ? <><br />{observed("refreshFailed")}</> : null}
          {region.stale && services ? (
            <>
              <br />
              {t("stale", { limit: formatStaleLimit(format, services.staleAfterMs / 1000) })}
            </>
          ) : null}
        </p>
      ) : refreshFailed ? (
        <p className="jp-adminkpi__sub">{observed("refreshFailed")}</p>
      ) : null}
    </div>
  );
}

/**
 * The card from the overview's services observation. The rows keep the time their sources were sampled and age by
 * the limit the API applied, so a row kept after a failed refresh goes old on the page's clock.
 */
export function AdminObservedServices({ observation, now }: {
  readonly observation: OverviewObservation<ServicesObservationDto>;
  readonly now: number;
}) {
  if (observation.kind === "loaded" || observation.kind === "empty") {
    return (
      <AdminServicesBody
        region={servicesRegion(toServicesReading(observation.data), now)}
        refreshFailed={observation.refreshFailed}
        now={now}
      />
    );
  }
  return <AdminServicesBody region={{ kind: observation.kind }} />;
}

function Row({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  );
}
