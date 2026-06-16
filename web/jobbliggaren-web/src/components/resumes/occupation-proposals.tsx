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
  proposals: OccupationProposalDto[];
}) {
  if (proposals.length === 0) return null;

  return (
    <section
      className="jp-occupations"
      aria-labelledby="occupations-title"
    >
      <h2 id="occupations-title" className="jp-occupations__title">
        Möjliga yrkesområden
      </h2>
      <p className="jp-occupations__lede">
        Förslag på yrkesområde utifrån ditt CV. Du bekräftar yrke i ett senare
        steg.
      </p>
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
