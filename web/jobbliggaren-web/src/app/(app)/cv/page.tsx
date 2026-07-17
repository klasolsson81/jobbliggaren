import Link from "next/link";
import { redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { FileText, Plus, Upload } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getLatestPendingParsedResume, getResumes } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { formatDaysAgo } from "@/lib/i18n/relative-time";
import { formatTime } from "@/lib/i18n/format";
import { ResumeCard } from "@/components/resumes/resume-card";
import { DiscardDraftButton } from "@/components/resumes/discard-draft-button";
import { StatusPill } from "@/components/ui/status-pill";

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
export default async function CvListPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  // Åtgärdskortets tids-/relativtids-formatering (Fas 4b PR-8.3). Formatteraren
  // och den relativtids-scopade translatorn måste skaffas på async-toppnivå
  // (inga hooks i en RSC); härledda värden beräknas nedan när pendingCv finns.
  const fmt = await getFormatter();
  const tPendingRel = await getTranslations("pages.cv.pending.relativeTime");

  // CV-listan + taxonomi + profil parallellt. Taxonomi/profil matar
  // match-setup-rail-modalen (samma BFF-fetches som /installningar). Båda
  // degraderar civilt: utan taxonomi visas ingen wizard-trigger (yrkesväljaren
  // vore tom), så match-setup utelämnas hellre än renderas trasig.
  // Onboarding-frikoppling (DEL 1, CTO-bind pending-card): det senaste pending-
  // parsade CV:t (non-PII summering) hämtas i samma parallell-svep. Backend
  // svarar 200 med `null` när inget pending CV finns (inte 404). Degraderar
  // civilt: vid icke-ok eller `null` visas inget "slutför ditt CV"-kort.
  const [result, pendingResult] = await Promise.all([
    getResumes(),
    getLatestPendingParsedResume(),
  ]);
  const pendingCv = pendingResult.kind === "ok" ? pendingResult.data : null;

  // Åtgärdskortets härledda värden (Fas 4b PR-8.3). Källrad: filnamn + "Importerad
  // {relativ dag} {tid}". Uppgiftsmätaren ("X av Y uppgifter klara") togs bort med
  // Slutför-guidens retirement (CV-pivot 5c, R4): uppgifterna var guidens steg,
  // och en mätare mot en åtgärd som inte längre finns i appen vore oärlig (§5).
  const pendingImportedWhen = pendingCv
    ? `${formatDaysAgo(tPendingRel, pendingCv.uploadedAt)} ${formatTime(
        fmt,
        new Date(pendingCv.uploadedAt),
      )}`
    : null;

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
            <div className="jp-cvaction__actions">
              <Link
                href={`/cv/granska/${pendingCv.id}`}
                className="jp-btn jp-btn--primary"
              >
                <FileText size={16} aria-hidden="true" />
                <span>{t("cv.pending.cta")}</span>
              </Link>
              <DiscardDraftButton parsedId={pendingCv.id} />
            </div>
          </div>
        )}

        {/* #815 (Klas): the match-setup card used to live here. It is gone. Matching is
            configured under Inställningar, and duplicating that entry point on the CV hub
            made this page about two different things at once. The hub is about your CVs.
            Removing it also drops three requests from the page — the taxonomy tree, the
            profile, and a SEQUENTIAL skill-label round-trip that ran after the parallel
            fetch purely to seed the wizard. */}

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
