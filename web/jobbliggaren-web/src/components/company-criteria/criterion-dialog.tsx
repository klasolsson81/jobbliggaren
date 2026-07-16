"use client";

// "use client": the dialog holds DRAFT selection state (two leaf-code Sets + the label), a live
// preview subscription, and a useTransition around the save action. None of this runs in a Server
// Component. The draft is committed atomically with "Spara bevakning".

import { useId, useMemo, useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CriterionPicker } from "./criterion-picker";
import type { CriterionTreeNode } from "./criterion-tree";
import { toggleGroup } from "@/lib/company-criteria/criterion-selection";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { useCriterionPreviewCount } from "@/lib/hooks/use-criterion-preview-count";
import {
  createCriterionAction,
  updateCriterionAction,
} from "@/lib/actions/company-criteria";
import type {
  CriterionReference,
  CompanyWatchCriterion,
} from "@/lib/dto/company-criteria";

const LABEL_MAX_LENGTH = 120;

interface CriterionDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  /** Absent = create; present = edit that criterion (the draft seeds from its codes + label). */
  readonly criterion?: CompanyWatchCriterion;
  /** The SCB reference tree the pickers render. An empty tree (degraded load) shows civil notices. */
  readonly reference: CriterionReference;
}

/**
 * #560 PR-3 — create/edit a criteria-based company watch. Two pickers (SNI branches, kommuner), a live
 * magnitude preview at the footer, and an optional name. The wire contract is LEAVES ONLY: selection
 * state is two Sets of leaf codes; picking a section/division/whole-län EXPANDS to its leaves via the
 * shared `toggleGroup`. Save closes the dialog BEFORE the revalidate lands (the #141 trap) so the RSC
 * re-render never unmounts the open dialog mid-flow.
 */
export function CriterionDialog({
  open,
  onOpenChange,
  criterion,
  reference,
}: CriterionDialogProps) {
  const t = useTranslations("pages.foretag.criteria.dialog");
  const format = useFormatter();
  const labelId = useId();
  const labelHintId = useId();

  const isEdit = criterion !== undefined;

  // ── Build the picker trees + flat leaf lists from the reference (per open) ──
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
        s.divisions.flatMap((d) => d.leaves.map((l) => ({ code: l.code, name: l.name }))),
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
    () => reference.lan.flatMap((l) => l.kommuner.map((k) => ({ code: k.code, name: k.name }))),
    [reference],
  );

  // ── Draft state (seeded from the edited criterion, or empty for create) ─────
  const [sniSelected, setSniSelected] = useState<ReadonlySet<string>>(
    () => new Set(criterion?.sniCodes ?? []),
  );
  const [kommunSelected, setKommunSelected] = useState<ReadonlySet<string>>(
    () => new Set(criterion?.municipalityCodes ?? []),
  );
  const [label, setLabel] = useState(criterion?.label ?? "");
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);

  const sniCodes = useMemo(() => [...sniSelected], [sniSelected]);
  const kommunCodes = useMemo(() => [...kommunSelected], [kommunSelected]);
  const bothChosen = sniSelected.size > 0 && kommunSelected.size > 0;

  // Live preview — only polls while the dialog is open AND both axes are chosen (the endpoint 400s a
  // missing axis; the hook enforces that too).
  const { preview, loading: previewLoading } = useCriterionPreviewCount(
    { sniCodes, municipalityCodes: kommunCodes },
    open,
  );

  // Four states, honestly told apart (design-review Minor 1, 2026-07-16): axes incomplete →
  // instruction; result → the count; in flight → loading; failed/degraded (preview null, nothing
  // in flight) → an explicit "unavailable" line. Falling back to the loading string on failure
  // would show "Räknar företag…" forever — a promise the UI cannot keep.
  const previewLine = !bothChosen
    ? t("previewIncomplete")
    : preview
      ? t("previewChosen", { count: formatMagnitude(format, preview) })
      : previewLoading
        ? t("previewLoading")
        : t("previewUnavailable");

  function handleSave() {
    setSaveError(null);
    startSaving(async () => {
      const input = {
        sniCodes: [...sniSelected],
        municipalityCodes: [...kommunSelected],
        label,
      };
      const result = isEdit
        ? await updateCriterionAction(criterion.id, input)
        : await createCriterionAction(input);

      if (!result.success) {
        // The error lives in the dialog, which stays open — no revalidate, no lost draft.
        setSaveError(result.error);
        return;
      }
      // Close BEFORE the revalidate lands (#141): a Server Action re-rendering the RSC tree unmounts
      // an open dialog mid-flow. Focus returns to the trigger (Radix).
      onOpenChange(false);
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="jp-matchdialog">
        <div className="jp-matchdialog__head">
          <DialogTitle className="jp-matchdialog__title">
            {isEdit ? t("editTitle") : t("createTitle")}
          </DialogTitle>
          <DialogDescription className="jp-matchdialog__intro">
            {t("intro")}
          </DialogDescription>
        </div>

        <div className="jp-matchdialog__body flex flex-col gap-6">
          <CriterionPicker
            nodes={sniNodes}
            leaves={sniLeaves}
            selected={sniSelected}
            onToggle={(codes) => setSniSelected((prev) => toggleGroup(prev, codes))}
            onClear={() => setSniSelected(new Set())}
            heading={t("sniHeading")}
            help={t("sniHelp")}
            filterLabel={t("sniFilterLabel")}
            filterHint={t("sniFilterHint")}
            groupAria={t("sniGroupAria")}
            selectedCountLabel={t("sniSelectedCount", { count: sniSelected.size })}
            optionsUnavailable={t("optionsUnavailable")}
          />

          <CriterionPicker
            nodes={kommunNodes}
            leaves={kommunLeaves}
            selected={kommunSelected}
            onToggle={(codes) => setKommunSelected((prev) => toggleGroup(prev, codes))}
            onClear={() => setKommunSelected(new Set())}
            heading={t("kommunHeading")}
            help={t("kommunHelp")}
            filterLabel={t("kommunFilterLabel")}
            filterHint={t("kommunFilterHint")}
            groupAria={t("kommunGroupAria")}
            selectedCountLabel={t("kommunSelectedCount", { count: kommunSelected.size })}
            optionsUnavailable={t("optionsUnavailable")}
          />

          <div className="flex flex-col gap-1.5">
            <Label htmlFor={labelId}>{t("labelLabel")}</Label>
            <Input
              id={labelId}
              type="text"
              value={label}
              maxLength={LABEL_MAX_LENGTH}
              aria-describedby={labelHintId}
              onChange={(e) => setLabel(e.target.value)}
            />
            <p id={labelHintId} className="text-body-sm text-text-primary">
              {t("labelHint")}
            </p>
          </div>
        </div>

        <div className="jp-matchdialog__foot flex flex-col gap-2">
          <p
            className="text-body-sm text-text-primary tabular-nums"
            aria-live="polite"
          >
            {previewLine}
          </p>
          <div className="flex items-center gap-3">
            <Button type="button" onClick={handleSave} disabled={!bothChosen || isSaving}>
              {isSaving ? t("saving") : t("save")}
            </Button>
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
              disabled={isSaving}
            >
              {t("cancel")}
            </Button>
          </div>
          {saveError && (
            <p role="alert" className="text-body-sm text-danger-700">
              {saveError}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
