import Link from "next/link";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { Plus, Upload } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getResumes } from "@/lib/api/resumes";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { assertNever } from "@/lib/dto/_helpers";
import { ResumeCard } from "@/components/resumes/resume-card";
import { CvMatchSetup } from "@/components/resumes/cv-match-setup";

/** Route till CV-importflödet (verifierad on-disk: app/(app)/cv/importera). */
const IMPORT_CV_HREF = "/cv/importera";

/**
 * /cv-listvyn (F6 P3a, HANDOVER §7.4 + målbild 09-cv-light.png).
 *
 * Backend 19cde94 (Resume-DTO-utvidgning) gör att alla 5 nya fält
 * (isPrimary/language/latestRole/sectionCount/topSkills) finns på
 * `ResumeListItemDto` och kan renderas direkt via `<ResumeCard />` i
 * v3-grid.
 *
 * AnpassaCvBanner är BORTTAGEN (Fas 4 STEG B-2): den marknadsförde CvTailor /
 * annons-skräddarsöm, en LLM-funktion som ADR 0071 garanterar aldrig byggs.
 * Förbättra-CV-flödet (deterministiskt, F4-10) lever i stället på granska-vyn.
 */
export default async function CvListPage({
  searchParams,
}: {
  searchParams: Promise<{ [key: string]: string | string[] | undefined }>;
}) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  // Post-promote-prompten (design C.3) visas när /cv öppnas med ?matchning=1
  // — sätts av promote-/upload-flödet vid redirect hit. Annars dold.
  const { matchning } = await searchParams;
  const showMatchPrompt = matchning === "1";

  // CV-listan + taxonomi + profil parallellt. Taxonomi/profil matar
  // match-setup-wizarden (samma BFF-fetches som /installningar). Båda
  // degraderar civilt: utan taxonomi visas ingen wizard-trigger (yrkesväljaren
  // vore tom), så match-setup utelämnas hellre än renderas trasig.
  const [result, taxonomyResult, profileResult] = await Promise.all([
    getResumes(),
    getTaxonomyTree(),
    getMyProfile(),
  ]);
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;
  const profile = profileResult.kind === "ok" ? profileResult.data : null;

  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("common.rateLimitedTitle")}</h1>
          <p className="jp-lede">
            {t("common.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="flex flex-col gap-4">
          <h1 className="jp-h1">{t("cv.loadErrorTitle")}</h1>
          <p className="jp-lede">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }

  const items = result.data.items;
  // API returnerar redan sorterat på senast uppdaterad; defensive sort.
  const sorted = [...items].sort(
    (a, b) =>
      new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
  );

  return (
    <>
      {/* F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.4). */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("cv.title")}</h1>
            <p className="jp-pagehero__lede">{t("cv.lede")}</p>
          </div>
          <div className="jp-pagehero__aside">
            {/* G3 (Klas-fynd 2026-06-10): vit knapp i plattan, konsekvent
                med /jobb-bannerns vita kontroller. */}
            <Link href="/cv/importera" className="jp-btn jp-btn--secondary">
              <Upload size={16} aria-hidden="true" />
              <span>{t("cv.importCv")}</span>
            </Link>
            <Link href="/cv/ny" className="jp-btn jp-btn--primary">
              <Plus size={16} aria-hidden="true" />
              <span>{t("cv.newCv")}</span>
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* Match-setup-affordans (ADR 0077 STEG 5): trigger + dismissbar
            post-promote-prompt. Visas när taxonomi + profil laddats och minst
            ett CV finns (wizarden prefillar från CV:t). Klient-ö — wizarden bär
            det enda MatchPreferences-PUT:et. */}
        {taxonomy !== null && profile !== null && sorted.length > 0 && (
          <div className="jp-cvmatch-bar">
            <div className="jp-cvmatch-bar__lead">
              <p className="jp-cvmatch-bar__title">{t("cv.matchBarTitle")}</p>
              <p className="jp-cvmatch-bar__text">{t("cv.matchBarText")}</p>
            </div>
            <CvMatchSetup
              occupationFields={taxonomy.occupationFields}
              regions={taxonomy.regions}
              employmentTypes={taxonomy.employmentTypes}
              persistedOccupationGroups={profile.preferredOccupationGroups}
              persistedRegions={profile.preferredRegions}
              persistedEmploymentTypes={profile.preferredEmploymentTypes}
              importCvHref={IMPORT_CV_HREF}
              hasPreferences={profile.hasStatedDesiredOccupation}
              showPrompt={showMatchPrompt}
            />
          </div>
        )}

        {sorted.length === 0 ? (
          <div className="jp-empty">
            <div className="jp-empty__kicker">{t("cv.emptyKicker")}</div>
            <div className="jp-empty__title">{t("cv.emptyTitle")}</div>
            <p className="jp-empty__body">{t("cv.emptyBody")}</p>
            <div className="jp-empty__actions">
              <Link href="/cv/ny" className="jp-btn jp-btn--primary">
                <Plus size={14} aria-hidden="true" /> {t("cv.emptyCreateFirst")}
              </Link>
              <Link href="/cv/importera" className="jp-btn jp-btn--secondary">
                <Upload size={14} aria-hidden="true" /> {t("cv.importCv")}
              </Link>
            </div>
          </div>
        ) : (
          <div className="jp-cvgrid">
            {sorted.map((resume) => (
              <ResumeCard key={resume.id} resume={resume} />
            ))}
          </div>
        )}
      </div>
    </>
  );
}
