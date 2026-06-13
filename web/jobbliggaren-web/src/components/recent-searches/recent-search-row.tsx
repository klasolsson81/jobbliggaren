"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { Clock, Search, Trash2 } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import { deleteRecentSearchAction } from "@/lib/actions/recent-searches";

interface RecentSearchRowProps {
  item: RecentJobSearchDto;
  onDeleted: (id: string) => void;
  onDeleteFailed: (id: string, error: string) => void;
}

function buildHrefFor(item: RecentJobSearchDto): string {
  return buildJobbHref({
    q: item.q ?? "",
    occupationGroup: item.occupationGroupList,
    region: item.regionList,
    municipality: item.municipalityList,
    // Klass 2 (ADR 0067 B2) — replay bär anställningsform/omfattning så
    // "Kör igen" inte tyst tappar filtret (backend-DTO bär listorna sedan #60).
    employmentType: item.employmentTypeList,
    worktimeExtent: item.worktimeExtentList,
    sortBy: item.sortBy,
  });
}

// Klas-direktiv 2026-05-20 (anti-AI-trope): INGEN "NY"-pill på raden.
//
// Träffräknaren ("(N) träffar" / "varav (M) nya") är TILLFÄLLIGT borttagen:
// `currentCount`/`newCount` är 0 så länge listan hämtas med `includeCount=false`
// (slow N+1-COUNT, TD-94). En synlig "(0) träffar" vore desinformation (husets
// degraderingskontrakt) — hellre ingen siffra. Återinförs via lat klient-
// hämtning (CTO-beslut 2026-06-13, samma mönster som useFacetCounts).

export function RecentSearchRow({ item, onDeleted, onDeleteFailed }: RecentSearchRowProps) {
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const href = buildHrefFor(item);

  function handleRowClick(e: React.MouseEvent<HTMLElement>) {
    // Skippa när klick var på en knapp/länk inuti raden — de bär egna handlers.
    const target = e.target as Element;
    if (target.closest("button, a")) return;
    router.push(href);
  }

  function handleDelete() {
    startTransition(async () => {
      const result = await deleteRecentSearchAction(item.id);
      if (result.success) {
        onDeleted(item.id);
      } else {
        onDeleteFailed(item.id, result.error);
      }
    });
  }

  return (
    <li>
      <article
        className="jp-job"
        style={{ gridTemplateColumns: "auto 1fr auto", cursor: "pointer" }}
        onClick={handleRowClick}
      >
        <div
          className="jp-job__match"
          style={{
            background: "var(--jp-surface-3)",
            borderColor: "var(--jp-border)",
            color: "var(--jp-ink-2)",
          }}
          aria-hidden="true"
        >
          <Clock size={20} />
        </div>
        <div className="jp-job__body">
          <h3 className="jp-job__title">{item.label}</h3>
        </div>
        <div className="jp-job__actions" style={{ flexDirection: "row" }}>
          <Link href={href} className="jp-btn jp-btn--primary jp-btn--sm">
            <Search size={14} aria-hidden="true" /> Kör igen
          </Link>
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={`Ta bort sökning ${item.label}`}
            onClick={handleDelete}
            disabled={isPending}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>
    </li>
  );
}
