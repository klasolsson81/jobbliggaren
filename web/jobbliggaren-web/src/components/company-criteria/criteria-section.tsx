"use client";

// "use client": owns the "create dialog" open state + the "Ny branschbevakning" trigger. The rows and their
// per-row dialogs are already client; this promotes no server logic to the client — the RSC page does
// all data fetching and passes the criteria + reference tree down.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import type {
  CompanyWatchCriterion,
  CriterionReference,
} from "@/lib/dto/company-criteria";
import { CriterionRow } from "./criterion-row";
import { CriterionDialog } from "./criterion-dialog";

// The server hard-caps at 20 criteria per user (409 beyond it). The button disables at the cap and
// says why, rather than letting the user compose a criterion the save will reject.
const MAX_PER_USER = 20;

interface CriteriaSectionProps {
  readonly items: ReadonlyArray<CompanyWatchCriterion>;
  readonly reference: CriterionReference;
}

/**
 * #560 PR-3 — the "Branschbevakningar" section body on `/foretag/branschbevakningar`: the user's
 * criteria (max 20) plus a "Ny branschbevakning" button that opens the create dialog. A civic empty
 * state names what a branschbevakning is and how to make one.
 *
 * <p>#1703 — this level owns the two facts a single row cannot know: how many rows there are, and
 * whether any of them is refused. Both feed the advice beneath the list.</p>
 */
export function CriteriaSection({ items, reference }: CriteriaSectionProps) {
  const t = useTranslations("pages.foretag.criteria");
  const [createOpen, setCreateOpen] = useState(false);

  // Stated once for the whole list when ANY row is refused, never once per row. Both arms count:
  // the ads arm is what the rows show, and a matching-only refusal is still a watch the advice would
  // help. Parity `criteria-summary.tsx`, deliberately not extracted — see the prop's docblock.
  const anyTooBroad = items.some((i) => i.ads.tooBroad || i.matching.tooBroad);

  // ONE value, gating both the rows' short refusal and this section's advice line below. The rule it
  // encodes has a single home in `CriterionAdLines`' own prop docblock; this is a pointer, not a
  // restatement (#1173).
  const adviceStatedByCaller = items.length > 1;

  const atMax = items.length >= MAX_PER_USER;
  // A degraded reference load (empty tree) means the picker has nothing to offer — disable creating
  // and say so, rather than opening a dialog that can never be saved.
  const referenceAvailable = reference.sni.length > 0 && reference.lan.length > 0;
  const canCreate = !atMax && referenceAvailable;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <p className="max-w-prose text-body-sm text-text-primary">{t("lede")}</p>
        <Button type="button" onClick={() => setCreateOpen(true)} disabled={!canCreate}>
          <Plus size={16} aria-hidden="true" />
          {t("newButton")}
        </Button>
      </div>

      {atMax && (
        <p className="text-body-sm text-text-primary">
          {t("maxReached", { max: MAX_PER_USER })}
        </p>
      )}
      {!referenceAvailable && (
        <p role="alert" className="text-body-sm text-text-primary">
          {t("referenceUnavailable")}
        </p>
      )}

      {items.length === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          <p className="jp-empty__body text-body-sm text-text-primary">{t("emptyBody")}</p>
        </div>
      ) : (
        <>
          <ul className="jp-jobs" aria-label={t("listLabel")}>
            {items.map((item) => (
              <CriterionRow
                key={item.id}
                item={item}
                reference={reference}
                adviceStatedByCaller={adviceStatedByCaller}
              />
            ))}
          </ul>
          {/* The rows state the STATUS; this states what to do about it — and it carries no link,
              because the action is the "Ändra" button in every row rather than a page to travel to
              (senior-cto-advisor D2, 2026-09-08). Deliberately NOT the `/oversikt` block's sentence:
              that one rewords the refusal the rows already carry above it, and its "här" points at
              another block. */}
          {anyTooBroad && adviceStatedByCaller && (
            <p className="jp-matchline jp-criteria-advice">{t("ads.tooBroadAdviceOnList")}</p>
          )}
        </>
      )}

      {createOpen && (
        <CriterionDialog
          open={createOpen}
          onOpenChange={setCreateOpen}
          reference={reference}
        />
      )}
    </div>
  );
}
