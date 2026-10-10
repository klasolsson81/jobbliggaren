import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getResumeById, getResumeReview } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import {
  renderProfileSchema,
  type CvReviewDto,
  type RenderProfile,
} from "@/lib/dto/parsed-resume";
import { CvReviewPanel } from "@/components/resumes/cv-review-panel";
import { CvPreamble } from "@/components/resumes/cv-preamble";
import { findMasterVersion } from "@/lib/resumes/content-utils";
import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";
import { PageFeedback } from "@/components/feedback/page-feedback";

/**
 * The title resolves against the record's ABSENCE: a missing record must not serve this
 * route's title over a "Sidan finns inte" body, and `(app)/not-found.tsx` cannot correct
 * that (`lib/metadata/not-found-title.ts` records why). The gate is `kind === "notFound"`
 * and nothing else — both halves are pinned by
 * `(app)/detail-route-not-found-title.test.ts`.
 */
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { id } = await params;
  const result = await getResumeById(id);
  if (result.kind === "notFound") return notFoundMetadata();

  const t = await getTranslations("pages");
  return { title: t("cv.granska.meta.title") };
}

interface Props {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/[id]/granska — den KANONISKA CV-granskningen (Fas 4b PR-8.4, stänger #657).
 * RSC. Granskar ett befordrat, sparat CV (Resume-id) i stället för importstagingen
 * (`/cv/granska/[parsedId]`). Skillnaden mot den parsade vyn: den kanoniska
 * granskningen bär finding-statusledgern (userStatus/stale/isIgnorable), så varje
 * åtgärdbar anmärkning får en per-anmärkning statuskontroll.
 *
 * Hämtar Resume-detaljen (PRIMÄR — styr 404/namn/header) + granskningen (SEKUNDÄR
 * — degraderas civilt till `null`; sidan 404:ar aldrig på ett granskningsfel)
 * parallellt. Auth-/fel-formen är den som `/cv/[id]`-detaljvyn bar innan #1373
 * grindade den routen; formen ärvdes därifrån och står nu på egna ben.
 * CV-PII läses bara server-side and is rendered there; the ledger's filter island receives the
 * rows as rendered nodes, never the text as props (#2083). Evidence is already
 * personnummer-redacted at the engine's choke point.
 *
 * Shell (CCP): both review surfaces use `jp-pagehero` + `jp-container jp-page`, the `(app)`
 * standard. The invitation to design-reviewer that used to sit here is answered — she ruled
 * pagehero (#1062). The back-link sits in the container and NOT in the hero, which is the
 * non-obvious half: `.jp-pagehero .jp-btn--secondary` fails 1.4.11 on the plate, and a solid
 * primary would breach ADR 0038's one-primary rule. Numbers and the rejected alternatives are
 * in the commit message.
 */
export default async function CanonicalCvReviewPage({
  params,
  searchParams,
}: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const { id } = await params;
  const { profile: rawProfile } = await searchParams;

  // Default till "Ats" vid saknad/ogiltig searchParam (case-sensitiv backend).
  const profileResult = renderProfileSchema.safeParse(rawProfile);
  const profile: RenderProfile = profileResult.success
    ? profileResult.data
    : "Ats";

  const [resumeResult, reviewResult] = await Promise.all([
    getResumeById(id),
    getResumeReview(id, profile),
  ]);

  // Resume-detaljen är primär och styr sidans utfall.
  switch (resumeResult.kind) {
    case "ok":
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
              seconds: resumeResult.retryAfterSeconds,
            })}
          </p>
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
          <h1 className="jp-h1">{t("cv.granska.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.granska.errorBody")}</p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(resumeResult);
  }

  const resume = resumeResult.data;

  // Granskningen degraderas civilt — bara "ok" ger en panel, övrigt → notis.
  const review: CvReviewDto | null =
    reviewResult.kind === "ok" ? reviewResult.data : null;

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            {/* The plate's contrast decision for a small label is already made and already
                scoped: `.jp-pagehero__kicker` is a mono overline in `--jp-hero-ink-soft`.
                A `.jp-tag` here would carry `--jp-ink-2` onto the gradient, the same way
                `.jp-btn--secondary` does — which is why the hero re-scopes every on-plate
                token it uses. Reusing the kicker needs no new rule and no `guard-allow`. */}
            <div className="jp-pagehero__kicker">{t("cv.granska.beta")}</div>
            <h1 className="jp-pagehero__title">{t("cv.granska.title")}</h1>
            {/* En naken "BETA" konstaterar något användaren inte kan agera på: sidan
                fäller omdömen om användarens CV, så betastatusen är ett förbehåll om just
                de omdömenas tillförlitlighet (design-reviewer, PR #1684). Meningen bor i
                ledet och inte i ett eget stycke — då reserverar `PageHeroSkeleton` rätt
                bandhöjd utan en ny prop, eftersom skelettet redan renderar samma sträng. */}
            <p className="jp-pagehero__lede">{t("cv.granska.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page flex flex-col gap-3">
        <Link href="/cv" className="jp-backlink self-start">
          <ChevronLeft size={16} aria-hidden="true" />
          <span>{t("cv.backLink")}</span>
        </Link>

        {/* The hero carries the page's identity; the CV's name stands in the panel's identity row
            and says which CV. The preamble notice (#1060) comes from the content the page already
            fetched; it is personnummer-free at the WRITE gate (ResumeContentPersonnummerGuard), and
            null for a template-made CV, where it renders nothing. */}
        <CvReviewPanel
          review={review}
          target={{ kind: "canonical", resumeId: id }}
          profile={profile}
          documentName={resume.name}
          notice={<CvPreamble preamble={findMasterVersion(resume)?.content.preamble ?? null} />}
        />
      </div>
      <PageFeedback pageKey="cv-review" />
    </>
  );
}
