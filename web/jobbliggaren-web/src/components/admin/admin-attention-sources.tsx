import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { backupAttention, servicesAttention, type ServiceRaised, type ServiceRow } from "@/lib/admin/attention";
import type { AwaitingObservation, OverviewObservation } from "@/lib/admin/overview";
import { formatServiceLimit } from "@/lib/admin/service-observation";
import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminRegionLine } from "./admin-region-line";
import { withWholeInstant } from "./admin-services-body";

/**
 * The attention card's lines for the Backup card (#2084): each state it shows in the warning form, in the card's own
 * words and linked to it, or why the source could not be read. A good state adds no line, and nothing is said to be
 * in order.
 */
export function BackupAttentionLines({ observation }: { readonly observation: AwaitingObservation<BackupObservationData> }) {
  const t = useTranslations("admin.overview");
  const shared = useTranslations("admin");
  const attention = backupAttention(observation);
  const source = t("backup.title");

  if (attention.kind === "unread") {
    return (
      <Unread label={source}>
        {attention.why === "awaiting"
          ? <p className="jp-adminsoon">{shared("regions.awaiting")}</p>
          : <AdminRegionLine kind={attention.why} quiet />}
      </Unread>
    );
  }
  return (
    <>
      {attention.raised.map((key) => (
        <Raised key={key} href="#admin-overview-backup">{t("attention.sourceState", { source, state: t(`backup.${key}`) })}</Raised>
      ))}
      {attention.timerUnknown ? (
        <Unread label={t("attention.sourceState", { source, state: t("backup.next") })}>
          <p className="jp-adminsoon">{shared("unavailable.unknownSpoken")}</p>
        </Unread>
      ) : null}
    </>
  );
}

/**
 * The attention card's lines for the Tjänster card (#2084): each warning it shows, named by its row and in the card's
 * own words, or the rows and the source it could not determine. It reads the rows at the page's clock as the card
 * does, so a row the card has withdrawn as too old is undetermined here too.
 */
export function ServicesAttentionLines({ observation, now }: {
  readonly observation: OverviewObservation<ServicesObservationDto>;
  readonly now: number;
}) {
  const t = useTranslations("admin.overview");
  const format = useFormatter();
  const attention = servicesAttention(observation, now);

  if (attention.kind === "unread") {
    return <Unread label={t("services.title")}><AdminRegionLine kind={attention.why} quiet /></Unread>;
  }

  const limit = formatServiceLimit(format, attention.importOverdueAfterSeconds);

  /**
   * The warning as its card words it, with the instant it carries, which the line keeps whole as the card does; null
   * when that instant cannot be read, which the card shows as undetermined.
   */
  function words(line: ServiceRaised): { readonly state: string; readonly time?: string } | null {
    switch (line.kind) {
      case "silent": {
        const time = formatDateTime(format, line.heartbeatAt, "\u00A0");
        return time === null ? null : { state: t("services.worker.silent", { time }), time };
      }
      case "noServer":
        return { state: t("services.worker.noServer") };
      case "unreachable":
        return { state: t("services.store.unreachable") };
      case "overdue": {
        const time = formatDateTime(format, line.lastRunAt, "\u00A0");
        return time === null ? null : { state: t("services.import.overdue", { time, limit }), time };
      }
      case "noneRecorded":
        return { state: t("services.import.noneRecorded") };
      case "errors":
        return { state: t("services.import.errors", { count: line.count }) };
    }
  }

  const undetermined = (row: ServiceRow) => (
    <Unread key={row} label={t(`services.${row}`)}><p className="jp-adminsoon">{t("services.unknown")}</p></Unread>
  );

  return (
    <>
      {attention.raised.map((line) => {
        const said = words(line);
        if (said === null) return undetermined(line.row);
        const text = t("attention.sourceState", { source: t(`services.${line.row}`), state: said.state });
        return (
          <Raised key={`${line.row}.${line.kind}`} href="#admin-overview-services">
            {said.time === undefined ? text : withWholeInstant(text, said.time)}
          </Raised>
        );
      })}
      {attention.undetermined.map(undetermined)}
    </>
  );
}

/**
 * A state that needs attention, linked to the card that shows it. The words sit in one span: the link is a flex box,
 * and a line with an unbroken instant in it would otherwise be laid out as separate items side by side.
 */
function Raised({ href, children }: { readonly href: string; readonly children: ReactNode }) {
  return <p><a href={href} className="jp-adminoverview__link"><span>{children}</span></a></p>;
}

/** What could not be read, in the form the attention card already uses for its other sources. */
function Unread({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return <div><p>{label}</p>{children}</div>;
}
