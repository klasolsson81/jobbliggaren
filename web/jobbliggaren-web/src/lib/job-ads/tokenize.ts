import { Q_MAX_LENGTH, Q_MIN_LENGTH, type SuggestionKind } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { composeSuggestionChip } from "./chip-composition";
import {
  splitQWords,
  type ConceptLabelResolver,
} from "./chip-models";
import type { JobbUrlState } from "./search-params";

/**
 * Spegel-sökfältets parse/serialize-kärna (Fas E2i, CTO VAL 1 = Variant C′ —
 * docs/reviews/2026-06-11-sok-paritet-e2i-cto.md). Rena funktioner, DOM-fria
 * (CLAUDE.md §2.4).
 *
 * Modell: fältets text är ANVÄNDARENS buffert (källa för sitt eget bidrag);
 * URL:en är persistent sanning. Invariant **I1: parse(text) ⊆ state** —
 * delmängd, inte likhet (state får innehålla MER: popover-valda dimensioner,
 * icke-representabla labels). Bron är envägs delta-parse (text → state) +
 * representabilitets-gated serialize (state → text) ENDAST vid extern
 * divergens.
 *
 * Tokenisering: mellanslag/komma avgränsar. **Greedy longest-match** (CTO
 * VAL 2): längsta UNIKA n-gram mot taxonomi-labels vinner ("Stockholms län",
 * "Upplands Väsby" matchar handskrivet); ambiguöst n-gram → kortare;
 * ambiguitet på längd 1 → fritext (gissa aldrig, D2-principen). n-gram
 * spänner ALDRIG över komma eller caret-segmentet.
 *
 * Operator-strip: ledande `+` (Klas-spec) och `-` (NOT-neutralisering —
 * `websearch_to_tsquery` tolkar ledande `-` som NOT redan idag; minus-
 * operatorn är Klas-pending egen fas) strippas per ord.
 *
 * Caret-segment-exkludering (CTO VAL 3): ordet som innehåller caret är
 * PÅGÅENDE och parsas inte — radering mitt i "Göteborg" släpper inte
 * ortfiltret per keystroke och fragmentet "götebor" committas aldrig
 * (ADR 0045-hygien: ingen replace-per-keystroke).
 */

export interface LabelMatch {
  kind: SuggestionKind;
  conceptId: string;
  label: string;
}

export interface LabelIndex {
  /** normaliserad lowercase-label (whitespace-kollapsad) → träffar. */
  byText: ReadonlyMap<string, LabelMatch[]>;
  /** Längsta labelns ordantal — bounder greedy-scanningen. */
  maxWords: number;
}

export function buildLabelIndex(taxonomy: TaxonomyTree | null): LabelIndex {
  const byText = new Map<string, LabelMatch[]>();
  let maxWords = 1;
  const add = (kind: SuggestionKind, conceptId: string, label: string) => {
    const key = normalizeLabel(label);
    const words = key.split(" ").length;
    if (words > maxWords) maxWords = words;
    const list = byText.get(key);
    if (list) list.push({ kind, conceptId, label });
    else byText.set(key, [{ kind, conceptId, label }]);
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
  return { byText, maxWords };
}

function normalizeLabel(label: string): string {
  return label.trim().replace(/\s+/g, " ").toLowerCase();
}

export interface ParsedClaims {
  /** Dimension-anspråk i textordning. */
  matches: LabelMatch[];
  /** Fritext-ord (operator-strippade), textordning, ci-dedupade. */
  qWords: string[];
}

export const EMPTY_CLAIMS: ParsedClaims = { matches: [], qWords: [] };

interface Token {
  /** Operator-strippad ordform. */
  word: string;
  start: number;
  end: number;
  /** true = ny run börjar här (komma före, eller caret-grannskap). */
  boundaryBefore: boolean;
}

/** Ordet (token-spannet) som innehåller caret — null om caret står i
 * avgränsar-rymd. Exporterad för typeaheadens suggest-prefix. */
export function getTokenRange(
  text: string,
  caretIndex: number,
): { start: number; end: number } | null {
  const re = /[^ ,]+/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const start = m.index;
    const end = start + m[0].length;
    if (caretIndex >= start && caretIndex <= end) return { start, end };
  }
  return null;
}

/**
 * Ren parse: text → anspråk per axel. `caretIndex != null` exkluderar ordet
 * under caret (pågående). Greedy longest-match per run (komma/caret bryter
 * runs — n-gram spänner aldrig över dem).
 */
export function parseSearchText(
  text: string,
  index: LabelIndex,
  caretIndex: number | null,
): ParsedClaims {
  const caretRange =
    caretIndex === null ? null : getTokenRange(text, caretIndex);

  // Tokenisera med run-gränser.
  const tokens: Token[] = [];
  const re = /[^ ,]+/g;
  let m: RegExpExecArray | null;
  let prevEnd = 0;
  let pendingBoundary = false;
  while ((m = re.exec(text)) !== null) {
    const start = m.index;
    const end = start + m[0].length;
    const sep = text.slice(prevEnd, start);
    const boundary: boolean = pendingBoundary || sep.includes(",");
    pendingBoundary = false;
    prevEnd = end;
    if (caretRange && start === caretRange.start) {
      // Caret-ordet exkluderas helt och bryter runnen på båda sidor.
      pendingBoundary = true;
      continue;
    }
    const word = m[0].replace(/^[+-]+/, "");
    if (word.length === 0) {
      // Bar operator-token ("+"/"-" ensamt) skippas men dess boundary får
      // inte sväljas — n-gram skulle annars kunna spänna över komma
      // (code-reviewer Minor 1 E2i).
      pendingBoundary = boundary;
      continue;
    }
    tokens.push({ word, start, end, boundaryBefore: boundary });
  }

  const matches: LabelMatch[] = [];
  const qWords: string[] = [];
  const seenQ = new Set<string>();

  let i = 0;
  while (i < tokens.length) {
    // Run-längd från i (bryts av nästa boundaryBefore).
    let runEnd = i + 1;
    while (runEnd < tokens.length && !tokens[runEnd]!.boundaryBefore)
      runEnd++;

    let consumed = false;
    const maxLen = Math.min(index.maxWords, runEnd - i);
    for (let len = maxLen; len >= 1; len--) {
      const key = tokens
        .slice(i, i + len)
        .map((t) => t.word)
        .join(" ")
        .toLowerCase();
      const found = index.byText.get(key);
      if (found && found.length === 1) {
        matches.push(found[0]!);
        i += len;
        consumed = true;
        break;
      }
      // Ambiguöst n-gram → prova kortare (gissa aldrig).
    }
    if (!consumed) {
      const word = tokens[i]!.word;
      const k = word.toLowerCase();
      if (!seenQ.has(k)) {
        seenQ.add(k);
        qWords.push(word);
      }
      i += 1;
    }
  }

  return { matches, qWords };
}

export interface ClaimsDeltaResult {
  next: JobbUrlState;
  addedLabels: string[];
  removedLabels: string[];
  /** q-ord vägrade av Q_MAX_LENGTH-guarden (står kvar i texten). */
  rejectedQ: string[];
  /**
   * #823 — q-ord som föll bort för att den SAMMANSATTA söktexten hamnade under
   * Q_MIN_LENGTH (står kvar i texten, precis som `rejectedQ`). Speglar backendens
   * `SearchQueryParser`: en residual under minimum NOLLAS, frågan körs vidare på
   * sina dimensioner — den vägras inte.
   */
  tooShortQ: string[];
  /** Anspråk som faktiskt applicerades (nästa prevClaims). */
  appliedClaims: ParsedClaims;
}

const matchKey = (m: LabelMatch) => `${m.kind}:${m.conceptId}`;

/**
 * Delta-sync (C′ regel 1): applicera SKILLNADEN mellan föregående och nya
 * anspråk på staten — texten är auktoritativ för sitt eget bidrag, aldrig
 * för hela staten (popover-valda dimensioner som texten inte gör anspråk
 * på rörs inte). Adds går genom `composeSuggestionChip` (SPOT — samma
 * kind→dimension-väg som förslags-valet, inkl. OccupationField-
 * materialisering + per-län-normalisering); removes via axel-filter.
 */
export function applyClaimsDelta(
  current: JobbUrlState,
  prev: ParsedClaims,
  next: ParsedClaims,
  taxonomy: TaxonomyTree | null,
): ClaimsDeltaResult {
  let state = current;
  const addedLabels: string[] = [];
  const removedLabels: string[] = [];
  const rejectedQ: string[] = [];

  const prevKeys = new Set(prev.matches.map(matchKey));
  const nextKeys = new Set(next.matches.map(matchKey));

  // Removes först (frigör bl.a. q-utrymme innan adds guard:as).
  for (const m of prev.matches) {
    if (nextKeys.has(matchKey(m))) continue;
    state = removeMatch(state, m, taxonomy);
    removedLabels.push(m.label);
  }
  const nextQKeys = new Set(next.qWords.map((w) => w.toLowerCase()));
  for (const w of prev.qWords) {
    if (nextQKeys.has(w.toLowerCase())) continue;
    const words = splitQWords(state.q).filter(
      (x) => x.toLowerCase() !== w.toLowerCase(),
    );
    state = { ...state, q: words.join(" ") };
    removedLabels.push(w);
  }

  // Adds.
  for (const m of next.matches) {
    if (prevKeys.has(matchKey(m))) continue;
    const after = composeSuggestionChip(
      { kind: m.kind, conceptId: m.conceptId, label: m.label },
      state,
      taxonomy,
    );
    if (!sameUrlState(after, state)) addedLabels.push(m.label);
    state = after;
  }
  const appliedQ: string[] = [];
  const prevQKeys = new Set(prev.qWords.map((w) => w.toLowerCase()));
  for (const w of next.qWords) {
    if (prevQKeys.has(w.toLowerCase())) {
      appliedQ.push(w);
      continue;
    }
    const words = splitQWords(state.q);
    if (words.some((x) => x.toLowerCase() === w.toLowerCase())) {
      appliedQ.push(w);
      continue;
    }
    const nextQ = [...words, w].join(" ");
    if (nextQ.length > Q_MAX_LENGTH) {
      rejectedQ.push(w);
      continue;
    }
    state = { ...state, q: nextQ };
    addedLabels.push(w);
    appliedQ.push(w);
  }

  // #823 — min-längdsregeln, spegelvänd mot Q_MAX_LENGTH-avvisningen ovan. Backend
  // avvisar en söktext under 2 tecken (ListJobAdsQueryValidator), medan dess EGEN parser
  // (SearchQueryParser) nollar en residual under minimum och kör vidare på dimensionerna.
  // Klienten gör detsamma: det för korta ordet står kvar i TEXTEN men appliceras inte på
  // staten — så "i göteborg" ger Göteborg-chippen (residualen "i" droppas), i stället för
  // att hela söket dör. Att i stället vägra committa hade skapat en återvändsgränd.
  //
  // Regeln gäller den SAMMANSATTA q-strängen, aldrig per ord: backend kräver minst 2
  // tecken på hela Q, inte på varje ord. "a bc" (4 tecken) är fullt giltigt och MÅSTE
  // släppas igenom — en per-ord-regel hade tyst strypt en term servern accepterar, dvs.
  // klienten hade uppfunnit en strängare regel än den som faktiskt gäller. (En sammansatt
  // q under 2 tecken kan bara vara ETT enteckensord: två ord med mellanslag är redan ≥ 3.)
  //
  // Ordningen är bärande: droppen måste ske FÖRE enforceClaims, annars läggs ordet
  // tillbaka av I1-enforcementen. `appliedQ` nollas med — prevClaims får aldrig göra
  // anspråk på något staten saknar (annars kan en borttagning aldrig härledas igen).
  const tooShortQ: string[] = [];
  const assembledQ = state.q.trim();
  if (assembledQ.length > 0 && assembledQ.length < Q_MIN_LENGTH) {
    tooShortQ.push(...splitQWords(state.q));
    state = { ...state, q: "" };
    appliedQ.length = 0;
  }

  // I1-enforcement (CTO-addendum 2026-06-12 BESLUT 2): texten är användarens
  // explicita anspråk — varje next-claim SKA finnas i staten även om compose-
  // vägens per-län-normalisering (dokumenterad kosmetik, E2b) eller
  // OccupationField-removal släppt den. Re-adds sker RÅTT (utan
  // normalisering — annars flip-flop) och annonseras inte.
  state = enforceClaims(state, { matches: next.matches, qWords: appliedQ }, taxonomy);

  return {
    next: state,
    addedLabels,
    removedLabels,
    rejectedQ,
    tooShortQ,
    // Vägrade q-ord (Q_MAX) och för korta q-ord (Q_MIN) ingår INTE — de står kvar i
    // texten och får nytt add-försök vid nästa commit-punkt (efter att utrymme frigjorts
    // respektive efter att texten blivit lång nog).
    appliedClaims: { matches: next.matches, qWords: appliedQ },
  };
}

/**
 * Säkerställer att varje anspråk i `claims` finns i staten (I1: parse(text)
 * ⊆ state). Rå append per axel — ingen normalisering. Exporterad så även
 * förslags-valets compose-väg kan enforce:a (CTO BESLUT 2-synergin).
 */
export function enforceClaims(
  state: JobbUrlState,
  claims: ParsedClaims,
  taxonomy: TaxonomyTree | null,
): JobbUrlState {
  let result = state;
  const ensure = (
    axis: "region" | "municipality" | "occupationGroup",
    id: string,
  ) => {
    if (!result[axis].includes(id))
      result = { ...result, [axis]: [...result[axis], id] };
  };
  for (const m of claims.matches) {
    switch (m.kind) {
      case "Region":
        ensure("region", m.conceptId);
        break;
      case "Municipality":
        ensure("municipality", m.conceptId);
        break;
      case "OccupationGroup":
        ensure("occupationGroup", m.conceptId);
        break;
      case "OccupationField": {
        const field = taxonomy?.occupationFields.find(
          (f) => f.conceptId === m.conceptId,
        );
        for (const g of field?.occupationGroups ?? [])
          ensure("occupationGroup", g.conceptId);
        break;
      }
      case "Title":
        break; // förekommer aldrig som parse-match.
    }
  }
  return result;
}

function removeMatch(
  state: JobbUrlState,
  m: LabelMatch,
  taxonomy: TaxonomyTree | null,
): JobbUrlState {
  switch (m.kind) {
    case "Region":
      return {
        ...state,
        region: state.region.filter((v) => v !== m.conceptId),
      };
    case "Municipality":
      return {
        ...state,
        municipality: state.municipality.filter((v) => v !== m.conceptId),
      };
    case "OccupationGroup":
      return {
        ...state,
        occupationGroup: state.occupationGroup.filter(
          (v) => v !== m.conceptId,
        ),
      };
    case "OccupationField": {
      // Fält-anspråket materialiserades till barn-grupper vid add (VAL 2a)
      // — borttagning speglar: släpp alla barn.
      const field = taxonomy?.occupationFields.find(
        (f) => f.conceptId === m.conceptId,
      );
      const own = new Set(
        field?.occupationGroups.map((g) => g.conceptId) ?? [],
      );
      if (own.size === 0) return state;
      return {
        ...state,
        occupationGroup: state.occupationGroup.filter((v) => !own.has(v)),
      };
    }
    case "Title":
      return state; // Title förekommer aldrig som parse-match.
  }
}

/**
 * Representabilitets-gate (C′ regel 4): en label får emitteras som text
 * ENDAST om parse bevisligen återfinner exakt den dimensionen (ambiguösa
 * labels, komma-labels och operator-prefixade ord faller). q-ord faller om
 * de råkar unikt matcha en taxonomi-label (skulle re-claimas som dimension).
 */
export function isTextRepresentable(
  label: string,
  expected: { kind: SuggestionKind; conceptId: string } | null,
  index: LabelIndex,
): boolean {
  if (label.includes(",")) return false;
  const parsed = parseSearchText(label, index, null);
  if (expected === null) {
    // q-ord: måste parse:as som EXAKT ett fritext-ord.
    return (
      parsed.matches.length === 0 &&
      parsed.qWords.length === 1 &&
      parsed.qWords[0]!.toLowerCase() === label.trim().toLowerCase()
    );
  }
  return (
    parsed.qWords.length === 0 &&
    parsed.matches.length === 1 &&
    parsed.matches[0]!.kind === expected.kind &&
    parsed.matches[0]!.conceptId === expected.conceptId
  );
}

/**
 * state → text (kanonisk ordning region → kommun → yrkesgrupp → q-ord).
 * Körs ENDAST vid extern divergens (C′ regel 3) — aldrig under användarens
 * egen skrivning (texten skulle re-ordnas under caret). Best-effort-spegel:
 * icke-representabla dimensioner utelämnas och lever enbart i filter-raden
 * (som är total spegel). Holistisk rundtripps-verifiering: vid cross-
 * boundary-capture (två grann-labels bildar en tredje) → komma-separering;
 * kvarstår miss → offret utelämnas.
 */
export function serializeSearchText(
  state: JobbUrlState,
  resolveLabel: ConceptLabelResolver,
  index: LabelIndex,
): string {
  interface Item {
    text: string;
    key: string; // matchKey eller q:<ord>
  }
  const items: Item[] = [];
  const push = (
    axis: "region" | "municipality" | "occupationGroup",
    kind: SuggestionKind,
    id: string,
  ) => {
    const label = resolveLabel(axis, id);
    if (isTextRepresentable(label, { kind, conceptId: id }, index))
      items.push({ text: label, key: `${kind}:${id}` });
  };
  for (const id of state.region) push("region", "Region", id);
  for (const id of state.municipality) push("municipality", "Municipality", id);
  for (const id of state.occupationGroup)
    push("occupationGroup", "OccupationGroup", id);
  for (const w of splitQWords(state.q)) {
    if (isTextRepresentable(w, null, index))
      items.push({ text: w, key: `q:${w.toLowerCase()}` });
  }

  const verify = (text: string): boolean => {
    const parsed = parseSearchText(text, index, null);
    const keys = new Set([
      ...parsed.matches.map(matchKey),
      ...parsed.qWords.map((w) => `q:${w.toLowerCase()}`),
    ]);
    return items.every((it) => keys.has(it.key)) && keys.size === items.length;
  };

  const spaceJoined = items.map((it) => it.text).join(" ");
  if (verify(spaceJoined)) return spaceJoined;
  const commaJoined = items.map((it) => it.text).join(", ");
  if (verify(commaJoined)) return commaJoined;
  // Sista utväg: släpp items tills rundtrippen håller (sällsynt — property-
  // testet vaktar de vanliga klasserna).
  while (items.length > 0) {
    items.pop();
    const t = items.map((it) => it.text).join(", ");
    if (verify(t)) return t;
  }
  return "";
}

/**
 * Text-uppdatering vid EXTERN state-ändring (C′ regel 2+3): ren borttagning
 * (toolbar-× / Rensa) → kirurgisk text-edit som bevarar användarens ord-
 * ordning; allt annat (tillägg/navigering) → full kanonisk serialize.
 */
export function updateTextForStateChange(
  text: string,
  prev: JobbUrlState,
  next: JobbUrlState,
  resolveLabel: ConceptLabelResolver,
  index: LabelIndex,
): string {
  const removedTexts: string[] = [];
  let hasAdditions = false;

  const diffAxis = (
    axis: "region" | "municipality" | "occupationGroup",
  ): void => {
    for (const id of prev[axis])
      if (!next[axis].includes(id)) removedTexts.push(resolveLabel(axis, id));
    for (const id of next[axis])
      if (!prev[axis].includes(id)) hasAdditions = true;
  };
  diffAxis("region");
  diffAxis("municipality");
  diffAxis("occupationGroup");

  const prevQ = splitQWords(prev.q);
  const nextQKeys = new Set(splitQWords(next.q).map((w) => w.toLowerCase()));
  const prevQKeys = new Set(prevQ.map((w) => w.toLowerCase()));
  for (const w of prevQ) if (!nextQKeys.has(w.toLowerCase())) removedTexts.push(w);
  for (const w of splitQWords(next.q))
    if (!prevQKeys.has(w.toLowerCase())) hasAdditions = true;

  if (!hasAdditions && removedTexts.length > 0) {
    let result = text;
    let allFound = true;
    for (const t of removedTexts) {
      const re = new RegExp(
        `(^|[ ,])${escapeRegExp(t)}(?=$|[ ,])`,
        "i",
      );
      if (re.test(result)) {
        result = result.replace(re, "$1");
      } else {
        allFound = false;
        break;
      }
    }
    if (allFound)
      return result.replace(/[ ,]{2,}/g, " ").replace(/^[ ,]+|[ ,]+$/g, "");
  }
  return serializeSearchText(next, resolveLabel, index);
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// composeSuggestionChip kopierar listorna även vid no-op (addUnique) —
// referens-jämförelse räcker inte; jämför innehållet.
export function sameUrlState(a: JobbUrlState, b: JobbUrlState): boolean {
  return (
    a.q === b.q &&
    sameList(a.occupationGroup, b.occupationGroup) &&
    sameList(a.region, b.region) &&
    sameList(a.municipality, b.municipality) &&
    // Klass 2 (ADR 0067 B2) — panel-dimensionerna ingår i "samma filter-state".
    // Inte text-representabla i hero-fältet, men kanoniska komparatorn ska
    // vara komplett så framtida skip-guards inte ger false-negative-drop.
    sameList(a.employmentType, b.employmentType) &&
    sameList(a.worktimeExtent, b.worktimeExtent) &&
    // STEG 5 (grade-filter) — matchningsgrad-filtret ingår i "samma filter-
    // state" (inte text-representabelt i fältet, men komparatorn ska vara
    // komplett så hero-sökets extern-divergens-detektor inte tappar ett aktivt
    // grad-filter när användaren ändrar sökordet).
    sameList(a.matchGrades, b.matchGrades) &&
    // #454 PR-0 — arbetsgivar-filtret ingår i "samma filter-state" (inte
    // text-representabelt; komparatorn ska vara komplett så en extern
    // employer-ändring klassas som extern divergens, inte som no-op).
    (a.employer ?? "") === (b.employer ?? "")
  );
}

function sameList(
  a: ReadonlyArray<string>,
  b: ReadonlyArray<string>,
): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}
