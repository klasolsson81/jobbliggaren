import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume, getCvReview } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import {
  renderProfileSchema,
  type CvReviewDto,
  type ParsedResumeDetailDto,
  type RenderProfile,
} from "@/lib/dto/parsed-resume";
import { CvBlockReason } from "@/components/resumes/cv-block-reason";
import { PersonnummerWarning } from "@/components/resumes/personnummer-warning";
import { ParseSummary } from "@/components/resumes/parse-summary";
import { OccupationProposals } from "@/components/resumes/occupation-proposals";
import { CvPreamble } from "@/components/resumes/cv-preamble";
import { CvReviewPanel } from "@/components/resumes/cv-review-panel";
import { CvPreview } from "@/components/resumes/cv-preview";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

/**
 * The title resolves against the record's ABSENCE: a missing record must not serve this
 * route's title over a "Sidan finns inte" body, and `(app)/not-found.tsx` cannot correct
 * that (`lib/metadata/not-found-title.ts` records why). The gate is `kind === "notFound"`
 * and nothing else — both halves are pinned by
 * `(app)/detail-route-not-found-title.test.ts`.
 *
 * `getParsedResume` and not `getCvReview`: the parse artefact is this page's existence
 * authority, and the review degrades civilly rather than 404-ing the page.
 */
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { parsedId } = await params;
  const result = await getParsedResume(parsedId);
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("cv.review.meta.title") };
}

interface Props {
  params: Promise<{ parsedId: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/granska/[parsedId] — CV-import, steg 2–3 (Fas 4 STEG B, F1). RSC.
 *
 * Shell choice on the ERROR branches (#1062, design-reviewer minor 6), written down rather
 * than left as silence — silence is how the container drift this PR fixes got in. The
 * `rateLimited` and `error` branches render `jp-container jp-page` WITHOUT the hero plate:
 * the plate carries the page's identity ("Granska importerat CV"), and neither branch has a
 * review to be the identity of. The cost is real and accepted — the fallback paints the plate
 * and an error branch then replaces it, so the plate flashes in and out on that path. The
 * alternative, a plate reading "Kunde inte ladda granskningen", gives a failure the page's
 * most prominent treatment.
 *
 * Hämtar parse-artefakten (primär) + granskningen (sekundär) parallellt.
 * Parse-resultatet styr sidans utfall (ok → rendera; notFound → 404; auth →
 * redirect; övrigt → civic fel-block). Granskningen degraderas civilt: om den
 * inte är ok renderas parse-vyn ändå + en notis i panelen (sidan 404:ar aldrig
 * på ett granskningsfel). CV-PII läses bara server-side (RSC) — `parsed.content`
 * passeras aldrig vidare till en klient-ö.
 */
export default async function CvReviewPage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { parsedId } = await params;
  const { profile: rawProfile } = await searchParams;

  // Default till "Ats" vid saknad/ogiltig searchParam (case-sensitiv backend).
  const profileResult = renderProfileSchema.safeParse(rawProfile);
  const profile: RenderProfile = profileResult.success
    ? profileResult.data
    : "Ats";

  const [parsedResult, reviewResult] = await Promise.all([
    getParsedResume(parsedId),
    getCvReview(parsedId, profile),
  ]);

  // Parse-resultatet är primärt och styr sidans utfall.
  let parsed: ParsedResumeDetailDto;
  switch (parsedResult.kind) {
    case "ok":
      parsed = parsedResult.data;
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "notFound":
      notFound();
    case "rateLimited":
      return (
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
          <p className="jp-lede">
            {t("common.rateLimitedBody", {
              seconds: parsedResult.retryAfterSeconds,
            })}
          </p>
          {/* #1062: this branch had no way back at all — parity with `error` below. */}
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.review.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.review.errorBody")}</p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(parsedResult);
  }

  // Granskningen degraderas civilt — bara "ok" ger en panel, övrigt → notis.
  const review: CvReviewDto | null =
    reviewResult.kind === "ok" ? reviewResult.data : null;

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("cv.review.title")}</h1>
            <p className="jp-pagehero__lede">{t("cv.review.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page flex flex-col gap-6">
        <Link href="/cv" className="jp-backlink self-start">
          <ChevronLeft size={16} aria-hidden="true" />
          <span>{t("cv.backLink")}</span>
        </Link>

        {/* The file name stays in the container, not in the hero: the hero carries the page's
            IDENTITY (title + lede), while this line says which document is under review —
            content, not identity.
            NOT for want of a contrast decision on the plate — `.jp-pagehero__kicker` is exactly
            a mono overline there, so that decision is already made. What rules it out is that
            the kicker sets `text-transform: uppercase`, which would render `cv.docx` as
            `CV.DOCX`. */}
        <p className="jp-cv-meta">
          <span className="jp-cv-meta__file">{parsed.sourceFileName}</span>
        </p>

        {/* #1060: varför filen inte är sparad som CV. Först på sidan, för det är frågan
            användaren kom hit med — hubbens åtgärdskort kunde bara säga ATT något
            behövde åtgärdas, aldrig VAD. Orsaken härleds server-side av samma grind som
            auto-promote kör; den lagras aldrig. */}
        <CvBlockReason reason={parsed.blockReason} className="jp-cvaction--flush" />

        <div className="jp-cv-preview-actions">
          <CvPreview previewUrl={`/api/cv/parsed/${parsedId}/preview`} initialProfile={profile} />
        </div>

        {/* Kompletterar blocket ovan, upprepar det inte: det säger VILKEN grind som föll,
            den här säger hur många förekomster scannern hittade. */}
        <PersonnummerWarning personnummer={parsed.personnummer} />

        <ParseSummary confidence={parsed.confidence} />

        <OccupationProposals proposals={parsed.occupationProposals} />

        {/* Neutral, display-only preamble affordance (#844, ADR 0109). CV-PII rendered
            server-side only — parsed.content.preamble is already pnr-redacted at the mapper
            egress and never crosses to a client island (page invariant, lines above). */}
        <CvPreamble preamble={parsed.content.preamble} />

        <CvReviewPanel
          review={review}
          target={{ kind: "parsed", parsedId }}
          profile={profile}
        />

        {/* Next-step row (design-m4, ADR 0047 "what do I do now?"). The review is read-only
            (ADR 0112): the Förbättra + Fortsätt-spara CTAs were retired (komplettera + slutfor
            404), so a user who deep-links straight here would otherwise dead-end. The way to a
            canonical CV is to fix the FILE and re-import (auto-promote, 5b B3). Low-key, and it
            never implies a write on the review — it navigates to a fresh import. */}
        <section
          aria-labelledby="cv-nextstep-title"
          className="flex flex-col gap-2 border-t border-border pt-6"
        >
          <h2
            id="cv-nextstep-title"
            className="text-h3 font-medium text-text-primary"
          >
            {t("cv.review.nextStepTitle")}
          </h2>
          <p className="max-w-[68ch] text-body-sm text-text-primary">
            {t("cv.review.nextStepBody")}
          </p>
          <div>
            <Link href="/cv/importera" className="jp-btn jp-btn--secondary">
              {t("cv.review.nextStepCta")}
            </Link>
          </div>
        </section>
      </div>
    </>
  );
}
