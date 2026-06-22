import { useTranslations } from "next-intl";
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

// Grad → modifier (design-reviewer 2026-06-20, F4-16); etiketten resolveras via
// next-intl (`ui.match.grade.*`). Inga utropstecken, ingen emoji, ingen
// versalisering (§10). Ladder: Grundmatch → Bra match → Stark match →
// Toppmatch. "Top" är den golden-rungen (Klas-bind: ordet "Toppmatch", färgen
// djupare/solid grön — INGEN ny token, INGEN guld-token); modifier `--top` ger
// solid `--jp-success`-fyllning över `--high`:s tonade grön (hierarki via
// fyllvikt, ej ny hue).
const GRADE_MODIFIER: Record<MatchGrade, string> = {
  Top: "jp-matchchip--top",
  Strong: "jp-matchchip--high",
  Good: "jp-matchchip--mid",
  Basic: "jp-matchchip--low",
};

export interface MatchChipProps {
  grade: MatchGrade;
}

export function MatchChip({ grade }: MatchChipProps) {
  // Synchronous next-intl translator — keeps MatchChip a non-async RSC (it
  // renders as a serialized list slot and has synchronous render tests).
  const t = useTranslations("jobads.ui.match");
  const modifier = GRADE_MODIFIER[grade];
  const label = t(`grade.${grade}`);
  return (
    <span className={`jp-matchchip ${modifier}`} data-tag="match">
      <span className="jp-matchchip__dot" aria-hidden="true" />
      {label}
    </span>
  );
}
