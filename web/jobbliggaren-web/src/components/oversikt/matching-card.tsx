import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight, Target } from "lucide-react";
import { OversiktCard, OversiktCardFoot, OversiktNumber } from "./oversikt-card";

interface MatchingCardProps {
  /**
   * ADR 0079 STEG 6 — the live match count (Bra + Stark). `number` = the backend's answer, 0
   * included (an honest zero); `null` = the read degraded (network, auth, rate limit) ⇒ an
   * en-dash and no CTA, never a fabricated number.
   */
  readonly matchCount: number | null;
  /**
   * The `/jobb` link carrying EXACTLY the facets the count hard-filters on and no
   * `matchGrades` (CTO H2, harmonised 2026-07-03): the list's TotalCount equals this card's
   * number by construction. `null` when the profile could not be read.
   */
  readonly matchHref: string | null;
  /**
   * ADR 0076 — the setup state and the count are mutually exclusive. No stated occupation ⇒ this
   * card carries the "Matchningen är inte klar" callout and the ONLY settings link on the page.
   */
  readonly hasStatedOccupation: boolean;
  /** 4 beside three siblings, 6 when the Branschbevakning card has reflowed to a full row. */
  readonly span: 4 | 6;
}

const ID = "oversikt-card-matching";

/**
 * "Matchning" (ADR 0140): the match count as the big number, the basis it is counted on, and
 * one solid accent CTA to the matching ads. The solid level is the card's own: the card's number
 * counts ads, and its button leads to exactly those ads.
 */
export function MatchingCard({
  matchCount,
  matchHref,
  hasStatedOccupation,
  span,
}: MatchingCardProps) {
  const t = useTranslations("oversikt");
  const title = t("cards.matching");

  if (!hasStatedOccupation) {
    return (
      <OversiktCard id={ID} title={title} tone="accent" span={span} icon={Target}>
        <p className="jp-ov-card__text">
          {t.rich("notices.calloutText", { b: (chunks) => <b>{chunks}</b> })}
        </p>
        <OversiktCardFoot>
          {/* `/oversikt?matchsetup=1` opens the match-setup modal via MatchSetupLauncher
              (epic #526) — the same destination the callout has always had. */}
          <Link className="jp-btn jp-btn--primary jp-ov-cta" href="/oversikt?matchsetup=1">
            {t("notices.calloutCta")} <ArrowRight size={14} aria-hidden="true" />
          </Link>
          <p className="jp-ov-card__hint">{t("notices.calloutHint")}</p>
        </OversiktCardFoot>
      </OversiktCard>
    );
  }

  if (matchCount === null || matchHref === null) {
    return (
      <OversiktCard id={ID} title={title} tone="accent" span={span} icon={Target}>
        <OversiktNumber value={null} />
        <p className="jp-ov-card__unavailable">{t("cards.matchingUnavailable")}</p>
      </OversiktCard>
    );
  }

  return (
    <OversiktCard id={ID} title={title} tone="accent" span={span} icon={Target}>
      <OversiktNumber
        value={matchCount}
        unit={t("cards.matchingUnit", { count: matchCount })}
      />
      <p className="jp-ov-sub">
        {matchCount > 0 ? t("cards.matchingBasis") : t("notices.matchTextZero")}
      </p>
      <OversiktCardFoot>
        {matchCount > 0 ? (
          <Link
            className="jp-btn jp-btn--primary jp-ov-cta"
            href={matchHref}
            prefetch={false}
            // Three cards say "Visa matchande annonser"; the accessible name opens with the visible
            // text (2.5.3) and says whose ads (design-reviewer Major 5).
            aria-label={t("cards.matchingCtaAria")}
          >
            {t("cards.matchingCta")}
          </Link>
        ) : (
          /* A counted zero: the facet-filtered list is empty BY CONSTRUCTION (the H2 invariant), so
             the solid CTA would lead nowhere. The way forward is the whole list, at the emphasised
             level — never the solid one for a zero (design-reviewer Major 6). */
          <Link className="jp-btn jp-btn--emphasis jp-ov-cta" href="/jobb">
            {t("cards.matchingCtaZero")}
          </Link>
        )}
      </OversiktCardFoot>
    </OversiktCard>
  );
}
