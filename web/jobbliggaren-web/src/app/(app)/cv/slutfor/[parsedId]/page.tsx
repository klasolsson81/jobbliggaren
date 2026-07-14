import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getParsedResume, getCvSectionSuggestions } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { CvCompleteGuide } from "@/components/resumes/cv-complete-guide";
import { PersonnummerWarning } from "@/components/resumes/personnummer-warning";

interface Props {
  params: Promise<{ parsedId: string }>;
}

/**
 * /cv/slutfor/[parsedId] — Slutför CV: den fyra-stegs-guide som ersätter
 * komplettera-formen (Fas 4b PR-8.3, design handoff §5.2, CTO-bind Q2/Q5/Q7). RSC.
 *
 * Hämtar parse-artefakten server-side (CV-PII läses ALDRIG i klientbunten) och
 * skickar det tolkade innehållet + konfidensen som props till klient-ön. Parsern
 * gissar aldrig datum (DQ3-3a) — guiden tvingar användaren att fylla i datum innan
 * CV:t kan sparas. Vid lyckad befordran redirectar server-actionen till granskningen
 * av det nya CV:t (`/cv/{resumeId}/granska`). Personnummer ytas flag-only via
 * `PersonnummerWarning` ovanför guiden (ADR 0074 Invariant 1).
 */
export default async function CvCompleteGuidePage({ params }: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
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
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
          <p className="jp-lede">
            {t("common.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    case "forbidden":
    case "error":
      return (
        <div className="jp-container jp-page flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.slutfor.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("cv.slutfor.errorBody")}</p>
          <div>
            <Link href="/cv" className="jp-btn jp-btn--secondary">
              {t("cv.slutfor.backLink")}
            </Link>
          </div>
        </div>
      );
    default:
      return assertNever(result);
  }

  const parsed = result.data;

  // Yrkesstyrda sektionsförslag (8b.4a, ADR 0107). Hämtas server-side, aldrig i en
  // useEffect. Förslagen är RÅDGIVANDE: går hämtningen fel renderar guiden sin generiska
  // "Lägg till sektion"-panel precis som förut. En trasig förslagsrad får aldrig blockera
  // det som faktiskt är uppgiften — att slutföra CV:t. Därför inget felläge här, bara
  // frånvaro.
  const suggestionsResult = await getCvSectionSuggestions(parsedId);
  const sectionSuggestions =
    suggestionsResult.kind === "ok" ? suggestionsResult.data : null;

  return (
    // .jp-container: guiden är en fullskärms-fokusyta men ska ändå cappa sin
    // measure på bred skärm (design-reviewer Major PR-8.3 — app-konventionen,
    // ADR 0052; utan cap sträcker sig bekräfta-raderna kant-till-kant på 3440).
    <div className="jp-container jp-page flex flex-col gap-6">
      {/* En h1 per sida (a11y): sidtiteln bärs här; guidens header-rad bär mono-
          källraden + Stäng-kontrollen (ingen dubbel titel), och varje steg är en
          h2 under den här h1:an. */}
      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">{t("cv.slutfor.title")}</h1>
        <p className="jp-lede">{t("cv.slutfor.lede")}</p>
      </header>

      <PersonnummerWarning personnummer={parsed.personnummer} />

      <CvCompleteGuide
        parsedId={parsedId}
        sourceFileName={parsed.sourceFileName}
        content={parsed.content}
        confidence={parsed.confidence}
        sectionSuggestions={sectionSuggestions}
      />
    </div>
  );
}
