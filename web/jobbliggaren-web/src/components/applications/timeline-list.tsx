import { useFormatter, useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  channelLabel,
  followUpOutcomeLabel,
} from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { TimelineEvent } from "@/lib/applications/timeline";

/**
 * Renders composed timeline events as `.jp-timeline__list` (datum mono + text),
 * resolving each event's Swedish label via next-intl. Pure presentational Server
 * Component. Shared by the read-mode detail-modal body (always-open) and the
 * full-page ApplicationDetail (collapsed inside a native <details>) so the event→label
 * knowledge lives in ONE place (DRY). The caller owns the disclosure wrapper and
 * the section heading; this renders only the list.
 */
export function TimelineList({
  events,
}: {
  events: ReadonlyArray<TimelineEvent>;
}) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();

  function labelFor(event: TimelineEvent): string {
    switch (event.kind) {
      case "created":
        return tUi("detail.eventCreated");
      case "note":
        return tUi("detail.eventNoteAdded");
      case "followUpScheduled":
        return tUi("detail.eventFollowUpScheduled", {
          channel: channelLabel(t, event.channel),
        });
      case "followUpOutcome":
        return tUi("detail.eventOutcome", {
          outcome: followUpOutcomeLabel(t, event.outcome),
        });
      case "statusChange":
        return tUi("detail.eventStatusChange", {
          from: applicationStatusLabel(t, event.from),
          to: applicationStatusLabel(t, event.to),
        });
    }
  }

  return (
    <ul className="jp-timeline__list">
      {events.map((event, i) => (
        <li key={`${event.at}-${i}`} className="jp-timeline__item">
          <span className="jp-mono jp-timeline__date">
            {formatDate(format, event.at) ?? ""}
          </span>
          <span className="jp-timeline__label">{labelFor(event)}</span>
        </li>
      ))}
    </ul>
  );
}
