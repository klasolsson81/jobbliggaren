"use client";

// "use client": each row owns its edit-dialog + delete-confirm open state and a useTransition around
// the delete action. The RSC page fetches the criteria and the reference tree and passes them down;
// no data fetching happens here.

import { useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Trash2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { deriveDisplayLabel } from "@/lib/company-criteria/display-label";
import { deleteCriterionAction } from "@/lib/actions/company-criteria";
import type {
  CompanyWatchCriterion,
  CriterionReference,
} from "@/lib/dto/company-criteria";
import { CriterionDialog } from "./criterion-dialog";

// The middle-dot separator is a layout glyph (parity with the audit-log ` · ` cells), not copy — it
// joins the derived-label axes and the count summary.
const SEPARATOR = " · ";

interface CriterionRowProps {
  readonly item: CompanyWatchCriterion;
  readonly reference: CriterionReference;
}

/**
 * #560 PR-3 — one "smart bevakning" row. The headline is the user's own label when set, else a label
 * derived from the codes via the reference tree, else a neutral fallback. A compact count summary
 * ("3 branscher · 2 kommuner") sits below. Actions: open the register browse (a link), edit (the
 * dialog), delete (a confirm dialog). Delete drives row removal through `revalidatePath` (server
 * state, no optimistic copy); on failure the row stays and shows the error inline.
 */
export function CriterionRow({ item, reference }: CriterionRowProps) {
  const t = useTranslations("pages.foretag.criteria");
  const [editOpen, setEditOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [isDeleting, startDeleting] = useTransition();
  const [error, setError] = useState<string | null>(null);

  const derived = deriveDisplayLabel(item.sniCodes, item.municipalityCodes, reference, {
    moreSuffix: t("moreSuffix"),
    separator: SEPARATOR,
  });
  const userLabel = item.label?.trim() ?? "";
  const heading = userLabel.length > 0 ? userLabel : (derived ?? t("row.untitled"));

  const summary = `${t("row.branschCount", { count: item.sniCodes.length })}${SEPARATOR}${t(
    "row.kommunCount",
    { count: item.municipalityCodes.length },
  )}`;

  function handleDelete() {
    setError(null);
    startDeleting(async () => {
      const result = await deleteCriterionAction(item.id);
      if (!result.success) {
        setError(result.error);
        return;
      }
      // Close BEFORE the revalidate lands (#141) — the RSC re-render drops this row.
      setConfirmOpen(false);
    });
  }

  return (
    <li>
      <article
        className="jp-job jp-job--static"
        style={{ gridTemplateColumns: "1fr auto" }}
      >
        <div className="jp-job__body">
          <h3 className="jp-job__title">{heading}</h3>
          <div className="jp-job__meta">
            <span className="tabular-nums">{summary}</span>
          </div>
          {error && (
            <p role="alert" className="mt-2 text-body-sm text-danger-700">
              {error}
            </p>
          )}
        </div>

        <div
          className="jp-job__actions"
          style={{ flexDirection: "row", alignItems: "center" }}
        >
          <Link
            href={`/foretag/smarta-bevakningar/${item.id}`}
            className="jp-rowbtn"
            aria-label={t("row.openBrowseAria", { label: heading })}
          >
            {t("row.openBrowse")}
          </Link>
          <button
            type="button"
            className="jp-rowbtn"
            aria-label={t("row.editAria", { label: heading })}
            onClick={() => setEditOpen(true)}
          >
            {t("row.edit")}
          </button>
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={t("row.deleteAria", { label: heading })}
            onClick={() => setConfirmOpen(true)}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>

      {/* Mounted only when opened; `key` on updatedAt remounts after a save so the draft can never
          show a stale predicate. */}
      {editOpen && (
        <CriterionDialog
          key={item.updatedAt}
          open={editOpen}
          onOpenChange={setEditOpen}
          criterion={item}
          reference={reference}
        />
      )}

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("row.deleteConfirmTitle")}</DialogTitle>
            <DialogDescription className="text-text-primary">
              {t("row.deleteConfirmBody", { label: heading })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              onClick={() => setConfirmOpen(false)}
              disabled={isDeleting}
            >
              {t("row.deleteCancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={handleDelete}
              disabled={isDeleting}
            >
              {isDeleting ? t("row.deleting") : t("row.deleteConfirm")}
            </Button>
          </DialogFooter>
          {error && (
            <p role="alert" className="text-body-sm text-danger-700">
              {error}
            </p>
          )}
        </DialogContent>
      </Dialog>
    </li>
  );
}
