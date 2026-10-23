"use client";

// "use client": a controlled field with its own Escape handling, and the status region it owns.

import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { OccupationSearchState } from "@/lib/hooks/use-occupation-search";
import { OCCUPATION_SEARCH_MAX_LENGTH } from "@/lib/occupation-search/resolve-occupation-search";

type Translate = ReturnType<typeof useTranslations<"components.occupationSearch">>;

export interface OccupationSearchFieldProps {
  readonly id: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly state: OccupationSearchState;
}

/**
 * The occupation picker's search field (#2198, ADR 0180) and the ONE status region that speaks for
 * the search. A plain search field, not a combobox (CTO ruling (e), Klas E1): the rows are
 * checkboxes and pressed buttons, with no active-descendant model behind them.
 *
 * The region lives here, beside the field, so it is never remounted when the box below swaps
 * between browsing and results: a region that mounts with its text already in it is not announced.
 * No hint: below two characters nothing is refused, the search simply has not started.
 *
 * `data-occupation-search-field` is `OCCUPATION_SEARCH_FIELD_ATTRIBUTE`, the mark a host's Escape
 * handler looks for (`keep-host-open.ts`), spelled out because a JSX attribute name cannot be a
 * computed key.
 */
export function OccupationSearchField({ id, value, onChange, state }: OccupationSearchFieldProps) {
  const t = useTranslations("components.occupationSearch");
  return (
    <div className="flex flex-col gap-1.5 mb-2">
      <Label htmlFor={id}>{t("fieldLabel")}</Label>
      <Input
        id={id}
        type="search"
        autoComplete="off"
        maxLength={OCCUPATION_SEARCH_MAX_LENGTH}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={(e) => {
          // With text: clear it and keep focus here. `preventDefault` also cancels the browser's
          // own clear of a search field, so the term is cleared once, through state. Empty: not
          // handled, and the host closes as it always has.
          if (e.key === "Escape" && e.currentTarget.value !== "") {
            e.preventDefault();
            onChange("");
          }
        }}
        data-occupation-search-field=""
      />
      <p
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="min-h-6 text-body-sm tabular-nums text-text-primary"
      >
        {statusText(t, state)}
      </p>
    </div>
  );
}

function statusText(t: Translate, state: OccupationSearchState): string {
  switch (state.kind) {
    case "idle":
      return "";
    case "pending":
      return t("pending");
    case "ok":
      return state.result.rows.length === 0
        ? t("noHits")
        : t("count", { count: state.result.totalRows });
    case "unauthorized":
      return t("unauthorized");
    case "unavailable":
      return t("unavailable");
  }
}
