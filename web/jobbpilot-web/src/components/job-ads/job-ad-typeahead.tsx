"use client";

import { useEffect, useId, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import {
  SUGGEST_MIN_PREFIX,
  SUGGEST_DEBOUNCE_MS,
  suggestJobAdTermsResultSchema,
} from "@/lib/dto/job-ads";

interface JobAdTypeaheadProps {
  id: string;
  value: string;
  onChange: (next: string) => void;
  // Anropas när användaren väljer ett förslag (Enter/klick) — föräldern kan
  // då tillämpa det som aktivt sökord (ADR 0042 Beslut C: "förslag → filter").
  onSelect: (term: string) => void;
  ariaInvalid?: boolean;
  ariaDescribedBy?: string;
}

type SuggestState =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; items: string[] }
  | { status: "rateLimited" };

/**
 * ADR 0042 Beslut C — live-typeahead mot `/api/jobb/suggest` (proxy →
 * backend ILIKE-prefix). Debounce ≥300ms + min 2 tecken (skickar ej request
 * under det; backend-validator + SuggestPolicy är sista barriär). In-flight-
 * request avbryts vid ny keystroke (AbortController) så svar inte race:ar.
 *
 * Ingen TanStack Query: codebase-konventionen (JobAdFilters-kommentar
 * "ingen TanStack — YAGNI för Fas 2-volym") + ingen QueryClientProvider-
 * infra finns; att introducera den i en LÅST session vore en otillåten
 * top-level-dependency utan diskussion (CLAUDE.md §9.2). Self-contained
 * debounce-hook uppfyller Beslut C:s DoS-krav. senior-cto-advisor
 * 2026-05-16 (Variant A): valet står — YAGNI + §9.1 (existerande mönster)
 * + §9.2 (dep-disciplin); §4.3 reglerar mutations/pollar, ej typeahead-read.
 *
 * Civic-utility: förslagslistan är en flat lista (regel 1, ingen drop-
 * shadow utöver popover-lager), ingen ikon-dekoration, saklig copy.
 * rateLimited degraderar tyst med en informativ rad (regel 6).
 */
export function JobAdTypeahead({
  id,
  value,
  onChange,
  onSelect,
  ariaInvalid,
  ariaDescribedBy,
}: JobAdTypeaheadProps) {
  const listId = useId();
  const statusId = useId();
  const [state, setState] = useState<SuggestState>({ status: "idle" });
  const [open, setOpen] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const prefix = value.trim();

    // Under min-prefix: ingen request, ingen synkron setState i effect-
    // kroppen (react-hooks/set-state-in-effect). Idle-reset schemaläggs på
    // microtask så lint-regeln (synkron setState i effect) inte triggas och
    // tidigare förslag rensas när användaren raderar ner under 2 tecken.
    if (prefix.length < SUGGEST_MIN_PREFIX) {
      abortRef.current?.abort();
      const id = setTimeout(() => setState({ status: "idle" }), 0);
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
          { signal: controller.signal }
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
          await res.json()
        );
        setState({
          status: "ready",
          items: parsed.success ? parsed.data : [],
        });
      } catch (err) {
        // AbortError = avsiktlig avbrytning vid ny keystroke — ignorera.
        if (err instanceof DOMException && err.name === "AbortError") return;
        setState({ status: "ready", items: [] });
      }
    }, SUGGEST_DEBOUNCE_MS);

    // Cleanup vid value-ändring OCH unmount: rensa debounce-timern och
    // avbryt ev. in-flight fetch (annars setState-on-unmounted vid unmount
    // mitt i await — senior-cto-advisor 2026-05-16 in-block). AbortError
    // fångas/ignoreras i catch ovan.
    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [value]);

  function choose(term: string) {
    onSelect(term);
    setOpen(false);
    setState({ status: "idle" });
  }

  const showList =
    open && state.status === "ready" && state.items.length > 0;

  return (
    <div className="relative flex flex-col gap-1.5">
      <Input
        id={id}
        type="search"
        inputMode="search"
        autoComplete="off"
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-invalid={ariaInvalid ? true : undefined}
        aria-describedby={
          [ariaDescribedBy, statusId].filter(Boolean).join(" ") || undefined
        }
        value={value}
        onChange={(e) => {
          onChange(e.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={(e) => {
          if (e.key === "Escape") setOpen(false);
        }}
      />

      <p
        id={statusId}
        role="status"
        aria-live="polite"
        className="sr-only"
      >
        {state.status === "loading"
          ? "Hämtar förslag…"
          : state.status === "ready" && state.items.length > 0
            ? `${state.items.length} förslag`
            : ""}
      </p>

      {state.status === "rateLimited" && (
        <p className="text-body-sm text-text-secondary">
          För många sökningar på kort tid. Förslagen pausas en stund — du
          kan fortsätta skriva och söka ändå.
        </p>
      )}

      {showList && (
        <ul
          id={listId}
          className="absolute top-full z-10 mt-1 w-full overflow-hidden rounded-md border border-border-default bg-surface-primary shadow-md"
          aria-label="Sökförslag"
        >
          {state.items.map((item) => (
            <li key={item}>
              <button
                type="button"
                onClick={() => choose(item)}
                className="block w-full px-3 py-2 text-left text-body-sm text-text-primary hover:bg-surface-tertiary"
              >
                {item}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
