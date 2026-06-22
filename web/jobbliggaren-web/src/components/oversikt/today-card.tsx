import { Calendar } from "lucide-react";
import { useTranslations } from "next-intl";
import { formatSwedishLongDate } from "@/lib/oversikt/aggregations";
import type { OversiktTodayEvent } from "@/lib/oversikt/mock-data";

interface TodayCardProps {
  readonly today: Date;
  readonly events: ReadonlyArray<OversiktTodayEvent>;
  readonly googleSynced: boolean;
  readonly syncedAt?: string;
}

/**
 * "I dag"-kort i Översikt-toppen. Server Component — ren render utan state.
 *
 * Tre tillstånd per HANDOVER §2.3:
 * - inga events ⇒ italic "Inget planerat i dag."
 * - Jobbliggaren-events ⇒ lista + fot "Google Calendar inte synkad"
 * - Google synkad (framtid) ⇒ events får --google-variant + fot "Synkad ..."
 */
export function TodayCard({
  today,
  events,
  googleSynced,
  syncedAt,
}: TodayCardProps) {
  const t = useTranslations("oversikt");
  const { day, weekday, monthYear } = formatSwedishLongDate(today);

  return (
    <div className="jp-oversikt__today">
      <div className="jp-oversikt__today__head">
        <div className="jp-oversikt__today__kicker">{t("today.kicker")}</div>
        <div className="jp-oversikt__today__date">
          <span className="jp-oversikt__today__day">{day}</span>
          <span className="jp-oversikt__today__rest">
            <span className="jp-oversikt__today__weekday">{weekday}</span>
            <span className="jp-oversikt__today__month">{monthYear}</span>
          </span>
        </div>
      </div>

      {events.length === 0 ? (
        <div className="jp-oversikt__today__empty">{t("today.empty")}</div>
      ) : (
        <ul className="jp-oversikt__today__list">
          {events.map((ev) => (
            <li
              key={ev.id}
              className={`jp-oversikt__today__event jp-oversikt__today__event--${ev.source}`}
            >
              <span className="jp-oversikt__today__time">{ev.time}</span>
              <span className="jp-oversikt__today__title">{ev.title}</span>
              {ev.where && (
                <span className="jp-oversikt__today__where">{ev.where}</span>
              )}
            </li>
          ))}
        </ul>
      )}

      <div className="jp-oversikt__today__foot">
        <Calendar size={12} aria-hidden="true" />
        <span>
          {googleSynced
            ? t("today.footSynced", { syncedAt: syncedAt ?? "" })
            : t("today.footNotSynced")}
        </span>
      </div>
    </div>
  );
}
