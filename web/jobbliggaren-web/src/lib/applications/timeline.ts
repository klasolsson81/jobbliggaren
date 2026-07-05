import { daysSince } from "@/lib/i18n/relative-time";
import type {
  ApplicationStatus,
  FollowUpChannel,
  FollowUpOutcome,
  FollowUpDto,
  NoteDto,
  StatusChangeDto,
} from "@/lib/dto/applications";

/**
 * One composed timeline event, carrying the RAW ISO timestamp (`at`) so callers
 * sort by real time and format for display separately. `kind` + payload lets the
 * consuming component resolve the Swedish label via next-intl (the helper stays
 * pure/request-context-free — §2.4 testability).
 */
export type TimelineEvent =
  | { at: string; kind: "created" }
  | { at: string; kind: "note" }
  | { at: string; kind: "followUpScheduled"; channel: FollowUpChannel }
  | { at: string; kind: "followUpOutcome"; outcome: FollowUpOutcome }
  | {
      at: string;
      kind: "statusChange";
      from: ApplicationStatus;
      to: ApplicationStatus;
    };

interface TimelineSource {
  createdAt: string;
  notes: ReadonlyArray<NoteDto>;
  followUps: ReadonlyArray<FollowUpDto>;
  statusChanges?: ReadonlyArray<StatusChangeDto>;
}

/**
 * Compose the application detail timeline, NEWEST FIRST, from REAL events only:
 * `createdAt` + each note + each follow-up (its scheduled event, plus a recorded
 * outcome event when the outcome is non-Pending and has an `outcomeAt`) + each
 * recorded status change.
 *
 * ADR 0092 D4 / CLAUDE.md §5 (never fabricate): the previous ApplicationDetail
 * synthesised a status event from `updatedAt` — that is RETIRED here. A status
 * event is emitted ONLY from a recorded `StatusChange` (a transition the system
 * actually logged), never inferred from `updatedAt`. Sorted by the raw ISO
 * timestamp (stable), not by a formatted date string.
 *
 * Shared by the read-mode drawer body and the full-page ApplicationDetail so the
 * timeline is one knowledge piece in one place (DRY / SPOT).
 */
export function composeTimeline(source: TimelineSource): TimelineEvent[] {
  const events: TimelineEvent[] = [{ at: source.createdAt, kind: "created" }];

  for (const note of source.notes) {
    events.push({ at: note.createdAt, kind: "note" });
  }

  for (const fu of source.followUps) {
    events.push({
      at: fu.scheduledAt,
      kind: "followUpScheduled",
      channel: fu.channel,
    });
    if (fu.outcome !== "Pending" && fu.outcomeAt) {
      events.push({
        at: fu.outcomeAt,
        kind: "followUpOutcome",
        outcome: fu.outcome,
      });
    }
  }

  for (const sc of source.statusChanges ?? []) {
    events.push({
      at: sc.changedAt,
      kind: "statusChange",
      from: sc.from,
      to: sc.to,
    });
  }

  return events.sort(
    (a, b) => new Date(b.at).getTime() - new Date(a.at).getTime(),
  );
}

/**
 * "N dagar i detta steg" — whole days since the LATEST recorded status transition
 * (the moment the current step began). Returns `null` when no transition has been
 * recorded (pre-timeline applications; the backend does not backfill) — the caller
 * then OMITS the day-count rather than deriving one from `updatedAt` (§5: a
 * day-count must be backed by a visible, recorded transition, mirroring the
 * cite-the-evidence discipline of the CV/matching engines). Clamped to >= 0 so a
 * future-dated transition (clock skew) never shows a negative count.
 */
export function daysInCurrentStep(
  statusChanges: ReadonlyArray<StatusChangeDto> | undefined,
  now: Date,
): number | null {
  if (statusChanges == null || statusChanges.length === 0) return null;
  const latest = statusChanges.reduce((max, sc) =>
    new Date(sc.changedAt).getTime() > new Date(max.changedAt).getTime()
      ? sc
      : max,
  );
  return Math.max(0, daysSince(latest.changedAt, now));
}
