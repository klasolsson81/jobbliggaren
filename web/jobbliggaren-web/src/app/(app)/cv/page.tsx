import Link from "next/link";
import { redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { FileText, Plus, Upload } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getLatestPendingParsedResume, getResumes } from "@/lib/api/resumes";
import { getMyProfile } from "@/lib/api/me";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { resolveSkillLabels } from "@/lib/api/skills";
import { assertNever } from "@/lib/dto/_helpers";
import { formatDaysAgo } from "@/lib/i18n/relative-time";
import { formatTime } from "@/lib/i18n/format";
import { ResumeCard } from "@/components/resumes/resume-card";
import { CvMatchSetup } from "@/components/resumes/cv-match-setup";
import { DiscardDraftButton } from "@/components/resumes/discard-draft-button";
import { StatusPill } from "@/components/ui/status-pill";
import { countCompletedTasks, TOTAL_GAP_TASKS } from "@/lib/resumes/gap-tasks";

/** Route till CV-importflödet (verifierad on-disk: app/(app)/cv/importera). */
const IMPORT_CV_HREF = "/cv/importera";

/** Stabil id för åtgärdskortets mätar-etikett → progressbarens tillgängliga namn
 * (aria-labelledby). Endast ett åtgärdskort renderas per sida (senaste pending),
 * så en konstant id är säker (ingen dubblett). */
const PENDING_METER_LABEL_ID = "cv-pending-meter-label";

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
  // Åtgärdskortets tids-/relativtids-formatering (Fas 4b PR-8.3). Formatteraren
  // och den relativtids-scopade translatorn måste skaffas på async-toppnivå
  // (inga hooks i en RSC); härledda värden beräknas nedan när pendingCv finns.
  const fmt = await getFormatter();
  const tPendingRel = await getTranslations("pages.cv.pending.relativeTime");

  // Post-promote-prompten (design C.3) visas när /cv öppnas med ?matchning=1
  // — sätts av promote-/upload-flödet vid redirect hit. Annars dold.
  const { matchning } = await searchParams;
  const showMatchPrompt = matchning === "1";

  // CV-listan + taxonomi + profil parallellt. Taxonomi/profil matar
  // match-setup-rail-modalen (samma BFF-fetches som /installningar). Båda
  // degraderar civilt: utan taxonomi visas ingen wizard-trigger (yrkesväljaren
  // vore tom), så match-setup utelämnas hellre än renderas trasig.
  // Onboarding-frikoppling (DEL 1, CTO-bind pending-card): det senaste pending-
  // parsade CV:t (non-PII summering) hämtas i samma parallell-svep. Backend
  // svarar 200 med `null` när inget pending CV finns (inte 404). Degraderar
  // civilt: vid icke-ok eller `null` visas inget "slutför ditt CV"-kort.
  const [result, taxonomyResult, profileResult, pendingResult] =
    await Promise.all([
      getResumes(),
      getTaxonomyTree(),
      getMyProfile(),
      getLatestPendingParsedResume(),
    ]);
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;
  const profile = profileResult.kind === "ok" ? profileResult.data : null;
  const pendingCv = pendingResult.kind === "ok" ? pendingResult.data : null;

  // Åtgärdskortets härledda värden (Fas 4b PR-8.3). Källrad: filnamn + "Importerad
  // {relativ dag} {tid}". Mätaren "X av Y uppgifter klara" renderas ENDAST när
  // gaps finns (icke-null) — ett ärligt "inte beräknat" (§5), aldrig en gissning.
  const pendingImportedWhen = pendingCv
    ? `${formatDaysAgo(tPendingRel, pendingCv.uploadedAt)} ${formatTime(
        fmt,
        new Date(pendingCv.uploadedAt),
      )}`
    : null;
  // Mätaren delar uppgiftsdefinitionen med Slutför-guiden (CTO-bind Q5) via
  // `gap-tasks.ts` — de får aldrig vara oense om vad en uppgift är. Räkningen
  // hämtas därför ur `countCompletedTasks`/`TOTAL_GAP_TASKS`, inte inline.
  const pendingGaps = pendingCv?.gaps ?? null;
  const pendingMeter = pendingGaps
    ? (() => {
        const done = countCompletedTasks(pendingGaps);
        return {
          done,
          total: TOTAL_GAP_TASKS,
          pct: Math.round((done / TOTAL_GAP_TASKS) * 100),
        };
      })()
    : null;

  // #422 (#253/#277 group-resolution): reverse-resolve the saved skill concept-
  // ids to GROUPS server-side, mirroring installningar/page.tsx:47-52. Without
  // this seed the match-setup wizard renders raw concept-ids for a returning
  // user's saved skills on a cold load (SkillSection's CV auto-suggest is gated
  // on parsedResumeId, which CvMatchSetup never passes). Depends on the profile
  // → runs after the parallel fetch; absent profile or a failed resolve → empty
  // list and the wizard keeps its graceful id-fallback. Empty preferredSkills
  // short-circuits with no backend round-trip.
  const skillGroupsResult =
    profile !== null ? await resolveSkillLabels(profile.preferredSkills) : null;
  const initialSkillGroups =
    skillGroupsResult?.kind === "ok" ? skillGroupsResult.data : [];

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
        {/* Åtgärdskort (design handoff §5.1): hubbens hero när användaren har ett
            inläst men ICKE-sparat (PendingReview) CV. Warning-tonad vänster-accent
            + StatusPill-kicker signalerar "kräver åtgärd" utan att vara ett fel —
            informationen bärs av text + struktur + pill, aldrig av färg allena
            (WCAG 1.4.1). Copyn påstår ALDRIG att CV:t är sparat — bara inläst.
            Discard-kontrollen är en klient-ö (bekräfta-dialog); resten är RSC. */}
        {pendingCv !== null && (
          <div className="jp-cvaction">
            <StatusPill tone="warning">{t("cv.pending.kicker")}</StatusPill>
            <p className="jp-cvaction__source">
              {pendingCv.sourceFileName}
              {pendingImportedWhen !== null && (
                <> · {t("cv.pending.imported", { when: pendingImportedWhen })}</>
              )}
            </p>
            <div className="jp-cvaction__lead">
              <p className="jp-cvaction__heading">{t("cv.pending.heading")}</p>
              <p className="jp-cvaction__body">{t("cv.pending.body")}</p>
            </div>
            {pendingMeter !== null && (
              <div className="jp-cvaction__meter">
                <span
                  id={PENDING_METER_LABEL_ID}
                  className="jp-cvaction__meter-label"
                >
                  {t("cv.pending.meter", {
                    done: pendingMeter.done,
                    total: pendingMeter.total,
                  })}
                </span>
                <div
                  className="jp-cvaction__meter-track"
                  role="progressbar"
                  aria-valuenow={pendingMeter.done}
                  aria-valuemin={0}
                  aria-valuemax={pendingMeter.total}
                  aria-labelledby={PENDING_METER_LABEL_ID}
                >
                  <div
                    className="jp-cvaction__meter-fill"
                    style={{ width: `${pendingMeter.pct}%` }}
                  />
                </div>
              </div>
            )}
            <div className="jp-cvaction__actions">
              <Link
                href={`/cv/slutfor/${pendingCv.id}`}
                className="jp-btn jp-btn--primary"
              >
                <FileText size={16} aria-hidden="true" />
                <span>{t("cv.pending.cta")}</span>
              </Link>
              <span className="jp-cvaction__estimate">
                {t("cv.pending.timeEstimate")}
              </span>
              <DiscardDraftButton parsedId={pendingCv.id} />
            </div>
          </div>
        )}

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
              persistedMunicipalities={profile.preferredMunicipalities}
              persistedEmploymentTypes={profile.preferredEmploymentTypes}
              persistedSkills={profile.preferredSkills}
              persistedSkillGroups={initialSkillGroups}
              persistedOccupationExperience={profile.preferredOccupationExperience}
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
