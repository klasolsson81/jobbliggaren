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
 * - Färg (#290): alla fyra grader är SOLIDA fyllningar på SAMMA `--jp-leaf-*`-
 *   ramp som trappar ner i vikt Topp -> Grund (`--top` leaf-700, `--high`
 *   leaf-600, `--mid` leaf-100, `--low` leaf-50). Hierarki via fyllvikt på EN
 *   grön hue, aldrig en andra färg; aldrig röd (röd = danger). De två ljusare
 *   stegen vänder texten till mörk `--jp-leaf-900` (vit text faller på ljus
 *   fyllning). Tokens flippar själva i dark (bright fyll bär canvas-ink, deep
 *   fyll bär ljus leaf-ink) — varje par >=4.5:1 i båda teman (CSS i globals.css).
 * - a11y (WCAG 1.4.1 / 4.1.2): den synliga label-texten ÄR det tillgängliga
 *   namnet → färg bär aldrig betydelse ensam. Pricken är dekorativ
 *   (`aria-hidden`) — den upprepar bara graden som texten redan uttrycker.
 */

// Grad → modifier (design-reviewer 2026-06-20, F4-16); etiketten resolveras via
// next-intl (`ui.match.grade.*`). Inga utropstecken, ingen emoji, ingen
// versalisering (§10). Ladder: Relaterat yrke → Grundmatch → Bra match →
// Stark match → Toppmatch. KEEP these modifier names stable — #290 ändrade BARA
// CSS:en bakom de fyra gröna stegen (alla blev solida fyllningar på
// `--jp-leaf-*`-rampen, Topp -> Grund i avtagande vikt; hierarki via fyllvikt på
// en grön hue, ej ny token/hue).
//
// #300 PR-5 (design-reviewer bind, ADR 0084): `Related` är INTE ett femte grönt
// blad-steg. En femte fyllning mellan Basic (leaf-50) och Good (leaf-100) hade
// krävt en NY leaf-hue (§5-Blocker). I stället bär den den etablerade
// status-neutral-chip-behandlingen (surface-2 fill, ink-2 text, border-strong) —
// den läser som "annat yrke", inte "svagare grönt". Pricken följer currentColor
// (ink-2). Färg bär aldrig betydelse ensam: den synliga labeln ÄR namnet.
const GRADE_MODIFIER: Record<MatchGrade, string> = {
  Top: "jp-matchchip--top",
  Strong: "jp-matchchip--high",
  Good: "jp-matchchip--mid",
  Basic: "jp-matchchip--low",
  Related: "jp-matchchip--related",
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
