"use client";

import { useTranslations } from "next-intl";
import { CopyTextButton } from "@/components/common/copy-text-button";

/**
 * A single-field copy button (issue #316, Kivra payment-view style). Each AF
 * activity-report field gets its own button so the user copies field by field
 * into Arbetsförmedlingen's per-field form — never a text block, which would
 * flag the report for manual review. The button itself is the shared
 * CopyTextButton (#2248); this file only supplies the report's wording.
 */
export function CopyButton({
  value,
  fieldLabel,
}: {
  value: string;
  fieldLabel: string;
}) {
  const t = useTranslations("aktivitetsrapport");
  return (
    <CopyTextButton
      value={value}
      ariaLabel={t("copy.button", { field: fieldLabel })}
      label={t("copy.label")}
      copiedLabel={t("copy.copied")}
      announcement={t("copy.announced", { field: fieldLabel })}
    />
  );
}
