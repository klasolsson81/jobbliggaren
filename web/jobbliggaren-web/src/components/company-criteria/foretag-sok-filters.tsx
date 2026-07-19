"use client";

// "use client": holds the DRAFT filter state (name string + two leaf-code Sets), composes the two
// CriterionPickers, and commits the whole draft to the URL on submit. The URL is the source of truth
// for the shareable table; org.nr is NOT here (it lives in the separate ForetagSokOrgnr island — D8(c)).

import { useId, useMemo, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { CriterionPicker } from "./criterion-picker";
import type { CriterionTreeNode } from "./criterion-tree";
import { toggleGroup } from "@/lib/company-criteria/criterion-selection";
import {
  buildForetagSokHref,
  MAX_NAME_PREFIX_LENGTH,
} from "@/lib/company-search/search-params";
import type { CriterionReference } from "@/lib/dto/company-criteria";

interface ForetagSokFiltersProps {
  /** The SCB reference tree the pickers render. An empty tree (degraded load) shows civil notices. */
  readonly reference: CriterionReference;
  /** The active filter axes, parsed from the URL — the draft seeds from these. */
  readonly namn: string;
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
}

/**
 * #560 PR-B — the `/foretag/sok` filter panel: a name-prefix field + the two SCB pickers (branch, seat
 * kommun), reusing `CriterionPicker`/`CriterionTree` (ADR 0105/RF-4 — the SCB namespace, never the
 * JobTech ort cascade). Filters are applied on an explicit "Sök företag" (not on every checkbox toggle)
 * so a browse over 1.07M rows is not re-queried on each keystroke; the commit resets to page 1. A no-JS
 * `<form method="get">` degrades to a name search (hidden inputs preserve the current SNI/kommun).
 */
export function ForetagSokFilters({
  reference,
  namn,
  sni,
  kommun,
}: ForetagSokFiltersProps) {
  const t = useTranslations("pages.foretag.sok");
  // The picker chrome (headings, filter labels, tri-state aria) is the same knowledge as the criterion
  // dialog's — reuse its namespace rather than duplicate it (the picker also reads it internally).
  const tp = useTranslations("pages.foretag.criteria.dialog");
  const router = useRouter();
  const nameId = useId();
  const nameHintId = useId();
  const [isPending, startTransition] = useTransition();

  // Build the picker trees + flat leaf lists from the reference (parity with criterion-dialog.tsx).
  const sniNodes = useMemo<CriterionTreeNode[]>(
    () =>
      reference.sni.map((section) => ({
        code: section.code,
        name: section.name,
        leafCodes: section.divisions.flatMap((d) => d.leaves.map((l) => l.code)),
        children: section.divisions.map((division) => ({
          code: division.code,
          name: division.name,
          leafCodes: division.leaves.map((l) => l.code),
          children: division.leaves.map((leaf) => ({
            code: leaf.code,
            name: leaf.name,
            leafCodes: [leaf.code],
          })),
        })),
      })),
    [reference],
  );
  const sniLeaves = useMemo(
    () =>
      reference.sni.flatMap((s) =>
        s.divisions.flatMap((d) =>
          d.leaves.map((l) => ({ code: l.code, name: l.name })),
        ),
      ),
    [reference],
  );
  const kommunNodes = useMemo<CriterionTreeNode[]>(
    () =>
      reference.lan.map((lan) => ({
        code: lan.code,
        name: lan.name,
        leafCodes: lan.kommuner.map((k) => k.code),
        children: lan.kommuner.map((kommun) => ({
          code: kommun.code,
          name: kommun.name,
          leafCodes: [kommun.code],
        })),
      })),
    [reference],
  );
  const kommunLeaves = useMemo(
    () =>
      reference.lan.flatMap((l) =>
        l.kommuner.map((k) => ({ code: k.code, name: k.name })),
      ),
    [reference],
  );

  // Draft state, seeded from the URL-parsed props.
  const [nameDraft, setNameDraft] = useState(namn);
  const [sniSelected, setSniSelected] = useState<ReadonlySet<string>>(
    () => new Set(sni),
  );
  const [kommunSelected, setKommunSelected] = useState<ReadonlySet<string>>(
    () => new Set(kommun),
  );

  const hasFilter =
    nameDraft.trim().length > 0 || sniSelected.size > 0 || kommunSelected.size > 0;

  function apply() {
    startTransition(() => {
      router.push(
        buildForetagSokHref({
          namn: nameDraft,
          sni: [...sniSelected],
          kommun: [...kommunSelected],
        }),
      );
    });
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    apply();
  }

  function onClear() {
    setNameDraft("");
    setSniSelected(new Set());
    setKommunSelected(new Set());
    startTransition(() => router.push(buildForetagSokHref({ namn: "", sni: [], kommun: [] })));
  }

  return (
    <form
      // No-JS fallback: a native GET to /foretag/sok. The name field submits as ?namn=…; the current
      // SNI/kommun are preserved via hidden inputs. With JS, onSubmit intercepts and commits the draft.
      action="/foretag/sok"
      method="get"
      onSubmit={onSubmit}
      className="flex flex-col gap-6 rounded-md border border-border p-4 md:p-6"
    >
      <h2 className="text-h3 font-semibold text-text-primary">
        {t("filterHeading")}
      </h2>

      <div className="jp-field max-w-xl">
        <label htmlFor={nameId} className="jp-label">
          {t("nameLabel")}
        </label>
        <input
          id={nameId}
          name="namn"
          className="jp-input"
          type="text"
          autoComplete="off"
          maxLength={MAX_NAME_PREFIX_LENGTH}
          aria-describedby={nameHintId}
          value={nameDraft}
          onChange={(e) => setNameDraft(e.target.value)}
        />
        <span id={nameHintId} className="jp-hint">
          {t("nameHint")}
        </span>
      </div>

      {/* Preserve the current code axes for the no-JS submit path (ignored when JS handles onSubmit). */}
      {sni.map((code) => (
        <input key={`sni-${code}`} type="hidden" name="sni" value={code} />
      ))}
      {kommun.map((code) => (
        <input key={`kommun-${code}`} type="hidden" name="kommun" value={code} />
      ))}

      <div className="grid gap-6 md:grid-cols-2">
        <CriterionPicker
          nodes={sniNodes}
          leaves={sniLeaves}
          selected={sniSelected}
          onToggle={(codes) => setSniSelected((prev) => toggleGroup(prev, codes))}
          onClear={() => setSniSelected(new Set())}
          heading={tp("sniHeading")}
          help={tp("sniHelp")}
          filterLabel={tp("sniFilterLabel")}
          filterHint={tp("sniFilterHint")}
          groupAria={tp("sniGroupAria")}
          selectedCountLabel={tp("sniSelectedCount", { count: sniSelected.size })}
          optionsUnavailable={tp("optionsUnavailable")}
        />
        <CriterionPicker
          nodes={kommunNodes}
          leaves={kommunLeaves}
          selected={kommunSelected}
          onToggle={(codes) => setKommunSelected((prev) => toggleGroup(prev, codes))}
          onClear={() => setKommunSelected(new Set())}
          heading={tp("kommunHeading")}
          help={tp("kommunHelp")}
          filterLabel={tp("kommunFilterLabel")}
          filterHint={tp("kommunFilterHint")}
          groupAria={tp("kommunGroupAria")}
          selectedCountLabel={tp("kommunSelectedCount", {
            count: kommunSelected.size,
          })}
          optionsUnavailable={tp("optionsUnavailable")}
        />
      </div>

      <div className="flex items-center gap-3">
        <button
          type="submit"
          className="jp-btn jp-btn--primary"
          aria-busy={isPending || undefined}
        >
          {t("searchButton")}
        </button>
        {hasFilter && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost"
            onClick={onClear}
            disabled={isPending}
          >
            {t("clearButton")}
          </button>
        )}
      </div>
    </form>
  );
}
