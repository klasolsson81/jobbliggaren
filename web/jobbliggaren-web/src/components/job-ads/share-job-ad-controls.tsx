"use client";

import { useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { shareTargetLinks } from "@/lib/job-ads/share-job-ad-links";
import { type JobAdShareData, copyLink, hasNativeShare, shareNatively } from "@/lib/job-ads/share-job-ad";
import type { ShareJobAdLabels } from "./share-job-ad";

const COPIED_MS = 3000;

/** The house text link (`TEXT_LINK`), semibold, with the 32 px target floor that grows to 44 px at ≤768 px. */
const ITEM =
  "inline-flex min-h-8 min-w-8 items-center justify-center text-body-sm font-semibold text-brand-700 underline underline-offset-2 [@media(max-width:768px)]:min-h-11 [@media(max-width:768px)]:min-w-11";
const BUTTON = `${ITEM} cursor-pointer border-0 bg-transparent p-0`;

const noSubscription = () => () => {};

/**
 * The share row's items (ADR 0183). The four links are rendered by the server and work without JavaScript. The two
 * buttons come last: "Kopiera länken" is rendered with its fixed label and hidden without scripting, and "Fler sätt
 * att dela" holds its place invisibly until mount and shows only where the browser has a share sheet that takes the
 * data, so nothing on the row moves when the page hydrates.
 *
 * Each button reports its own outcome. A copy confirms itself on the button and to a screen reader for three seconds,
 * keeping the wider label's width; a failed copy, or a sheet that refuses for any reason but a cancel, shows the link
 * in a read-only field under the row, focused and selected.
 */
export function ShareJobAdControls({ data, labels }: { data: JobAdShareData; labels: ShareJobAdLabels }) {
  const nativeShare = useSyncExternalStore(
    noSubscription,
    () => hasNativeShare(navigator, data),
    () => false
  );
  const [copied, setCopied] = useState(false);
  const [failed, setFailed] = useState<"copy" | "share" | null>(null);
  const [pending, setPending] = useState(false);
  const pendingRef = useRef(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const fieldRef = useRef<HTMLInputElement>(null);
  const fieldId = useId();
  const hintId = useId();

  useEffect(() => {
    if (failed === null) return;
    fieldRef.current?.focus();
    fieldRef.current?.select();
  }, [failed]);

  useEffect(() => () => {
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
  }, []);

  async function handleCopy() {
    if ((await copyLink(navigator, data.url)) === "failed") {
      setFailed("copy");
      return;
    }
    setCopied(true);
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => setCopied(false), COPIED_MS);
  }

  async function handleShare() {
    if (pendingRef.current) return;
    pendingRef.current = true;
    setPending(true);
    const outcome = await shareNatively(navigator, data);
    pendingRef.current = false;
    setPending(false);
    if (outcome === "failed") setFailed("share");
  }

  return (
    <>
      <ul className="flex flex-wrap gap-x-5">
        {shareTargetLinks(data).map(({ target, href }) => (
          <li key={target}>
            {target === "email" ? (
              <a href={href} className={ITEM}>
                <span className="sr-only">{labels.via}</span> {labels.targets[target]}
              </a>
            ) : (
              <a href={href} target="_blank" rel="nofollow noopener noreferrer" className={ITEM}>
                <span className="sr-only">{labels.via}</span> {labels.targets[target]}{" "}
                <span className="sr-only">{labels.newTab}</span>
              </a>
            )}
          </li>
        ))}
        <li className="noscript:hidden">
          <button type="button" className={BUTTON} onClick={handleCopy}>
            <span className="grid text-start">
              <span className={`[grid-area:1/1] ${copied ? "invisible" : ""}`} aria-hidden={copied || undefined}>
                {labels.copy}
              </span>
              <span className={`[grid-area:1/1] ${copied ? "" : "invisible"}`} aria-hidden={!copied || undefined}>
                {labels.copied}
              </span>
            </span>
          </button>
        </li>
        <li className={`noscript:hidden ${nativeShare ? "" : "invisible"}`}>
          <button type="button" className={BUTTON} aria-disabled={pending || undefined} onClick={handleShare}>
            {labels.more}
          </button>
        </li>
      </ul>
      <span role="status" className="sr-only">
        {copied ? labels.copied : ""}
      </span>
      {failed !== null && (
        <div className="mt-3 max-w-[480px]">
          <label htmlFor={fieldId} className="mb-1 block text-body-sm font-semibold">
            {labels.field}
          </label>
          <input
            ref={fieldRef}
            id={fieldId}
            type="text"
            readOnly
            value={data.url}
            aria-describedby={hintId}
            className="jp-input"
          />
          <p id={hintId} className="mt-1 text-body-sm">
            {failed === "copy" ? labels.copyFailed : labels.shareFailed}
          </p>
        </div>
      )}
    </>
  );
}
