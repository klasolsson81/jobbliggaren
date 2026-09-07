import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft, Info } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionAds,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import { getMyProfile } from "@/lib/api/me";
import { getJobAdMatchTags } from "@/lib/api/job-ad-match";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import type { JobAdMatchBatch, MatchGrade } from "@/lib/dto/job-ad-match";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import {
  buildCriterionAdsHref,
  parseCriterionAdsScope,
} from "@/lib/company-criteria/criterion-ads-href";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

/**
 * The title resolves against the record's ABSENCE, exactly as the parent route's does — a missing
 * criterion must not serve this route's title over a "Sidan finns inte" body, and
 * `(app)/not-found.tsx` cannot correct that. The gate is `kind === "notFound"` and nothing else.
 */
export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  const { id } = await params;
  const { page: pageParam, visa: visaParam } = await searchParams;
  // The SAME arguments the page uses. Measured 2026-09-05: identical reads collapse to one request
  // and divergent ones do not, so an axis omitted here doubles this route's backend cost.
  const result = await browseCriterionAds(
    id,
    parsePageParam(pageParam),
    parseCriterionAdsScope(visaParam) === "matching",
  );
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("foretag.smartaBevakningar.ads.meta.title") };
}

const EMPTY_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

const NO_MATCH_TAGS: JobAdMatchBatch = { entries: {} };

interface Props {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}

/**
 * `/foretag/smarta-bevakningar/[id]/annonser` (#1559) — the ACTIVE job ads posted by the companies a
 * saved criterion matches. RSC, jp-pagehero standard, the exact structural sibling of the parent
 * route (which lists the COMPANIES) and of `/foretag/bevakade/nya` (#1576, the ads behind the
 * Översikt number).
 *
 * <para/> **Why this is a route and not a `/jobb` link.** Klas asked for "en länk som visar
 * annonserna" (#1559) and `/jobb` cannot express this set: it has no SNI axis at all; its only
 * company axis is `?employer=`, whose producer refuses above `MAX_CONCEPT_IDS` = 400 org.nrs on an
 * every-value-or-none doctrine; and its `municipality` axis is the AD's workplace while a
 * criterion's kommun is the
 * company's REGISTERED SEAT. Every link buildable from those axes is partial or false, so the
 * criterion's own id is the destination and no new `/jobb` axis is minted
 * (senior-cto-advisor 2026-09-04).
 *
 * <para/> **The seat explainer is mandatory here, not decorative.** The number above it is true, but
 * without the explainer it carries a false implicature — that these are jobs IN the watched
 * kommuner. They are jobs at companies SEATED there. A true number under a false implicature is the
 * same defect as a false number.
 *
 * <para/> **The per-card match mark is `/jobb`'s overlay, reused as-is (#1656 (a)).** No count and
 * no "only matching" filter: the page is paginated at 20, so either would silently be about the
 * page, not the watch — the false implicature the `showTotalCount={false}` below already refuses
 * once. The aggregate is #1656 (b), bound and untouched.
 *
 * <para/> 404 (unknown OR another user's id — never an enumeration oracle) → notFound().
 * unauthorized → /logga-in. rateLimited/error → civic notice.
 */
export default async function BevakningAdsPage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages.foretag.criteria");
  // The nudge copy is /jobb's own ("…hur väl annonser matchar din profil"), never the follow
  // dialog's "…för att se matchande annonser" — that one promises a set this page does not render.
  const tMatch = await getTranslations("jobads.ui.match");
  const format = await getFormatter();

  const { id } = await params;
  const { page: pageParam, visa: visaParam } = await searchParams;
  const page = parsePageParam(pageParam);
  const scope = parseCriterionAdsScope(visaParam);
  const onlyMatching = scope === "matching";

  // The ad browse is this route's authority on existence (404 → notFound). The criteria list +
  // reference resolve the human title only; a degraded read of either falls back to a neutral title
  // rather than failing the page — parity with the parent route.
  // #1681 part 2 — the criteria LIST read is gone from this page too, for the reason the detail
  // page records: it was only ever the heading's source, and part 2 made that list expensive.
  const [adsResult, referenceResult, profileResult] = await Promise.all([
    browseCriterionAds(id, page, onlyMatching),
    getCriterionReference(),
    getMyProfile(),
  ]);

  switch (adsResult.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <ErrorShell
          title={t("ads.loadErrorTitle")}
          body={t("ads.rateLimited")}
          backHref={`/foretag/smarta-bevakningar/${id}`}
          backLabel={t("ads.backLink")}
        />
      );
    case "forbidden":
    case "error":
      return (
        <ErrorShell
          title={t("ads.loadErrorTitle")}
          body={t("ads.loadErrorBody")}
          backHref={`/foretag/smarta-bevakningar/${id}`}
          backLabel={t("ads.backLink")}
        />
      );
  }

  const { ads, magnitude, matching, criterion } = adsResult.data;

  // The count when the filter was HONOURED, as opposed to merely requested — and null otherwise, so
  // one narrowing carries both facts. The filter is inert for a caller who has stated no occupation
  // and for a watch too broad to grade; both get the unfiltered list, so the headline and the empty
  // state must describe THAT list, not the one that was asked for.
  const matchingCount = matching !== null ? matching.count : null;
  const reference = referenceResult.kind === "ok" ? referenceResult.data : EMPTY_REFERENCE;

  // Three states, and they must not collapse into two. A stated occupation → the chips. A profile
  // that says none is stated → the nudge. A profile read that FAILED → neither: the nudge would then
  // tell the user they have stated no occupation when the page does not know that, and a chip-less
  // list under it would read as "nothing matches". Silence is the only honest arm there.
  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" && profileResult.data.hasStatedDesiredOccupation;
  const showMatchNudge =
    profileResult.kind === "ok" && !profileResult.data.hasStatedDesiredOccupation;

  // A one-step waterfall, as on /jobb: the ids exist only once the browse has resolved. Without a
  // stated occupation no ad can earn a grade, so the call is skipped rather than answered empty.
  // `includeRelated` stays false — this route has no `?relaterade=` axis, so "Relaterat yrke" never
  // appears here. A failed batch degrades to no chips inside `getJobAdMatchTags` itself.
  const matchTags =
    hasStatedDesiredOccupation && ads.items.length > 0
      ? await getJobAdMatchTags(
          ads.items.map((it) => it.id),
          false,
        )
      : NO_MATCH_TAGS;
  const matchGradeById = new Map<string, MatchGrade>(
    Object.entries(matchTags.entries).map(([adId, entry]) => [adId, entry.grade] as const),
  );

  const userLabel = criterion.label?.trim() ?? "";
  const derived = deriveDisplayLabel(
    criterion.sniCodes, criterion.municipalityCodes, reference, {
      moreSuffix: t("moreSuffix"),
      separator: " · ",
    });
  const title = userLabel.length > 0 ? userLabel : (derived ?? t("row.untitled"));

  // #1681 part 2 (ADR 0139) — the magnitude may have no number at all now: the criterion can be too
  // broad to materialise, or not yet materialised for its current predicate. `formatMagnitude` takes
  // a counted magnitude, so it is only reached in the arm that has one.
  const magnitudeUnanswerable = magnitude.tooBroad || magnitude.notMaterialised;
  // Narrowed on `magnitude.magnitude` itself rather than on the derived flag, so the type system
  // carries the guarantee instead of a `!` asserting it. The two are equivalent today by the DTO's
  // own constructor; an assertion would be a claim the compiler cannot check.
  const magnitudeText =
    magnitude.magnitude === null
      ? null
      : formatMagnitude(format, { magnitude: magnitude.magnitude, saturated: magnitude.saturated });

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{title}</h1>
            <p className="jp-pagehero__lede">{t("ads.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <Link href={`/foretag/smarta-bevakningar/${id}`} className="jp-backlink mb-4">
          <ArrowLeft size={16} aria-hidden="true" />
          {t("ads.backLink")}
        </Link>

        {/* The filtered headline reads the PERSONAL count, never `ads.totalCount` — that one is a
            pagination quantity by contract even here, where it happens to equal the set (ADR 0120
            clause 4). The unfiltered headline is unchanged. */}
        <h2 className={`text-h2 text-text-primary${magnitudeUnanswerable ? "" : " tabular-nums"}`}>
          {matchingCount !== null
            ? t("ads.matchingHeadline", { count: matchingCount })
            : magnitudeUnanswerable
              ? // A headline is owed even when there is no number: rendering "0 aktiva annonser"
                // for a watch nobody counted is the dishonest zero, and rendering nothing would
                // leave the page without its subject. It is a NOUN PHRASE because an h2 is a heading
                // tier and a screen reader navigating by heading should not be read a two-sentence
                // instruction (design-reviewer, WCAG 2.4.6). The explanation lives in the block
                // below — NOT the next line: the säteskommun note sits between them (measured in the
                // rendered verification, 2026-09-07), which is a pre-existing layout order this
                // delta does not change.
                magnitude.tooBroad
                ? t("ads.adsTooBroadHeadline")
                : t("ads.adsNotMaterialisedHeadline")
              : magnitudeText === null
                ? // Unreachable today — the DTO's constructor makes a null magnitude imply one of the
                  // two flags above. But `?? ""` would render "  aktiva annonser", a magnitude-shaped
                  // sentence with a blank where the number goes, which is the form ADR 0120 / #859
                  // forbid outright: true or absent, never blank. Removing the `!` was right; trading
                  // a loud impossibility for a silent misrender was not (design-reviewer Minor D).
                  t("ads.adsNotMaterialisedHeadline")
                : t("ads.magnitudeHeadline", { count: magnitudeText })}
        </h2>

        {/* The refusal, stated plainly and without blame: no number exists for a watch this broad,
            and the actionable next step is to narrow it. Not role="alert" — nothing failed. */}
        {/* ⚠ Gated on `!magnitudeUnanswerable`, and that gate is the fix for a sentence THIS PR
            falsified. "…så alla aktiva annonser visas här" was true while the inert-filter arm fell
            through to a register-backed browse. Commit 7e208ae5 moved that browse to the
            materialised source, so in both unanswerable states the list is now EMPTY and the clause
            asserted something the page below it contradicts. The explanation for those states is the
            headline plus the line beneath it, not a consequence clause about a list that is not
            there. */}
        {matching?.tooBroad && !magnitudeUnanswerable && (
          <p className="jp-matchline">
            {t("ads.matchingTooBroadOnList")}{" "}
            <Link className="jp-nudgelink" href="/foretag/smarta-bevakningar">
              {t("ads.matchingTooBroadCta")}
            </Link>
          </p>
        )}

        {/* The `matching.notMaterialised` line is DELETED rather than re-worded (code-reviewer
            2026-09-07). Once gated on `!magnitudeUnanswerable` the only state that reaches it is a
            row crossing the read-age bound BETWEEN the magnitude call and the id-set call in one
            request — and there a real list renders, so the sentence "det finns inga annonser att
            visa här" would be false at the moment it appeared. The state is explained where it is
            true: the empty block below, whose body is this same string. Adding copy for a branch
            whose own premise is false is how a surface acquires a sentence nobody can trust. */}
        {/* ⚠ The SAME gate as its two siblings, and it was missing until rendered verification found
            it (2026-09-07). "…så alla aktiva annonser visas här" is false whenever the magnitude is
            unanswerable, and this combination is PRODUCIBLE rather than theoretical:
            `CriterionMatchingAdSetResolver.ResolveAsync` returns `NotAssessed` BEFORE it consults the
            magnitude, so an unassessable caller legitimately arrives beside a too-broad or
            not-materialised watch — and the page then showed "for bred" in the heading and "all
            active ads are shown here" beneath it, over an empty list. Two of the three branches were
            gated in this PR; this was the third. */}
        {matching !== null &&
          matching.count === null &&
          !matching.tooBroad &&
          !matching.notMaterialised &&
          !magnitudeUnanswerable && (
          <p className="jp-matchline">
            {t("ads.matchingNotAssessedOnList")}{" "}
            <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
              {tMatch("settingsCta")}
            </Link>
          </p>
        )}

        {matchingCount !== null && (
          <p className="jp-matchline">
            <Link className="jp-nudgelink" href={buildCriterionAdsHref(id, 1, "all")}>
              {t("ads.showAll")}
            </Link>
          </p>
        )}

        {/* The house's load-bearing "what you see is narrower than reality" primitive: the
            counter-claim has to stand against an h1 that says "i Göteborg" while the list can hold a
            job in Linköping. NOT --inline-control — that modifier centres a SINGLE line against its
            control, and this sentence wraps at every viewport, which puts the leading icon in the gap
            between the two lines. The base binds it to line one. */}
        <p className="jp-transparency-note mt-3">
          <Info size={16} aria-hidden="true" />
          <span>{t("ads.seatExplainer")}</span>
          <InfoDialog
            title={t("ads.seatHelpTitle")}
            paragraphs={[t("ads.seatHelpBody1"), t("ads.seatHelpBody2")]}
            ariaLabel={t("ads.seatHelpAria")}
          />
        </p>

        {/* ⚠ THREE branches, and the first one exists because suppressing this page's own empty state
            was not enough. `JobAdList` renders its OWN unconditional empty block
            (`job-ad-list.tsx`) with `jobads.ui.list.emptyTitle` = "Inga jobb hittades" and a body
            telling the reader to adjust filters and clear the search box — controls this route does
            not have (its only axis is `?visa=`). Falling through to it made the false zero STRONGER,
            not weaker, and gave advice that cannot be followed. So the unanswerable states render no
            list and no pagination at all: the headline says what is not known, this block says why,
            and the primary way back is kept (design-reviewer Blocker 1 + Minor 3). */}
        {magnitudeUnanswerable ? (
          <div className="jp-empty mt-6">
            {/* `matchingNotMaterialisedOnList` is written for THIS surface — a page opened to see a
                LIST — where `adsNotMaterialised` talks about figures. Using the figures sentence here
                left the right sentence written and never rendered (design-reviewer Minor B). */}
            <p className="jp-empty__body text-body-sm text-text-primary">
              {magnitude.tooBroad
                ? t("ads.tooBroadNoList")
                : t("ads.matchingNotMaterialisedOnList")}
            </p>
            {/* `jp-btn--ghost`, not `jp-nudgelink`: `.jp-empty__actions` sets no `align-items`, so a
                blockified text link stretches to the 44px sibling button's height with its text at
                the top. Every other `jp-empty__actions` in the tree carries `jp-btn` variants only,
                and ghost is the house's secondary in that row (design-reviewer Minor A). */}
            <div className="jp-empty__actions">
              <Link className="jp-btn jp-btn--primary" href={`/foretag/smarta-bevakningar/${id}`}>
                {t("ads.backLink")}
              </Link>
              {magnitude.tooBroad && (
                <Link className="jp-btn jp-btn--ghost" href="/foretag/smarta-bevakningar">
                  {t("ads.matchingTooBroadCta")}
                </Link>
              )}
            </div>
          </div>
        ) : ads.items.length === 0 ? (
          <div className="jp-empty mt-6">
            <div className="jp-empty__title">
              {matchingCount !== null ? t("ads.matchingEmptyTitle") : t("ads.emptyTitle")}
            </div>
            <p className="jp-empty__body text-body-sm text-text-primary">
              {matchingCount !== null ? t("ads.matchingEmptyBody") : t("ads.emptyBody")}
            </p>
            <div className="jp-empty__actions">
              <Link className="jp-btn jp-btn--primary" href={`/foretag/smarta-bevakningar/${id}`}>
                {t("ads.backLink")}
              </Link>
            </div>
          </div>
        ) : (
          <div className="mt-6 flex flex-col gap-4">
            {/* `.jp-matchline`, the form `/foretag/bevakade/nya` uses for the same sentence —
                never `.jp-transparency-note`, whose flex layout for a leading icon tears the CTA
                out of the sentence. */}
            {showMatchNudge && matching === null && (
              <p className="jp-matchline">
                {tMatch("noStatedOccupation")}{" "}
                <Link className="jp-nudgelink" href={MATCH_SETTINGS_HREF}>
                  {tMatch("settingsCta")}
                </Link>
              </p>
            )}
            <JobAdList jobAds={ads.items} matchGradeById={matchGradeById} />
            <JobAdPagination
              page={ads.page}
              pageSize={ads.pageSize}
              totalCount={ads.totalCount}
              // #1149's precedent, and the same reason as the sibling company browse: this
              // `totalCount` saturates at the pagination cap, so rendering it beside a headline that
              // honestly says "10 000+" would put two disagreeing numbers on one screen. The
              // magnitude above is this surface's number.
              showTotalCount={false}
              // The axis rides every page href. Without it page 2 would silently drop the filter
              // and show more ads than page 1 promised.
              buildHref={(targetPage) => buildCriterionAdsHref(id, targetPage, scope)}
            />
          </div>
        )}

        {/* Mandatory source attribution (DPIA C-D2/M-C4) — the SELECTION is register-derived even
            though the ads themselves are Platsbanken's. */}
        <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
          {t("ads.source")}
        </p>
      </div>
    </>
  );
}

function parsePageParam(raw: string | string[] | undefined): number {
  const value = typeof raw === "string" ? Number.parseInt(raw, 10) : NaN;
  return Number.isInteger(value) && value > 0 ? value : 1;
}

function ErrorShell({ title, body, backHref, backLabel }: {
  title: string;
  body: string;
  backHref: string;
  backLabel: string;
}) {
  return (
    <div className="jp-container jp-page">
      {/* The back link lives here too: the ok-branch's copy is unreachable in this arm, and without
          it the error screen has no way back to the watch except the global nav. */}
      <Link href={backHref} className="jp-backlink mb-4">
        <ArrowLeft size={16} aria-hidden="true" />
        {backLabel}
      </Link>
      <div
        role="alert"
        className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
        <p className="text-body font-medium">{title}</p>
        <p className="mt-1 text-body-sm">{body}</p>
      </div>
    </div>
  );
}
