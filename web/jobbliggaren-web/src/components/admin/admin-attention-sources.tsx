import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { backupAttention, servicesAttention, type ServiceRaised, type ServiceRow } from "@/lib/admin/attention";
import { formatStaleLimit } from "@/lib/admin/host-observation";
import type { AwaitingObservation, OverviewObservation } from "@/lib/admin/overview";
import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminRegionLine } from "./admin-region-line";

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
 * The attention card's lines for the Tjänster card (#2084): each row it shows in the warning form, named by its row
 * and in the card's own words, or the rows and the source it could not determine.
 */
export function ServicesAttentionLines({ observation }: { readonly observation: OverviewObservation<ServicesObservationDto> }) {
  const t = useTranslations("admin.overview");
  const format = useFormatter();
  const attention = servicesAttention(observation);

  if (attention.kind === "unread") {
    return <Unread label={t("services.title")}><AdminRegionLine kind={attention.why} quiet /></Unread>;
  }

  const limit = formatStaleLimit(format, attention.importOverdueAfterSeconds);

  /** The row's warning as its card words it; null when its time cannot be read, which the card shows as undetermined. */
  function words({ reading }: ServiceRaised): string | null {
    switch (reading.kind) {
      case "silent": {
        const time = formatDateTime(format, reading.heartbeatAt);
        return time === null ? null : t("services.worker.silent", { time });
      }
      case "noServer":
        return t("services.worker.noServer");
      case "unreachable":
        return t("services.store.unreachable");
      case "overdue": {
        const time = formatDateTime(format, reading.lastRunAt);
        return time === null ? null : t("services.import.overdue", { time, limit });
      }
      case "noneRecorded":
        return t("services.import.noneRecorded");
    }
  }

  const undetermined = (row: ServiceRow) => (
    <Unread key={row} label={t(`services.${row}`)}><p className="jp-adminsoon">{t("services.unknown")}</p></Unread>
  );

  return (
    <>
      {attention.raised.map((line) => {
        const state = words(line);
        return state === null ? undetermined(line.row) : (
          <Raised key={line.row} href="#admin-overview-services">
            {t("attention.sourceState", { source: t(`services.${line.row}`), state })}
          </Raised>
        );
      })}
      {attention.undetermined.map(undetermined)}
    </>
  );
}

/** A state that needs attention, linked to the card that shows it. */
function Raised({ href, children }: { readonly href: string; readonly children: ReactNode }) {
  return <p><a href={href} className="jp-adminoverview__link">{children}</a></p>;
}

/** What could not be read, in the form the attention card already uses for its other sources. */
function Unread({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return <div><p>{label}</p>{children}</div>;
}
