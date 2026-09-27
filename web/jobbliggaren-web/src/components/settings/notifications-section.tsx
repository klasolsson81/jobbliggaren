"use client";

// "use client": the section holds the two consent flags and the shared cadence, saves each change
// optimistically inside a transition, reverts on failure, and shows a receipt or an alert under the
// control that started the write. None of that runs in a Server Component.

import { type RefObject, useEffect, useId, useRef, useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
import type { DigestCadence } from "@/lib/dto/me";
import {
  updateDigestCadenceAction,
  updateFollowedCompanyNotificationConsentAction,
  updateNotificationConsentAction,
} from "@/lib/actions/me";
import { ToggleRow } from "@/components/ui/toggle-row";
import { Segment, type SegmentOption } from "@/components/ui/segment";

/** The outcome of one write, owned by the control that started it (#1391). */
type WriteOutcome = { ok: true; at: Date } | { ok: false; error: string };

const SECTION_ID = "notiser";

/** A switch, or the checked option of a segment: the control a group's write started from. */
const GROUP_CONTROL = '[role="switch"], [role="radiogroup"] button[aria-checked="true"]';

interface NotificationsSectionProps {
  /** The saved background-match consent (default off, GDPR Art. 6(1)(a)/7). */
  readonly initialMatchEnabled: boolean;
  /** The saved followed-company mail consent (default off, Art. 6(1)(a)/7). */
  readonly initialFollowEnabled: boolean;
  readonly initialCadence: DigestCadence;
}

/**
 * The Notiser card of /mina-sidor (#1891): the background-match consent, the followed-company mail
 * consent, and the one digest cadence both mails follow (ADR 0087 D2), under both switches. Every
 * value has one owner here, so no text has to point from one control to another. Each switch's
 * description is its legally bound consent text (ADR 0144 Decision 4, rows 7 and 8).
 *
 * Three writes, one per control: each request carries only its own control's value (`{enabled}` for
 * either switch, `{cadence}` for the selector), and each control has its own pending state.
 */
export function NotificationsSection({
  initialMatchEnabled,
  initialFollowEnabled,
  initialCadence,
}: NotificationsSectionProps) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages.minaSidor");
  const cadenceHintId = useId();
  const matchErrorId = useId();
  const followErrorId = useId();
  const cadenceErrorId = useId();

  const [matchEnabled, setMatchEnabled] = useState(initialMatchEnabled);
  const [followEnabled, setFollowEnabled] = useState(initialFollowEnabled);
  const [cadence, setCadence] = useState<DigestCadence>(initialCadence);

  const [isSavingMatch, startMatchSave] = useTransition();
  const [isSavingFollow, startFollowSave] = useTransition();
  const [isSavingCadence, startCadenceSave] = useTransition();

  // Each control's outcome is cleared when its own write starts, so a pending write hides only the
  // receipt of the control that started it.
  const [matchOutcome, setMatchOutcome] = useState<WriteOutcome | null>(null);
  const [followOutcome, setFollowOutcome] = useState<WriteOutcome | null>(null);

  const sectionRef = useRef<HTMLElement>(null);
  const matchGroup = useRef<HTMLDivElement>(null);
  const followGroup = useRef<HTMLDivElement>(null);
  const cadenceGroup = useRef<HTMLDivElement>(null);

  // A control is `disabled` while its write is pending, and Chromium then drops focus to <body>
  // (#1391 measurement).
  const returnRef = useFocusReturn(isSavingMatch || isSavingFollow || isSavingCadence);

  useEffect(() => {
    if (window.location.hash === `#${SECTION_ID}`) sectionRef.current?.scrollIntoView();
  }, []);

  const cadenceOptions: ReadonlyArray<SegmentOption<DigestCadence>> = [
    { value: "Daily", label: t("backgroundMatch.cadenceDaily") },
    { value: "Weekly", label: t("backgroundMatch.cadenceWeekly") },
  ];
  const cadenceOpen = matchEnabled || followEnabled;

  // Opening or closing the selector clears its message; a reply shows only if the selector is open
  // when it lands.
  const [cadenceView, setCadenceView] = useState<{ open: boolean; outcome: WriteOutcome | null }>({
    open: cadenceOpen,
    outcome: null,
  });
  if (cadenceView.open !== cadenceOpen) {
    setCadenceView({ open: cadenceOpen, outcome: null });
  }

  function onMatchToggle(nextEnabled: boolean) {
    const previous = matchEnabled;
    returnRef.current = matchGroup.current;
    setMatchOutcome(null);
    setMatchEnabled(nextEnabled);
    startMatchSave(async () => {
      const result = await updateNotificationConsentAction({ enabled: nextEnabled });
      if (result.success) {
        setMatchOutcome({ ok: true, at: new Date() });
      } else {
        setMatchEnabled(previous);
        setMatchOutcome({ ok: false, error: result.error });
      }
    });
  }

  function onFollowToggle(nextEnabled: boolean) {
    const previous = followEnabled;
    returnRef.current = followGroup.current;
    setFollowOutcome(null);
    setFollowEnabled(nextEnabled);
    startFollowSave(async () => {
      const result = await updateFollowedCompanyNotificationConsentAction({
        enabled: nextEnabled,
      });
      if (result.success) {
        setFollowOutcome({ ok: true, at: new Date() });
      } else {
        setFollowEnabled(previous);
        setFollowOutcome({ ok: false, error: result.error });
      }
    });
  }

  function onCadenceChange(nextCadence: DigestCadence) {
    const previous = cadence;
    returnRef.current = cadenceGroup.current;
    setCadenceView((view) => ({ ...view, outcome: null }));
    setCadence(nextCadence);
    startCadenceSave(async () => {
      const result = await updateDigestCadenceAction({ cadence: nextCadence });
      if (!result.success) setCadence(previous);
      const outcome: WriteOutcome = result.success
        ? { ok: true, at: new Date() }
        : { ok: false, error: result.error };
      setCadenceView((view) => (view.open ? { ...view, outcome } : view));
    });
  }

  return (
    <section ref={sectionRef} className="jp-card" id={SECTION_ID}>
      <h2 className="jp-card__title">{tp("sections.notiser")}</h2>

      <div ref={matchGroup} className="jp-settings-group">
        <ToggleRow
          label={t("backgroundMatch.toggleLabel")}
          description={t("backgroundMatch.toggleDescription")}
          checked={matchEnabled}
          onChange={onMatchToggle}
          disabled={isSavingMatch}
          describedBy={matchOutcome?.ok === false ? matchErrorId : undefined}
        />
        <Outcome id={matchErrorId} outcome={matchOutcome} />
      </div>

      <div ref={followGroup} className="jp-settings-group">
        <ToggleRow
          label={t("followedCompanyNotifications.toggleLabel")}
          description={t("followedCompanyNotifications.toggleDescription")}
          checked={followEnabled}
          onChange={onFollowToggle}
          disabled={isSavingFollow}
          describedBy={followOutcome?.ok === false ? followErrorId : undefined}
        />
        <Outcome id={followErrorId} outcome={followOutcome} />
      </div>

      <div ref={cadenceGroup} className="jp-settings-group jp-settings-field">
        <span className="jp-settings-field__label">{t("backgroundMatch.cadenceLabel")}</span>
        <Segment
          aria-label={t("backgroundMatch.cadenceLabel")}
          aria-describedby={joinIds(
            !cadenceOpen && cadenceHintId,
            cadenceView.outcome?.ok === false && cadenceErrorId,
          )}
          value={cadence}
          onChange={onCadenceChange}
          options={cadenceOptions}
          disabled={!cadenceOpen || isSavingCadence}
        />
        {!cadenceOpen && (
          <p id={cadenceHintId} className="jp-settings-field__hint">
            {t("backgroundMatch.cadenceHintDisabled")}
          </p>
        )}
        <Outcome id={cadenceErrorId} outcome={cadenceView.outcome} />
      </div>
    </section>
  );
}

/**
 * The ref a write stores its group in. Once `pending` clears, focus goes back to that group's
 * control, if focus was lost meanwhile.
 */
function useFocusReturn(pending: boolean): RefObject<HTMLDivElement | null> {
  const returnRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (pending) return;
    const target = returnRef.current;
    returnRef.current = null;
    if (!target) return;
    const active = document.activeElement;
    if (active !== null && active !== document.body) return;
    target.querySelector<HTMLElement>(GROUP_CONTROL)?.focus();
  }, [pending]);
  return returnRef;
}

/**
 * The receipt or the refusal under one control. Mutually exclusive live regions, the shape the
 * settings cards have shipped since #1391: a refusal is an assertive alert, otherwise a polite status
 * that stays mounted so a later receipt is announced.
 */
function Outcome({ id, outcome }: { id: string; outcome: WriteOutcome | null }) {
  const t = useTranslations("settings");
  const format = useFormatter();
  if (outcome?.ok === false) {
    return (
      <p id={id} role="alert" className="jp-settings-group__message text-body-sm text-danger-600">
        {outcome.error}
      </p>
    );
  }
  return (
    <p
      role="status"
      aria-live="polite"
      className="jp-settings-group__message text-body-sm text-text-secondary"
    >
      {outcome?.ok ? t("savedAt", { time: formatTime(format, outcome.at) }) : ""}
    </p>
  );
}

/** The ids of the texts that describe a control, or nothing when none is shown. */
function joinIds(...ids: ReadonlyArray<string | false>): string | undefined {
  const shown = ids.filter((id): id is string => id !== false);
  return shown.length > 0 ? shown.join(" ") : undefined;
}
