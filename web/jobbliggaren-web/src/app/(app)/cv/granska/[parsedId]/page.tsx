import Link from "next/link";
import { notFound, redirect } from "next/navigation";
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
import { PersonnummerWarning } from "@/components/resumes/personnummer-warning";
import { ParseSummary } from "@/components/resumes/parse-summary";
import { OccupationProposals } from "@/components/resumes/occupation-proposals";
import { CvReviewPanel } from "@/components/resumes/cv-review-panel";

interface Props {
  params: Promise<{ parsedId: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/granska/[parsedId] — CV-import, steg 2–3 (Fas 4 STEG B, F1). RSC.
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
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">För många förfrågningar</h1>
          <p className="jp-lede">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {parsedResult.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">Kunde inte ladda granskningen</h1>
          <p className="jp-lede">
            Ett tekniskt fel uppstod. Försök ladda om sidan eller gå tillbaka
            till CV-listan.
          </p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              Tillbaka till CV
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
    <div className="flex flex-col gap-6">
      <Link
        href="/cv"
        className="inline-flex items-center gap-1 text-body-sm text-text-secondary hover:text-text-primary self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>Tillbaka till CV</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">Granska importerat CV</h1>
        <p className="jp-cv-meta">
          <span className="jp-cv-meta__file">{parsed.sourceFileName}</span>
        </p>
        <p className="jp-lede">
          Det här är en deterministisk granskning av ditt importerade CV. Inget
          har ändrats i filen — granskningen pekar bara ut vad du kan förbättra,
          med citerad evidens ur din egen text.
        </p>
      </header>

      <PersonnummerWarning personnummer={parsed.personnummer} />

      <ParseSummary confidence={parsed.confidence} />

      <OccupationProposals proposals={parsed.occupationProposals} />

      <CvReviewPanel review={review} parsedId={parsedId} profile={profile} />

      {/* CTA-söm mot F2: spara-flödet (komplettera + promote). Förbättra-CTA:n
          (F4-10) ligger som sekundär knapp FÖRE den primära spara-knappen —
          den är display-only vägledning, inte en del av spara-flödet. */}
      <div className="jp-cv-cta">
        <div className="jp-cv-cta__actions">
          <Link
            href={`/cv/granska/${parsedId}/forbattra?profile=${profile}`}
            className="jp-btn jp-btn--secondary"
          >
            Visa förbättringsförslag
          </Link>
          <Link
            href={`/cv/granska/${parsedId}/komplettera`}
            className="jp-btn jp-btn--primary"
          >
            Fortsätt och spara CV
          </Link>
        </div>
      </div>
    </div>
  );
}
