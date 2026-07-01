"use client";

// #452 — the hub-level "matchande / alla annonser" toggle needs client state (useState) shared
// across every row, so the list wrapper is a Client Component. Its children rows are already client
// (per-row unfollow), so this promotes no new server logic to the client; the RSC page still does all
// data fetching and passes the fetched rows down.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Segment } from "@/components/ui/segment";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import { CompanyWatchRow, type CompanyWatchViewMode } from "./company-watch-row";

interface CompanyWatchListViewProps {
  items: ReadonlyArray<CompanyWatch>;
}

/**
 * #311 #452 — the interactive list on `/foretag`. Holds the view-mode the segmented control governs
 * and passes it to each row:
 *  - `matching` (DEFAULT) — the per-company "X matchande annonser" signal is emphasised; if the user
 *    has stated no occupation the row shows an honest not-assessed nudge instead of a false "0".
 *  - `all` — emphasises the public "X aktiva annonser just nu" count (#447) for every company.
 *
 * Both counts already travel in the DTO, so the toggle is purely presentational — no refetch. Default
 * is the matching view because that is the valuable signal (which followed companies are hiring roles
 * that fit you), matching the /jobb default of leading with match.
 */
export function CompanyWatchListView({ items }: CompanyWatchListViewProps) {
  const t = useTranslations("jobads.companyWatches");
  const [mode, setMode] = useState<CompanyWatchViewMode>("matching");

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Segment<CompanyWatchViewMode>
          value={mode}
          onChange={setMode}
          aria-label={t("view.groupLabel")}
          options={[
            { value: "matching", label: t("view.matching") },
            { value: "all", label: t("view.all") },
          ]}
        />
      </div>
      <ul className="jp-jobs" aria-label={t("listLabel")}>
        {items.map((item) => (
          <CompanyWatchRow key={item.id} item={item} mode={mode} />
        ))}
      </ul>
    </div>
  );
}
