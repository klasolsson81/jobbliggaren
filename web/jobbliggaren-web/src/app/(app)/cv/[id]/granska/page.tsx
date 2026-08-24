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

/**
 * The title resolves against the record's ABSENCE, not just against the route.
 *
 * Without this the document read "CV-granskning" while its body read "Sidan finns inte":
 * `(app)/not-found.tsx` cannot title itself (its metadata is inert — the `notFound()`
 * is thrown mid-stream, after the head has flushed), so the title that survives is
 * this page's, unconditionally. Measured on `a0956bfd` before this change.
 *
 * The gate is `kind === "notFound"` and nothing else, deliberately. Titling an
 * `error`, `rateLimited` or `unauthorized` result "Sidan finns inte" would assert
 * something false — the same defect class, pointed the other way.
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
 * CV-PII läses bara server-side; evidensen är redan personnummer-redigerad vid
 * motorns choke point innan den når klienten.
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
            <h1 className="jp-pagehero__title">{t("cv.granska.title")}</h1>
            <p className="jp-pagehero__lede">{t("cv.granska.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page flex flex-col gap-6">
        <Link href="/cv" className="jp-backlink self-start">
          <ChevronLeft size={16} aria-hidden="true" />
          <span>{t("cv.backLink")}</span>
        </Link>

        {/* The CV's name stays in the container for the same reason as the staging surface's
            file name: the hero carries the page's identity, this line says which CV. */}
        <p className="jp-cv-meta">
          <span className="jp-cv-meta__file">{resume.name}</span>
        </p>

        {/* #1060 — samma neutrala, visnings-bara affordance som stagingvyn, nu på det
            SPARADE CV:t. Texten kommer från innehållet sidan redan hämtar (ingen extra
            request); den bärs på ResumeContent.Preamble sedan importen och är därmed
            garanterat personnummer-fri vid SKRIVGRINDEN (ResumeContentPersonnummerGuard),
            inte via en redigerare på läsvägen — se ResumeContentDto för varför de två
            armarna inte delar kontroll. Renderas server-side, aldrig i en klient-ö.
            Null för mall-skapade CV, så komponenten renderar ingenting där. */}
        <CvPreamble preamble={findMasterVersion(resume)?.content.preamble ?? null} />

        <CvReviewPanel
          review={review}
          target={{ kind: "canonical", resumeId: id }}
          profile={profile}
        />
      </div>
    </>
  );
}
