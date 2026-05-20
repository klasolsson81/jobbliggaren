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
    ssyk: item.ssykList,
    region: item.regionList,
    sortBy: item.sortBy,
  });
}

// Klas-direktiv 2026-05-20 (anti-AI-trope): INGEN "NY"-pill på raden.
// Format: "(N) träffar" om newCount === 0, "(N) träffar, varav (M) nya"
// om newCount > 0. Mono via `.jp-job__meta b`, ink-2 via `.jp-job__meta`.
function CountMeta({ currentCount, newCount }: { currentCount: number; newCount: number }) {
  if (newCount > 0) {
    return (
      <div className="jp-job__meta" style={{ marginTop: 8 }}>
        <span>
          <b>{currentCount.toLocaleString("sv-SE")}</b> träffar, varav <b>{newCount.toLocaleString("sv-SE")}</b> nya
        </span>
      </div>
    );
  }
  return (
    <div className="jp-job__meta" style={{ marginTop: 8 }}>
      <span>
        <b>{currentCount.toLocaleString("sv-SE")}</b> träffar
      </span>
    </div>
  );
}

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
          <CountMeta currentCount={item.currentCount} newCount={item.newCount} />
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
