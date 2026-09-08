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
import { CriterionAdLines } from "./criterion-ad-lines";
import { CriterionBreadth } from "./criterion-breadth";
import { CriterionDialog } from "./criterion-dialog";

// The middle-dot separator is a layout glyph (parity with the audit-log ` · ` cells), not copy — it
// joins the derived-label axes.
const SEPARATOR = " · ";

interface CriterionRowProps {
  readonly item: CompanyWatchCriterion;
  readonly reference: CriterionReference;
  /**
   * Threaded from `CriteriaSection`, which is the only level that can count the rows, and passed on
   * to {@link CriterionAdLines} under the same name at every hop. The rule it encodes lives in that
   * component's own prop docblock and is deliberately not restated here.
   */
  readonly adviceStatedByCaller: boolean;
}

/**
 * #560 PR-3 — one "branschbevakning" row. The headline is the user's own label when set, else a label
 * derived from the codes via the reference tree, else a neutral fallback. A compact count summary
 * ("3 branscher · 2 kommuner") sits below, then the watch's two ad numbers and every honest way of
 * not having them. Actions: open the register browse (a link), edit (the dialog), delete (a confirm
 * dialog). Delete drives row removal through `revalidatePath` (server state, no optimistic copy); on
 * failure the row stays and shows the error inline.
 *
 * <p><b>#1703 — the numbers are read, not rebuilt.</b> The route already pays the full per-user
 * grading `#1681` part 2 put on `GET /me/company-watch-criteria`, and until this delta the row
 * rendered none of it: `/oversikt` linked here and answered more than the page it linked to. The
 * ladder is {@link CriterionAdLines}, of which this row is the THIRD consumer — the criterion detail
 * page and `CriteriaSummary` are the other two — because a copy of seven honesty branches is how
 * three surfaces come to disagree about one watch (ADR 0139, "Båda ytorna läser samma källa").</p>
 */
export function CriterionRow({ item, reference, adviceStatedByCaller }: CriterionRowProps) {
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
            {/* The line this row already carried, now READ from the shared component rather than
                built here: three more surfaces need the same sentence, and four inline copies is
                how they come to disagree (design-reviewer B-1/B-3, 2026-09-08). The wrapper stays,
                and the rendering is unchanged — `.jp-job__meta` already sets the same 14px and
                ink-1 the component's own rule sets, and `tabular-nums` moved from a utility into
                that rule. */}
            <CriterionBreadth
              sniCodes={item.sniCodes}
              municipalityCodes={item.municipalityCodes}
            />
          </div>
          {/* Beneath the breadth, never above it: the breadth DEFINES the predicate and these
              numbers are its OUTCOME, which is the reading order design-reviewer bound for the two
              detail surfaces (B-3, 2026-09-08). The row's own `.jp-job__meta` wrapper is unchanged;
              `.jp-matchline` inside `.jp-job__body` is already delivered grammar on the sibling
              catalogue (`company-watch-row.tsx`), so no new row-level CSS is owed.

              `ads` and `matching` are non-nullable on a list row (`companyWatchCriterionSchema`) —
              the degraded-read arm belongs to the detail page, which reads them separately. Here a
              failed list read is the whole section's error notice, not a row. */}
          <CriterionAdLines
            criterionId={item.id}
            ads={item.ads}
            matching={item.matching}
            /* No company is rendered in this row — they sit behind "Visa företag" — so an ad label
               must carry its own antecedent. */
            variant="standalone"
            adviceStatedByCaller={adviceStatedByCaller}
            /* TRUE here and nowhere else: this row renders the "Ändra" control itself, so the
               too-broad CTA would name the page the reader is standing on, in the same Swedish word
               as a button beside it. The row is the only place that can state this. */
            actionOfferedByCaller={true}
          />
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
            href={`/foretag/branschbevakningar/${item.id}`}
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
