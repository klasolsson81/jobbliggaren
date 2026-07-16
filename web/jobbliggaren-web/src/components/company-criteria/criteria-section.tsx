"use client";

// "use client": owns the "create dialog" open state + the "Ny bevakning" trigger. The rows and their
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
 * #560 PR-3 — the "Smarta bevakningar" section body on `/foretag`: the user's criteria (max 20) plus a
 * "Ny bevakning" button that opens the create dialog. A civic empty state names what a smart bevakning
 * is and how to make one.
 */
export function CriteriaSection({ items, reference }: CriteriaSectionProps) {
  const t = useTranslations("pages.foretag.criteria");
  const [createOpen, setCreateOpen] = useState(false);

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
          <p className="text-body-sm text-text-primary">{t("emptyBody")}</p>
        </div>
      ) : (
        <ul className="jp-jobs" aria-label={t("listLabel")}>
          {items.map((item) => (
            <CriterionRow key={item.id} item={item} reference={reference} />
          ))}
        </ul>
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
