"use client";

import {
  useId,
  useMemo,
  useState,
  useSyncExternalStore,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";
import {
  Q_MAX_LENGTH,
  Q_MIN_LENGTH,
  type JobAdSortBy,
  type SuggestionDto,
} from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  withCommitFlag,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import { composeSuggestionChip } from "@/lib/job-ads/chip-composition";
import { buildTaxonomyLabelResolver } from "@/lib/job-ads/chip-models";
import {
  applyClaimsDelta,
  buildLabelIndex,
  EMPTY_CLAIMS,
  enforceClaims,
  getTokenRange,
  isTextRepresentable,
  parseSearchText,
  sameUrlState,
  serializeSearchText,
  updateTextForStateChange,
  type ParsedClaims,
} from "@/lib/job-ads/tokenize";
import { JobAdTypeahead } from "./job-ad-typeahead";

/**
 * Hero-sökruta som SPEGLAR söket (Fas E2i, Klas-val 2026-06-11 "Normal ruta
 * som speglar söket" — ersätter E2h:s chips-i-fältet som blev fult renderat
 * och inte visade alla taggar i filter-raden).
 *
 * Modell (CTO VAL 1 = Variant C′, docs/reviews/2026-06-11-sok-paritet-
 * e2i-cto.md): fältets text är ANVÄNDARENS buffert; URL:en är persistent
 * sanning; invariant I1: parse(text) ⊆ state (delmängd — state får bära MER:
 * popover-valda dimensioner och icke-representabla labels lever enbart i
 * filter-raden under träffarna, som är TOTAL spegel; fältet är best-effort).
 *
 * - **Egen skrivning = delta-parse vid commit-punkter** (avgränsar-keystroke,
 *   Enter/Sök, förslags-val): skillnaden mellan föregående och nya text-
 *   anspråk appliceras på staten — popover-valda filter som texten aldrig
 *   gjort anspråk på rörs inte. Ordet under caret är pågående (CTO VAL 3 —
 *   radering mitt i ett ord släpper inte filtret per keystroke).
 * - **Texten behålls** — ord försvinner ALDRIG ur fältet vid taggning
 *   (E2d/E2h-felklassen). Tar man bort en tagg i filter-raden (×) uppdateras
 *   texten via extern-divergens-synken (kirurgisk borttagning som bevarar
 *   ordningen; annars kanonisk serialize).
 * - **Popover-val skrivs INTE in i texten** (CTO VAL 4a) — fältet visar det
 *   SKRIVNA; filter-raden visar allt.
 * - `router.replace` + `{scroll:false}` (E2h VAL 2 består); toolbar pushar.
 * - No-JS/pre-hydration: rått `<input name="q">`; efter hydration är synliga
 *   inputen NAMNLÖS (texten "Göteborg systemutvecklare" som q vore dubbel-
 *   filtrering) — hidden inputs bär de riktiga parametrarna.
 */

interface JobbHeroSearchProps {
  taxonomy: TaxonomyTree | null;
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  // Klass 2 (2026-06-13) — panel-valda anställningsform/omfattning. Aldrig
  // text-representabla i fältet (som popover-dimensionerna, CTO VAL 4a) —
  // de bärs bara genom delta-/commit-vägen så en sökord-ändring inte raderar
  // ett aktivt Klass-2-filter (buildJobbHref kräver dem; utan denna tråd
  // skulle fältets commit bygga en href som tappar dem).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  // STEG 5 (grade-filter, 2026-06-23) — aktivt matchningsgrad-filter. Som
  // Klass-2-dimensionerna: aldrig text-representabelt i fältet, men bärs genom
  // commit-/delta-vägen + no-JS-hidden-inputs så en sökord-ändring inte raderar
  // ett aktivt grad-filter (buildJobbHref kräver fältet).
  matchGrades: ReadonlyArray<string>;
  // #454 PR-0 — aktivt arbetsgivar-filter (ETT org.nr, page-validerat). Som
  // Klass-2-dimensionerna: aldrig text-representabelt i fältet, men bärs genom
  // commit-/delta-vägen + no-JS-hidden-input så en sökord-ändring inte raderar
  // ett aktivt arbetsgivar-filter (samma param-bevarande-disciplin).
  employer: string | undefined;
  sortBy: JobAdSortBy;
  pageSize?: string;
  // #419 pt6 (CTO A1) — commit-intent på mount-URL:en (page.tsx parsar `?commit=true`).
  // Init-värde för `savedByIntent`: true ⇒ sökningen är redan sparad (Enter/Sök/förslag/
  // no-JS-submit fångade den) → ingen "Spara sökningen"-länk. false (delad/bokmärkt URL
  // utan commit) ⇒ länken visas så mottagaren kan spara söket i sin lista.
  initialCommitted: boolean;
}

const emptySubscribe = () => () => {};

export function JobbHeroSearch({
  taxonomy,
  q,
  occupationGroup,
  region,
  municipality,
  employmentType,
  worktimeExtent,
  matchGrades,
  employer,
  sortBy,
  pageSize,
  initialCommitted,
}: JobbHeroSearchProps) {
  const router = useRouter();
  const t = useTranslations("jobads.ui");
  const [, startTransition] = useTransition();
  const helpId = useId();

  const hydrated = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false,
  );

  const labelIndex = useMemo(() => buildLabelIndex(taxonomy), [taxonomy]);
  const resolveLabel = useMemo(
    () => buildTaxonomyLabelResolver(taxonomy),
    [taxonomy],
  );

  const base = useMemo<JobbUrlState>(
    () => ({
      q,
      occupationGroup: [...occupationGroup],
      region: [...region],
      municipality: [...municipality],
      employmentType: [...employmentType],
      worktimeExtent: [...worktimeExtent],
      matchGrades: [...matchGrades],
      // #454 PR-0 — bärs genom delta-/commit-vägen så en sökord-ändring inte
      // raderar arbetsgivar-filtret (ingår i sameUrlState-komparatorn).
      employer,
      sortBy,
      pageSize,
    }),
    [
      q,
      occupationGroup,
      region,
      municipality,
      employmentType,
      worktimeExtent,
      matchGrades,
      employer,
      sortBy,
      pageSize,
    ],
  );

  // Fältets text — användarens buffert. Initieras till kanonisk spegel av
  // landnings-staten (recent-/direktlänk-navigering visar sitt sök).
  const [text, setText] = useState(() =>
    serializeSearchText(base, resolveLabel, labelIndex),
  );
  const [caret, setCaret] = useState<number | null>(null);
  // Hjälpradens notis-slot. Ett värde i taget: "limit" = söktexten är full
  // (Q_MAX_LENGTH), "tooShort" = söktexten är kortare än Q_MIN_LENGTH (#823).
  // Båda vaktar samma sak — att navigera med ett q som backend avvisar (400) och
  // därmed måla teknisk-fel-kortet mitt i skrivflödet.
  const [notice, setNotice] = useState<"limit" | "tooShort" | null>(null);
  const [announcement, setAnnouncement] = useState("");

  // #419 pt6 (CTO A1) — "är det här söket sparat i Senaste sökningar?". SSOT-signalen
  // är commit-INTENT: true efter en avsiktlig commit (Enter/Sök/förslags-val/×-clear —
  // alla markCommit), false efter en live-delta (typeahead utan Enter). Init ur
  // mount-URL:ens commit-flagga. Sätts på ETT ställe (commit()); extern-divergens sätter
  // det direkt (recent-nav = redan sparad). `savedNotice` = den korta inline-bekräftelsen
  // efter ett explicit spara-klick (självrensande vid nästa redigering).
  const [savedByIntent, setSavedByIntent] = useState(initialCommitted);
  const [savedNotice, setSavedNotice] = useState(false);

  // Senast applicerade text-anspråk (delta-basen). Init mot start-texten
  // så landnings-spegeln inte re-committas som "nya" anspråk. State (inte
  // ref) — läses/skrivs i render-sentinelen nedan (react-hooks/refs
  // förbjuder ref-access under render).
  const [prevClaims, setPrevClaims] = useState<ParsedClaims>(() =>
    parseSearchText(text, labelIndex, null),
  );

  // ARBETS-STATEN (delta-basen): URL-sanningen sådan vi känner den
  // INKLUSIVE egna in-flight-commits. Delta-appliceringen går mot denna —
  // INTE mot en useOptimistic-overlay, som reverterar till (stale) base
  // mellan transitions och då skulle tappa nyss committade dimensioner ur
  // nästa delta-bas (CTO-addendum BESLUT 3, deviation-ACK).
  const [lastCommitted, setLastCommitted] = useState<JobbUrlState>(base);
  // Own-roundtrip-DETEKTORN (CTO-addendum BESLUT 1): lista över egna
  // commits i flykt — singel-värdet räckte inte (två commits i flykt →
  // mellanliggande egen props-leverans mis-klassades som extern och
  // serialiserade om texten mitt under skrivning, E2d/E2h-felklassen).
  // Base som matchar NÅGON post = egen (prune t.o.m. träffen; lastCommitted
  // RÖRS EJ — den ligger före base tills listan är tom); annars extern.
  const [recentCommits, setRecentCommits] = useState<JobbUrlState[]>([]);
  const [prevBase, setPrevBase] = useState(base);
  if (base !== prevBase) {
    const hitIndex = recentCommits.findIndex((s) => sameUrlState(base, s));
    const adoptSortPageSize = () => {
      // Adoptera sort/pageSize ur basen — sameUrlState jämför dem inte, så
      // en extern sort-ändring vars filter-axlar matchar (in-flight-post
      // eller oförändrad state) får inte lämna stale sortBy i delta-basen
      // (code-reviewer re-review Minor: nästa text-commit skulle annars
      // tyst revertera sort-valet).
      if (
        lastCommitted.sortBy !== base.sortBy ||
        lastCommitted.pageSize !== base.pageSize
      )
        setLastCommitted({
          ...lastCommitted,
          sortBy: base.sortBy,
          pageSize: base.pageSize,
        });
    };
    if (hitIndex >= 0) {
      // Egen roundtrip (in-flight-commit landar) — texten orörd.
      setRecentCommits(recentCommits.slice(hitIndex + 1));
      adoptSortPageSize();
    } else if (sameUrlState(base, lastCommitted)) {
      // E2j skip-guard: den inkommande basens filter-state (q + dimensioner)
      // matchar vad vi SENAST committade — endast en icke-state-param
      // (commit-flaggan, sort eller pageSize) skiftade. Texten speglar redan
      // den staten → ingen resync, ingen extern-divergens-klassning. Detta
      // skyddar strip-efter-mount (?commit=1-borttagning, StripCommitParam)
      // från att felaktigt serialisera om användarens text (E2d/E2h-felklassen).
      // Jämförs mot lastCommitted (hero:ns auktoritativa state), INTE prevBase
      // — prevBase kan vara stale (props uppdateras inte synkront med egna
      // commits) och en äkta extern "Rensa allt" till tomt får då inte
      // miss-klassas som no-op. sort/pageSize adopteras fortfarande.
      adoptSortPageSize();
    } else {
      // EXTERN divergens (toolbar-×/Rensa/recent-nav): synka texten,
      // nollställ delta-bokföringen + caret/notis/annons (annars kan en
      // stale suggestQuery hålla listan öppen och en identisk framtida
      // annons-sträng utebli — code-reviewer Minor 2 + design Mi2).
      const nextText = updateTextForStateChange(
        text,
        prevBase,
        base,
        resolveLabel,
        labelIndex,
      );
      setText(nextText);
      setPrevClaims(parseSearchText(nextText, labelIndex, null));
      setNotice(null);
      setCaret(null);
      setAnnouncement("");
      setLastCommitted(base);
      setRecentCommits([]);
      // #419 pt6 — extern divergens (recent-nav/toolbar-×/Rensa): state kom utifrån
      // URL:en → behandla som redan-i-världen (recent-nav ÄR en sparad sökning). Nolla
      // spara-bekräftelsen; capture förblir backendens best-effort-ansvar.
      setSavedByIntent(true);
      setSavedNotice(false);
    }
    setPrevBase(base);
  }

  // markCommit (E2j) = avsiktlig commit (Enter/Sök/förslags-val/×-clear) →
  // ?commit=1-suffix så backend auto-capturerar. Live-delta (onFieldChange)
  // utelämnar det. commit-flaggan ligger UTANFÖR JobbUrlState/buildJobbHref/
  // sameUrlState (transient signal) — den adderas bara på navigerings-
  // strängen och strippas efter mount (StripCommitParam).
  function commit(next: JobbUrlState, announce: string, markCommit = false) {
    // #823 — min-längdsgrinden sitter HÄR för att commit() är den enda
    // navigeringspunkten: live-delta (runDelta), Enter/Sök, förslags-val och
    // ×-clear går alla igenom den. Ett q under backendens minimum (2 tecken)
    // navigerar annars till ?q=a, får 400 av ListJobAdsQueryValidator och målar
    // teknisk-fel-kortet — redan vid FÖRSTA tecknet i ett nytt sökord. Håll
    // navigeringen, visa vägledningen i hjälpraden i stället. Tomt q (×-clear,
    // ren filter-commit) är giltigt och passerar.
    const pendingQ = next.q.trim();
    if (pendingQ.length > 0 && pendingQ.length < Q_MIN_LENGTH) {
      setNotice("tooShort");
      return;
    }

    setLastCommitted(next);
    setRecentCommits((prev) => [...prev, next].slice(-10));
    // #419 pt6 (CTO A1) — ETT ställe för spar-signalen: en avsiktlig commit (markCommit)
    // capture:as av backend (?commit=true) → sparad; en live-delta (markCommit=false) är
    // en ny osparad sökning → länken återkommer. Enda mutationspunkten utöver spara-klicket.
    setSavedByIntent(markCommit);
    startTransition(() => {
      const href = buildJobbHref(next);
      router.replace(markCommit ? withCommitFlag(href) : href, {
        scroll: false,
      });
    });
    if (announce) setAnnouncement(announce);
  }

  // Delta-commit (C′ regel 1): parse → diff mot förra anspråken → applicera.
  function runDelta(nextText: string, caretIndex: number | null) {
    const claims = parseSearchText(nextText, labelIndex, caretIndex);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    setNotice(result.rejectedQ.length > 0 ? "limit" : null);
    if (!sameUrlState(result.next, lastCommitted)) {
      commit(
        result.next,
        [
          ...result.addedLabels.map((l) =>
            t("heroSearch.announceAdded", { label: l }),
          ),
          ...result.removedLabels.map((l) =>
            t("heroSearch.announceRemoved", { label: l }),
          ),
        ].join(". "),
      );
    }
  }

  function onFieldChange(nextText: string, caretIndex: number | null) {
    setText(nextText);
    setCaret(caretIndex);
    // #419 pt6 — börja användaren redigera söket är ev. "sparad"-bekräftelse inaktuell
    // (redan på första tecknet, före delta-commit) → nolla den så UI:t inte påstår sparat
    // om texten redan divergerat. Länken återkommer när deltat committas (savedByIntent).
    if (savedNotice) setSavedNotice(false);
    // Commit-punkt = tecknet före caret är en avgränsare (ordet avslutades
    // nyss). Ren radering committas inte per keystroke — deltat landar vid
    // nästa commit-punkt/Enter (CTO VAL 3, dokumenterad konsekvens).
    const justTyped = caretIndex !== null ? nextText[caretIndex - 1] : null;
    if (justTyped === " " || justTyped === ",")
      runDelta(nextText, caretIndex);
  }

  // Förslags-val (klick / Tab / pil+Enter). Text-insert är GATED: labeln
  // skrivs in ENDAST om parse bevisligen återfinner den (dimensions-label:
  // isTextRepresentable; Title-label: inga taxonomi-ord — annars skulle
  // texten claima en dimension staten inte fick, I1-brott; code-reviewer
  // Major 2). State går via DELTA-vägen (enforcement-täckt, CTO BESLUT 2-
  // synergin) + en garanterad compose av själva valet (täcker icke-
  // insertbara: ambiguös label/Title-med-taxonomi-ord — staten får valet,
  // texten claimar det inte) + slutlig enforceClaims (compose-vägens
  // normalisering får inte släcka text-claimade dimensioner).
  function onSelectSuggestion(suggestion: SuggestionDto) {
    const range =
      caret !== null
        ? getTokenRange(text, caret)
        : getTokenRange(text, text.length);
    const insertable =
      suggestion.kind === "Title"
        ? parseSearchText(suggestion.label, labelIndex, null).matches
            .length === 0
        : suggestion.conceptId !== null &&
          isTextRepresentable(
            suggestion.label,
            { kind: suggestion.kind, conceptId: suggestion.conceptId },
            labelIndex,
          );
    const insert = insertable ? `${suggestion.label} ` : "";
    const nextText = range
      ? text.slice(0, range.start) + insert + text.slice(range.end)
      : text + (text.length > 0 && !/[ ,]$/.test(text) ? " " : "") + insert;

    const claims = parseSearchText(nextText, labelIndex, null);
    const delta = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    const withSelection = enforceClaims(
      composeSuggestionChip(suggestion, delta.next, taxonomy),
      delta.appliedClaims,
      taxonomy,
    );

    setText(nextText);
    setCaret(null);
    setPrevClaims(delta.appliedClaims);
    setNotice(delta.rejectedQ.length > 0 ? "limit" : null);
    // Förslags-val är en commit-punkt (E2j): committa ALLTID med commit-intent
    // så sökningen auto-capturas — även i det sällsynta fall valet inte
    // ändrar filter-staten (re-val av redan applicerat förslag = "kör igen").
    commit(
      withSelection,
      t("heroSearch.announceAdded", { label: suggestion.label }),
      true,
    );
  }

  // Sök/Enter utan markerat förslag: finalisera HELA texten (inget caret-
  // undantag) — pågående ord committas. E2j: detta är den primära commit-
  // punkten → committa ALLTID med commit-intent (?commit=1), även när filter-
  // staten är oförändrad. "Sök" betyder "kör/spara den här sökningen" — en
  // re-sökning på samma filter ska bumpa recency, inte vara en no-op.
  function onSubmitText() {
    const claims = parseSearchText(text, labelIndex, null);
    const result = applyClaimsDelta(lastCommitted, prevClaims, claims, taxonomy);
    setPrevClaims(result.appliedClaims);
    setNotice(result.rejectedQ.length > 0 ? "limit" : null);
    commit(
      result.next,
      [
        ...result.addedLabels.map((l) =>
          t("heroSearch.announceAdded", { label: l }),
        ),
        ...result.removedLabels.map((l) =>
          t("heroSearch.announceRemoved", { label: l }),
        ),
      ].join(". "),
      true,
    );
  }

  // ×-clear (E2j, CTO VAL 4 = semantik ii): rensa texten + de filter texten
  // gjorde anspråk på (parse(text)-delmängden) — INTE popover-valda
  // dimensioner (I1: state får bära mer än texten). Delta mot tomma claims
  // tar bort exakt prevClaims ur staten; popover-dim överlever. Egen commit
  // via commit()-vägen (recentCommits-registrering) så texten inte
  // serialiseras om vid props-retur. commit-intent satt (CTO VAL 5 punkt 3).
  function onClear() {
    const delta = applyClaimsDelta(lastCommitted, prevClaims, EMPTY_CLAIMS, taxonomy);
    setText("");
    setCaret(null);
    setPrevClaims(EMPTY_CLAIMS);
    setNotice(null);
    commit(delta.next, t("heroSearch.announceCleared"), true);
  }

  // Suggest-prefix = ordet under caret (fältet bär hela söktexten — förslag
  // ska gälla det man skriver, inte hela strängen).
  const caretToken =
    caret !== null ? getTokenRange(text, caret) : null;
  const suggestQuery = caretToken
    ? text.slice(caretToken.start, caretToken.end)
    : "";

  const committedQ = lastCommitted.q.trim();

  // #419 pt6 (CTO A1) — "sparbart sök?" speglar backendens capture-guard
  // (RecentJobSearchCaptureBehavior: q ELLER någon dimension icke-tom; matchGrades/
  // sortBy/runtime-flaggor räknas INTE). Samma knowledge piece, inte en egen definition.
  const hasSavableSearch =
    committedQ.length > 0 ||
    lastCommitted.occupationGroup.length > 0 ||
    lastCommitted.region.length > 0 ||
    lastCommitted.municipality.length > 0 ||
    lastCommitted.employmentType.length > 0 ||
    lastCommitted.worktimeExtent.length > 0;

  // "Spara sökningen"-länken visas när det finns ett sparbart sök som ännu inte
  // committats med intent (typeahead-komponerat utan Enter/Sök). Klick återkommittar
  // nuvarande state MED intent → backend auto-capturerar (samma väg som "kör om sök",
  // ingen ny navigeringsmekanik); commit() sätter savedByIntent → länken försvinner.
  function onSaveSearch() {
    // Dirigera bekräftelsen genom den BEFINTLIGA persistenta aria-live-regionen
    // (commit → setAnnouncement) i stället för en villkorligt mountad role="status" —
    // en live-region som injiceras med sitt innehåll redan på plats annonseras inte
    // tillförlitligt av alla skärmläsare (design-reviewer Minor, a11y §6). Den synliga
    // .jp-hero__searchsaved-spanen är då rent visuell (aria-hidden) → ingen dubbel.
    commit(lastCommitted, t("heroSearch.saved"), true);
    setSavedNotice(true);
  }
  const showSaveAction = hydrated && hasSavableSearch && !savedByIntent;

  return (
    <form
      action="/jobb"
      method="get"
      className="jp-hero__searchblock"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmitText();
      }}
    >
      <label htmlFor="jobb-q" className="jp-hero__searchlabels">
        {t("heroSearch.fieldLabel")}
      </label>
      <div className="jp-hero__searchrow">
        {hydrated ? (
          <JobAdTypeahead
            id="jobb-q"
            value={text}
            suggestQuery={suggestQuery}
            onChange={onFieldChange}
            onSelect={onSelectSuggestion}
            selectOnTab
            wrapperClassName="jp-hero__searchfield"
            inputClassName="jp-hero__input"
            ariaDescribedBy={helpId}
          />
        ) : (
          // Pre-hydration/no-JS: rått q-fält — native GET-submit bär hela
          // söktexten som q (backend-parsern är SPOT och tål rå sträng).
          <input
            id="jobb-q"
            name="q"
            type="search"
            defaultValue={q}
            className="jp-hero__input"
            aria-describedby={helpId}
          />
        )}
        {/* Kontrollerad ×-clear (E2j): ersätter native
            ::-webkit-search-cancel-button (suppress:ad i CSS) som bara
            rensade texten utan att committa en delta → filtren överlevde.
            Denna går genom onClear → applyClaimsDelta(EMPTY_CLAIMS) (semantik
            ii). Visas bara när det finns text att rensa. */}
        {hydrated && text.length > 0 && (
          <button
            type="button"
            className="jp-hero__clearbtn"
            onClick={onClear}
            aria-label={t("heroSearch.clearField")}
          >
            <X size={18} aria-hidden="true" />
          </button>
        )}
        <button type="submit" className="jp-hero__searchbtn">
          <Search size={18} aria-hidden="true" /> {t("heroSearch.submit")}
        </button>
      </div>
      {/* Hjälptext bär tagg-/Tab-instruktionen (ALDRIG placeholder — Klas
          hård regel). role="status" så notis-skiftet (q-max/q-min) annonseras. */}
      <p id={helpId} role="status" className="jp-hero__searchhelp">
        {notice === "limit"
          ? t("heroSearch.limitNotice", { max: Q_MAX_LENGTH })
          : notice === "tooShort"
            ? t("heroSearch.minNotice", { min: Q_MIN_LENGTH })
            : t("heroSearch.help")}
      </p>

      {/* #419 pt6 (Klas + CTO A1) — "Spara sökningen"-länken: diskret text-knapp
          (INTE <a> — ingen navigering) som visas när ett sparbart sök komponerats via
          typeahead utan Enter/Sök (osparat). Klick capture:ar via ?commit=true + visar en
          kort inline-bekräftelse (role="status", polite) på samma plats — inget toast-
          bibliotek (§9.2). Bekräftelsen står kvar tills söket ändras (civic, ingen
          animation). Placerad direkt efter hjälptexten, delar dess soft-ink-ton. */}
      {showSaveAction && (
        <button
          type="button"
          className="jp-hero__searchsaveaction"
          onClick={onSaveSearch}
        >
          {t("heroSearch.save")}
        </button>
      )}
      {savedNotice && (
        // Rent visuell (aria-hidden) — SR-annonsen bärs av den persistenta
        // aria-live-regionen nedan (announcement), satt av onSaveSearch via commit.
        <p className="jp-hero__searchsaved" aria-hidden="true">
          {t("heroSearch.saved")}
        </p>
      )}

      {/* aria-live-annons för tagg-tillägg/-borttagning — viktigare än i
          E2h: den visuella feedbacken (taggarna) sitter nu i filter-raden
          under träfflistan, långt från fältet. */}
      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>

      {/* No-JS-fallback: aktiva filter som hidden inputs. Synliga inputen
          är NAMNLÖS efter hydration — spegel-texten som q vore dubbel-
          filtrering; committad residual-q bärs som hidden input. */}
      {hydrated && committedQ.length > 0 && (
        <input type="hidden" name="q" value={committedQ} />
      )}
      {lastCommitted.occupationGroup.map((v) => (
        <input
          key={`occupationGroup-${v}`}
          type="hidden"
          name="occupationGroup"
          value={v}
        />
      ))}
      {lastCommitted.region.map((v) => (
        <input key={`region-${v}`} type="hidden" name="region" value={v} />
      ))}
      {lastCommitted.municipality.map((v) => (
        <input
          key={`municipality-${v}`}
          type="hidden"
          name="municipality"
          value={v}
        />
      ))}
      {/* Klass 2 — no-JS-submit bär aktiva anställningsform/omfattning-filter
          så en sökord-sökning utan JS inte tappar panelvalen. */}
      {lastCommitted.employmentType.map((v) => (
        <input
          key={`employmentType-${v}`}
          type="hidden"
          name="employmentType"
          value={v}
        />
      ))}
      {lastCommitted.worktimeExtent.map((v) => (
        <input
          key={`worktimeExtent-${v}`}
          type="hidden"
          name="worktimeExtent"
          value={v}
        />
      ))}
      {/* STEG 5 — no-JS-submit bär aktivt grad-filter så en sökord-sökning utan
          JS inte tappar det (paritet med Klass-2-dimensionerna ovan). */}
      {lastCommitted.matchGrades.map((v) => (
        <input
          key={`matchGrades-${v}`}
          type="hidden"
          name="matchGrades"
          value={v}
        />
      ))}
      {/* #454 PR-0 — no-JS-submit bär aktivt arbetsgivar-filter så en sökord-
          sökning utan JS inte tappar det (paritet med dimensionerna ovan). */}
      {lastCommitted.employer && (
        <input type="hidden" name="employer" value={lastCommitted.employer} />
      )}
      {sortBy !== DEFAULT_SORT_BY && (
        <input type="hidden" name="sortBy" value={sortBy} />
      )}
      {pageSize && <input type="hidden" name="pageSize" value={pageSize} />}
      {/* E2j — no-JS-submit ÄR per definition en commit (användaren tryckte
          Sök) → statiskt commit=1 så backend auto-capturerar. Vid hydration
          interceptas submit (onSubmit preventDefault) och router-vägen bär
          commit som transient suffix istället — denna åker då aldrig.
          Värde "true" (ASP.NET bool-binding tar inte "1"). */}
      <input type="hidden" name="commit" value="true" />
    </form>
  );
}
