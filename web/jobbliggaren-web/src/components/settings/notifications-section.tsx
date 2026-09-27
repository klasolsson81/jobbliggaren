"use client";

// "use client": the section holds the two consent flags and the shared cadence, saves each change
// optimistically inside a transition, reverts on failure, and shows a receipt or an alert under the
// control that started the write. None of that runs in a Server Component.

import { type RefObject, useEffect, useId, useRef, useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatTime } from "@/lib/i18n/format";
import type { DigestCadence } from "@/lib/dto/me";
import {
  updateFollowedCompanyNotificationConsentAction,
  updateNotificationConsentAction,
} from "@/lib/actions/me";
import { ToggleRow } from "@/components/ui/toggle-row";
import { Segment, type SegmentOption } from "@/components/ui/segment";

/** The outcome of one write, owned by the control that started it (#1391). */
type WriteOutcome = { ok: true; at: Date } | { ok: false; error: string };

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
 * Two endpoints, as before. The match switch and the cadence both write
 * `PUT /me/notification-consent` as a full replace of `{enabled, cadence}`, and they share ONE
 * pending state. Next sends a client's Server Actions one at a time, so the two writes cannot cross
 * on the wire; what they could do is build a second body while the first is pending, and that body
 * carries the first one's optimistic `enabled`. Were the first refused and reverted, the queued
 * second would write the consent back, and the server would hold a consent the switch shows as off
 * (Art. 4(11)). Disabling both controls while one of them saves means no second body is built.
 *
 * Saving the cadence while the match switch is off sends `{enabled: false, cadence}`, which is
 * GDPR-safe because `JobSeeker.UpdateNotificationConsent` stamps an Art. 7(3) withdrawal only on the
 * on-to-off transition. The followed-company switch writes
 * `PUT /me/followed-company-notification-consent` with `{enabled}` and has its own pending state.
 */
export function NotificationsSection({
  initialMatchEnabled,
  initialFollowEnabled,
  initialCadence,
}: NotificationsSectionProps) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages.minaSidor");
  const cadenceHintId = useId();

  const [matchEnabled, setMatchEnabled] = useState(initialMatchEnabled);
  const [followEnabled, setFollowEnabled] = useState(initialFollowEnabled);
  const [cadence, setCadence] = useState<DigestCadence>(initialCadence);

  const [isSavingConsent, startConsentSave] = useTransition();
  const [isSavingFollow, startFollowSave] = useTransition();

  // Each control's outcome is cleared when its own write starts, so a pending write hides only the
  // receipt of the control that started it.
  const [matchOutcome, setMatchOutcome] = useState<WriteOutcome | null>(null);
  const [followOutcome, setFollowOutcome] = useState<WriteOutcome | null>(null);
  const [cadenceOutcome, setCadenceOutcome] = useState<WriteOutcome | null>(null);

  const matchGroup = useRef<HTMLDivElement>(null);
  const followGroup = useRef<HTMLDivElement>(null);
  const cadenceGroup = useRef<HTMLDivElement>(null);

  // A control is `disabled` while its write is pending, and Chromium then drops focus to <body>
  // (#1391 measurement). When the write settles, focus goes back to the control that started it,
  // saved or refused, so a keyboard or screen-reader user stays where they were. Nothing moves if
  // focus went somewhere else in the meantime.
  const consentReturnRef = useFocusReturn(isSavingConsent);
  const followReturnRef = useFocusReturn(isSavingFollow);

  const cadenceOptions: ReadonlyArray<SegmentOption<DigestCadence>> = [
    { value: "Daily", label: t("backgroundMatch.cadenceDaily") },
    { value: "Weekly", label: t("backgroundMatch.cadenceWeekly") },
  ];
  const cadenceOpen = matchEnabled || followEnabled;

  function saveConsent(
    next: { enabled: boolean; cadence: DigestCadence },
    group: RefObject<HTMLDivElement | null>,
    revert: () => void,
    report: (outcome: WriteOutcome | null) => void,
  ) {
    consentReturnRef.current = group.current;
    report(null);
    startConsentSave(async () => {
      const result = await updateNotificationConsentAction(next);
      if (result.success) {
        report({ ok: true, at: new Date() });
      } else {
        revert();
        report({ ok: false, error: result.error });
      }
    });
  }

  function onMatchToggle(nextEnabled: boolean) {
    const previous = matchEnabled;
    setMatchEnabled(nextEnabled);
    saveConsent(
      { enabled: nextEnabled, cadence },
      matchGroup,
      () => setMatchEnabled(previous),
      setMatchOutcome,
    );
  }

  function onCadenceChange(nextCadence: DigestCadence) {
    const previous = cadence;
    setCadence(nextCadence);
    saveConsent(
      { enabled: matchEnabled, cadence: nextCadence },
      cadenceGroup,
      () => setCadence(previous),
      setCadenceOutcome,
    );
  }

  function onFollowToggle(nextEnabled: boolean) {
    const previous = followEnabled;
    followReturnRef.current = followGroup.current;
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

  return (
    <section className="jp-card" id="notiser">
      <h2 className="jp-card__title">{tp("sections.notiser")}</h2>

      <div ref={matchGroup} className="jp-settings-group">
        <ToggleRow
          label={t("backgroundMatch.toggleLabel")}
          description={t("backgroundMatch.toggleDescription")}
          checked={matchEnabled}
          onChange={onMatchToggle}
          disabled={isSavingConsent}
        />
        <Outcome outcome={matchOutcome} />
      </div>

      <div ref={followGroup} className="jp-settings-group">
        <ToggleRow
          label={t("followedCompanyNotifications.toggleLabel")}
          description={t("followedCompanyNotifications.toggleDescription")}
          checked={followEnabled}
          onChange={onFollowToggle}
          disabled={isSavingFollow}
        />
        <Outcome outcome={followOutcome} />
      </div>

      <div ref={cadenceGroup} className="jp-settings-group jp-settings-field">
        <span className="jp-settings-field__label">{t("backgroundMatch.cadenceLabel")}</span>
        <Segment
          aria-label={t("backgroundMatch.cadenceLabel")}
          aria-describedby={cadenceOpen ? undefined : cadenceHintId}
          value={cadence}
          onChange={onCadenceChange}
          options={cadenceOptions}
          disabled={!cadenceOpen || isSavingConsent}
        />
        {!cadenceOpen && (
          <p id={cadenceHintId} className="jp-settings-field__hint">
            {t("backgroundMatch.cadenceHintDisabled")}
          </p>
        )}
        <Outcome outcome={cadenceOutcome} />
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
function Outcome({ outcome }: { outcome: WriteOutcome | null }) {
  const t = useTranslations("settings");
  const format = useFormatter();
  if (outcome?.ok === false) {
    return (
      <p role="alert" className="text-body-sm text-danger-600">
        {outcome.error}
      </p>
    );
  }
  return (
    <p role="status" aria-live="polite" className="text-body-sm text-text-secondary">
      {outcome?.ok ? t("savedAt", { time: formatTime(format, outcome.at) }) : ""}
    </p>
  );
}
