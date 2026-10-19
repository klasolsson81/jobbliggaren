import { useFormatter, useTranslations } from "next-intl";
import { formatDateTime } from "@/lib/i18n/format";
import { OVERVIEW_STALE_MS, type AwaitingObservation } from "@/lib/admin/overview";

export function AdminObservationNote({ observation, now, label = "sampledAt" }: {
  readonly observation: AwaitingObservation<unknown>;
  readonly now: number;
  /** "Hämtat" dates the delivery read as the e-post page's Leverans does; every other source says "Uppgift från". */
  readonly label?: "sampledAt" | "fetchedAt";
}) {
  const t = useTranslations("admin.overview.observation");
  const format = useFormatter();
  if (observation.kind !== "loaded" && observation.kind !== "empty") return null;
  const stale = now - Date.parse(observation.sampledAt) > OVERVIEW_STALE_MS;
  return (
    <p className="jp-adminkpi__sub">
      {t(label)} <time dateTime={observation.sampledAt}>{formatDateTime(format, observation.sampledAt)}</time>
      {observation.refreshFailed ? <><br />{t("refreshFailed")}</> : null}
      {stale ? <><br />{t("stale")}</> : null}
    </p>
  );
}
