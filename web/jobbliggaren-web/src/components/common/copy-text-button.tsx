"use client";

import { useRef, useState } from "react";
import { Check, Copy } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * Copies one value to the clipboard (issue #316, generalised for #2248 so the activity report and
 * the application card share one implementation). The caller owns every string, so each surface
 * keeps its own wording and translation namespace.
 *
 * Accessibility: a real <button> (keyboard-operable) named by `ariaLabel`, and a polite aria-live
 * status that announces `announcement` after a copy. The visible confirmation clears after ~1.6 s.
 * A failed copy (clipboard unavailable or denied) changes nothing on screen.
 */
export function CopyTextButton({
  value,
  ariaLabel,
  label,
  copiedLabel,
  announcement,
  className,
}: {
  value: string;
  ariaLabel: string;
  label: string;
  copiedLabel: string;
  announcement: string;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const [status, setStatus] = useState("");
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      return;
    }
    setCopied(true);
    setStatus(announcement);
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => {
      setCopied(false);
      setStatus("");
    }, 1600);
  }

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={handleCopy}
        aria-label={ariaLabel}
        className={className ?? "shrink-0"}
      >
        {copied ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}
        <span aria-hidden="true">{copied ? copiedLabel : label}</span>
      </Button>
      <span role="status" aria-live="polite" className="sr-only">
        {status}
      </span>
    </>
  );
}
