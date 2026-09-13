"use client";

import { useRef, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { NoticeRow } from "./notice-row";
import { NoticePrefsPopover, type NoticePrefType } from "./notice-prefs-popover";
import { useNoticeList } from "./use-notice-list";

// NOTICE_TYPES (the runtime SSOT) and its derived types live in the RSC-safe ./notice-types
// module; they are re-exported here so existing importers of this file keep resolving. They must
// NOT be defined in this "use client" module: the Server Component oversikt-page.tsx reads
// NOTICE_TYPES, and a value imported across the "use client" boundary becomes a client reference
// (undefined on the server) → `NOTICE_TYPES[source].map(...)` crashed the server render (#726).
import {
  NOTICE_TYPES,
  type NoticeKind,
  type NoticeSource,
  type NoticeType,
  type SectionNoticeData,
} from "./notice-types";

export { NOTICE_TYPES };
export type { NoticeSource, NoticeType, NoticePrefType, SectionNoticeData };

interface NoticeSectionProps {
  readonly source: NoticeSource;
  readonly titleId: string;
  readonly title: string;
  readonly notices: ReadonlyArray<SectionNoticeData>;
  /** Underrad i tomt-läget — vad sektionen kommer att visa. */
  readonly emptyBody: string;
  /**
   * Typer som listas i kugghjuls-popovern (inkl. förberedda typer utan notiser).
   *
   * UTELÄMNAD (eller tom) = inget kugghjul, ingen popover — och ALLTSÅ INGEN VÄG ATT
   * ÄNDRA en preferens. Den här propen stänger bara SKRIVNINGEN. Sektionen filtrerar
   * fortfarande på `isEnabled`, så en yta som utelämnar propen UTAN att också omslutas
   * av `<InertNoticePrefsProvider>` blir filtrerad av en preferens besökaren inte kan
   * se — exakt det läget CTO-domen 2026-08-29 avvisade. De två hör ihop. Gäst-demon (#1572) skickar ingen, för popovern skriver till
   * localStorage-nyckeln `jp-oversikt-notice-prefs`, vars `"<källa>:<typ>"`-nycklar
   * DELAS med den inloggade appen i samma webbläsare — till skillnad från notis-id:na
   * i systerstoren, som är disjunkta. En utloggad besökare ska inte kunna släcka
   * notiser i ett konto hen ännu inte har (Klas-direktiv 2026-08-29).
   */
  readonly prefTypes?: ReadonlyArray<NoticePrefType>;
  /**
   * Stående tillstånd över notislistan (#1548) — render-only. AVSIKTLIGT en
   * ReactNode och inte en datastruktur: sektionen är källagnostisk, och en
   * `summaryData`-prop hade dragit in applikationskunskap i en komponent som
   * också bär jobbannonser och företagsbevakning.
   */
  readonly summary?: ReactNode;
  /**
   * Vad {@link summary} redan säger om sektionen, när den säger något.
   * "unreadable" = källan kunde inte läsas, så även oläst-räknaren vore ett
   * påstående om odata. "empty" = källan lästes och höll inget.
   * Frånvarande = sektionen bär sitt eget tomt-läge som förut.
   *
   * En renderingsfakta om sektionen, aldrig applikationsdata: sektionen bär
   * också jobbannonser och företagsbevakning.
   */
  readonly summaryOwns?: "unreadable" | "empty";
}

// Åtgärdsnotiser (warning/success) sorteras först, info/brand därefter — övrigt
// bevarar konstruktionsordningen (Array.prototype.sort är stabil sedan ES2019).
const ACTION_KINDS: ReadonlySet<NoticeKind> = new Set<NoticeKind>([
  "warning",
  "success",
]);
const isAction = (kind: NoticeKind) => ACTION_KINDS.has(kind);

/**
 * En notissektion per källa (Mina ansökningar / Jobbannonser / Företagsbevakning).
 * Client Component — läst-läget (dismiss/restore via delad store) och fokusflytten bor i
 * `useNoticeList`, inställnings-popovern i `NoticePrefsPopover`; sektionen komponerar dem
 * (ADR 0140). Renderas i dag av gäst-demon; appens `/oversikt` bär korten.
 */
export function NoticeSection({
  source,
  titleId,
  title,
  notices,
  emptyBody,
  prefTypes,
  summary,
  summaryOwns,
}: NoticeSectionProps) {
  const t = useTranslations("oversikt");
  const hasPrefs = (prefTypes?.length ?? 0) > 0;

  // Efter restore av SISTA lästa raden avmonteras även foten → kugghjulet.
  const gearRef = useRef<HTMLButtonElement>(null);
  const {
    unread,
    read,
    showRead,
    toggleShowRead,
    handleDismiss,
    handleRestore,
    footToggleRef,
  } = useNoticeList(notices, { isAction, fallbackRef: gearRef });

  // Klas-direktiv 2026-08-30: en sektion som redan BÄR information ska inte också säga att
  // information samlas här. Tom-raden är notislistans tomt-läge, inte sektionens — och när en
  // sammanfattning står ovanför är sektionen inte tom. `summary` är alltid en renderad nod när
  // den skickas (räknare, tomt-läge eller en ohämtbar-rad), så dess NÄRVARO är signalen.
  const listRendered =
    unread.length > 0 || read.length > 0 || (!summaryOwns && !summary);

  return (
    <section className="jp-section" aria-labelledby={titleId}>
      <div className="jp-section__head">
        <h2 className="jp-section__title" id={titleId}>
          {title}
        </h2>
        {summaryOwns !== "unreadable" && (
          <span className="jp-section__count">
            {t("notices.unreadCount", { count: unread.length })}
          </span>
        )}
        <span style={{ flex: 1 }} />
        {hasPrefs && (
          <NoticePrefsPopover
            ref={gearRef}
            groups={[{ source, types: prefTypes ?? [] }]}
            notices={notices}
          />
        )}
      </div>

      {summary}

      {listRendered && (
        <ul className="jp-notice-list">
          {unread.length > 0 ? (
            unread.map((n) => (
              <NoticeRow key={n.id} notice={n} onDismiss={handleDismiss} />
            ))
          ) : summaryOwns || read.length > 0 ? null : (
            <li className="jp-notice-empty">{emptyBody}</li>
          )}
          {showRead &&
            read.map((n) => (
              <NoticeRow key={n.id} notice={n} read onRestore={handleRestore} />
            ))}
          {read.length > 0 && (
            <li className="jp-notice-foot">
              <span className="jp-notice-foot__count">
                {t("notices.readCount", { count: read.length })}
              </span>
              <button
                ref={footToggleRef}
                type="button"
                className="jp-notice-foot__toggle"
                aria-expanded={showRead}
                onClick={toggleShowRead}
              >
                {showRead ? t("notices.hideRead") : t("notices.showRead")}
              </button>
            </li>
          )}
        </ul>
      )}
    </section>
  );
}
