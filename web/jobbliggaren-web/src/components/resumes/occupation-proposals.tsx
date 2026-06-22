import { useTranslations } from "next-intl";
import type { OccupationProposalDto } from "@/lib/dto/parsed-resume";

/**
 * Yrkesgruppsförslag (F4-9, ADR 0040). RSC. DISPLAY-ONLY i F1 — derive/confirm-
 * flödet är STEG B-2 (ingen bekräfta-action här). Visas endast när listan inte
 * är tom (caller-gatad). Förslagen är obekräftade och aldrig auto-valda — copy:n
 * gör det tydligt att användaren bekräftar yrke i ett senare steg.
 */
export function OccupationProposals({
  proposals,
}: {
  proposals: readonly OccupationProposalDto[];
}) {
  const t = useTranslations("resumes");

  if (proposals.length === 0) return null;

  return (
    <section
      className="jp-occupations"
      aria-labelledby="occupations-title"
    >
      <h2 id="occupations-title" className="jp-occupations__title">
        {t("occupations.title")}
      </h2>
      <p className="jp-occupations__lede">{t("occupations.lede")}</p>
      <ul className="jp-occupations__list">
        {proposals.map((proposal) => (
          <li key={proposal.conceptId} className="jp-skill-chip">
            {proposal.label}
          </li>
        ))}
      </ul>
    </section>
  );
}
