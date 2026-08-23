import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { FileText } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { CvPreview } from "@/components/resumes/cv-preview";
import { RenameResumeForm } from "@/components/resumes/rename-resume-form";
import { DeleteResumeDialog } from "@/components/resumes/delete-resume-dialog";
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
 *  - jp-cv__actions: Granska → /cv/{id}/granska (primär) + Förhandsgranska,
 *    och högerskjutet Byt namn + Radera. Redigera-länken till /cv/{id} är
 *    borttagen (#1373) — se kommentaren vid raden.
 *
 * Förhandsgranska-knapp (TD-112 / #202): den befordrade Resume-griden saknar ett
 * parsedId, men konsumerar nu render-by-Resume-id-vägen
 * `/api/cv/{id}/preview` (BFF → `GET /api/v1/resumes/{id}/render`) via samma
 * `CvPreview`-modal som de parsade ytorna (`/cv/granska/[parsedId]`-familjen).
 * Trigger-storleken är `--sm` för att matcha resten av actions-raden
 * (design-koherens). Den matchades tidigare mot Redigera-knappen, som #1373 tog
 * bort; `jp-btn--sm` och shadcn-knapparnas `size="sm"` är båda 36px höga, så
 * raden står jämn trots två knappfamiljer.
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
              länken bär fokusring + hover-affordans. Vid `null` är pillen OLÄNKAD:
              den säger då bara "Granska", samma ord och samma mål som radens egen
              knapp, och bär noll extra information — två identiska länkar till
              samma URL. Med ett antal (`N`/`0`) bär den däremot siffran, som
              knappen inte gör, och förblir en länk. */}
          {resume.openFindingCount === null ? (
            <StatusPill tone={findingBadge.tone}>{findingBadge.label}</StatusPill>
          ) : (
            <Link
              href={`/cv/${resume.id}/granska`}
              className="jp-cv__badge-link"
            >
              <StatusPill tone={findingBadge.tone}>{findingBadge.label}</StatusPill>
            </Link>
          )}
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

      {/* #1373 — hela motiveringen (varför redigeringen pausades, varför radering och
          namnbyte flyttade hit i stället för att stranda, och Art. 7(3)-grunden) bor i
          EN fil: doc-kommentaren på den grindade routen, `app/(app)/cv/[id]/page.tsx`.
          Här står bara det som är sant om just den här raden.

          Ordningen bär hierarkin: Granska är radens betonade kontroll (produktens
          centrala verb, som annars bara nåtts via statuspillen), och de två
          hanterings-kontrollerna är grupperade sist så den destruktiva aldrig är radens
          mest framträdande element. Betoningen är `--emphasis`, INTE `--primary`:
          kortet renderas en gång per CV, och en solid fyllning per kort ger N primärer
          i samma grid, vilket DESIGN.md §6 förbjuder uttryckligen (CTO-bind #788).

          `flex-wrap` är en buggfix, inte kosmetik: raden är `nowrap` som default och
          fyra kontroller ryms inte i griddcellen, så hanteringsgruppen hamnade utanför
          kortet och under grannkortet, som avlyssnade klicket. Regenerera geometrin med
          E2E-sonden i commit-meddelandet. Wrap sätts på elementet och inte i regeln,
          per DESIGN.md §6:s "`.jp-*` är OLAGRAT"-punkt: `.jp-cv__actions` deklarerar
          aldrig `flex-wrap`, så
          utilityn biter — men den blir tyst verkningslös om någon senare lägger
          `flex-wrap` i själva regeln. Klassen har TVÅ konsumenter, och skelettet
          (`app/(app)/cv/(hub)/loading.tsx`) fick samma ändring; ändras raden här ska den
          ändras där. */}
      <div className="jp-cv__actions flex-wrap">
        <Link
          href={`/cv/${resume.id}/granska`}
          className="jp-btn jp-btn--emphasis jp-btn--sm"
          aria-label={t("card.reviewCtaAria", { name: resume.name })}
        >
          <FileText size={14} aria-hidden="true" />
          <span>{t("card.reviewCta")}</span>
        </Link>
        <CvPreview
          previewUrl={`/api/cv/${resume.id}/preview`}
          atsTextUrl={`/api/cv/${resume.id}/ats-text`}
          initialProfile="Ats"
          triggerClassName="jp-btn jp-btn--secondary jp-btn--sm"
          triggerIconSize={14}
          triggerAriaLabel={t("preview.triggerAria", { name: resume.name })}
        />
        <div className="ms-auto flex flex-wrap gap-2">
          <RenameResumeForm
            resumeId={resume.id}
            currentName={resume.name}
            triggerClassName="jp-btn jp-btn--secondary jp-btn--sm"
            triggerAriaLabel={t("rename.triggerAria", { name: resume.name })}
          />
          <DeleteResumeDialog
            resumeId={resume.id}
            resumeName={resume.name}
            triggerClassName="jp-btn jp-btn--danger jp-btn--sm"
            triggerAriaLabel={t("delete.triggerAria", { name: resume.name })}
          />
        </div>
      </div>
    </article>
  );
}
