import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume, getCvImprovements } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import {
  renderProfileSchema,
  type CvImprovementDto,
  type ParsedResumeDetailDto,
  type RenderProfile,
} from "@/lib/dto/parsed-resume";
import { CvImprovePanel } from "@/components/resumes/cv-improve-panel";

interface Props {
  params: Promise<{ parsedId: string }>;
  searchParams: Promise<{ profile?: string }>;
}

/**
 * /cv/granska/[parsedId]/forbattra — deterministiska CV-förbättringsförslag
 * (Fas 4 STEG B-2, F4-10, propose-and-approve). RSC. Display-only i v1: förslagen
 * visas read-only som vägledning — det finns inget apply-endpoint och ingen
 * tillämpa/godkänn-interaktion (CLAUDE.md §5 — en regelmotor skriver aldrig om tyst).
 *
 * Hämtar parse-artefakten (primär) + förbättringsförslagen (sekundär) parallellt.
 * Parse-resultatet styr sidans utfall (ok → rendera; notFound → 404; auth →
 * redirect; övrigt → civic fel-block). Förslagen degraderas civilt: om de inte är
 * ok renderas sid-skalet ändå + en notis i panelen (sidan 404:ar aldrig på ett
 * förbättrings-fel). CV-PII läses bara server-side (RSC) — `parsed.content`
 * passeras aldrig vidare till en klient-ö.
 */
export default async function CvImprovePage({ params, searchParams }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { parsedId } = await params;
  const { profile: rawProfile } = await searchParams;

  // Default till "Ats" vid saknad/ogiltig searchParam (case-sensitiv backend).
  const profileResult = renderProfileSchema.safeParse(rawProfile);
  const profile: RenderProfile = profileResult.success
    ? profileResult.data
    : "Ats";

  const [parsedResult, improvementsResult] = await Promise.all([
    getParsedResume(parsedId),
    getCvImprovements(parsedId, profile),
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
          <h1 className="jp-h1">Kunde inte ladda förbättringsförslagen</h1>
          <p className="jp-lede">
            Ett tekniskt fel uppstod. Försök ladda om sidan eller gå tillbaka
            till granskningen.
          </p>
          <div>
            <Link
              href={`/cv/granska/${parsedId}`}
              className="jp-btn jp-btn--secondary"
            >
              Tillbaka till granskningen
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(parsedResult);
  }

  // Parse-resultatet används bara för att bevisa ägarskap/existens (primär
  // grind) — själva CV-innehållet renderas inte här (förslagen bär sin egen
  // citerade evidens). OBS: detta är 404-PARITETS-grinden (forbattra 404:ar när
  // staging-artefakten är borta, precis som granska), INTE säkerhetsgränsen —
  // SuggestCvImprovementsQuery är oberoende ägar-scopad + IDOR fail-closed
  // server-side (404 cross-user), så grinden bär inte ensam åtkomstskyddet och
  // får inte "optimeras bort" som redundant. Förslagen degraderas civilt: bara
  // "ok" ger en panel.
  void parsed;
  const improvements: CvImprovementDto | null =
    improvementsResult.kind === "ok" ? improvementsResult.data : null;

  return (
    <div className="flex flex-col gap-6">
      <Link
        href={`/cv/granska/${parsedId}`}
        className="inline-flex items-center gap-1 text-body-sm text-text-secondary hover:text-text-primary self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>Tillbaka till granskningen</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">Förbättra importerat CV</h1>
        <p className="jp-lede">
          Det här är deterministiska förbättringsförslag för ditt importerade CV.
          Inget har ändrats i filen — varje förslag visar en före-och-efter-
          ändring med citerad evidens ur din egen text. Du väljer själv vad du
          tar med när du skriver ditt CV.
        </p>
      </header>

      <CvImprovePanel
        improvements={improvements}
        parsedId={parsedId}
        profile={profile}
      />
    </div>
  );
}
