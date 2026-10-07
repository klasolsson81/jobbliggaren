"use client";

import Link from "next/link";
import { useTransition } from "react";
import type { RefCallback } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ExternalLink, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import { unsaveJobAdAction } from "@/lib/actions/saved-job-ads";

interface SavedJobAdRowProps {
  item: SavedJobAdDto;
  /** The row's first focus stop: the title link, or the remove button on an erased ad's row. */
  firstStopRef?: RefCallback<HTMLElement>;
  onUnsaveStart?: (jobAdId: string) => void;
  /** `receipt` is the sentence the list announces once the row is gone. */
  onUnsaved: (jobAdId: string, receipt: string) => void;
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
  firstStopRef,
  onUnsaveStart,
  onUnsaved,
  onUnsaveFailed,
}: SavedJobAdRowProps) {
  const t = useTranslations("jobads.saved");
  const format = useFormatter();
  const [isPending, startTransition] = useTransition();
  const savedAt = formatDate(format, item.savedAt) ?? "";

  // The remove button stays enabled while the action runs: a `disabled` button that has focus drops it
  // to <body> (the focus fixup rule; measured for the language switcher in #1391). This guard is what
  // prevents a second removal.
  function handleUnsave() {
    if (isPending) return;
    onUnsaveStart?.(item.jobAdId);
    const receipt =
      item.jobAd === null
        ? t("bookmarkRemoved")
        : t("bookmarkRemovedFor", { title: item.jobAd.title });
    startTransition(async () => {
      const result = await unsaveJobAdAction(item.jobAdId);
      if (result.success) {
        onUnsaved(item.jobAdId, receipt);
      } else {
        onUnsaveFailed(item.jobAdId, result.error);
      }
    });
  }

  // The ad id is unique within the list, so it keys the description's IDREFs.
  const idBase = `saved-${item.jobAdId}`;

  if (item.jobAd === null) {
    return (
      <li>
        <article className="jp-job jp-job--static" style={{ opacity: 0.7 }}>
          <div className="jp-job__body">
            <h3 id={`${idBase}-title`} className="jp-job__title">
              {t("removed")}
            </h3>
            <div className="jp-job__meta" style={{ marginTop: 8 }}>
              <span>
                {t("saved")} <b>{savedAt}</b>
              </span>
            </div>
          </div>
          <div className="jp-job__actions" style={{ flexDirection: "row" }}>
            <button
              ref={firstStopRef}
              type="button"
              className="jp-icon-btn"
              aria-label={t("removeBookmark")}
              aria-describedby={`${idBase}-title`}
              onClick={handleUnsave}
              aria-disabled={isPending || undefined}
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
      <article className="jp-job">
        <div className="jp-job__body">
          <h3 className="jp-job__title">
            {/* The row's one link, stretched over the card; the controls sit above it. No
                aria-label: the modal's focus return re-finds a restored opener by its href and
                whether it has one (useInformationModalFocus). */}
            <Link
              ref={firstStopRef}
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
            aria-disabled={isPending || undefined}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>
    </li>
  );
}
