import { Q_MAX_LENGTH, type SuggestionKind } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { composeSuggestionChip } from "./chip-composition";
import { splitQWords } from "./chip-models";
import type { JobbUrlState } from "./search-params";

/**
 * Sökfälts-tokenizer (Fas E2h, Klas-spec 2026-06-11). Ren funktion —
 * testbar utan DOM (CLAUDE.md §2.4).
 *
 * Mellanslag/komma avslutar ett ord. Färdigt ord:
 * - **exakt UNIK case-insensitiv match** mot taxonomi-label (Län/Kommun/
 *   Yrkesområde/Yrkesgrupp) → dimension-chip via `composeSuggestionChip`
 *   (SPOT — tokenizern syntetiserar en SuggestionDto och går SAMMA väg som
 *   förslags-valet; ingen andra kind→dimension-mappning).
 * - **ambiguitet** (samma label på flera noder) → fritext (gissa aldrig —
 *   D2 Variant A+A-principen; disambiguering sker via förslags-valet).
 * - **ingen match** → fritext-ord i q (residual-FTS, recall-bevarande).
 *
 * Multi-ord-labels ("Stockholms län", "Upplands Väsby") nås ENDAST via
 * förslags-val (architect F2-dom): mellanslaget splittrar innan labeln är
 * komplett → orden blir fritext → q-FTS — recall-bevarande fallback, inget
 * tyst fel.
 *
 * Operator-strip: ledande `+` strippas (Klas-spec). Ledande `-` strippas
 * OCKSÅ (E2h-KRAV, CTO-bekräftad architect-upptäckt): `websearch_to_tsquery`
 * tolkar ledande `-` som NOT redan idag — utan strip skulle E2h shippa en
 * oavsiktlig negations-feature i en Klas-pending produktfråga (minus-
 * operatorn = egen fas, ADR 0062-notat-kandidat vid Klas-GO).
 *
 * q-max-guard: fritext-ord som tar joinad q över `Q_MAX_LENGTH` vägras
 * (hamnar i remainder + flaggas) — backend-VALIDATORN skulle annars avvisa
 * hela list-queryn (400) mitt i skrivflödet. Ord < 2 tecken chippas ÄNDÅ
 * (spec: allt blir taggar); en ensam 1-teckens-q nullas av backend-parsern
 * (recall-bevarande no-op, självläkande — parsern är SPOT för q-hygien,
 * FE duplicerar inte dess regler).
 */

export interface LabelMatch {
  kind: SuggestionKind;
  conceptId: string;
  label: string;
}

/**
 * Förberäknat index lowercase-label → taxonomi-träffar. Byggs en gång per
 * taxonomy-referens (memo i konsumenten), inte per tangenttryck.
 */
export function buildLabelIndex(
  taxonomy: TaxonomyTree | null,
): ReadonlyMap<string, LabelMatch[]> {
  const index = new Map<string, LabelMatch[]>();
  const add = (kind: SuggestionKind, conceptId: string, label: string) => {
    const key = label.toLowerCase();
    const list = index.get(key);
    if (list) list.push({ kind, conceptId, label });
    else index.set(key, [{ kind, conceptId, label }]);
  };
  for (const r of taxonomy?.regions ?? []) {
    add("Region", r.conceptId, r.label);
    for (const m of r.municipalities)
      add("Municipality", m.conceptId, m.label);
  }
  for (const f of taxonomy?.occupationFields ?? []) {
    add("OccupationField", f.conceptId, f.label);
    for (const g of f.occupationGroups)
      add("OccupationGroup", g.conceptId, g.label);
  }
  return index;
}

export interface TokenizeOutcome {
  next: JobbUrlState;
  /** Ofärdigt sista ord (+ ev. q-max-vägrade ord) — stannar i fältet. */
  remainder: string;
  /** Ord som vägrades pga q-max-guarden (visas som hjälptext). */
  rejected: string[];
  /** Labels för det som faktiskt lades till (aria-live-annonser). */
  addedLabels: string[];
  changed: boolean;
}

export function tokenizeDraft(
  draft: string,
  current: JobbUrlState,
  taxonomy: TaxonomyTree | null,
  labelIndex: ReadonlyMap<string, LabelMatch[]>,
  opts: { finalizeAll: boolean },
): TokenizeOutcome {
  const endsWithDelimiter = /[ ,]$/.test(draft);
  const segments = draft.split(/[ ,]+/);
  // Sista segmentet är ett PÅGÅENDE ord om draften inte slutar med
  // avgränsare och vi inte finaliserar allt (Enter/Sök).
  let remainderSeed = "";
  if (!opts.finalizeAll && !endsWithDelimiter)
    remainderSeed = segments.pop() ?? "";
  const tokens = segments.filter((s) => s.length > 0);

  let state = current;
  let changed = false;
  const rejected: string[] = [];
  const addedLabels: string[] = [];

  for (const raw of tokens) {
    // Strippa ALLA ledande +/- (operator-neutralisering, se modulkommentar).
    const word = raw.replace(/^[+-]+/, "");
    if (word.length === 0) continue;

    const matches = labelIndex.get(word.toLowerCase()) ?? [];
    if (matches.length === 1) {
      const m = matches[0]!;
      const next = composeSuggestionChip(
        { kind: m.kind, conceptId: m.conceptId, label: m.label },
        state,
        taxonomy,
      );
      if (!sameState(next, state)) {
        changed = true;
        addedLabels.push(m.label);
      }
      state = next;
      continue;
    }

    // Fritext-ord → q. Dedupe case-insensitivt (idempotent — FTS lexem-
    // dedupe:ar ändå; FE-dedupen är UX, inte korrekthet).
    const words = splitQWords(state.q);
    if (words.some((w) => w.toLowerCase() === word.toLowerCase())) continue;
    const nextQ = [...words, word].join(" ");
    if (nextQ.length > Q_MAX_LENGTH) {
      rejected.push(word);
      continue;
    }
    state = { ...state, q: nextQ };
    changed = true;
    addedLabels.push(word);
  }

  return {
    next: state,
    remainder: [...rejected, remainderSeed].filter((s) => s.length > 0).join(" "),
    rejected,
    addedLabels,
    changed,
  };
}

// composeSuggestionChip kopierar listorna även vid no-op (addUnique) —
// referens-jämförelse räcker inte; jämför innehållet. Exporterad så även
// förslags-valets commit-väg kan no-op-guarda (code-reviewer Minor 2 E2h).
export function sameUrlState(a: JobbUrlState, b: JobbUrlState): boolean {
  return sameState(a, b);
}

function sameState(a: JobbUrlState, b: JobbUrlState): boolean {
  return (
    a.q === b.q &&
    sameList(a.occupationGroup, b.occupationGroup) &&
    sameList(a.region, b.region) &&
    sameList(a.municipality, b.municipality)
  );
}

function sameList(
  a: ReadonlyArray<string>,
  b: ReadonlyArray<string>,
): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}
