import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { ArrowLeft, Info } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import {
  browseCriterionAds,
  getCompanyWatchCriteria,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import { getMyProfile } from "@/lib/api/me";
import { getJobAdMatchTags } from "@/lib/api/job-ad-match";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import type { JobAdMatchBatch, MatchGrade } from "@/lib/dto/job-ad-match";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
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
  const { page: pageParam } = await searchParams;
  const result = await browseCriterionAds(id, parsePageParam(pageParam));
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
 * <para/> **The per-card match mark is `/jobb`'s overlay, reused as-is (#1656 (a)).** The same
 * `getJobAdMatchTags` → `MatchChip` path paints every per-ad match mark the product has, so the
 * same ad reads the same here as on `/jobb`. No count and no "only matching" filter: the page is
 * paginated at 20, so either would silently be about the page, not the watch — the false
 * implicature the `showTotalCount={false}` below already refuses once. The aggregate is #1656 (b),
 * bound and untouched.
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
  const { page: pageParam } = await searchParams;
  const page = parsePageParam(pageParam);

  // The ad browse is this route's authority on existence (404 → notFound). The criteria list +
  // reference resolve the human title only; a degraded read of either falls back to a neutral title
  // rather than failing the page — parity with the parent route. The profile read is cache()-deduped
  // with the app shell's, so it costs no round-trip of its own.
  const [adsResult, criteriaResult, referenceResult, profileResult] = await Promise.all([
    browseCriterionAds(id, page),
    getCompanyWatchCriteria(),
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

  const { ads, magnitude } = adsResult.data;
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

  const criterion =
    criteriaResult.kind === "ok"
      ? criteriaResult.data.find((c) => c.id === id)
      : undefined;
  const userLabel = criterion?.label?.trim() ?? "";
  const derived = criterion
    ? deriveDisplayLabel(criterion.sniCodes, criterion.municipalityCodes, reference, {
        moreSuffix: t("moreSuffix"),
        separator: " · ",
      })
    : null;
  const title = userLabel.length > 0 ? userLabel : (derived ?? t("row.untitled"));

  const magnitudeText = formatMagnitude(format, magnitude);

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

        <h2 className="text-h2 text-text-primary tabular-nums">
          {t("ads.magnitudeHeadline", { count: magnitudeText })}
        </h2>

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

        {ads.items.length === 0 ? (
          <div className="jp-empty mt-6">
            <div className="jp-empty__title">{t("ads.emptyTitle")}</div>
            <p className="jp-empty__body text-body-sm text-text-primary">{t("ads.emptyBody")}</p>
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
            {showMatchNudge && (
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
              buildHref={(targetPage) =>
                targetPage <= 1
                  ? `/foretag/smarta-bevakningar/${id}/annonser`
                  : `/foretag/smarta-bevakningar/${id}/annonser?page=${targetPage}`
              }
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
