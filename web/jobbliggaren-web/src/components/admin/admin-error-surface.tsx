// Retry controls and focus restoration run in the browser.
"use client";

import type { ErrorInfo } from "next/error";
import { useTranslations } from "next-intl";
import { useFocusOnMount } from "@/lib/hooks/use-focus-on-mount";
import { useFocusMainOnUnmount } from "@/lib/hooks/use-focus-main-on-unmount";

export function AdminErrorSurface({ retry }: Pick<ErrorInfo, "retry">) {
  const t = useTranslations("fallback");
  const headingRef = useFocusOnMount<HTMLHeadingElement>();
  useFocusMainOnUnmount();

  return (
    <div className="flex flex-col gap-4">
      <h1 ref={headingRef} tabIndex={-1} className="jp-h1">{t("errorTitle")}</h1>
      <p className="jp-lede">{t("errorBodyRetry")}</p>
      <div>
        <button type="button" onClick={() => retry()} className="jp-btn jp-btn--primary">
          {t("retry")}
        </button>
      </div>
    </div>
  );
}
