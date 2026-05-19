"use client";

import { useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { ChevronDown } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import {
  JobbFilterPopover,
  type PopoverGroup,
  type PopoverItem,
} from "./jobb-filter-popover";

/**
 * Hero-filter-pills + Platsbanken-popovers (HANDOVER-v3.md §5.4/§5.5,
 * ADR 0055 + amendment 2026-05-19). Client-island under hero-sökrutan:
 * `Ort ▾` (enkelkolumns Län) · `Yrke ▾` (tvåkolumns Yrkesområde→Yrken).
 * INGEN Filter-pill (deferred helt — amendment).
 *
 * RSC→client-kontrakt: tar serialiserbara props (taxonomy-träd, valda
 * conceptId string[], q, sortBy, pageSize) från jobb/page.tsx (RSC).
 * Live-commit: varje markering → `router.push` i `useTransition`, övriga
 * params bevaras symmetriskt via `buildJobbHref` (ADR 0042 Beslut B,
 * OFÖRÄNDRAT; samma param-bevarande-disciplin som F3 B-FIX).
 */

interface JobbHeroFiltersProps {
  taxonomy: TaxonomyTree | null;
  initialSsyk: ReadonlyArray<string>;
  initialRegion: ReadonlyArray<string>;
  /** Hero-sökordet — bärs vidare så filter-klick inte raderar q. */
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

type OpenPop = "ort" | "yrke" | null;

export function JobbHeroFilters({
  taxonomy,
  initialSsyk,
  initialRegion,
  q,
  sortBy,
  pageSize,
}: JobbHeroFiltersProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();

  const [ssyk, setSsyk] = useState<string[]>([...initialSsyk]);
  const [region, setRegion] = useState<string[]>([...initialRegion]);
  const [openPop, setOpenPop] = useState<OpenPop>(null);

  const ortBtnRef = useRef<HTMLButtonElement>(null);
  const yrkeBtnRef = useRef<HTMLButtonElement>(null);

  // Taxonomi → popover-form. Län = enkelkolumns items; Yrkesområde→Yrken
  // = tvåkolumns grupper.
  const regionItems: PopoverItem[] = (taxonomy?.regions ?? []).map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
  }));
  const occupationGroups: PopoverGroup[] = (
    taxonomy?.occupationFields ?? []
  ).map((f) => ({
    conceptId: f.conceptId,
    label: f.label,
    items: f.occupations.map((o) => ({
      conceptId: o.conceptId,
      label: o.label,
    })),
  }));

  function push(nextSsyk: string[], nextRegion: string[]) {
    startTransition(() => {
      router.push(
        buildJobbHref({
          q,
          ssyk: nextSsyk,
          region: nextRegion,
          sortBy,
          pageSize,
        }),
      );
    });
  }

  function changeSsyk(next: string[]) {
    setSsyk(next);
    push(next, region);
  }
  function changeRegion(next: string[]) {
    setRegion(next);
    push(ssyk, next);
  }

  return (
    <div className="jp-hero__pills">
      <button
        ref={ortBtnRef}
        type="button"
        className="jp-hero-pill"
        data-active={openPop === "ort" || region.length > 0}
        aria-haspopup="dialog"
        aria-expanded={openPop === "ort"}
        onClick={() => setOpenPop(openPop === "ort" ? null : "ort")}
      >
        {region.length > 0 && (
          <span className="jp-hero-pill__dot" aria-hidden="true" />
        )}
        Ort
        {region.length > 0 && (
          <span className="jp-hero-pill__count">{region.length}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      <button
        ref={yrkeBtnRef}
        type="button"
        className="jp-hero-pill"
        data-active={openPop === "yrke" || ssyk.length > 0}
        aria-haspopup="dialog"
        aria-expanded={openPop === "yrke"}
        onClick={() => setOpenPop(openPop === "yrke" ? null : "yrke")}
      >
        {ssyk.length > 0 && (
          <span className="jp-hero-pill__dot" aria-hidden="true" />
        )}
        Yrke
        {ssyk.length > 0 && (
          <span className="jp-hero-pill__count">{ssyk.length}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      <JobbFilterPopover
        mode="single-column"
        open={openPop === "ort"}
        title="Län"
        selectAllLabel="Välj alla län"
        items={regionItems}
        selected={region}
        onChange={changeRegion}
        onClose={() => setOpenPop(null)}
        onClearAll={() => changeRegion([])}
        triggerRef={ortBtnRef}
      />

      {/* key-remount vid öppning → activeLeft re-initieras till första
          yrkesområdet (src-v3 FilterPopover-paritet) utan setState-i-effect. */}
      <JobbFilterPopover
        key={openPop === "yrke" ? "yrke-open" : "yrke-closed"}
        mode="two-column"
        open={openPop === "yrke"}
        leftTitle="Yrkesområde"
        rightTitle="Yrken"
        selectAllLabel="Välj alla yrken"
        groups={occupationGroups}
        selected={ssyk}
        onChange={changeSsyk}
        onClose={() => setOpenPop(null)}
        onClearAll={() => changeSsyk([])}
        triggerRef={yrkeBtnRef}
      />
    </div>
  );
}
