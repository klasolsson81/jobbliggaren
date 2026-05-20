import type { ReactNode } from "react";

/**
 * Konverterar rå Platsbanken-annonstext till hierarkisk markup för
 * JobAdDetail (Klas pre-F6 Prompt 2). Ren server-säker funktion (ingen
 * "use client") — anropas i RSC (JobAdDetail) och i vitest-tester.
 *
 * Parser-strategi (medvetet enkel, Klas-direktiv "enkla mönster"):
 *
 *  1. Dela texten på dubbla radbrytningar (`\n\s*\n`) till BLOCK.
 *  2. Per block:
 *     a) Om varje rad börjar med bullet-prefix (`- `, `* `, `• `) →
 *        rendera som `<ul>` med en `<li>` per rad.
 *     b) Om blocket är EN rad, kort (≤60 tecken), och inte slutar med
 *        skiljetecken (`.`, `!`, `?`, `,`, `:`) → rendera som `<h3>`
 *        (sektionsrubrik). Vanliga signalord: "Beskrivning", "Övrigt",
 *        "Om arbetsgivaren", "Kvalifikationer", "Kontakt".
 *     c) Annars: stycke (`<p>` med `white-space: pre-line` så mjuka
 *        radbrytningar inom blocket bevaras).
 *
 * Säkerhet: ingen HTML-injection. Texten passeras genom React som
 * children → automatisk escape. INGEN dangerouslySetInnerHTML.
 * INGA regex som försöker tolka HTML-markup (Klas-direktiv).
 *
 * Defensivt: hela funktionen är inom try/catch. Om parsern kastar
 * (orealistiskt med pure funktion på string, men säkerhetsbälte mot
 * framtida ändringar) returneras råtexten oförändrad inom en `<p>` med
 * pre-line så användaren ser åtminstone något läsbart.
 */

const HEADING_MAX_LENGTH = 60;
// Kolon (":") inkluderas EJ — "Your responsibilities:" / "Kvalifikationer:"
// är giltiga rubriker. Punkt/utropstecken/frågetecken/komma indikerar
// mening eller komma-list, inte rubrik.
const HEADING_TRAILING_TERMINATORS = /[.!?,]$/;
const BULLET_PREFIX = /^\s*[-•*]\s+/;

type Block =
  | { kind: "heading"; text: string }
  | { kind: "list"; items: string[] }
  | { kind: "paragraph"; text: string };

function isAllBullets(lines: readonly string[]): boolean {
  if (lines.length === 0) return false;
  return lines.every((l) => BULLET_PREFIX.test(l));
}

function stripBullet(line: string): string {
  return line.replace(BULLET_PREFIX, "").trim();
}

function looksLikeHeading(lines: readonly string[]): boolean {
  const first = lines[0];
  if (lines.length !== 1 || first === undefined) return false;
  const t = first.trim();
  if (t.length === 0 || t.length > HEADING_MAX_LENGTH) return false;
  if (HEADING_TRAILING_TERMINATORS.test(t)) return false;
  return true;
}

export function parseAdDescription(raw: string): Block[] {
  const blocks: Block[] = [];
  // Normalisera radslut (CRLF/CR → LF), split på en eller flera helt
  // blanka rader. Whitespace-rader (bara mellanslag/tab) räknas som tomma.
  const normalized = raw.replace(/\r\n?/g, "\n");
  const rawBlocks = normalized.split(/\n[ \t]*\n+/);

  for (const block of rawBlocks) {
    const lines = block
      .split("\n")
      .map((l) => l.trimEnd())
      .filter((l) => l.length > 0);

    if (lines.length === 0) continue;

    if (isAllBullets(lines)) {
      const items = lines.map(stripBullet).filter((s) => s.length > 0);
      if (items.length > 0) blocks.push({ kind: "list", items });
      continue;
    }

    if (looksLikeHeading(lines)) {
      // looksLikeHeading-guard säkerställer att lines[0] finns och är
      // narrow:bar till string — TS strict tål inte indexering utan check.
      const first = lines[0] as string;
      blocks.push({ kind: "heading", text: first.trim() });
      continue;
    }

    blocks.push({ kind: "paragraph", text: lines.join("\n") });
  }

  return blocks;
}

export function formatAdDescription(raw: string): ReactNode {
  try {
    const blocks = parseAdDescription(raw);
    if (blocks.length === 0) {
      // Tom-input — visa ingenting (caller kan välja egen empty-state).
      return null;
    }
    return (
      <>
        {blocks.map((block, idx) => {
          if (block.kind === "heading") {
            return (
              <h3 key={idx} className="jp-ad-desc__h3">
                {block.text}
              </h3>
            );
          }
          if (block.kind === "list") {
            return (
              <ul key={idx} className="jp-ad-desc__list">
                {block.items.map((item, j) => (
                  <li key={j}>{item}</li>
                ))}
              </ul>
            );
          }
          return (
            <p key={idx} className="jp-ad-desc__p">
              {block.text}
            </p>
          );
        })}
      </>
    );
  } catch {
    // Defensivt fallback (Klas-direktiv: visa råtexten oförändrad om
    // parsern kastar). pre-line bevarar radbrytningar.
    return <p className="jp-ad-desc__p">{raw}</p>;
  }
}
