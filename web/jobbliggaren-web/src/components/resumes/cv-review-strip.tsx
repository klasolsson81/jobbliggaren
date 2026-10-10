import { Fragment, useId } from "react";
import { useTranslations } from "next-intl";
import { CircleAlert, CircleCheck, CircleDashed, CircleX } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { StatusPill } from "@/components/ui/status-pill";
import { bandLabel, categoryLabel } from "@/lib/resumes/review-labels";
import type {
  CriterionVerdict,
  RubricCategory,
  ScoreBandLabel,
} from "@/lib/dto/parsed-resume";

/**
 * The dimension strip above the review ledger (#2083): per dimension its band, its coverage and
 * how its criteria fell out. Each cell is a toggle that filters the ledger to that dimension.
 * Rendered inside the ledger's client island; it holds no state of its own.
 *
 * The band never stands without its coverage (#1062 M1/M2), and a dimension where nothing could be
 * assessed says so in words instead of showing a low band (#1062 B1).
 */

export interface StripDimension {
  readonly category: RubricCategory;
  readonly band: ScoreBandLabel | null;
  readonly counts: Readonly<Record<CriterionVerdict, number>>;
}

const COUNT_ORDER: ReadonlyArray<{ verdict: CriterionVerdict; Icon: LucideIcon }> = [
  { verdict: "Fail", Icon: CircleX },
  { verdict: "Warn", Icon: CircleAlert },
  { verdict: "Pass", Icon: CircleCheck },
  { verdict: "NotAssessed", Icon: CircleDashed },
];

function StripCell({
  dimension,
  selected,
  onToggle,
  cellRef,
}: {
  dimension: StripDimension;
  selected: boolean;
  onToggle: () => void;
  cellRef: (element: HTMLButtonElement | null) => void;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const id = useId();
  const { counts, band } = dimension;
  const assessed = counts.Fail + counts.Warn + counts.Pass;
  const total = assessed + counts.NotAssessed;

  return (
    <button
      ref={cellRef}
      type="button"
      className="jp-cvstrip__cell"
      aria-pressed={selected}
      aria-labelledby={`${id}-name`}
      aria-describedby={`${id}-detail`}
      onClick={onToggle}
    >
      <span id={`${id}-name`} className="jp-cvstrip__name">
        {categoryLabel(tEnum, dimension.category)}
      </span>
      <span id={`${id}-detail`} className="jp-cvstrip__detail">
        {/* The spaces between the parts separate them in the description; a flex container does
            not render them. */}
        {band !== null ? (
          <StatusPill tone={bandLabel(tEnum, band).tone}>
            {bandLabel(tEnum, band).label}
          </StatusPill>
        ) : (
          assessed === 0 && <span className="jp-cvstrip__none">{t("review.band.none")}</span>
        )}{" "}
        <span className="jp-cvstrip__coverage">
          {t("review.band.coverage", { assessed, total })}
        </span>{" "}
        <span className="jp-cvstrip__counts">
          {COUNT_ORDER.map(({ verdict, Icon }, index) => (
            <Fragment key={verdict}>
              {index > 0 && " "}
              <span
                className="jp-cvstrip__count"
                data-verdict={verdict}
                data-zero={counts[verdict] === 0 || undefined}
              >
                <Icon size={14} strokeWidth={2.25} aria-hidden="true" />
                <span className="sr-only">{tEnum(`verdict.${verdict}`)}</span> {counts[verdict]}
              </span>
            </Fragment>
          ))}
        </span>
      </span>
    </button>
  );
}

export function CvReviewStrip({
  dimensions,
  selected,
  onToggle,
  cellRef,
}: {
  dimensions: ReadonlyArray<StripDimension>;
  selected: RubricCategory | null;
  onToggle: (category: RubricCategory) => void;
  cellRef: (category: RubricCategory, element: HTMLButtonElement | null) => void;
}) {
  const t = useTranslations("resumes");
  return (
    <div className="jp-cvstrip" role="group" aria-label={t("review.filter.dimensionGroupLabel")}>
      {dimensions.map((dimension) => (
        <StripCell
          key={dimension.category}
          dimension={dimension}
          selected={selected === dimension.category}
          onToggle={() => onToggle(dimension.category)}
          cellRef={(element) => cellRef(dimension.category, element)}
        />
      ))}
    </div>
  );
}
