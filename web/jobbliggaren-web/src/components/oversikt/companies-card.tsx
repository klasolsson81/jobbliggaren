import Link from "next/link";
import { useTranslations } from "next-intl";
import { Building2, EyeOff, Filter } from "lucide-react";
import { summariseWatches } from "@/lib/company-watches/watch-summary";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListCompanyWatchesResult } from "@/lib/dto/company-follows";
import { OversiktCard, OversiktCardFoot, OversiktNumber } from "./oversikt-card";

interface CompaniesCardProps {
  /** The watches as a Result — only a Result tells "you follow nobody" from "could not be read". */
  readonly watches: ApiResult<ListCompanyWatchesResult>;
  /**
   * New ads from followed companies since the last `/foretag` visit (live, read-time graded).
   * `0` hides the pill; the card's numbers do not depend on it.
   */
  readonly newAdCount: number;
  /** 4 beside two siblings, 6 when the Branschbevakning card has reflowed to a full row. */
  readonly span: 4 | 6;
}

const ID = "oversikt-card-companies";

/**
 * "Bevakade företag" (ADR 0140): the matching-ad sum as the big number — or, while matching is
 * not assessed, the active-ad sum with its own unit, never a false zero — the anchor sentence
 * with its count link beneath, and one solid slate-teal CTA to the matching ads. The "N nya"
 * pill in the head links straight to the new ads it counts (#1576).
 *
 * Numbers and the all-or-none link rule come from `summariseWatches`; no company name is ever
 * rendered here — the catalogue is `/foretag/bevakade`.
 */
export function CompaniesCard({ watches, newAdCount, span }: CompaniesCardProps) {
  const t = useTranslations("oversikt");
  const title = t("companySummary.heading");

  if (watches.kind !== "ok") {
    return (
      <OversiktCard id={ID} title={title} tone="follow" span={span} icon={Building2}>
        <OversiktNumber value={null} />
        <p className="jp-ov-card__unavailable">{t("companySummary.unavailable")}</p>
      </OversiktCard>
    );
  }

  if (watches.data.length === 0) {
    return (
      <OversiktCard id={ID} title={title} tone="follow" span={span} icon={Building2}>
        <p className="jp-ov-card__emptytitle">{t("companySummary.emptyTitle")}</p>
        <p className="jp-ov-card__emptybody">{t("companySummary.emptyBody")}</p>
        <OversiktCardFoot>
          <Link className="jp-btn jp-btn--emphasis jp-ov-cta" href="/foretag/sok">
            {t("companySummary.emptyCta")}
          </Link>
        </OversiktCardFoot>
      </OversiktCard>
    );
  }

  const summary = summariseWatches(watches.data, true);
  const assessed = summary.matchingAds !== null;

  const pill =
    newAdCount > 0 ? (
      <Link
        href="/foretag/bevakade/nya"
        className="jp-ov-card__pill tabular-nums"
        // 2.5.3 Label in Name: the visible "N nya" opens the accessible name.
        aria-label={t("cards.newPillAria", { count: newAdCount })}
      >
        {t("cards.newPill", { count: newAdCount })}
      </Link>
    ) : undefined;

  return (
    <OversiktCard
      id={ID}
      title={title}
      tone="follow"
      span={span}
      icon={Building2}
      aside={pill}
    >
      <OversiktNumber
        value={assessed ? summary.matchingAds : summary.activeAds}
        unit={
          assessed
            ? t("cards.matchingAdsUnit", { count: summary.matchingAds ?? 0 })
            : t("cards.activeAdsUnit", { count: summary.activeAds })
        }
      />
      <p className="jp-ov-sub tabular-nums">
        {t.rich("companySummary.anchor", {
          count: summary.count,
          active: summary.activeAds,
          lnk: (chunks) =>
            summary.activeAdsHref ? (
              <Link href={summary.activeAdsHref} className="jp-countlink" prefetch={false}>
                {chunks}
              </Link>
            ) : (
              <>{chunks}</>
            ),
        })}
      </p>
      {summary.explainMissingLinks && (
        <p className="jp-transparency-note">
          <EyeOff size={16} aria-hidden="true" />
          <span>{t("companySummary.notLinkable")}</span>
        </p>
      )}
      {summary.filteredWatches > 0 && (
        <p className="jp-transparency-note">
          <Filter size={16} aria-hidden="true" />
          <span>{t("companySummary.filter", { count: summary.filteredWatches })}</span>
        </p>
      )}
      {summary.matchingAdsHref !== null && (
        <OversiktCardFoot>
          <Link
            className="jp-btn jp-ov-cta jp-ov-cta--follow"
            href={summary.matchingAdsHref}
            prefetch={false}
            aria-label={t("cards.companiesCtaAria")}
          >
            {t("cards.matchingCta")}
          </Link>
        </OversiktCardFoot>
      )}
    </OversiktCard>
  );
}
