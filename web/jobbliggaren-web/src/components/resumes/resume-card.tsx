import Link from "next/link";
import { Edit } from "lucide-react";
import type { ResumeListItemDto } from "@/lib/types/resumes";

interface ResumeCardProps {
  resume: ResumeListItemDto;
}

const MAX_VISIBLE_SKILLS = 5;

/**
 * Resume/CV-kort i v3-listvy (`.jp-cv`-mönstret per HANDOVER-v3 §7.4 + målbild
 * 09-cv-light.png). F6 P3a frontend återupptas efter backend-leverans 19cde94
 * (Resume-DTO-utvidgning) — alla 5 nya fält wirede.
 *
 * Layout (matchar prototyp src-v3/pages.jsx CvPage):
 *  - jp-cv__head: vänster titel + roll, höger Standard-pill (om isPrimary)
 *  - skill-chips: visa upp till 5 (`topSkills` är redan capped till 5 i DTO),
 *    "+N"-chip om versionens skills.length > 5 (backend-projektion förlorar
 *    den info; vi kan inte rendera "+N" utan content-fetch — utelämnas medvetet)
 *  - jp-cv__meta: "N sektioner" (NORMAL font) + språkkod "SV"/"EN" (MONO)
 *    + "Uppd. YYYY-MM-DD" (MONO) — per HANDOVER §3 (mono endast för data)
 *  - jp-cv__actions: Redigera → /cv/{id} (existing route)
 *
 * Ingen förhandsgranska-knapp här: den befordrade Resume-griden saknar parsedId
 * och kan därför inte konsumera `/api/v1/resumes/parsed/{id}/render` (det finns
 * ingen render-by-Resume-id-väg — uppskjuten backend-STEG). Den döda disabled-
 * stuben togs bort per #119:s banner-precedent (annonsera aldrig en backend-väg
 * som inte finns). PDF-förhandsgranskning lever på de parsade ytorna
 * (`/cv/granska/[parsedId]`-familjen) via CvPreview.
 *
 * FAS-DEFERRAL (ADR 0058 amend):
 *  - "+N"-skill-chip när content.skills.length > 5: kräver content-fetch,
 *    skippas tills denormalisering av total-skills-count finns
 */
export function ResumeCard({ resume }: ResumeCardProps) {
  const updatedAt = new Date(resume.updatedAt).toLocaleDateString("sv-SE");
  const languageLabel = resume.language === "En" ? "EN" : "SV";

  return (
    <article className="jp-cv">
      <div className="jp-cv__head">
        <div style={{ minWidth: 0, flex: 1 }}>
          <h3 className="jp-cv__title">{resume.name}</h3>
          {resume.latestRole && (
            <p className="jp-cv__role">{resume.latestRole}</p>
          )}
        </div>
        {resume.isPrimary && (
          <span className="jp-pill jp-pill--brand">
            <span className="jp-pill__dot" aria-hidden="true" />
            Standard
          </span>
        )}
      </div>

      {resume.topSkills.length > 0 && (
        <div className="jp-cv__skills">
          {resume.topSkills.slice(0, MAX_VISIBLE_SKILLS).map((skill) => (
            <span key={skill} className="jp-skill-chip">
              {skill}
            </span>
          ))}
        </div>
      )}

      <div className="jp-cv__meta">
        <span className="jp-cv__meta__sections">
          {resume.sectionCount}{" "}
          {resume.sectionCount === 1 ? "sektion" : "sektioner"}
        </span>
        <span>{languageLabel}</span>
        <span>Uppd. {updatedAt}</span>
      </div>

      <div className="jp-cv__actions">
        <Link
          href={`/cv/${resume.id}`}
          className="jp-btn jp-btn--secondary jp-btn--sm"
        >
          <Edit size={14} aria-hidden="true" />
          <span>Redigera</span>
        </Link>
      </div>
    </article>
  );
}
