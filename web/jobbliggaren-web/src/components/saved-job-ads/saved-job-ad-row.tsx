"use client";

import Link from "next/link";
import { useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { Bookmark, ExternalLink, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import { unsaveJobAdAction } from "@/lib/actions/saved-job-ads";

interface SavedJobAdRowProps {
  item: SavedJobAdDto;
  onUnsaved: (jobAdId: string) => void;
  onUnsaveFailed: (jobAdId: string, error: string) => void;
}

/**
 * F6 P5 Punkt 2 Del A — rad i `/sparade`-listan. Visar JobAd-metadata
 * från ADR 0048 in-handler-join (`item.jobAd`). När annonsen soft-deletats
 * eller borttagits från Platsbanken (ADR 0032 snapshot-retention) →
 * `item.jobAd === null` → fallback-rendering med "Annonsen är borttagen".
 *
 * Borttag = `unsaveJobAdAction(item.jobAdId)` (ej SavedJobAdId — backend
 * matchar på composite-key per ADR 0011 strongly-typed soft-ref).
 */
export function SavedJobAdRow({
  item,
  onUnsaved,
  onUnsaveFailed,
}: SavedJobAdRowProps) {
  const t = useTranslations("jobads.saved");
  const format = useFormatter();
  const [isPending, startTransition] = useTransition();
  const savedAt = formatDate(format, item.savedAt) ?? "";

  function handleUnsave() {
    startTransition(async () => {
      const result = await unsaveJobAdAction(item.jobAdId);
      if (result.success) {
        onUnsaved(item.jobAdId);
      } else {
        onUnsaveFailed(item.jobAdId, result.error);
      }
    });
  }

  // Fallback-rendering när JobAd är null (soft-deletad / borttagen).
  if (item.jobAd === null) {
    return (
      <li>
        <article
          className="jp-job"
          style={{
            gridTemplateColumns: "auto 1fr auto",
            opacity: 0.7,
          }}
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
            <Bookmark size={20} />
          </div>
          <div className="jp-job__body">
            <h3 className="jp-job__title">{t("removed")}</h3>
            <div className="jp-job__meta" style={{ marginTop: 8 }}>
              <span>
                {t("saved")} <b>{savedAt}</b>
              </span>
            </div>
          </div>
          <div className="jp-job__actions" style={{ flexDirection: "row" }}>
            <button
              type="button"
              className="jp-icon-btn"
              aria-label={t("removeBookmark")}
              onClick={handleUnsave}
              disabled={isPending}
            >
              <Trash2 size={16} aria-hidden="true" />
            </button>
          </div>
        </article>
      </li>
    );
  }

  // JobAd finns — normal rad.
  const publishedAt = formatDate(format, item.jobAd.publishedAt);
  const expiresAt = formatDate(format, item.jobAd.expiresAt);

  return (
    <li>
      <article
        className="jp-job"
        style={{ gridTemplateColumns: "auto 1fr auto" }}
      >
        <Link
          href={`/jobb/${item.jobAdId}`}
          className="jp-job__match"
          style={{
            background: "var(--jp-surface-3)",
            borderColor: "var(--jp-border)",
            color: "var(--jp-ink-2)",
            textDecoration: "none",
          }}
          aria-label={t("openAd", { title: item.jobAd.title })}
        >
          <Bookmark size={20} aria-hidden="true" />
        </Link>
        <div className="jp-job__body">
          <h3 className="jp-job__title">
            <Link
              href={`/jobb/${item.jobAdId}`}
              style={{ color: "inherit", textDecoration: "none" }}
            >
              {item.jobAd.title}
            </Link>
          </h3>
          <div className="jp-job__company">{item.jobAd.company}</div>
          <div className="jp-job__meta">
            {publishedAt && (
              <span>
                {t("published")} <b>{publishedAt}</b>
              </span>
            )}
            {expiresAt && (
              <span>
                {t("lastApplication")} <b>{expiresAt}</b>
              </span>
            )}
            <span>
              {t("saved")} <b>{savedAt}</b>
            </span>
          </div>
        </div>
        <div className="jp-job__actions" style={{ flexDirection: "row" }}>
          {item.jobAd.url && (
            <a
              href={item.jobAd.url}
              target="_blank"
              rel="noopener noreferrer"
              className="jp-icon-btn"
              aria-label={t("openExternal")}
            >
              <ExternalLink size={16} aria-hidden="true" />
            </a>
          )}
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={t("removeBookmarkFor", { title: item.jobAd.title })}
            onClick={handleUnsave}
            disabled={isPending}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>
    </li>
  );
}
