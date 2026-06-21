import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { CvGapFillForm } from "@/components/resumes/cv-gapfill-form";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/granska/[parsedId]/komplettera — CV-import, gap-fill → promote (Fas 4
 * STEG B, F2). RSC.
 *
 * Hämtar parse-artefakten server-side (CV-PII läses ALDRIG i klientbunten) och
 * prefyller gap-fill-formen. Parsern gissar aldrig datum (DQ3-3a) — formen tvingar
 * användaren att fylla i strukturerade datum innan CV:t kan sparas. Vid lyckad
 * befordran navigerar server-actionen vidare till det nya CV:t (/cv/{id}).
 */
export default async function CvGapFillPage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const { parsedId } = await params;
  const result = await getParsedResume(parsedId);

  switch (result.kind) {
    case "ok":
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
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">Kunde inte ladda CV:t</h1>
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
      return assertNever(result);
  }

  const parsed = result.data;

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
        <h1 className="jp-h1">Komplettera och spara CV</h1>
        <p className="jp-lede">
          Vi har fyllt i det parsern hittade i din fil. Komplettera de fält som
          krävs (särskilt datum, som parsern aldrig gissar) och spara för att
          skapa ditt CV.
        </p>
      </header>

      <CvGapFillForm
        parsedId={parsedId}
        sourceFileName={parsed.sourceFileName}
        content={parsed.content}
      />
    </div>
  );
}
