"use client";

// "use client": the card holds five switches and the cadence, saves each change optimistically in its
// own transition, reverts a refused one, shows each outcome under the control that saved, and moves
// focus when a change removes or disables the focused control. None of that runs in a Server Component.

import {
  type FocusEvent,
  type RefObject,
  type TransitionStartFunction,
  useEffect,
  useId,
  useRef,
  useState,
  useTransition,
} from "react";
import { useTranslations } from "next-intl";
import { useRefreshNotificationFeed } from "@/components/shell/notification-feed-provider";
import type { DigestCadence } from "@/lib/dto/me";
import type { ActionResult } from "@/lib/actions/_action-result";
import {
  updateDigestCadenceAction,
  updateFollowedCompanyNotificationConsentAction,
  updateMatchSummaryMailAction,
  updateNotificationConsentAction,
  updateNotificationFeedFollowedAdsAction,
  updateNotificationFeedMatchesAction,
  updateNotificationMailAction,
  updateTopMatchMailAction,
} from "@/lib/actions/me";
import { ToggleRow } from "@/components/ui/toggle-row";
import { Segment, type SegmentOption } from "@/components/ui/segment";
import { Outcome, type WriteOutcome } from "./write-outcome";

/** A switch, or the checked option of a segment: the control a unit's write started from. */
const UNIT_CONTROL = '[role="switch"], [role="radiogroup"] button[aria-checked="true"]';

/** One control with its receipt or refusal (#1391). */
type UnitRef = RefObject<HTMLDivElement | null>;

interface NotificationsSectionProps {
  /** Every notification mail on, or paused (ADR 0164). A pause, never a consent. */
  readonly initialMailEnabled: boolean;
  /** The saved background-match consent (default off, GDPR Art. 6(1)(a)/7). */
  readonly initialMatchEnabled: boolean;
  readonly initialTopMatchEnabled: boolean;
  readonly initialSummaryEnabled: boolean;
  /** The saved followed-company mail consent (default off, Art. 6(1)(a)/7). */
  readonly initialFollowEnabled: boolean;
  /** Whether new matches reach the bell (ADR 0162). A delivery choice, never a consent. */
  readonly initialFeedMatchesEnabled: boolean;
  /** Whether new ads from followed companies reach the bell (ADR 0162). */
  readonly initialFeedFollowedAdsEnabled: boolean;
  readonly initialCadence: DigestCadence;
}

/**
 * The Notiser card of /mina-sidor (#1891, #2107): the e-mail switch first, then the background-match
 * consent with the two mails it covers, the followed-company mail consent, and the cadence the
 * summary and the follow mail share (ADR 0087 D2).
 *
 * The consent switches carry their bound texts (ADR 0144 Decision 4, rows 7 and 8), always render,
 * and are locked only by their own write (security V2). The Toppmatch and summary switches render
 * while the e-mail switch and the match consent are both on; hidden, they keep their saved values.
 * The cadence renders while a mail it paces can be sent.
 *
 * #2130 (ADR 0162) - each type's group also holds ONE switch for what reaches the notification bell, before the
 * e-mail rows. They are delivery choices, never consents: they do not depend on the e-mail switch (the bell
 * ignores the mail pauses, ADR 0164 D1), they decide only what the bell shows (never Översikt or the lists),
 * and the matches switch renders only while the match consent is on, keeping its saved value when it is hidden,
 * with no line about why. A saved change asks the bell to read again at once.
 *
 * One write per control, each carrying only its own value.
 */
export function NotificationsSection({
  initialMailEnabled,
  initialMatchEnabled,
  initialTopMatchEnabled,
  initialSummaryEnabled,
  initialFollowEnabled,
  initialFeedMatchesEnabled,
  initialFeedFollowedAdsEnabled,
  initialCadence,
}: NotificationsSectionProps) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages.minaSidor");
  const id = useId();
  const ids = {
    mailError: `${id}-mail-error`,
    matchesTitle: `${id}-matches`,
    matchError: `${id}-match-error`,
    feedMatchesError: `${id}-feed-matches-error`,
    topError: `${id}-top-error`,
    summaryError: `${id}-summary-error`,
    followTitle: `${id}-follow`,
    followError: `${id}-follow-error`,
    feedFollowedError: `${id}-feed-followed-error`,
    paused: `${id}-paused`,
    cadenceHint: `${id}-cadence-hint`,
    cadenceError: `${id}-cadence-error`,
  };

  const saves = {
    mail: useTransition(),
    match: useTransition(),
    feedMatches: useTransition(),
    top: useTransition(),
    summary: useTransition(),
    follow: useTransition(),
    feedFollowed: useTransition(),
    cadence: useTransition(),
  };
  const focus = useFocusKeeper(Object.values(saves).some(([saving]) => saving));

  // Each unit's ref stands alone: the compiler lint reads any object holding a ref as a ref.
  const mailUnit = useRef<HTMLDivElement>(null);
  const matchUnit = useRef<HTMLDivElement>(null);
  const feedMatchesUnit = useRef<HTMLDivElement>(null);
  const topUnit = useRef<HTMLDivElement>(null);
  const summaryUnit = useRef<HTMLDivElement>(null);
  const followUnit = useRef<HTMLDivElement>(null);
  const feedFollowedUnit = useRef<HTMLDivElement>(null);
  const cadenceUnit = useRef<HTMLDivElement>(null);

  const mail = useControlWrite(
    initialMailEnabled,
    (enabled) => updateNotificationMailAction({ enabled }),
    true,
    saves.mail,
    mailUnit,
    focus,
  );
  const match = useControlWrite(
    initialMatchEnabled,
    (enabled) => updateNotificationConsentAction({ enabled }),
    true,
    saves.match,
    matchUnit,
    focus,
  );
  const follow = useControlWrite(
    initialFollowEnabled,
    (enabled) => updateFollowedCompanyNotificationConsentAction({ enabled }),
    true,
    saves.follow,
    followUnit,
    focus,
  );
  // A saved bell switch asks the bell to read again now, past its 30 s throttle, so its badge and rows follow the
  // choice without a reload. Only a confirmed save does: a refused one changed nothing the bell shows.
  const refreshBell = useRefreshNotificationFeed();
  const feedMatches = useControlWrite(
    initialFeedMatchesEnabled,
    async (enabled) => {
      const result = await updateNotificationFeedMatchesAction({ enabled });
      if (result.success) void refreshBell();
      return result;
    },
    match.value,
    saves.feedMatches,
    feedMatchesUnit,
    focus,
  );
  const feedFollowed = useControlWrite(
    initialFeedFollowedAdsEnabled,
    async (enabled) => {
      const result = await updateNotificationFeedFollowedAdsAction({ enabled });
      if (result.success) void refreshBell();
      return result;
    },
    true,
    saves.feedFollowed,
    feedFollowedUnit,
    focus,
  );
  const matchMailsShown = mail.value && match.value;
  const top = useControlWrite(
    initialTopMatchEnabled,
    (enabled) => updateTopMatchMailAction({ enabled }),
    matchMailsShown,
    saves.top,
    topUnit,
    focus,
  );
  const summary = useControlWrite(
    initialSummaryEnabled,
    (enabled) => updateMatchSummaryMailAction({ enabled }),
    matchMailsShown,
    saves.summary,
    summaryUnit,
    focus,
  );
  const cadenceShown = mail.value && ((match.value && summary.value) || follow.value);
  const cadence = useControlWrite(
    initialCadence,
    (next) => updateDigestCadenceAction({ cadence: next }),
    cadenceShown,
    saves.cadence,
    cadenceUnit,
    focus,
  );

  const cadenceOptions: ReadonlyArray<SegmentOption<DigestCadence>> = [
    { value: "Daily", label: t("backgroundMatch.cadenceDaily") },
    { value: "Weekly", label: t("backgroundMatch.cadenceWeekly") },
  ];

  return (
    <section className="jp-card" onFocus={focus.onFocus}>
      <h2 className="jp-card__title">{tp("sections.notiser")}</h2>

      <SwitchUnit
        write={mail}
        unitRef={mailUnit}
        errorId={ids.mailError}
        label={t("notices.mailLabel")}
        className="jp-settings-group jp-settings-control jp-settings-control--master"
      />

      <section className="jp-settings-group" aria-labelledby={ids.matchesTitle}>
        <h3 id={ids.matchesTitle} className="jp-settings-group__title">
          {t("notices.matchesTitle")}
        </h3>
        <SwitchUnit
          write={match}
          unitRef={matchUnit}
          errorId={ids.matchError}
          label={t("backgroundMatch.toggleLabel")}
          description={t("backgroundMatch.toggleDescription")}
        />
        {match.value && (
          <SwitchUnit
            write={feedMatches}
            unitRef={feedMatchesUnit}
            errorId={ids.feedMatchesError}
            label={t("notices.feedMatchesLabel")}
          />
        )}
        {matchMailsShown && (
          <>
            <SwitchUnit
              write={top}
              unitRef={topUnit}
              errorId={ids.topError}
              label={t("backgroundMatch.topMatchLabel")}
              description={t("backgroundMatch.topMatchDescription")}
            />
            <SwitchUnit
              write={summary}
              unitRef={summaryUnit}
              errorId={ids.summaryError}
              label={t("backgroundMatch.summaryLabel")}
              description={t("backgroundMatch.summaryDescription")}
            />
          </>
        )}
      </section>

      <section className="jp-settings-group" aria-labelledby={ids.followTitle}>
        <h3 id={ids.followTitle} className="jp-settings-group__title">
          {t("notices.followTitle")}
        </h3>
        <SwitchUnit
          write={feedFollowed}
          unitRef={feedFollowedUnit}
          errorId={ids.feedFollowedError}
          label={t("notices.feedFollowedAdsLabel")}
        />
        <SwitchUnit
          write={follow}
          unitRef={followUnit}
          errorId={ids.followError}
          label={t("followedCompanyNotifications.toggleLabel")}
          description={t("followedCompanyNotifications.toggleDescription")}
          status={mail.value ? undefined : { id: ids.paused, text: t("notices.mailPaused") }}
        />
      </section>

      {cadenceShown && (
        <div
          ref={cadenceUnit}
          className="jp-settings-group jp-settings-field jp-settings-control"
        >
          <span className="jp-settings-field__label">{t("backgroundMatch.cadenceLabel")}</span>
          <Segment
            aria-label={t("backgroundMatch.cadenceLabel")}
            aria-describedby={joinIds(
              ids.cadenceHint,
              cadence.outcome?.ok === false && ids.cadenceError,
            )}
            value={cadence.value}
            onChange={cadence.change}
            options={cadenceOptions}
            disabled={cadence.saving}
          />
          <p id={ids.cadenceHint} className="jp-settings-field__hint">
            {t("backgroundMatch.cadenceHint")}
          </p>
          <Outcome id={ids.cadenceError} outcome={cadence.outcome} />
        </div>
      )}
    </section>
  );
}

interface ControlWrite<T> {
  readonly value: T;
  readonly saving: boolean;
  readonly outcome: WriteOutcome | null;
  readonly change: (next: T) => void;
}

function SwitchUnit({
  write,
  unitRef,
  errorId,
  label,
  description,
  status,
  className = "jp-settings-control",
}: {
  write: ControlWrite<boolean>;
  unitRef: UnitRef;
  errorId: string;
  label: string;
  description?: string;
  /** A line under the switch, read after its description. */
  status?: { id: string; text: string };
  className?: string;
}) {
  return (
    <div ref={unitRef} className={className}>
      <ToggleRow
        label={label}
        description={description}
        checked={write.value}
        onChange={write.change}
        disabled={write.saving}
        describedBy={joinIds(status !== undefined && status.id, write.outcome?.ok === false && errorId)}
      />
      {status && (
        <p id={status.id} className="jp-settings-control__status">
          {status.text}
        </p>
      )}
      <Outcome id={errorId} outcome={write.outcome} />
    </div>
  );
}

/**
 * One control's value and its write: set optimistically, sent alone, reverted when refused. The
 * control is locked only while its own write is pending.
 */
function useControlWrite<T>(
  initial: T,
  send: (value: T) => Promise<ActionResult>,
  shown: boolean,
  [saving, startSave]: readonly [boolean, TransitionStartFunction],
  unit: UnitRef,
  focus: FocusKeeper,
): ControlWrite<T> {
  const [value, setValue] = useState(initial);
  const { outcome, clear, land } = useShownOutcome(shown);

  function change(next: T) {
    const previous = value;
    focus.started(unit);
    clear();
    setValue(next);
    startSave(async () => {
      const result = await send(next);
      if (result.success) {
        land({ ok: true, at: new Date() });
        return;
      }
      focus.reverted(unit);
      setValue(previous);
      land({ ok: false, error: result.error });
    });
  }

  return { value, saving, outcome, change };
}

/**
 * A control's receipt or refusal. Hiding or showing the control clears it, so a reply that lands
 * while the control is unmounted is never shown when it returns (design M8).
 */
function useShownOutcome(shown: boolean) {
  const [view, setView] = useState<{ shown: boolean; outcome: WriteOutcome | null }>({
    shown,
    outcome: null,
  });
  if (view.shown !== shown) {
    setView({ shown, outcome: null });
  }
  return {
    outcome: view.outcome,
    clear: () => setView((current) => ({ ...current, outcome: null })),
    land: (outcome: WriteOutcome) => setView((current) => ({ ...current, outcome })),
  };
}

interface FocusKeeper {
  readonly onFocus: (event: FocusEvent<HTMLElement>) => void;
  /** A unit's write started: its control gets focus back once every write has settled. */
  readonly started: (unit: UnitRef) => void;
  /** A refused write put a unit's value back. */
  readonly reverted: (unit: UnitRef) => void;
}

/**
 * Focus is lost here in two ways, both measured in Chromium. A control is disabled while its own
 * write is pending (#1391): once every write has settled, focus goes back to the control whose write
 * started last. A change can unmount the focused control (design B2): focus goes to the switch whose
 * change removed it, or, while that switch's own write is pending, to it once the writes settle.
 * Either way only when focus sits on <body>, so a user who has moved on keeps their place.
 */
function useFocusKeeper(pending: boolean): FocusKeeper {
  const returnTo = useRef<UnitRef | null>(null);
  const cause = useRef<UnitRef | null>(null);
  const focused = useRef<Element | null>(null);

  useEffect(() => {
    const lost = focused.current;
    if (lost === null || lost.isConnected) return;
    focused.current = null;
    const unit = cause.current;
    if (unit === null || !focusIsLost()) return;
    const control = controlOf(unit);
    control?.focus();
    if (control !== null && document.activeElement !== control) returnTo.current = unit;
  });

  useEffect(() => {
    if (pending) return;
    const unit = returnTo.current;
    returnTo.current = null;
    if (unit === null || !focusIsLost()) return;
    controlOf(unit)?.focus();
  }, [pending]);

  return {
    onFocus: (event) => {
      focused.current = event.target;
    },
    started: (unit) => {
      returnTo.current = unit;
      cause.current = unit;
    },
    reverted: (unit) => {
      cause.current = unit;
    },
  };
}

function controlOf(unit: UnitRef): HTMLElement | null {
  return unit.current?.querySelector<HTMLElement>(UNIT_CONTROL) ?? null;
}

function focusIsLost(): boolean {
  const active = document.activeElement;
  return active === null || active === document.body;
}

/** The ids of the texts that describe a control, or nothing when none is shown. */
function joinIds(...ids: ReadonlyArray<string | false>): string | undefined {
  const shown = ids.filter((id): id is string => id !== false);
  return shown.length > 0 ? shown.join(" ") : undefined;
}
