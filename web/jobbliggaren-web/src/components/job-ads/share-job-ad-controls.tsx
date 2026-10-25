"use client";

import { Check, Link, Share2 } from "lucide-react";
import { type ReactNode, useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { type JobAdShareData, copyLink, hasNativeShare, shareNatively } from "@/lib/job-ads/share-job-ad";
import { SHARE_ICON_SIZE, SHARE_ITEM, SHARE_LIST } from "./share-job-ad-styles";

export interface ShareJobAdButtonLabels {
  readonly copy: string;
  readonly copied: string;
  readonly more: string;
  readonly field: string;
  readonly copyFailed: string;
  readonly shareFailed: string;
}

const COPIED_MS = 3000;

const noSubscription = () => () => {};

/**
 * The share row's list (ADR 0183): the server-rendered links come in as `children`, and the two buttons follow them.
 * "Kopiera länken" is rendered with its fixed label and hidden without scripting. "Fler sätt att dela" holds its place
 * invisibly until mount and shows only where the browser has a share sheet that takes the data, so nothing on the row
 * moves when the page hydrates.
 *
 * Each button reports its own outcome. A copy confirms itself on the button and to a screen reader for three seconds,
 * keeping the wider label's width. Every failed copy, and every refusal of the sheet but a cancel, shows the link in a
 * read-only field under the row and moves focus there with the link selected.
 */
export function ShareJobAdControls({
  data,
  labels,
  children,
}: {
  data: JobAdShareData;
  labels: ShareJobAdButtonLabels;
  children: ReactNode;
}) {
  const nativeShare = useSyncExternalStore(
    noSubscription,
    () => hasNativeShare(navigator, data),
    () => false
  );
  const [copied, setCopied] = useState(false);
  const [failure, setFailure] = useState<{ kind: "copy" | "share"; attempt: number } | null>(null);
  const [pending, setPending] = useState(false);
  const pendingRef = useRef(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const fieldRef = useRef<HTMLInputElement>(null);
  const fieldId = useId();
  const hintId = useId();

  useEffect(() => {
    if (failure === null) return;
    fieldRef.current?.focus();
    fieldRef.current?.select();
  }, [failure]);

  useEffect(() => () => {
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
  }, []);

  const fail = (kind: "copy" | "share") =>
    setFailure((current) => ({ kind, attempt: (current?.attempt ?? 0) + 1 }));

  async function handleCopy() {
    if ((await copyLink(navigator, data.url)) === "failed") {
      fail("copy");
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
    if (outcome === "failed") fail("share");
  }

  return (
    <>
      <ul className={SHARE_LIST}>
        {children}
        <li className="noscript:hidden">
          <button type="button" className={SHARE_ITEM} onClick={handleCopy}>
            {copied ? (
              <Check size={SHARE_ICON_SIZE} aria-hidden="true" />
            ) : (
              <Link size={SHARE_ICON_SIZE} aria-hidden="true" />
            )}
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
          <button type="button" className={SHARE_ITEM} aria-disabled={pending || undefined} onClick={handleShare}>
            <Share2 size={SHARE_ICON_SIZE} aria-hidden="true" />
            {labels.more}
          </button>
        </li>
      </ul>
      <span role="status" className="sr-only">
        {copied ? labels.copied : ""}
      </span>
      {failure !== null && (
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
            {failure.kind === "copy" ? labels.copyFailed : labels.shareFailed}
          </p>
        </div>
      )}
    </>
  );
}
