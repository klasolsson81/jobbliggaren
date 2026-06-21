"use client";

import { useCallback, useMemo, useState, useSyncExternalStore } from "react";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";
import { NoticeRow, type NoticeData } from "./notice-row";

interface NoticeListProps {
  readonly actionNotices: ReadonlyArray<NoticeData>;
  readonly infoNotices: ReadonlyArray<NoticeData>;
  readonly lastUpdated: string;
}

const LS_KEY = "jp-oversikt-dismissed-notices";

/**
 * Subscribe-funktion för useSyncExternalStore — kör en gång per mount och
 * lyssnar på "storage"-events (om andra flikar dismissar; bonus, inte krav).
 */
function subscribeStorage(callback: () => void): () => void {
  if (typeof window === "undefined") return () => undefined;
  window.addEventListener("storage", callback);
  return () => window.removeEventListener("storage", callback);
}

function getDismissedSnapshot(): string {
  if (typeof window === "undefined") return "[]";
  try {
    return window.localStorage.getItem(LS_KEY) ?? "[]";
  } catch {
    return "[]";
  }
}

function getServerSnapshot(): string {
  return "[]";
}

function parseDismissed(raw: string): ReadonlySet<string> {
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return new Set();
    return new Set(parsed.filter((v): v is string => typeof v === "string"));
  } catch {
    return new Set();
  }
}

/**
 * Client Component — dismiss-state via useSyncExternalStore + localStorage.
 *
 * Ingen `markNotificationRead`-server-action finns ännu (HANDOVER §3.7);
 * vi gör optimistic local-only-state. Persistens sker till localStorage så
 * notiser inte återkommer vid reload tills BE-port finns. `useSyncExternalStore`
 * ger SSR-säker hydration (server-snapshot = tom array; klient-snapshot =
 * faktiska localStorage-värden post-hydration).
 *
 * Lokal `additions`-state lägger på dismiss-IDs sedan senaste sync —
 * vi mergar med localStorage-snapshoten vid render så multi-tab-rörelser
 * inte tappas. Notice-id är stabilt (genereras i page.tsx från pipeline-
 * data eller mock-snippet-key) så localStorage-värden är meningsfulla
 * mellan sessioner.
 */
export function NoticeList({
  actionNotices,
  infoNotices,
  lastUpdated,
}: NoticeListProps) {
  const t = useTranslations("oversikt");
  const storedRaw = useSyncExternalStore(
    subscribeStorage,
    getDismissedSnapshot,
    getServerSnapshot
  );
  const [additions, setAdditions] = useState<ReadonlySet<string>>(
    () => new Set<string>()
  );

  const dismissed = useMemo(() => {
    const merged = new Set(parseDismissed(storedRaw));
    for (const id of additions) merged.add(id);
    return merged;
  }, [storedRaw, additions]);

  const persist = useCallback((next: ReadonlySet<string>) => {
    if (typeof window === "undefined") return;
    try {
      window.localStorage.setItem(LS_KEY, JSON.stringify([...next]));
    } catch {
      // localStorage kan vara blockerad (private-mode/Safari ITP) — degradera tyst
    }
  }, []);

  const dismissOne = useCallback(
    (id: string) => {
      const next = new Set(dismissed);
      next.add(id);
      persist(next);
      setAdditions((prev) => {
        const merged = new Set(prev);
        merged.add(id);
        return merged;
      });
    },
    [dismissed, persist]
  );

  const dismissAll = useCallback(() => {
    // F4-12 PR-B (ADR 0076): icke-avfärdbara notiser (setup-nudgen) får INTE
    // markeras som lästa — de exkluderas ur id-insamlingen så "Markera alla
    // som lästa" lämnar dem synliga.
    const allIds = [...actionNotices, ...infoNotices]
      .filter((n) => n.dismissible !== false)
      .map((n) => n.id);
    const next = new Set(dismissed);
    for (const id of allIds) next.add(id);
    persist(next);
    setAdditions((prev) => {
      const merged = new Set(prev);
      for (const id of allIds) merged.add(id);
      return merged;
    });
  }, [actionNotices, infoNotices, dismissed, persist]);

  // En icke-avfärdbar notis är ALLTID synlig (filtreras aldrig av dismissed-
  // mängden); en avfärdbar göms när dess id finns i dismissed.
  const isVisible = useCallback(
    (n: NoticeData) => n.dismissible === false || !dismissed.has(n.id),
    [dismissed]
  );

  const visibleAction = useMemo(
    () => actionNotices.filter(isVisible),
    [actionNotices, isVisible]
  );
  const visibleInfo = useMemo(
    () => infoNotices.filter(isVisible),
    [infoNotices, isVisible]
  );
  const visibleCount = visibleAction.length + visibleInfo.length;

  // "Markera alla som lästa" visas bara när minst en SYNLIG notis faktiskt går
  // att avfärda — annars vore knappen en no-op (t.ex. när bara den persistenta
  // setup-nudgen finns kvar).
  const hasDismissibleVisible =
    visibleAction.some((n) => n.dismissible !== false) ||
    visibleInfo.some((n) => n.dismissible !== false);

  return (
    <section className="jp-section" aria-labelledby="oversikt-notiser">
      <div className="jp-section__head">
        <h2 className="jp-section__title" id="oversikt-notiser">
          {t("notices.title")}
        </h2>
        <span className="jp-section__count">
          {t.rich("notices.lastUpdated", {
            date: lastUpdated,
            mono: (chunks) => <span className="jp-mono">{chunks}</span>,
          })}
        </span>
        <span style={{ flex: 1 }} />
        {hasDismissibleVisible && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm"
            onClick={dismissAll}
          >
            <Check size={14} aria-hidden="true" /> {t("notices.markAllRead")}
          </button>
        )}
      </div>

      {visibleCount === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("notices.emptyTitle")}</div>
          {t("notices.emptyBody")}
        </div>
      ) : (
        <>
          {visibleAction.length > 0 && (
            <>
              <div className="jp-notice-group">
                <span className="jp-notice-group__title">
                  {t("notices.groupAction")}
                </span>
                <span className="jp-notice-group__count">
                  {visibleAction.length}
                </span>
              </div>
              <ul className="jp-notice-list">
                {visibleAction.map((n) => (
                  <NoticeRow key={n.id} notice={n} onDismiss={dismissOne} />
                ))}
              </ul>
            </>
          )}
          {visibleInfo.length > 0 && (
            <>
              <div className="jp-notice-group jp-notice-group--info">
                <span className="jp-notice-group__title">
                  {t("notices.groupInfo")}
                </span>
                <span className="jp-notice-group__count">
                  {visibleInfo.length}
                </span>
              </div>
              <ul className="jp-notice-list">
                {visibleInfo.map((n) => (
                  <NoticeRow key={n.id} notice={n} onDismiss={dismissOne} />
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </section>
  );
}
