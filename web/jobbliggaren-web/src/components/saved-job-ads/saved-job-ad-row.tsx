"use client";

import Link from "next/link";
import { useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ExternalLink, Trash2 } from "lucide-react";
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
 * från ADR 0048 in-handler-join (`item.jobAd`). `item.jobAd === null` →
 * fallback "Annonsen är borttagen".
 *
 * #805-3 sanningssynk: den tidigare utsagan ("när annonsen soft-deletats eller
 * borttagits från Platsbanken") var falsk. En annons som försvinner ur
 * Platsbankens flöde ARKIVERAS
 * (`Status = "Archived"`) — den joinar fortfarande och renderas som en vanlig rad.
 * Den sanningsenliga signalen är `item.jobAd.status`, som DTO:n numera bär. Att
 * surfa "aktiv/inte längre aktiv" på den här raden är **#817**.
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

  if (item.jobAd === null) {
    return (
      <li>
        <article className="jp-job jp-job--static" style={{ opacity: 0.7 }}>
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
  // The ad id is unique within the list, so it keys the description's IDREFs.
  const idBase = `saved-${item.jobAdId}`;

  return (
    <li>
      <article className="jp-job">
        <div className="jp-job__body">
          <h3 className="jp-job__title">
            {/* The row's one link, stretched over the card; the controls sit above it. No
                aria-label: the modal's focus return re-finds a restored opener by its href and
                whether it has one (useInformationModalFocus). */}
            <Link
              href={`/jobb/${item.jobAdId}`}
              className="jp-job__rowlink"
              aria-describedby={`${idBase}-company ${idBase}-meta`}
            >
              {item.jobAd.title}
            </Link>
          </h3>
          <div id={`${idBase}-company`} className="jp-job__company">
            {item.jobAd.company}
          </div>
          {/* Label and space in ONE text node: Chrome drops a whitespace-only node from the
              link's computed description. */}
          <div id={`${idBase}-meta`} className="jp-job__meta">
            {publishedAt && (
              <span>
                {`${t("published")} `}
                <b>{publishedAt}</b>
              </span>
            )}
            {expiresAt && (
              <span>
                {`${t("lastApplication")} `}
                <b>{expiresAt}</b>
              </span>
            )}
            <span>
              {`${t("saved")} `}
              <b>{savedAt}</b>
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
