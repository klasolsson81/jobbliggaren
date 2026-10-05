import { useTranslations } from "next-intl";

/**
 * An unknown value (ADR 0150 D2): the dash the eye reads, and words for a screen reader, which
 * at its default symbol level does not speak a lone dash.
 */
export function AdminUnknown() {
  const t = useTranslations("admin.unavailable");
  return (
    <>
      <span aria-hidden="true">{t("unknownValue")}</span>
      <span className="sr-only">{t("unknownSpoken")}</span>
    </>
  );
}
