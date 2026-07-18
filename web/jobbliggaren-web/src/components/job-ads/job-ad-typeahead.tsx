"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import {
  SUGGEST_MIN_PREFIX,
  SUGGEST_DEBOUNCE_MS,
  suggestJobAdTermsResultSchema,
  type SuggestionDto,
} from "@/lib/dto/job-ads";

interface JobAdTypeaheadProps {
  id: string;
  value: string;
  /** `caretIndex` = selectionStart efter ändringen (E2i spegel-fältet
   * parsar caret-medvetet — ordet under caret är pågående). */
  onChange: (next: string, caretIndex: number | null) => void;
  /**
   * Anropas när användaren väljer ett förslag (klick / Enter på markerad rad).
   * Får HELA `SuggestionDto` (kind + conceptId + label) så föräldern kan
   * komponera rätt filter-chip (ADR 0067 Beslut 5b / Fas E2d) — taxonomi-träff
   * → dimension-param, Title → fri q.
   */
  onSelect: (suggestion: SuggestionDto) => void;
  /** `name` på inputen (no-JS GET-form-fältet — föräldern bär en `<form>`). */
  name?: string;
  /**
   * E2h (Klas-spec): Tab väljer det markerade förslaget ("tabba-klart")
   * i stället för fokus-flytt. Medvetet WAI-ARIA APG-avsteg, mitigerat:
   * intercept ENDAST när listan är öppen OCH en markering finns (pil/
   * mouseover) — annars normal fokus-flytt, aldrig en fokus-fälla.
   * Shift+Tab interceptas aldrig.
   */
  selectOnTab?: boolean;
  /**
   * E2i (spegel-fältet): strängen som används för SUGGEST-hämtningen i
   * stället för hela `value` — fältet bär hela söktexten men förslagen ska
   * gälla ordet under caret. Utelämnad → `value` (bakåtkompat).
   */
  suggestQuery?: string;
  /** Styling-override för inputen (t.ex. `jp-hero__input`). */
  inputClassName?: string;
  /** Styling-override för wrappern (t.ex. `relative flex-1`). */
  wrapperClassName?: string;
  ariaInvalid?: boolean;
  ariaDescribedBy?: string;
}

type SuggestState =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; items: SuggestionDto[] }
  | { status: "rateLimited" };

/**
 * ADR 0042 Beslut C + ADR 0067 Beslut 5a/5b — live-typeahead-combobox mot
 * `/api/jobb/suggest` (proxy → backend taxonomi-union + titel-ILIKE-prefix).
 * Debounce ≥300ms + min 2 tecken; in-flight-request avbryts vid ny keystroke
 * (AbortController) så svar inte race:ar.
 *
 * Tangentbord (WAI-ARIA combobox + listbox-popup, jobbliggaren-design-a11y):
 * Pil ned/upp flyttar `aria-activedescendant` i listan, Enter väljer markerad
 * rad (annars bubblar Enter till förälderns `<form>` = fri sökning), Escape
 * stänger. Optionerna är `role="option"` (fokus stannar i inputen — inga
 * separata tab-stopp), `onMouseDown` förhindrar blur så klicket hinner före.
 *
 * Ingen TanStack Query (codebase-konvention + ingen QueryClientProvider-infra;
 * self-contained debounce uppfyller DoS-kravet — senior-cto-advisor 2026-05-16
 * Variant A). Civic-utility: flat lista, ingen ikon-dekoration, saklig copy;
 * rateLimited degraderar tyst med en informativ rad (regel 6).
 */
export function JobAdTypeahead({
  id,
  value,
  onChange,
  onSelect,
  name,
  selectOnTab,
  suggestQuery,
  inputClassName,
  wrapperClassName,
  ariaInvalid,
  ariaDescribedBy,
}: JobAdTypeaheadProps) {
  const t = useTranslations("jobads.ui");
  const listId = useId();
  const statusId = useId();
  const optionBaseId = useId();
  const [state, setState] = useState<SuggestState>({ status: "idle" });
  const [open, setOpen] = useState(false);
  // Markerad rad för tangentbordsnavigering (-1 = ingen → Enter = fri sökning).
  const [active, setActive] = useState(-1);
  const abortRef = useRef<AbortController | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  const effectivePrefix = suggestQuery ?? value;

  useEffect(() => {
    const prefix = effectivePrefix.trim();

    // Under min-prefix: ingen request, ingen synkron setState i effect-
    // kroppen (react-hooks/set-state-in-effect). Idle-reset schemaläggs på
    // microtask så lint-regeln inte triggas och tidigare förslag rensas.
    if (prefix.length < SUGGEST_MIN_PREFIX) {
      abortRef.current?.abort();
      const id = setTimeout(() => {
        setState({ status: "idle" });
        setActive(-1);
      }, 0);
      return () => clearTimeout(id);
    }

    const timer = setTimeout(async () => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setState({ status: "loading" });

      try {
        const res = await fetch(
          `/api/jobb/suggest?prefix=${encodeURIComponent(prefix)}`,
          { signal: controller.signal },
        );
        if (res.status === 429) {
          setState({ status: "rateLimited" });
          return;
        }
        if (!res.ok) {
          setState({ status: "ready", items: [] });
          return;
        }
        const parsed = suggestJobAdTermsResultSchema.safeParse(
          await res.json(),
        );
        setState({
          status: "ready",
          items: parsed.success ? parsed.data : [],
        });
        // Ny resultatmängd → nollställ markeringen (annars pekar den på fel rad).
        setActive(-1);
      } catch (err) {
        // AbortError = avsiktlig avbrytning vid ny keystroke — ignorera.
        if (err instanceof DOMException && err.name === "AbortError") return;
        setState({ status: "ready", items: [] });
      }
    }, SUGGEST_DEBOUNCE_MS);

    // Cleanup vid value-ändring OCH unmount: rensa debounce-timern och avbryt
    // ev. in-flight fetch (annars setState-on-unmounted vid unmount mitt i
    // await — senior-cto-advisor 2026-05-16 in-block).
    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [effectivePrefix]);

  // Outside-press dismissal (#295, WAI-ARIA APG combobox): while the popup is
  // open, a pointer press anywhere outside the widget closes it. Bound to
  // `pointerdown` (not `mousedown`/`click`) so it is input-modality-agnostic —
  // mouse, pen AND touch — and fires for a tap on *inert* page content where
  // mobile browsers synthesise no mouse event and focus never leaves the input
  // (so neither a `mousedown` listener nor the `onBlur` path below would fire;
  // that touch tap was the reported bug, just on mobile). `pointerdown` fires
  // before the focus/blur chain and before the option rows' own `onMouseDown`;
  // an option press stays inside `rootRef` and is therefore never treated as
  // "outside". The listener is attached only while open — no idle global handler.
  useEffect(() => {
    if (!open) return;
    function onDocumentPointerDown(e: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
        setActive(-1);
      }
    }
    document.addEventListener("pointerdown", onDocumentPointerDown);
    return () =>
      document.removeEventListener("pointerdown", onDocumentPointerDown);
  }, [open]);

  const items = state.status === "ready" ? state.items : [];
  const showList = open && items.length > 0;
  // The rate-limited degradation row shares the popup's open lifecycle so it is
  // dismissed by the same gestures as the list (#295) — outside-click, Escape,
  // focus-out — instead of lingering until the next keystroke.
  const showRateLimited = open && state.status === "rateLimited";
  // #757 — sighted loading affordance. During an in-flight fetch `items` is
  // empty, so `showList` is false and the suggestion list would unmount: sighted
  // users saw their suggestions flash out to an empty gap until the next list
  // popped in. This boolean gates ONLY the visible skeleton row below; it never
  // feeds `aria-expanded`/`aria-activedescendant`/`showList` (the skeleton is not
  // a list of options — the combobox stays truthfully collapsed during loading,
  // and the sr-only status region carries the sole SR announcement). `showList`,
  // `showRateLimited` and `showLoading` are mutually exclusive by the state union.
  const showLoading = open && state.status === "loading";

  function choose(item: SuggestionDto) {
    onSelect(item);
    setOpen(false);
    setActive(-1);
    setState({ status: "idle" });
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Escape") {
      setOpen(false);
      setActive(-1);
      return;
    }
    if (!showList) return;
    // Tab väljer markerat förslag (E2h, Klas-spec "tabba-klart") — ENDAST
    // vid öppen lista + markering; Shift+Tab aldrig (se prop-dokumentationen).
    if (selectOnTab && e.key === "Tab" && !e.shiftKey && active >= 0) {
      e.preventDefault();
      choose(items[active]!);
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((i) => (i + 1) % items.length);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((i) => (i <= 0 ? items.length - 1 : i - 1));
    } else if (e.key === "Enter") {
      if (active >= 0) {
        // Markerad rad → välj den (och låt INTE Enter bubbla till form-submit).
        e.preventDefault();
        choose(items[active]!);
      } else {
        // Fri sökning (#295): close the popup but do NOT preventDefault — Enter
        // still bubbles to the parent <form>, which commits the search. The
        // commit navigates via router.replace (client navigation, no remount),
        // so without this explicit setOpen(false) the suggestion list lingered
        // after the search ran (the reported bug).
        setOpen(false);
        setActive(-1);
      }
    }
  }

  const optionId = (i: number) => `${optionBaseId}-${i}`;

  return (
    <div
      ref={rootRef}
      className={wrapperClassName ?? "relative flex flex-col gap-1.5"}
      // Focus leaving the combobox widget (Tab to the next control, or a click
      // on a focusable element outside) closes the popup (#295, APG combobox
      // dismissal). Selecting an option does not blur the input (its
      // `onMouseDown` preventDefault keeps focus), so a real selection is never
      // mistaken for a focus-out. Pointer clicks on non-focusable areas are
      // also covered by the document `mousedown` listener above; Escape is
      // handled in `onKeyDown`.
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) {
          setOpen(false);
          setActive(-1);
        }
      }}
    >
      <input
        id={id}
        name={name}
        type="search"
        inputMode="search"
        autoComplete="off"
        className={inputClassName}
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={
          showList && active >= 0 ? optionId(active) : undefined
        }
        aria-invalid={ariaInvalid ? true : undefined}
        aria-describedby={
          [ariaDescribedBy, statusId].filter(Boolean).join(" ") || undefined
        }
        value={value}
        onChange={(e) => {
          onChange(e.target.value, e.target.selectionStart);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
      />

      <p id={statusId} role="status" aria-live="polite" className="sr-only">
        {state.status === "loading"
          ? t("typeahead.loading")
          : items.length > 0
            ? t("typeahead.suggestionCount", { count: items.length })
            : ""}
      </p>

      {showLoading && (
        // #757 sighted loading row: a flat skeleton row in the same popup chrome
        // as the list, so sighted users see that a suggestion is on its way instead
        // of an empty gap while the next response is fetched (skeleton doctrine:
        // "prefer Skeleton over Spinner", flat neutral grey, no shimmer/pulse/glow).
        // Purely decorative → aria-hidden: the sr-only role="status" region above
        // carries the sole screen-reader announcement ("Hämtar förslag…"), unchanged.
        // The popup is absolutely positioned → no page layout shift; the outer frame
        // is byte-identical to the list's (border/bg/shadow/position) and the loading
        // row's px-3 py-2 mirrors an option row's padding, so the first result row
        // lands at the same top padding on swap.
        <div
          aria-hidden="true"
          className="absolute top-full left-0 z-10 mt-1 w-full overflow-hidden rounded-md border border-border-default bg-surface-primary shadow-md"
        >
          <div className="px-3 py-2">
            <span className="jp-skeleton block h-5 w-40 max-w-full" />
          </div>
        </div>
      )}

      {showRateLimited && (
        // Absolut-positionerad (som listan) så degraderingsraden escapar en
        // ev. overflow:hidden-wrapper (hero-searchrowen) och blir läsbar på
        // surface-primary i stället för pill-bakgrunden (design-reviewer
        // Minor 1 E2d). Listan och rateLimited är ömsesidigt uteslutande.
        <p className="absolute top-full left-0 z-10 mt-1 w-full rounded-md border border-border-default bg-surface-primary px-3 py-2 text-body-sm text-text-primary shadow-md">
          {t("typeahead.rateLimited")}
        </p>
      )}

      {showList && (
        <ul
          id={listId}
          role="listbox"
          className="absolute top-full left-0 z-10 mt-1 w-full overflow-hidden rounded-md border border-border-default bg-surface-primary shadow-md"
          aria-label={t("typeahead.listLabel")}
          // Rensa markeringen när pekaren lämnar listan (design-reviewer M4
          // E2h): en parkerad muspekare får inte lämna stale markering som
          // Tab sedan väljer utan att användaren tittar.
          onMouseLeave={() => setActive(-1)}
        >
          {items.map((item, i) => (
            <li
              key={`${item.kind}:${item.conceptId ?? item.label}`}
              id={optionId(i)}
              role="option"
              aria-selected={i === active}
              className={`cursor-pointer px-3 py-2 text-body-sm ${
                i === active
                  ? "bg-surface-tertiary text-text-primary"
                  : "text-text-primary"
              }`}
              // onMouseDown (ej onClick) → preventDefault behåller inputens
              // fokus så blur inte stänger listan före valet registreras.
              onMouseDown={(e) => {
                e.preventDefault();
                choose(item);
              }}
              onMouseEnter={() => setActive(i)}
            >
              {item.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
