import Link from "next/link";
import { useTranslations, useFormatter } from "next-intl";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { buildCriterionAdsHref } from "@/lib/company-criteria/criterion-ads-href";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type {
  CriterionAdMagnitude,
  MyMatchingAdCount,
} from "@/lib/dto/company-criteria";

/**
 * Does the surface rendering these lines also render the COMPANIES the numbers count? It changes the
 * COPY, never the logic, and both values are reached in production, so neither arm is a branch
 * nothing can produce.
 *
 * - `"withCompanies"`: the criterion's own page, which lists those companies beneath these lines, so
 *   "från dessa företag" has an antecedent. One producer today.
 * - `"standalone"`: a row that renders no company at all — one summary line among up to `MaxPerUser`
 *   on `/oversikt`, or one catalogue row on `/foretag/branschbevakningar`, where the companies sit
 *   behind a "Visa företag" link rather than on the page. Each label must carry itself. Two
 *   producers today.
 *
 * ⚠ <b>Named for the property, never for the surface</b> (`senior-cto-advisor` D1/in-block 2,
 * 2026-09-08). The values were `"detail"`/`"summary"` while there were exactly two consumers and the
 * surface picked out the property by accident; at the third, `variant="summary"` on a catalogue page
 * is disinformation, and a docblock explaining that "summary" means "standalone" is the smell rather
 * than the fix.
 *
 * ⚠ It decides the ANTECEDENT and nothing else. Whether the too-broad advice is hoisted out of the
 * row is `adviceStatedByCaller`; whether the row's CTA has anywhere to go is
 * `actionOfferedByCaller`. Three independent facts, three props — see each prop.
 */
export type CriterionAdLinesVariant = "withCompanies" | "standalone";

interface CriterionAdLinesProps {
  readonly criterionId: string;
  /**
   * `null` = the ad-count READ degraded (network, rate-limit, parse). Distinct from every
   * no-number state inside the DTO: those are answers, this is the absence of one.
   */
  readonly ads: CriterionAdMagnitude | null;
  readonly matching: MyMatchingAdCount | null;
  readonly variant: CriterionAdLinesVariant;
  /**
   * Does the CALLER state the too-broad advice beneath the list, so this row states only its
   * status? True exactly when the caller renders more than one row (`design-reviewer` B1,
   * 2026-09-08).
   *
   * <p><b>This is a second, independent fact and it must not be folded back into `variant`.</b>
   * `variant` decides whether an ad label carries its own antecedent — a property of the SURFACE,
   * true at every N, because a standalone surface renders no company at any count. This flag
   * decides whether the advice would REPEAT — a property of the COUNT. At N=1 they disagree: the
   * label must still carry itself while the advice must not be hoisted, and one flag cannot say
   * both. Deriving this one from `variant` alone put the refusal on screen twice (#1707); deriving
   * it from the count alone would put "från dessa företag" back on a block that renders no company
   * (`design-reviewer` part-3 Major 3).</p>
   *
   * <p>The caller computes it ONCE and gates its own advice line on the same value — two
   * independent `length > 1` expressions is how the row and the block drift apart.</p>
   */
  readonly adviceStatedByCaller: boolean;
  /**
   * Does the CALLER already render the control that edits the watch, so the too-broad CTA would
   * point at the page the reader is already on? True exactly on `/foretag/branschbevakningar`, whose
   * every row carries its own "Ändra" button (`senior-cto-advisor` D1, 2026-09-08).
   *
   * <p><b>A THIRD independent fact, and it must not be folded into either of the other two.</b>
   * `variant` decides whether a label needs its own antecedent; `adviceStatedByCaller` decides
   * whether the advice would repeat; this decides whether a shortcut to another surface leads
   * anywhere. They change for three different reasons — a surface gaining a company list, a list
   * crossing N=1, a caller gaining an edit affordance — and the catalogue is `standalone` AND
   * action-offering at once, so no single enum can say both.</p>
   *
   * <p><b>It gates the `Link` only, never the choice of sentence.</b> At N=1 the catalogue still
   * renders the whole refusal + reason + advice; only the shortcut is dropped, because the action it
   * shortcuts to is a button in the same row. Collapsing this into `shortRefusal` would silently
   * take the reason and the advice with it.</p>
   *
   * <p>It reaches the three `ads.matchingTooBroadCta` links and nothing else. It deliberately does
   * NOT reach the not-assessed nudge to {@link MATCH_SETTINGS_HREF} below: that is a different
   * action, on a surface no caller of this component offers a substitute for, and gating it would
   * delete a live way forward (ADR 0047).</p>
   */
  readonly actionOfferedByCaller: boolean;
}

/**
 * The criterion's TWO ad numbers and every honest way of not having them — one component, because
 * it is one knowledge piece (SRP: one authority per rule).
 *
 * <p>Extracted from `(app)/foretag/branschbevakningar/[id]/page.tsx` by `senior-cto-advisor`'s
 * binding ruling for #1681 part 3 (in-block requirement 1,
 * `docs/reviews/2026-09-07-1681-part3-form-cto.md`): part 3 puts these same numbers on `/oversikt`,
 * and copying ~60 lines of honesty logic is what guarantees the two surfaces drift — the class ADR
 * 0139's "Båda ytorna läser samma källa" exists to close.</p>
 *
 * <p>⚠ <b>The extraction was not byte-for-byte, and the one behavioural change is named here rather
 * than left to be discovered</b> (`dotnet-architect` + `code-reviewer`, 2026-09-07): the ads link
 * gained `prefetch={false}`, which the detail page's original did not carry (its matching sibling
 * already did). Harmonised deliberately — `CompanySummary` prefetches neither of its count links,
 * and on `/oversikt` up to twenty rows × two links would otherwise warm forty routes nobody asked
 * for.</p>
 *
 * <p><b>Seven answers across the two lines, and none of them collapses into another.</b> The ads
 * line: a degraded read · both arms refusing for the same reason (said ONCE) · too broad · not
 * materialised · a number · a counted zero. The matching line: too broad · not materialised · not
 * assessed (no stated occupation) · a number, zero included. <b>Only a counted zero is ever
 * rendered as 0</b> — every other non-number would be a lie about a measurement that never
 * happened (ADR 0120, #859).</p>
 *
 * <p><b>The branch order is load-bearing, not stylistic.</b> The two unanswerable states are tested
 * BEFORE the `> 0` test, because `magnitude` is `null` in both and `null > 0` is `false` — which
 * would silently render "inga aktiva annonser" over a watch nobody counted.</p>
 *
 * <p>Copy comes from `pages.foretag.criteria.ads` and `jobads.companyWatches` — foreign namespaces
 * to `/oversikt` on purpose, the same choice and the same reason as `CompanySummary` reading
 * `jobads.companyWatches.filter`: duplicating the sentences here is how two surfaces drift apart on
 * the next edit.</p>
 */
export function CriterionAdLines({
  criterionId,
  ads,
  matching,
  variant,
  adviceStatedByCaller,
  actionOfferedByCaller,
}: CriterionAdLinesProps) {
  const t = useTranslations("pages.foretag.criteria");
  // Klas 2026-09-05: the personal count works "på samma sätt som vanlig företagsbevakning", so it
  // reuses that surface's own sentences rather than minting a second vocabulary for one question.
  const tWatch = useTranslations("jobads.companyWatches");
  const format = useFormatter();

  // Where several rows exist the advice belongs to the BLOCK, not to the row — one axis up from the
  // same rule the `sharedRefusal` collapse applies within a row (design-reviewer Major 2,
  // 2026-09-07, measured: the 160-character sentence rendered five times verbatim at the per-user
  // cap). ⚠ That binding was argued FROM REPETITION, so it does not reach a single row: at N=1 the
  // advice cannot repeat, and hoisting it anyway said the refusal twice (#1707, measured
  // 2026-09-08). The row then states the whole thing itself.
  const shortRefusal = adviceStatedByCaller;

  // Separate, and separate on purpose (see the prop's docblock): whether an ad label must carry its
  // own antecedent. A standalone surface renders no company at ANY count, so this follows the
  // surface and never the row count.
  const standalone = variant === "standalone";

  // The shortcut to where a watch is edited, built ONCE and rendered by all three too-broad arms.
  // Absent where the caller already offers that action in the row itself — there the link would name
  // the page the reader is standing on, in the same Swedish word as a control beside it, which is a
  // control describing an action it does not perform rather than a way forward (ADR 0047; the
  // sub-nav's own self-reference is legitimate because it announces itself with `aria-current`).
  //
  // Every too-broad arm carries it, and its absence was a dead end rather than a state
  // (design-reviewer B2, ADR 0047): the `ads.tooBroad` arm is where a refused watch lands when only
  // that arm is unanswerable, and with the block advice correctly silent at N=1 the watch was left
  // refused with no way forward at all.
  const tooBroadCta = actionOfferedByCaller ? null : (
    <>
      {" "}
      <Link className="jp-nudgelink" href="/foretag/branschbevakningar">
        {t("ads.matchingTooBroadCta")}
      </Link>
    </>
  );

  // design-reviewer Major 1 (#1681 part 2) — rendering both meant two blocks, no visual
  // separation, and the advice sentence repeated verbatim — 40 words for one fact, which reads as a
  // fault rather than as two answers. When they agree, say it once. They do not always: see
  // `CriterionMatchingAdSetResolver.ResolveIdsAsync`, whose third refusal path fires on a counted
  // magnitude.
  const sharedRefusal =
    ads !== null && matching !== null
      ? ads.tooBroad && matching.tooBroad
        ? "tooBroad"
        : ads.notMaterialised && matching.notMaterialised
          ? "notMaterialised"
          : null
      : null;

  // Narrowed on `magnitude` itself rather than on a derived flag, so the type system carries the
  // guarantee instead of a `!` asserting it. The two are equivalent today by the DTO's own
  // constructor; an assertion would be a claim the compiler cannot check.
  const adsCountText =
    ads !== null && ads.magnitude !== null
      ? formatMagnitude(format, { magnitude: ads.magnitude, saturated: ads.saturated })
      : null;
  const adsCount = ads?.magnitude ?? null;

  return (
    <>
      {ads === null ? (
        <p className="jp-matchline">{t("ads.countUnavailable")}</p>
      ) : sharedRefusal === "tooBroad" ? (
        <p className="jp-matchline">
          {shortRefusal ? (
            t("ads.tooBroadShort")
          ) : (
            <>
              {t("ads.adsAndMatchingTooBroad")}
              {tooBroadCta}
            </>
          )}
        </p>
      ) : sharedRefusal === "notMaterialised" ? (
        <p className="jp-matchline">{t("ads.adsNotMaterialised")}</p>
      ) : ads.tooBroad ? (
        <p className="jp-matchline">
          {shortRefusal ? (
            t("ads.tooBroadShort")
          ) : (
            <>
              {t("ads.adsTooBroad")}
              {tooBroadCta}
            </>
          )}
        </p>
      ) : ads.notMaterialised ? (
        /* SINGULAR, and that is a fix rather than a nicety (design-reviewer Minor 7): only the ads
           arm is unanswerable here, and the matching line below may well carry a number. The plural
           "Annonssiffrorna" claims both, so it read as a contradiction against the very next line. */
        <p className="jp-matchline">{t("ads.adsNotMaterialisedOnly")}</p>
      ) : adsCount !== null && adsCount > 0 && adsCountText !== null ? (
        <p className="jp-matchline tabular-nums">
          <Link
            className="jp-countlink"
            href={buildCriterionAdsHref(criterionId, 1, "all")}
            prefetch={false}
          >
            {/* "från dessa företag" needs an antecedent, and only a surface that renders the
                companies themselves has one (design-reviewer Major 3). */}
            {standalone
              ? t("ads.linkLabelStandalone", { count: adsCountText })
              : t("ads.linkLabel", { count: adsCountText })}
          </Link>
        </p>
      ) : (
        /* A counted zero — a real answer, and the one the whole family exists to keep sayable. */
        <p className="jp-matchline">
          {standalone ? t("ads.noneStandalone") : t("ads.none")}
        </p>
      )}

      {/* Two branches suppress this line entirely rather than adding an answer: a degraded read
          (the line above already says the numbers cannot be shown) and a refusal the ads line has
          just stated for BOTH numbers. */}
      {matching !== null &&
        sharedRefusal === null &&
        (matching.tooBroad ? (
          <p className="jp-matchline">
            {shortRefusal ? (
              t("ads.tooBroadShort")
            ) : (
              <>
                {t("ads.matchingTooBroad")}
                {tooBroadCta}
              </>
            )}
          </p>
        ) : matching.notMaterialised ? (
          /* Deliberately WITHOUT the "ändra bevakningen" call to action the too-broad arm carries.
             Nothing is wrong with this watch; it simply has not been counted yet, and inviting the
             user to edit it would be advice that cannot help. */
          <p className="jp-matchline">{t("ads.matchingNotMaterialised")}</p>
        ) : matching.count === null ? (
          /* NOT ASSESSED — about the user's profile, never about this watch. The resolver returns
             it BEFORE consulting the magnitude, so it pairs with any ads state above. */
          <p className="jp-matchline">
            {tWatch("matchNudge")}{" "}
            <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
              {tWatch("matchNudgeCta")}
            </Link>
          </p>
        ) : (
          <p className="jp-matchline tabular-nums">
            {matching.count > 0 ? (
              <Link
                className="jp-countlink"
                href={buildCriterionAdsHref(criterionId, 1, "matching")}
                prefetch={false}
              >
                {tWatch("matchingAds", { count: matching.count })}
              </Link>
            ) : (
              tWatch("matchingAds", { count: matching.count })
            )}
          </p>
        ))}
    </>
  );
}
