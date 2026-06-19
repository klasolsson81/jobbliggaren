import type { MatchGrade } from "@/lib/dto/job-ad-match";

/**
 * F4-13 (ADR 0076) — graderad match-tagg på /jobb-kortet. Ren presentations-
 * komponent (ingen client-state → server component, hydrerar inget). Renderar
 * den pre-staged `.jp-matchchip` (globals.css:1844): en prick + namngiven label.
 *
 * Designkontrakt (design-reviewer 2026-06-19, Form A — godkänd utan anmärkning):
 * - POSITIVE-ONLY: komponenten renderas bara när annonsen HAR en grad. Ingen
 *   "ingen match"/negativ-state existerar (parent renderar inte chip:en alls).
 * - Goodhart-vakt (hård): INGEN siffra/procent/mätare/stapel — bara en
 *   namngiven kategori + en prick. Aldrig en poäng.
 * - Färg: positiv = grön `--jp-success` (--high); aldrig röd (röd = danger).
 *   `--mid`/`--low` trappar ner mot neutral ink (success-rampen, ej accent-grön
 *   — kollision flaggad i globals.css:1870). Tokens flippar själva i dark.
 * - a11y (WCAG 1.4.1 / 4.1.2): den synliga label-texten ÄR det tillgängliga
 *   namnet → färg bär aldrig betydelse ensam. Pricken är dekorativ
 *   (`aria-hidden`) — den upprepar bara graden som texten redan uttrycker.
 */

interface GradePresentation {
  readonly modifier: string;
  readonly label: string;
}

// Grad → modifier + svensk civic label. Copy är F4-16-förfinbar (design-
// reviewer §Copy); inga utropstecken, ingen emoji, ingen versalisering (§10).
const GRADE_PRESENTATION: Record<MatchGrade, GradePresentation> = {
  Strong: { modifier: "jp-matchchip--high", label: "Stark match" },
  Good: { modifier: "jp-matchchip--mid", label: "Bra match" },
  Basic: { modifier: "jp-matchchip--low", label: "Grundmatch" },
};

export interface MatchChipProps {
  grade: MatchGrade;
}

export function MatchChip({ grade }: MatchChipProps) {
  const { modifier, label } = GRADE_PRESENTATION[grade];
  return (
    <span className={`jp-matchchip ${modifier}`} data-tag="match">
      <span className="jp-matchchip__dot" aria-hidden="true" />
      {label}
    </span>
  );
}
