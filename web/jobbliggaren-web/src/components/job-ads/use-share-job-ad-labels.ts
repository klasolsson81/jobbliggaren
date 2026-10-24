import { useTranslations } from "next-intl";
import type { ShareJobAdLabels } from "./share-job-ad";

/** The share row's labels from `publicjob.share`, resolved in the Server Component that renders the row (ADR 0183). */
export function useShareJobAdLabels(): ShareJobAdLabels {
  const t = useTranslations("publicjob.share");
  return {
    heading: t("heading"),
    via: t("via"),
    newTab: t("newTab"),
    targets: { facebook: t("facebook"), linkedin: t("linkedin"), whatsapp: t("whatsapp"), email: t("email") },
    copy: t("copy"),
    copied: t("copied"),
    more: t("more"),
    field: t("field"),
    copyFailed: t("copyFailed"),
    shareFailed: t("shareFailed"),
  };
}
