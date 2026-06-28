"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Check, Copy } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * A single-field copy button (issue #316, Kivra payment-view style). Each AF
 * activity-report field gets its own button so the user copies field by field
 * into Arbetsförmedlingen's per-field form — never a text block, which would
 * flag the report for manual review.
 *
 * Accessibility: a real <button> (keyboard-operable), an aria-label naming the
 * field ("Kopiera arbetsgivare"), and a polite aria-live status that announces
 * the copy to screen readers. The visible "Kopierad" confirmation clears after
 * ~1.6 s. Copy failures degrade silently (clipboard unavailable / denied).
 */
export function CopyButton({
  value,
  fieldLabel,
}: {
  value: string;
  fieldLabel: string;
}) {
  const t = useTranslations("aktivitetsrapport");
  const [copied, setCopied] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      return;
    }
    setCopied(true);
    setAnnouncement(t("copy.announced", { field: fieldLabel }));
    if (timeoutRef.current) clearTimeout(timeoutRef.current);
    timeoutRef.current = setTimeout(() => {
      setCopied(false);
      setAnnouncement("");
    }, 1600);
  }

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={handleCopy}
        aria-label={t("copy.button", { field: fieldLabel })}
        className="shrink-0"
      >
        {copied ? (
          <Check aria-hidden="true" />
        ) : (
          <Copy aria-hidden="true" />
        )}
        <span aria-hidden="true">
          {copied ? t("copy.copied") : t("copy.label")}
        </span>
      </Button>
      <span role="status" aria-live="polite" className="sr-only">
        {announcement}
      </span>
    </>
  );
}
