import Link from "next/link";
import { useTranslations } from "next-intl";
import { Inbox } from "lucide-react";
import {
  applicationBars,
  type ApplicationBar,
} from "@/lib/applications/application-bars";
import { countByStatus } from "@/lib/applications/pipeline-counts";
import { applicationStatusLabel } from "@/lib/applications/status";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import { OversiktCard, OversiktCardFoot, OversiktNumber } from "./oversikt-card";

interface ApplicationsCardProps {
  /**
   * The pipeline as a Result, never degraded to `[]`: the card must tell "no applications" from
   * "could not be read", and only a Result carries that difference.
   */
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
}

const ID = "oversikt-card-applications";

/**
 * "Mina ansökningar" (ADR 0140): the active count as the big number, one bar per pipeline step
 * beneath it, the terminal statuses rolled into one line, and an outline CTA to the list — outline
 * because it leads to a list, not to ads; the solid level is for a card whose number counts ads.
 *
 * The empty state is the one place on the page a create-link is allowed, and only when the count
 * is zero: with a live pipeline the way to a new application is `/ansokningar`.
 */
export function ApplicationsCard({ pipeline }: ApplicationsCardProps) {
  const t = useTranslations("oversikt");
  const tEnum = useTranslations("applications.enums");
  const tCounts = useTranslations("applications.ui");
  const title = t("cards.applications");

  if (pipeline.kind !== "ok") {
    return (
      <OversiktCard id={ID} title={title} tone="plain" span={4} icon={Inbox}>
        <OversiktNumber value={null} />
        <p className="jp-ov-card__unavailable">{t("summary.unavailable")}</p>
      </OversiktCard>
    );
  }

  const bars = applicationBars(countByStatus(pipeline.data));

  if (bars.total === 0) {
    return (
      <OversiktCard id={ID} title={title} tone="plain" span={4} icon={Inbox}>
        <p className="jp-ov-card__emptytitle">{t("summary.emptyTitle")}</p>
        <OversiktCardFoot>
          {/* Emphasised, not solid: the solid level belongs to a card whose number counts ads. */}
          <Link className="jp-btn jp-btn--emphasis jp-ov-cta" href="/ny-ansokan">
            {t("summary.emptyCta")}
          </Link>
        </OversiktCardFoot>
      </OversiktCard>
    );
  }

  // The interview bar sums two statuses, so it cannot borrow either status's enum label.
  const barLabel = (bar: ApplicationBar): string =>
    bar.key === "interview"
      ? t("cards.interviewStep")
      : applicationStatusLabel(tEnum, bar.statuses[0]!);

  return (
    <OversiktCard id={ID} title={title} tone="plain" span={4} icon={Inbox}>
      <OversiktNumber
        value={bars.active}
        unit={t("cards.activeOf", { total: bars.total })}
      />
      <ul className="jp-ov-bars" aria-label={t("summary.stepsAriaLabel")}>
        {bars.rows.map((bar) => (
          <li
            key={bar.key}
            className="jp-ov-bars__row"
            data-empty={bar.count === 0 ? "true" : undefined}
          >
            <span className="jp-ov-bars__name">{barLabel(bar)}</span>
            <span className="jp-ov-bars__track" aria-hidden="true">
              <span
                className="jp-ov-bars__fill"
                data-tone={bar.key}
                style={{ width: `${Math.round(bar.fraction * 100)}%` }}
              />
            </span>
            <span className="jp-ov-bars__num tabular-nums">{bar.count}</span>
          </li>
        ))}
        <li
          className="jp-ov-bars__row jp-ov-bars__row--terminal"
          data-empty={bars.terminal === 0 ? "true" : undefined}
        >
          <span className="jp-ov-bars__name">{tCounts("counts.terminalGroup")}</span>
          <span aria-hidden="true" />
          <span className="jp-ov-bars__num tabular-nums">{bars.terminal}</span>
        </li>
      </ul>
      <OversiktCardFoot>
        <Link className="jp-btn jp-ov-cta jp-ov-cta--outline" href="/ansokningar">
          {t("summary.link")}
        </Link>
      </OversiktCardFoot>
    </OversiktCard>
  );
}
