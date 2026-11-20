import { Q_MAX_LENGTH, Q_MIN_LENGTH, type SuggestionKind } from "@/lib/dto/job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { composeSuggestionChip } from "./chip-composition";
import {
  splitQWords,
  type ConceptLabelResolver,
} from "./chip-models";
import type { JobbUrlState } from "./search-params";
import {
  Q_MAX_EXCLUDED_TERMS,
  classifyWord,
  excludedQWord,
  excludedTermOf,
  isExcludedTermTooShort,
  positiveQ,
} from "./search-text-grammar";

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
 * Operatorer: ledande `+` strippas (Klas-spec). Ett ord som börjar med minus
 * är ett uteslutet ord (#2143, ADR 0167, `search-text-grammar.ts`): det blir
 * q-ordet `-term`, matchas aldrig mot taxonomin och bryter n-gram-runnen.
 *
 * Caret-segment-exkludering (CTO VAL 3): ordet som innehåller caret är
 * PÅGÅENDE och parsas inte — radering mitt i "Göteborg" släpper inte
 * ortfiltret per keystroke och fragmentet "götebor" committas aldrig
 * (ADR 0045-hygien: ingen replace-per-keystroke).
 */

/**
 * What a text word can claim. The wire's `SuggestionKind` plus the two Klass 2 axes (#2148):
 * the backend never suggests an omfattning or anställningsform, so they are not in that enum
 * and are kept here rather than widening a contract the server owns.
 */
export type ClaimKind = SuggestionKind | "EmploymentType" | "WorktimeExtent";

export interface LabelMatch {
  kind: ClaimKind;
  conceptId: string;
  label: string;
}

export interface LabelIndex {
  /** normaliserad lowercase-label (whitespace-kollapsad) → träffar. */
  byText: ReadonlyMap<string, LabelMatch[]>;
  /** Längsta labelns ordantal — bounder greedy-scanningen. */
  maxWords: number;
}

/**
 * `localeName` is the reader's name for a coded (Klass 2) concept. The source label, which is
 * Swedish, is always indexed; the locale name is indexed beside it so a label written back
 * into the field in another locale ("Full-time") parses to the same claim.
 */
export function buildLabelIndex(
  taxonomy: TaxonomyTree | null,
  localeName?: (conceptId: string, fallback: string) => string,
): LabelIndex {
  const byText = new Map<string, LabelMatch[]>();
  let maxWords = 1;
  const add = (kind: ClaimKind, conceptId: string, label: string) => {
    const key = normalizeLabel(label);
    const words = key.split(" ").length;
    if (words > maxWords) maxWords = words;
    const list = byText.get(key);
    if (list) {
      if (!list.some((m) => m.kind === kind && m.conceptId === conceptId))
        list.push({ kind, conceptId, label });
    } else byText.set(key, [{ kind, conceptId, label }]);
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
  // #2148 — omfattning and anställningsform become filter tags just like an ort: "heltid"
  // is Omfattning: Heltid, not a word that only appears in the ad text.
  for (const e of taxonomy?.employmentTypes ?? []) {
    add("EmploymentType", e.conceptId, e.label);
    if (localeName) add("EmploymentType", e.conceptId, localeName(e.conceptId, e.label));
  }
  for (const w of taxonomy?.worktimeExtents ?? []) {
    add("WorktimeExtent", w.conceptId, w.label);
    if (localeName) add("WorktimeExtent", w.conceptId, localeName(w.conceptId, w.label));
  }
  // #1546 — arbetsgivarnamn läggs MEDVETET inte in här, och det är inte ett glapp.
  // Indexet avgör vad som är text-representabelt; en post här skulle göra att
  // "Volvo" skrivet som fritext i hero-fältet gjorde anspåk på ?employer= som
  // användaren aldrig valde (I1-brott). Axeln sätts enbart genom ett explicit
  // förslags-val. Egenskapen följer av signaturen: buildLabelIndex tar bara en
  // TaxonomyTree, så en arbetsgivare kan inte nå indexet.
  return { byText, maxWords };
}

function normalizeLabel(label: string): string {
  return label.trim().replace(/\s+/g, " ").toLowerCase();
}

export interface ParsedClaims {
  /** Dimension-anspråk i textordning. */
  matches: LabelMatch[];
  /** Fritext-ord i textordning, ci-dedupade; uteslutna ord i formen `-term`. */
  qWords: string[];
}

export const EMPTY_CLAIMS: ParsedClaims = { matches: [], qWords: [] };

interface Token {
  /** Operator-strippad ordform; ett uteslutet ord i formen `-term`. */
  word: string;
  start: number;
  end: number;
  /** true = ny run börjar här (komma före, eller caret-grannskap). */
  boundaryBefore: boolean;
  excluded: boolean;
}

/** Ordet (token-spannet) som innehåller caret — null om caret står i
 * avgränsar-rymd. Exporterad för typeaheadens suggest-prefix. */
export function getTokenRange(
  text: string,
  caretIndex: number,
): { start: number; end: number } | null {
  const re = /[^\s,]+/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    const start = m.index;
    const end = start + m[0].length;
    if (caretIndex >= start && caretIndex <= end) return { start, end };
  }
  return null;
}

interface SpannedMatch extends LabelMatch {
  /** Character span of the matched words in the text. */
  start: number;
  end: number;
}

/**
 * Ren parse: text → anspråk per axel. `caretIndex != null` exkluderar ordet
 * under caret (pågående). Greedy longest-match per run (komma/caret bryter
 * runs — n-gram spänner aldrig över dem).
 *
 * Omfattning is a radio (0 or 1 value, search-params.ts): when the text holds several omfattning
 * words the LAST one claims it and the earlier ones claim nothing (#2148, CTO). They never become
 * q-words; `withoutDisplacedWorktimeWords` removes them from the text at commit.
 */
export function parseSearchText(
  text: string,
  index: LabelIndex,
  caretIndex: number | null,
): ParsedClaims {
  const scanned = scanSearchText(text, index, caretIndex);
  const lastWorktime = scanned.matches.findLastIndex((m) => m.kind === "WorktimeExtent");
  const matches = scanned.matches
    .filter((m, i) => m.kind !== "WorktimeExtent" || i === lastWorktime)
    .map(({ kind, conceptId, label }) => ({ kind, conceptId, label }));
  return { matches, qWords: scanned.qWords };
}

/**
 * The text without the omfattning words a later omfattning word has displaced, so a word that no
 * longer means anything does not stay in the field (the delta applies the replacement; this is the
 * text half). Unchanged text is returned as the same string.
 */
export function withoutDisplacedWorktimeWords(
  text: string,
  index: LabelIndex,
  caretIndex: number | null,
): string {
  const worktime = scanSearchText(text, index, caretIndex).matches.filter(
    (m) => m.kind === "WorktimeExtent",
  );
  let result = text;
  for (const m of worktime.slice(0, -1).reverse()) {
    const tail = result.slice(m.end).replace(/^[ ,]+/, "");
    result = result.slice(0, m.start) + tail;
  }
  return result;
}

function scanSearchText(
  text: string,
  index: LabelIndex,
  caretIndex: number | null,
): { matches: SpannedMatch[]; qWords: string[] } {
  const caretRange =
    caretIndex === null ? null : getTokenRange(text, caretIndex);

  // Tokenisera med run-gränser.
  const tokens: Token[] = [];
  const re = /[^\s,]+/g;
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
    const cls = classifyWord(m[0]);
    if (cls.kind === "operator") {
      // Bar operator-token ("+"/"-" ensamt) skippas men dess boundary får
      // inte sväljas — n-gram skulle annars kunna spänna över komma
      // (code-reviewer Minor 1 E2i).
      pendingBoundary = boundary;
      continue;
    }
    if (cls.kind === "excluded") {
      // A run of its own: "-stockholms län" must not claim the region, and the
      // word after it must not be glued to the word before it.
      tokens.push({
        word: excludedQWord(cls.term),
        start,
        end,
        boundaryBefore: true,
        excluded: true,
      });
      pendingBoundary = true;
      continue;
    }
    tokens.push({
      word: cls.word,
      start,
      end,
      boundaryBefore: boundary,
      excluded: false,
    });
  }

  const matches: SpannedMatch[] = [];
  const qWords: string[] = [];
  const seenQ = new Set<string>();

  const pushQ = (word: string) => {
    const k = word.toLowerCase();
    if (!seenQ.has(k)) {
      seenQ.add(k);
      qWords.push(word);
    }
  };

  let i = 0;
  while (i < tokens.length) {
    if (tokens[i]!.excluded) {
      pushQ(tokens[i]!.word);
      i += 1;
      continue;
    }

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
        matches.push({
          ...found[0]!,
          start: tokens[i]!.start,
          end: tokens[i + len - 1]!.end,
        });
        i += len;
        consumed = true;
        break;
      }
      // Ambiguöst n-gram → prova kortare (gissa aldrig).
    }
    if (!consumed) {
      pushQ(tokens[i]!.word);
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
  /** #2143 — uteslutna ord vägrade av Q_MAX_EXCLUDED_TERMS (står kvar i texten). */
  rejectedExcluded: string[];
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
  const rejectedExcluded: string[] = [];
  const tooShortExcluded: string[] = [];

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
    // #2148 — the two Klass 2 axes are not suggestion kinds: the word sets its axis directly.
    // Anställningsform is a checkbox group and appends; omfattning is a radio and replaces.
    if (m.kind === "EmploymentType" || m.kind === "WorktimeExtent") {
      const after: JobbUrlState =
        m.kind === "EmploymentType"
          ? state.employmentType.includes(m.conceptId)
            ? state
            : { ...state, employmentType: [...state.employmentType, m.conceptId] }
          : sameList(state.worktimeExtent, [m.conceptId])
            ? state
            : { ...state, worktimeExtent: [m.conceptId] };
      if (!sameUrlState(after, state)) addedLabels.push(m.label);
      state = after;
      continue;
    }
    const after = composeSuggestionChip(
      {
        kind: m.kind,
        conceptId: m.conceptId,
        label: m.label,
        // A parse-match is never an Employer (an employer name is not in the
        // label index), so the employer-only fields are absent by construction.
        organizationNumber: null,
        adCount: null,
        isProtectedIdentity: false,
      },
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
    // #2143 — the server drops an excluded term under Q_MIN_LENGTH and keeps at most
    // Q_MAX_EXCLUDED_TERMS; the field says so instead of sending a word that does nothing.
    const excludedTerm = excludedTermOf(w);
    if (excludedTerm !== null) {
      if (isExcludedTermTooShort(excludedTerm)) {
        tooShortExcluded.push(excludedTerm);
        continue;
      }
      const excludedCount = words.filter((x) => excludedTermOf(x) !== null).length;
      if (excludedCount >= Q_MAX_EXCLUDED_TERMS) {
        rejectedExcluded.push(w);
        continue;
      }
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
  // `appliedQ` tappar de droppade orden MED droppen: prevClaims får aldrig göra anspråk på något staten
  // saknar. Annars ser nästa delta ordet i prevQKeys, tar add-loopens "finns redan"-genväg,
  // och ordet återförs ALDRIG till q — tyst förlorat även när texten blivit lång nog
  // ("i" → "i java" hade gett q="java"). Vaktat av tokenize.test.ts.
  //
  // Droppen ligger före enforceClaims. (Den ordningen är korrekt men inte bärande:
  // enforceClaims itererar bara `claims.matches` och rör aldrig q-ord. En tidigare
  // kommentar påstod att ordningen var nödvändig för att ordet annars skulle läggas
  // tillbaka — det var fiktion. code-reviewer fångade det.)
  //
  // #2143 — the minimum is measured on the POSITIVE words only, as the server's parser does;
  // excluded words are kept and checked one by one above.
  const tooShortQ: string[] = [...tooShortExcluded];
  const assembledQ = positiveQ(state.q);
  if (assembledQ.length > 0 && assembledQ.length < Q_MIN_LENGTH) {
    const isPositive = (w: string) => classifyWord(w).kind === "positive";
    const words = splitQWords(state.q);
    tooShortQ.push(...words.filter(isPositive));
    state = { ...state, q: words.filter((w) => !isPositive(w)).join(" ") };
    const kept = appliedQ.filter((w) => !isPositive(w));
    appliedQ.length = 0;
    appliedQ.push(...kept);
    // Ordet APPLICERADES aldrig — då får det inte heller annonseras som tillagt. Utan
    // detta hörde skärmläsaren "Lade till i. Sökordet ”i” är kortare än 2 tecken och
    // används inte i sökningen." — första halvan osann (CLAUDE.md §10).
    for (const w of tooShortQ) {
      const at = addedLabels.indexOf(w);
      if (at >= 0) addedLabels.splice(at, 1);
    }
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
    rejectedExcluded,
    tooShortQ,
    // Vägrade q-ord (Q_MAX) och för korta q-ord (Q_MIN) ingår INTE — de står kvar i
    // texten och får nytt add-försök vid nästa commit-punkt (efter att utrymme frigjorts
    // respektive efter att texten blivit lång nog).
    appliedClaims: { matches: next.matches, qWords: appliedQ },
  };
}

/**
 * #2143 — the claims of `claims` that `state` actually carries, for the delta bookkeeping after an
 * external change. A q-word a guard refused (Q_MAX_LENGTH, Q_MAX_EXCLUDED_TERMS, the minimum) is still
 * in the text but not in q; left out here, the next commit point retries it — so freeing room with a
 * chip's × lets the refused word in, or brings its notice back. Matches are kept as parsed: no guard
 * refuses one, and re-adding one would re-run the per-län normalisation the user never asked for.
 */
export function claimsCarriedBy(
  claims: ParsedClaims,
  state: JobbUrlState,
): ParsedClaims {
  const carried = new Set(splitQWords(state.q).map((w) => w.toLowerCase()));
  return {
    matches: claims.matches,
    qWords: claims.qWords.filter((w) => carried.has(w.toLowerCase())),
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
    axis: "region" | "municipality" | "occupationGroup" | "employmentType",
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
      case "EmploymentType":
        ensure("employmentType", m.conceptId);
        break;
      case "WorktimeExtent":
        // A radio: the text's single omfattning claim is the value.
        if (!sameList(result.worktimeExtent, [m.conceptId]))
          result = { ...result, worktimeExtent: [m.conceptId] };
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
      case "Employer":
        // #1546 — ett arbetsgivarnamn läggs ALDRIG i label-indexet
        // (`buildLabelIndex`), så det kan inte uppstå som parse-match. Se
        // `removeMatch` nedan för samma resonemang på borttagningssidan.
        break;
      default:
        // #1546 — den här switchen SÅG UT som sin kompilator-tvingade
        // syskonfunktion `removeMatch` fyrtio rader ned, men var det inte: den
        // är ett statement i en loop, så en ny SuggestionKind föll rakt igenom
        // och tappades tyst. `assertNever` gör den lika fail-closed som
        // syskonet. Beviset att det landade: stryk `case "Employer"` ovan och
        // filen ska sluta kompilera.
        assertNever(m.kind);
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
    case "EmploymentType":
      return {
        ...state,
        employmentType: state.employmentType.filter((v) => v !== m.conceptId),
      };
    case "WorktimeExtent":
      return {
        ...state,
        worktimeExtent: state.worktimeExtent.filter((v) => v !== m.conceptId),
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
    case "Employer":
      // #1546 — samma skäl som Title, men det är värt att skriva ut VARFÖR det
      // är ett medvetet val och inte ett glapp: ett arbetsgivarnamn läggs
      // ALDRIG i `buildLabelIndex`. Gjorde det det, skulle "Volvo" skrivet som
      // fritext i hero-fältet göra anspråk på `?employer=` som användaren
      // aldrig valde — ett I1-brott. Axeln sätts bara genom ett explicit
      // förslags-val, och tas bort genom toolbarens egen chip.
      return state;
  }
}

/**
 * Representabilitets-gate (C′ regel 4): en label får emitteras som text
 * ENDAST om parse bevisligen återfinner exakt den dimensionen (ambiguösa
 * labels, komma-labels och ord vars parse ändrar formen faller — `+ord`,
 * `−ord`; ett uteslutet ord i kanonisk form `-ord` består). q-ord faller om
 * de råkar unikt matcha en taxonomi-label (skulle re-claimas som dimension).
 */
export function isTextRepresentable(
  label: string,
  expected: { kind: ClaimKind; conceptId: string } | null,
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
 * state → text (kanonisk ordning region → kommun → yrkesgrupp → anställningsform → omfattning → q-ord).
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
    axis:
      | "region"
      | "municipality"
      | "occupationGroup"
      | "employmentType"
      | "worktimeExtent",
    kind: ClaimKind,
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
  for (const id of state.employmentType) push("employmentType", "EmploymentType", id);
  for (const id of state.worktimeExtent) push("worktimeExtent", "WorktimeExtent", id);
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
    axis:
      | "region"
      | "municipality"
      | "occupationGroup"
      | "employmentType"
      | "worktimeExtent",
  ): void => {
    for (const id of prev[axis])
      if (!next[axis].includes(id)) removedTexts.push(resolveLabel(axis, id));
    for (const id of next[axis])
      if (!prev[axis].includes(id)) hasAdditions = true;
  };
  diffAxis("region");
  diffAxis("municipality");
  diffAxis("occupationGroup");
  diffAxis("employmentType");
  diffAxis("worktimeExtent");

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
    // #2199 — an occupation-name is picked in the Yrke popover and is not
    // text-representable; same reason as the Distans line below.
    sameList(a.occupationName, b.occupationName) &&
    sameList(a.region, b.region) &&
    sameList(a.municipality, b.municipality) &&
    // #2072 — Distans is set in the place popover and is not text-representable; without
    // this line an external Distans change is classed as a no-op and the next text commit
    // builds on a stale `remote`.
    a.remote === b.remote &&
    // Klass 2 (ADR 0067 B2) — panel-dimensionerna ingår i "samma filter-state".
    // Since #2148 the field can claim them (a word equal to a label).
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
    sameList(a.employer ?? [], b.employer ?? [])
  );
}

function sameList(
  a: ReadonlyArray<string>,
  b: ReadonlyArray<string>,
): boolean {
  return a.length === b.length && a.every((v, i) => v === b[i]);
}
