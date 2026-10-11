import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import type { OverviewObservation } from "@/lib/admin/overview";
import {
  formatServiceLimit,
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
import { unbroken } from "./admin-account-status";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";

/**
 * The Tjänster card's body (#1982, ADR 0150 D2): the Worker, the temporary store and the job ad import, each its own
 * row and its own state. No row is worded as working and none is coloured as healthy, because nothing here verifies
 * function: a positive state is a plain observation with its time, a state that needs attention reads in the
 * warning form with its words carrying it, and an undetermined row is an en-dash with one quiet line.
 *
 * The region carries the newest sample time and whether any row is old (`servicesRegion` builds it at the page's
 * clock and withdraws an old row as undetermined); the card prints them once, in the same note as the Server card.
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
  const unavailable = useTranslations("admin.unavailable");
  const format = useFormatter();
  const services = region.kind === "loaded" ? region.data : null;

  const warn = (text: ReactNode) => <span className="jp-admin-warning">{text}</span>;
  // The visible words say it, so the dash is not also spoken as "Uppgift saknas".
  const undetermined = (
    <><span aria-hidden="true">{unavailable("unknownValue")}</span> <span className="jp-admindl__aside">{t("unknown")}</span></>
  );

  /**
   * A row whose sentence carries a time, kept whole on one line; an instant the formatter cannot read is undetermined,
   * never guessed.
   */
  function at(instant: string, render: (time: string) => ReactNode): ReactNode {
    const time = formatDateTime(format, instant, "\u00A0");
    return time === null ? undetermined : render(time);
  }

  /**
   * How long ago `instant` was. A row shown here is young (`servicesRegion` withdraws an old one); an instant ahead of
   * the page's clock gets no age.
   */
  function age(instant: string): ReactNode {
    if (now === undefined || now <= 0 || Date.parse(instant) > now) return null;
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
        return at(row.heartbeatAt, (time) => <>{withWholeInstant(t("worker.alive", { time }), time)}{age(row.heartbeatAt)}</>);
      case "silent":
        return at(row.heartbeatAt, (time) => warn(withWholeInstant(t("worker.silent", { time }), time)));
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
          <>{withWholeInstant(t("import.recorded", { time }), time)}{age(row.lastRunAt)}{errors(row.errors)}</>
        ));
      case "overdue":
        return at(row.lastRunAt, (time) => {
          const limit = formatServiceLimit(format, overdueAfterSeconds);
          return <>{warn(withWholeInstant(t("import.overdue", { time, limit }), time))}{errors(row.errors)}</>;
        });
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
              {t("stale", { limit: formatServiceLimit(format, services.staleAfterMs / 1000) })}
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
 * The card from the overview's services observation. A row kept after a failed refresh stays while its own sample is
 * within the limit the API applied, and is withdrawn as undetermined once it is older on the page's clock.
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

/**
 * `text` with the instant `time` kept on one line wherever the sentence places it (the house `unbroken`). The no-break
 * space keeps the time with its date, but a line could still break after a hyphen inside the date.
 */
export function withWholeInstant(text: string, time: string): ReactNode {
  const start = text.indexOf(time);
  if (start < 0) return text;
  return <>{text.slice(0, start)}{unbroken(time)}{text.slice(start + time.length)}</>;
}

function Row({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  );
}
