import Link from "next/link";
import { redirect } from "next/navigation";
import { getFormatter, getTranslations } from "next-intl/server";
import { FileText, Upload } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { getLatestPendingParsedResume, getResumes } from "@/lib/api/resumes";
import { assertNever } from "@/lib/dto/_helpers";
import { formatDaysAgo } from "@/lib/i18n/relative-time";
import { formatTime } from "@/lib/i18n/format";
import { ResumeCard } from "@/components/resumes/resume-card";
import { DiscardDraftButton } from "@/components/resumes/discard-draft-button";
import { StatusPill } from "@/components/ui/status-pill";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("cv.meta.title") };
}

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
 *
 * #1061: de två CTA:erna in i skapa-från-grunden ("Nytt CV" i plattan, "Skapa
 * första CV" i tomt-tillståndet) är BORTTAGNA, inte inaktiverade. Vägen är
 * deferrad utan återkomstdatum, och en "kommer senare"-etikett hade påstått ett
 * schema ingenting bär — samma klass av obelagt påstående som ADR 0112 valde
 * 404 framför 308 för att undvika. Huset har avgjort samma fråga två gånger
 * (mallbyggaren, Förbättra-lagret) och tog bort ingången båda gångerna.
 * Import är hubbens enda ingång i MVP:n. `cv.lede` och `cv.emptyBody` är
 * omskrivna i samma ändring: de lovade skapande i prosa. Nycklarna `cv.newCv`
 * och `cv.emptyCreateFirst` ligger kvar inerta (ADR 0112 §Mechanism 1).
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
                med /jobb-bannerns vita kontroller. `--primary` ÄR den vita
                fyllda formen här (`.jp-pagehero .jp-btn--primary`); den
                outline-formade `--secondary` är genomskinlig med vit kant och
                var underordnad den vita primären som stod bredvid. #1061 tog
                bort den primären, så outline-formen blev plattans ENDA
                kontroll och föll till 1.4.11-kontrast mot gradienten. */}
            <Link href="/cv/importera" className="jp-btn jp-btn--primary">
              <Upload size={16} aria-hidden="true" />
              <span>{t("cv.importCv")}</span>
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
            Discard-kontrollen är en klient-ö (bekräfta-dialog); resten är RSC.

            #1383: the card is a section level with the CV list, not a preamble to it, so its
            heading is an h2. `components/resumes/cv-block-reason.tsx` already renders a
            `.jp-cvaction` block as a `<section aria-labelledby>` with an
            `<h2 className="jp-cvaction__heading">`; the two differ in content, not in that
            shape. */}
        {pendingCv !== null && (
          <section aria-labelledby="cv-pending-title" className="jp-cvaction">
            <StatusPill tone="warning">{t("cv.pending.kicker")}</StatusPill>
            <p className="jp-cvaction__source">
              {pendingCv.sourceFileName}
              {pendingImportedWhen !== null && (
                <> · {t("cv.pending.imported", { when: pendingImportedWhen })}</>
              )}
            </p>
            <div className="jp-cvaction__lead">
              <h2 id="cv-pending-title" className="jp-cvaction__heading">
                {t("cv.pending.heading")}
              </h2>
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
          </section>
        )}

        {/* #815 (Klas): the match-setup card used to live here. It is gone. Matching is
            configured under Inställningar, and duplicating that entry point on the CV hub
            made this page about two different things at once. The hub is about your CVs.
            Removing it also drops three requests from the page — the taxonomy tree, the
            profile, and a SEQUENTIAL skill-label round-trip that ran after the parallel
            fetch purely to seed the wizard. */}

        {/* #1060: tomt-tillståndet undertrycks när ett pending-artefakt finns. De två
            villkoren var oberoende, så en användare med ett inläst men osparat CV fick
            "Kräver åtgärd" direkt ovanför "Inga CV ännu" — två motsägande besked om
            samma fil. Åtgärdskortet vinner: det är sant, och det bär redan båda
            vägarna vidare (öppna granskningen, eller ta bort utkastet). Utan pending
            är listan verkligen tom och tomt-tillståndet är rätt. */}
        {sorted.length === 0 && pendingCv === null ? (
          <div className="jp-empty">
            <div className="jp-empty__title">{t("cv.emptyTitle")}</div>
            <p className="jp-empty__body">{t("cv.emptyBody")}</p>
            <div className="jp-empty__actions">
              <Link href="/cv/importera" className="jp-btn jp-btn--primary">
                <Upload size={14} aria-hidden="true" /> {t("cv.importCv")}
              </Link>
            </div>
          </div>
        ) : sorted.length === 0 ? null : (
          /* #1383: the grid was an unlabelled region, so the next heading after the page's
             h1 was a card title — h1 -> h3, a skipped level (WCAG 1.3.1). The heading names
             what the grid HOLDS rather than repeating the page title: these CVs are saved,
             which is the distinction the pending card above draws in prose.
             `jp-h2` and not `text-h2` for the reason written at
             `components/company-criteria/foretag-sok-results.tsx`. */
          <section
            aria-labelledby="cv-list-title"
            /* Only when the action card sits above: the heading otherwise binds almost as
               strongly upward to that card as downward to the list it names. It has to EXCEED
               the card's own bottom margin to move anything at all — `.jp-page` is a plain
               block container, so adjacent sibling margins collapse and any smaller value is
               silently inert. Alone under the hero the spacing is the container's padding and
               is already right, which is why this stays conditional. */
            className={pendingCv !== null ? "mt-8" : undefined}
          >
            <h2 id="cv-list-title" className="jp-h2">
              {t("cv.listHeading")}
            </h2>
            <div className="jp-cvgrid mt-4">
              {sorted.map((resume) => (
                <ResumeCard key={resume.id} resume={resume} />
              ))}
            </div>
          </section>
        )}
      </div>
    </>
  );
}
