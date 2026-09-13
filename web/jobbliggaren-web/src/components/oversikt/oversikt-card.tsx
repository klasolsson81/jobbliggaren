import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import type { LucideIcon } from "lucide-react";

export type OversiktCardTone = "plain" | "accent" | "follow" | "info";

interface OversiktCardProps {
  /** `aria-labelledby` target — the card's own heading id. */
  readonly id: string;
  readonly title: string;
  readonly tone: OversiktCardTone;
  /** Column span in the 12-column grid. Every card collapses to 12 under 1024px (CSS). */
  readonly span: 4 | 6 | 8 | 12;
  readonly icon: LucideIcon;
  /** Right-hand slot in the head row — the "N nya" pill on Bevakade företag. */
  readonly aside?: ReactNode;
  readonly children: ReactNode;
}

/**
 * The card shell every number card on `/oversikt` renders in (ADR 0140 Beslut 1): a labelled
 * `<section>`, a 40×40 icon box in the card's own axis colour, and an `h2` — one heading tier
 * per card, so heading navigation lands on the six cards and nothing else.
 *
 * Tone is the card's colour axis — accent for matching, slate-teal for followed companies, blue
 * for industry watches (ADR 0116's semantics, reused rather than reinvented) — and it colours the
 * tint, the border, the icon box and the solid CTA together, so a card can never wear one axis
 * on its button and another on its frame.
 */
export function OversiktCard({
  id,
  title,
  tone,
  span,
  icon: Icon,
  aside,
  children,
}: OversiktCardProps) {
  return (
    <section
      className={tone === "plain" ? "jp-ov-card" : `jp-ov-card jp-ov-card--${tone}`}
      data-span={span}
      aria-labelledby={id}
    >
      <div className="jp-ov-card__head">
        <span className="jp-ov-card__icon" aria-hidden="true">
          <Icon size={20} aria-hidden="true" />
        </span>
        <h2 className="jp-ov-card__title" id={id}>
          {title}
        </h2>
        {aside}
      </div>
      {children}
    </section>
  );
}

interface OversiktNumberProps {
  /** `null` = unmeasured. Renders an en-dash and NO unit — a unit beside a dash claims a count. */
  readonly value: number | null;
  readonly unit?: string;
}

/**
 * The card's big number and its unit, baseline-aligned. An unmeasured value renders as an
 * en-dash, never as a digit — the `HeaderStats` doctrine (CTO-bind 2026-07-13 A′), because a
 * fabricated zero beside a real CTA would send the reader to a list that then disagrees.
 */
export function OversiktNumber({ value, unit }: OversiktNumberProps) {
  const t = useTranslations("oversikt.cards");
  const format = useFormatter();
  return (
    <p className="jp-ov-num">
      <span className="jp-ov-num__value tabular-nums">
        {value === null ? t("unmeasured") : format.number(value)}
      </span>
      {value !== null && unit !== undefined && (
        <span className="jp-ov-num__unit">{unit}</span>
      )}
    </p>
  );
}

/**
 * The card's bottom slot: pushes the CTA to the card's floor so three cards in a row share one
 * button baseline whatever their body height, with the 18px minimum the handoff draws above it.
 */
export function OversiktCardFoot({ children }: { readonly children: ReactNode }) {
  return <div className="jp-ov-card__foot">{children}</div>;
}
