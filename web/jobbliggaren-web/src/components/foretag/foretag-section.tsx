import type { ReactNode } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { assertNever, type ApiResult } from "@/lib/dto/_helpers";

export type PagesTranslator = Awaited<ReturnType<typeof getTranslations<"pages">>>;

/**
 * Shared discriminated-union renderer for a /foretag surface: `ok` → the section's
 * own content; `unauthorized` → login redirect; `rateLimited`/error → civic inline
 * notices (parity across surfaces so every read degrades identically). Extracted
 * from the pre-split /foretag hub (S1 #996) so the bevakade / smarta-bevakningar /
 * historik surfaces reuse one renderer (ADR 0030 list semantics — a collection
 * endpoint never surfaces `notFound`; it collapses to the error notice).
 */
export function renderSection<T>(
  result: ApiResult<T>,
  t: PagesTranslator,
  loadErrorTitle: string,
  renderOk: (data: T) => ReactNode,
): ReactNode {
  switch (result.kind) {
    case "ok":
      return renderOk(result.data);
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div
          role="alert"
          className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
        >
          <p className="text-body font-medium text-warning-700">
            {t("common.rateLimitedTitle")}
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            {t("common.rateLimitedBody", { seconds: result.retryAfterSeconds })}
          </p>
        </div>
      );
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">{loadErrorTitle}</p>
          <p className="mt-1 text-body-sm">{t("common.errorBodyReload")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
