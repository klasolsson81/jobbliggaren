import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { Edit } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { CvPreview } from "@/components/resumes/cv-preview";
import { StatusPill, type PillTone } from "@/components/ui/status-pill";
import type { ResumeListItemDto } from "@/lib/types/resumes";

interface ResumeCardProps {
  resume: ResumeListItemDto;
}

const MAX_VISIBLE_SKILLS = 5;

/** Kända mallnycklar (paritet backend-enum, ADR 0096). Ett okänt värde faller
 * tillbaka till råsträngen — nya mallar får aldrig krascha listvyn. */
const KNOWN_TEMPLATES = ["Klar", "Accentlinje", "MorkPanel"] as const;
type KnownTemplate = (typeof KNOWN_TEMPLATES)[number];
function isKnownTemplate(value: string): value is KnownTemplate {
  return (KNOWN_TEMPLATES as readonly string[]).includes(value);
}

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
 * Förhandsgranska-knapp (TD-112 / #202): den befordrade Resume-griden saknar ett
 * parsedId, men konsumerar nu render-by-Resume-id-vägen
 * `/api/cv/{id}/preview` (BFF → `GET /api/v1/resumes/{id}/render`) via samma
 * `CvPreview`-modal som de parsade ytorna (`/cv/granska/[parsedId]`-familjen).
 * Trigger-storleken matchas till Redigera-knappens `--sm` (design-koherens).
 *
 * FAS-DEFERRAL (ADR 0058 amend):
 *  - "+N"-skill-chip när content.skills.length > 5: kräver content-fetch,
 *    skippas tills denormalisering av total-skills-count finns
 */
export function ResumeCard({ resume }: ResumeCardProps) {
  const t = useTranslations("resumes");
  const format = useFormatter();
  const updatedAt = formatDate(format, resume.updatedAt) ?? "";
  const languageLabel = resume.language === "En" ? "EN" : "SV";

  // Mallnamn visas bara för Skapad-CV (origin Template); okänd mall → råvärdet.
  const templateLabel = isKnownTemplate(resume.template)
    ? t(`card.templateName.${resume.template}`)
    : resume.template;

  // Granskningsstatus-badge ur den DEK-fria finding-ledgern (§5-ärlighet):
  // null → "Granska" (aldrig "0"/"Inga åtgärder"), 0 → "Inga åtgärder", N → "N
  // att åtgärda". Länkar till den kanoniska granska-vyn i alla tre lägen (PR-8.4);
  // pill-texten är länkens tillgängliga namn.
  const findingBadge: { tone: PillTone; label: string } =
    resume.openFindingCount === null
      ? { tone: "neutral", label: t("card.findingsReview") }
      : resume.openFindingCount === 0
        ? { tone: "success", label: t("card.findingsNone") }
        : {
            tone: "warning",
            label: t("card.findingsCount", { count: resume.openFindingCount }),
          };

  return (
    <article className="jp-cv">
      <div className="jp-cv__head">
        <div style={{ minWidth: 0, flex: 1 }}>
          <h3 className="jp-cv__title">{resume.name}</h3>
          {resume.latestRole && (
            <p className="jp-cv__role">{resume.latestRole}</p>
          )}
        </div>
        <div className="jp-cv__badges">
          {resume.isPrimary && (
            <span className="jp-pill jp-pill--brand">
              <span className="jp-pill__dot" aria-hidden="true" />
              {t("card.primary")}
            </span>
          )}
          {/* Ursprungs-badge: Import → "Importerad", Template → "Skapad",
              Legacy (pre-origin-CV) → ingen badge. */}
          {resume.origin === "Import" && (
            <StatusPill tone="info">{t("card.originImport")}</StatusPill>
          )}
          {resume.origin === "Template" && (
            <StatusPill tone="neutral">{t("card.originTemplate")}</StatusPill>
          )}
          {/* Länkad granskningsstatus (PR-8.4): pillen behåller sitt utseende,
              länken bär fokusring + hover-affordans. */}
          <Link
            href={`/cv/${resume.id}/granska`}
            className="jp-cv__badge-link"
          >
            <StatusPill tone={findingBadge.tone}>{findingBadge.label}</StatusPill>
          </Link>
        </div>
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
          {t("card.sections", { count: resume.sectionCount })}
        </span>
        <span>{languageLabel}</span>
        <span>{t("card.updated", { date: updatedAt })}</span>
        {resume.origin === "Template" && (
          <span>{t("card.template", { name: templateLabel })}</span>
        )}
      </div>

      <div className="jp-cv__actions">
        <CvPreview
          previewUrl={`/api/cv/${resume.id}/preview`}
          atsTextUrl={`/api/cv/${resume.id}/ats-text`}
          initialProfile="Ats"
          triggerClassName="jp-btn jp-btn--secondary jp-btn--sm"
          triggerIconSize={14}
        />
        <Link
          href={`/cv/${resume.id}`}
          className="jp-btn jp-btn--secondary jp-btn--sm"
        >
          <Edit size={14} aria-hidden="true" />
          <span>{t("card.edit")}</span>
        </Link>
      </div>
    </article>
  );
}
