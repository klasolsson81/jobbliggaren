"use client";

import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { batchTransitionAction } from "@/lib/actions/applications";
import { showApplicationToast } from "@/lib/applications/toast-store";
import { applicationStatusLabel } from "@/lib/applications/status";
import {
  compareApplications,
  type SortDir,
  type TableSortKey,
} from "@/lib/applications/table-sort";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";
import { ApplicationsTableRow } from "./applications-table-row";
import { ApplicationsBulkBar } from "./applications-bulk-bar";
import { ApplicationsPager } from "./applications-pager";

const PAGE_SIZE = 50;

interface ApplicationsTableProps {
  rows: ApplicationDto[];
  /** Server-beräknad referenstidpunkt (#336-determinism), trädad ned per rad. */
  now: Date;
}

/**
 * Tabell-vyn (#630 PR 10, design §7, ADR 0092 D1) — volymvyn över SAMMA redan
 * hämtade array som Lista/Tavla (Option B, CTO-bind 2026-07-10): sortering,
 * paginering och urval sker helt klient-side, INGEN ny data-hämtning.
 *
 * Urvalet är EFEMÄRT och sidbundet (CTO-bind): rubrik-checkboxen markerar bara
 * den aktuella sidans 50 rader, och urvalet NOLLSTÄLLS vid sid-, sök-, filter-
 * och sorteringsbyte (eliminerar dold-rad-bulk-faran). Bulk-målet Nekad
 * (destruktivt) grindas av en bekräftelsedialog; Inget svar (Ghosted) körs direkt
 * med ångra-toast. Bägge går via EN atomär `batchTransitionAction` (PR 9) och
 * publicerar `statusChangeBatch`-toasten (grupp-ångra, ADR 0092 D3).
 */
export function ApplicationsTable({ rows, now }: ApplicationsTableProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  const [selectedIds, setSelectedIds] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const [sortKey, setSortKey] = useState<TableSortKey>("days");
  const [sortDir, setSortDir] = useState<SortDir>("desc");
  const [page, setPage] = useState(1);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [bulkPending, startBulk] = useTransition();

  const captionRef = useRef<HTMLTableCaptionElement>(null);
  const selectAllRef = useRef<HTMLInputElement>(null);

  // Efemärt urval: en ändring av radmängden (sök/filter → föräldern skickar en
  // ny `rows`) nollställer urvalet och backar till sida 1. `rowsKey` är radernas
  // id-mängd som en stabil primitiv sträng (arrayens identitet ändras varje
  // render; strängvärdet gör innehålls-jämförelsen). Reset:et sker SYNKRONT
  // under render via prev-jämförelse (Reacts "adjusting state during render"-
  // mönster) — ingen effekt, ingen flash av inaktuellt urval.
  const rowsKey = useMemo(() => rows.map((r) => r.id).join(","), [rows]);
  const [prevRowsKey, setPrevRowsKey] = useState(rowsKey);
  if (prevRowsKey !== rowsKey) {
    setPrevRowsKey(rowsKey);
    setSelectedIds(new Set());
    setPage(1);
  }

  const sorted = useMemo(
    () => [...rows].sort((a, b) => compareApplications(a, b, sortKey, sortDir, now)),
    [rows, sortKey, sortDir, now],
  );

  const totalPages = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const pageRows = sorted.slice(
    (currentPage - 1) * PAGE_SIZE,
    currentPage * PAGE_SIZE,
  );

  const pageIds = pageRows.map((r) => r.id);
  const selectedRows = pageRows.filter((r) => selectedIds.has(r.id));
  const allSelected = pageIds.length > 0 && selectedRows.length === pageIds.length;
  const someSelected = selectedRows.length > 0 && !allSelected;

  useEffect(() => {
    if (selectAllRef.current) selectAllRef.current.indeterminate = someSelected;
  }, [someSelected]);

  const toggleRow = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleAll = () => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (allSelected) for (const id of pageIds) next.delete(id);
      else for (const id of pageIds) next.add(id);
      return next;
    });
  };

  const handleSort = (col: TableSortKey) => {
    setSelectedIds(new Set());
    if (sortKey === col) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortKey(col);
      // Vettig default-riktning: "I steget" desc (längst väntan överst, §7),
      // Roll/Status stigande (A→Ö respektive pipeline-början → slut).
      setSortDir(col === "days" ? "desc" : "asc");
    }
  };

  const handlePageChange = (next: number) => {
    setSelectedIds(new Set());
    setPage(next);
  };

  const runBulk = (target: ApplicationStatus) => {
    const selected = pageRows.filter((r) => selectedIds.has(r.id));
    if (selected.length === 0) return;
    const count = selected.length;
    startBulk(async () => {
      const result = await batchTransitionAction(
        selected.map((a) => ({ applicationId: a.id, targetStatus: target })),
      );
      if (result.success) {
        showApplicationToast({
          kind: "statusChangeBatch",
          count,
          to: target,
          items: selected.map((a) => ({ applicationId: a.id, from: a.status })),
        });
        setSelectedIds(new Set());
        setConfirmOpen(false);
        // Fokus till en stabil punkt när bulk-raden avmonteras (urvalet tömt):
        // tabellens caption-region (WCAG 2.4.3 — fokus försvinner aldrig).
        captionRef.current?.focus();
      } else {
        showApplicationToast({ kind: "error", message: result.error });
        setConfirmOpen(false);
      }
    });
  };

  const rejectedLabel = applicationStatusLabel(t, "Rejected");

  return (
    <div className="jp-apptable-wrap">
      {selectedRows.length > 0 && (
        <ApplicationsBulkBar
          count={selectedRows.length}
          onMarkGhosted={() => runBulk("Ghosted")}
          onMarkRejected={() => setConfirmOpen(true)}
          onClear={() => setSelectedIds(new Set())}
          pending={bulkPending}
        />
      )}

      {rows.length === 0 ? (
        <div className="jp-apptable__empty" role="status">
          <p>{tUi("table.empty")}</p>
        </div>
      ) : (
        <>
          <div className="jp-apptable__scroll">
            <table className="jp-apptable" aria-label={tUi("table.ariaLabel")}>
              <caption ref={captionRef} tabIndex={-1} className="sr-only">
                {tUi("table.caption")}
              </caption>
              <colgroup>
                <col className="jp-apptable__col--check" />
                <col className="jp-apptable__col--role" />
                <col className="jp-apptable__col--status" />
                <col className="jp-apptable__col--step" />
                <col className="jp-apptable__col--event" />
                <col className="jp-apptable__col--next" />
              </colgroup>
              <thead>
                <tr>
                  <th scope="col" className="jp-apptable__th jp-apptable__th--check">
                    <input
                      ref={selectAllRef}
                      type="checkbox"
                      className="jp-apptable__check"
                      checked={allSelected}
                      onChange={toggleAll}
                      disabled={pageIds.length === 0}
                      aria-label={tUi("table.selectAllAriaLabel")}
                    />
                  </th>
                  <SortableHeader
                    col="role"
                    label={tUi("table.colRole")}
                    sortKey={sortKey}
                    sortDir={sortDir}
                    onSort={handleSort}
                  />
                  <SortableHeader
                    col="status"
                    label={tUi("table.colStatus")}
                    sortKey={sortKey}
                    sortDir={sortDir}
                    onSort={handleSort}
                  />
                  <SortableHeader
                    col="days"
                    label={tUi("table.colStep")}
                    sortKey={sortKey}
                    sortDir={sortDir}
                    onSort={handleSort}
                  />
                  <th scope="col" className="jp-apptable__th">
                    {tUi("table.colLastEvent")}
                  </th>
                  <th scope="col" className="jp-apptable__th">
                    {tUi("table.colNextStep")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((application) => (
                  <ApplicationsTableRow
                    key={application.id}
                    application={application}
                    now={now}
                    selected={selectedIds.has(application.id)}
                    onToggleSelect={toggleRow}
                  />
                ))}
              </tbody>
            </table>
          </div>

          <div className="jp-apptable__footer">
            <p className="jp-apptable__summary">
              {tUi("table.footerSummary", {
                shown: pageRows.length,
                total: sorted.length,
              })}
            </p>
            <ApplicationsPager
              page={currentPage}
              totalPages={totalPages}
              onPageChange={handlePageChange}
            />
          </div>
        </>
      )}

      {/* Guarden `selectedRows.length > 0`: en bakgrunds-revalidate som tömmer
          urvalet medan dialogen är öppen får aldrig lämna en "0 ansökningar"-
          dialog kvar (code-reviewer Minor 1). */}
      <Dialog
        open={confirmOpen && selectedRows.length > 0}
        onOpenChange={(open) => {
          if (!open) setConfirmOpen(false);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {tUi("bulk.confirmTitle", {
                count: selectedRows.length,
                status: rejectedLabel,
              })}
            </DialogTitle>
            <DialogDescription>
              {tUi("bulk.confirmBody", { count: selectedRows.length })}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={bulkPending}
              onClick={() => setConfirmOpen(false)}
            >
              {tUi("common.cancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={bulkPending}
              onClick={() => runBulk("Rejected")}
            >
              {bulkPending
                ? tUi("bulk.confirming")
                : tUi("bulk.confirmCta", { status: rejectedLabel })}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

interface SortableHeaderProps {
  col: TableSortKey;
  label: string;
  sortKey: TableSortKey;
  sortDir: SortDir;
  onSort: (col: TableSortKey) => void;
}

/**
 * Sorterbart kolumnhuvud (design §7): `<th aria-sort>` + `<button>`-växlare
 * (native tangentbord). Endast en kolumn är någonsin ≠ "none". Pil-glyfen är
 * `aria-hidden`; sorterings-tillståndet annonseras via `aria-sort` PLUS en
 * sr-only riktnings-etikett (aria-sort-stöd varierar mellan skärmläsare).
 */
function SortableHeader({
  col,
  label,
  sortKey,
  sortDir,
  onSort,
}: SortableHeaderProps) {
  const tUi = useTranslations("applications.ui");
  const active = sortKey === col;
  const ariaSort: "ascending" | "descending" | "none" = active
    ? sortDir === "asc"
      ? "ascending"
      : "descending"
    : "none";

  return (
    <th
      scope="col"
      className="jp-apptable__th jp-apptable__th--sortable"
      aria-sort={ariaSort}
    >
      <button
        type="button"
        className="jp-apptable__sortbtn"
        data-active={active || undefined}
        aria-label={tUi("table.sortAriaLabel", { column: label })}
        onClick={() => onSort(col)}
      >
        <span>{label}</span>
        {active && (
          <span className="jp-apptable__sortarrow" aria-hidden="true">
            {sortDir === "asc" ? "▲" : "▼"}
          </span>
        )}
      </button>
      {active && (
        <span className="sr-only">
          {sortDir === "asc"
            ? tUi("table.sortedAscending")
            : tUi("table.sortedDescending")}
        </span>
      )}
    </th>
  );
}
