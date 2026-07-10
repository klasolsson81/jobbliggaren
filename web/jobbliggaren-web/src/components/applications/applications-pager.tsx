"use client";

import { useTranslations } from "next-intl";
import { buildPageItems } from "@/components/job-ads/job-ad-pagination";

interface ApplicationsPagerProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

/**
 * Klient-paginering för Tabell-vyn (#630 PR 10, Option B — CTO-bind 2026-07-10):
 * ren klient-state över den redan hämtade arrayen, INGEN ny data-hämtning och
 * ingen URL-navigering (till skillnad från `JobAdPagination` som är `<Link>`-
 * baserad). Vi återbrukar bara dess rena `buildPageItems`-helper (GOV.UK-mönster:
 * första + sista + aktuell ± 1 med ellipsis) — knapparna byter sida via callback.
 *
 * A11y (jobbpilot-design-a11y): `<nav aria-label>`, `aria-current="page"` på
 * aktiv sida, sr-only sid-prefix på siffrorna, ellipsis `aria-hidden`.
 */
export function ApplicationsPager({
  page,
  totalPages,
  onPageChange,
}: ApplicationsPagerProps) {
  const t = useTranslations("jobads.ui");
  if (totalPages <= 1) return null;

  const items = buildPageItems(page, totalPages);

  return (
    <nav aria-label={t("pagination.navLabel")} className="jp-apppager">
      <ol className="jp-apppager__list">
        {page > 1 && (
          <li>
            <button
              type="button"
              className="jp-apppager__btn"
              onClick={() => onPageChange(page - 1)}
            >
              {t("pagination.previous")}
            </button>
          </li>
        )}
        {items.map((item, idx) =>
          item === "ellipsis" ? (
            <li key={`gap-${idx}`} aria-hidden="true" className="jp-apppager__gap">
              …
            </li>
          ) : item === page ? (
            <li key={item}>
              <span aria-current="page" className="jp-apppager__btn jp-apppager__btn--current">
                <span className="sr-only">{t("pagination.pagePrefix")}</span>
                {item}
              </span>
            </li>
          ) : (
            <li key={item}>
              <button
                type="button"
                className="jp-apppager__btn"
                onClick={() => onPageChange(item)}
              >
                <span className="sr-only">{t("pagination.pagePrefix")}</span>
                {item}
              </button>
            </li>
          ),
        )}
        {page < totalPages && (
          <li>
            <button
              type="button"
              className="jp-apppager__btn"
              onClick={() => onPageChange(page + 1)}
            >
              {t("pagination.next")}
            </button>
          </li>
        )}
      </ol>
    </nav>
  );
}
