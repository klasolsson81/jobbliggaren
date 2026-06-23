"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { useTranslations } from "next-intl";
import { Clock, Search, Trash2 } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import { deleteRecentSearchAction } from "@/lib/actions/recent-searches";
import type { RecentSearchCount } from "@/lib/hooks/use-recent-search-counts";

interface RecentSearchRowProps {
  item: RecentJobSearchDto;
  /**
   * Lat-hämtad träffräknare (B, CTO 2026-06-13). `undefined` = ännu inte
   * laddad / timeout / fel → ingen siffra renderas (ALDRIG falsk "(0)").
   * Kommer från `useRecentSearchCounts` i listan, INTE från `item.currentCount`
   * (som är 0 vid sidladdning, `includeCount=false`).
   */
  count?: RecentSearchCount;
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
    // STEG 5 (grade-filter) — matchGrades är runtime-view-state, INTE en
    // sparad sök-angelägenhet (Klas: håll det utanför recent-search-concern:en).
    // En "Kör igen" replayar därför aldrig ett grad-filter — tom lista.
    matchGrades: [],
    sortBy: item.sortBy,
  });
}

// Klas-direktiv 2026-05-20 (anti-AI-trope): INGEN "NY"-pill på raden.
// Format: "(N) träffar" om newCount === 0, "(N) träffar, varav (M) nya"
// om newCount > 0. Mono via `.jp-job__meta b`, ink-2 via `.jp-job__meta`.
//
// Talet hämtas LAT klient-side (B, CTO 2026-06-13) via `useRecentSearchCounts`
// i listan och skickas in som `count`-prop. Saknas det (laddar/timeout/fel)
// renderas ingen siffra — ALDRIG en falsk "(0)" (husets degraderingskontrakt).
function CountMeta({
  currentCount,
  newCount,
  t,
}: RecentSearchCount & { t: ReturnType<typeof useTranslations<"jobads.recent">> }) {
  const bold = (chunks: React.ReactNode) => <b>{chunks}</b>;
  if (newCount > 0) {
    return (
      <div className="jp-job__meta" style={{ marginTop: 8 }}>
        <span>
          {t.rich("hitsWithNew", {
            b: bold,
            currentCount: currentCount.toLocaleString("sv-SE"),
            newCount: newCount.toLocaleString("sv-SE"),
          })}
        </span>
      </div>
    );
  }
  return (
    <div className="jp-job__meta" style={{ marginTop: 8 }}>
      <span>
        {t.rich("hits", {
          b: bold,
          currentCount: currentCount.toLocaleString("sv-SE"),
        })}
      </span>
    </div>
  );
}

export function RecentSearchRow({ item, count, onDeleted, onDeleteFailed }: RecentSearchRowProps) {
  const router = useRouter();
  const t = useTranslations("jobads.recent");
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
          {count !== undefined && (
            <CountMeta currentCount={count.currentCount} newCount={count.newCount} t={t} />
          )}
        </div>
        <div className="jp-job__actions" style={{ flexDirection: "row" }}>
          <Link href={href} className="jp-btn jp-btn--primary jp-btn--sm">
            <Search size={14} aria-hidden="true" /> {t("runAgain")}
          </Link>
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={t("removeSearch", { label: item.label })}
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
