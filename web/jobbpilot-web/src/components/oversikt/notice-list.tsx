"use client";

import { useCallback, useMemo, useState, useSyncExternalStore } from "react";
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
    const allIds = [...actionNotices, ...infoNotices].map((n) => n.id);
    const next = new Set(dismissed);
    for (const id of allIds) next.add(id);
    persist(next);
    setAdditions((prev) => {
      const merged = new Set(prev);
      for (const id of allIds) merged.add(id);
      return merged;
    });
  }, [actionNotices, infoNotices, dismissed, persist]);

  const visibleAction = useMemo(
    () => actionNotices.filter((n) => !dismissed.has(n.id)),
    [actionNotices, dismissed]
  );
  const visibleInfo = useMemo(
    () => infoNotices.filter((n) => !dismissed.has(n.id)),
    [infoNotices, dismissed]
  );
  const visibleCount = visibleAction.length + visibleInfo.length;

  return (
    <section className="jp-section" aria-labelledby="oversikt-notiser">
      <div className="jp-section__head">
        <h2 className="jp-section__title" id="oversikt-notiser">
          Notiser
        </h2>
        <span className="jp-section__count">
          senast uppdaterad <span className="jp-mono">{lastUpdated}</span>
        </span>
        <span style={{ flex: 1 }} />
        {visibleCount > 0 && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm"
            onClick={dismissAll}
          >
            <Check size={14} aria-hidden="true" /> Markera alla som lästa
          </button>
        )}
      </div>

      {visibleCount === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">Inga olästa notiser</div>
          Inkorgen är tom. Du får besked så snart det händer något i ditt
          ärende.
        </div>
      ) : (
        <>
          {visibleAction.length > 0 && (
            <>
              <div className="jp-notice-group">
                <span className="jp-notice-group__title">Kräver åtgärd</span>
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
                <span className="jp-notice-group__title">Information</span>
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
