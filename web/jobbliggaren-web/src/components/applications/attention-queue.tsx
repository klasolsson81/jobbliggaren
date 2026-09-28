"use client";

import { memo, useId, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  ATTENTION_SIGNAL_BUCKET,
  ATTENTION_SIGNAL_ORDER,
  attentionReasonKey,
  isFiringSignal,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import { urgencyTagFor } from "@/lib/applications/urgency";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import { adIdentityOf } from "./ad-identity";
import {
  useApplicationActions,
  useApplicationPending,
} from "./application-actions";
import type { RowAction } from "./use-row-actions";
import { useUrgencyValue } from "./use-urgency-label";

type FiringSignal = Exclude<ApplicationAttentionSignal, "None">;

interface AttentionItem {
  key: string;
  signal: FiringSignal;
  application: ApplicationDto;
}

// Rader synliga innan "Visa N till" (design 2a §4: "Max 4 kort synliga
// (tweakbart)"). Enkel konstant, ingen config — gäller bara den visuella
// kapningen; inget som kräver åtgärd döljs permanent (knappen expanderar).
const VISIBLE_ROW_CAP = 4;

interface AttentionQueueProps {
  // Hela pipelinen (alla 10 grupper). Kön byggs PURT ur `attentionSignal` som
  // backend (ApplicationAttentionEvaluator, SSOT) redan har beslutat — FE
  // återimplementerar aldrig fyrningsregeln (CLAUDE.md §5 / ADR 0071).
  groups: PipelineGroupDto[];
  // Server-beräknad referenstidpunkt (page.tsx, #336-determinism), rekonstruerad
  // en gång i containern och nedtrådt hit → raderna. Aldrig new Date() här.
  now: Date;
}

/**
 * "Kräver åtgärd"-kön (design 2a §3–4) — en alltid-synlig, prioritetssorterad
 * accelerator ovanför Lista-vyn. Varje ansökan med en fyrande `attentionSignal`
 * lyfts hit som en liggarrad (DESIGN.md §6, #1827 M1: ingen låda per ärende),
 * sorterad på signalprioritet (ATTENTION_SIGNAL_ORDER: erbjudande → förfallen
 * uppföljning → utkast-deadline → ghost-förslag → utan-svar → tyst-efter-intervju).
 *
 * 2a-doktrin (ADR 0092 supersederar ADR 0085 §343): kön DUPLICERAR — appen
 * ligger kvar i sin statusgrupp i "Alla ansökningar" (listan är komplett). Ingen
 * MOVE-semantik längre.
 */
export const AttentionQueue = memo(function AttentionQueue({
  groups,
  now,
}: AttentionQueueProps) {
  const tUi = useTranslations("applications.ui");
  const [expanded, setExpanded] = useState(false);
  // Trådar per-rad `pending` ned (d4): raderna prenumererar aldrig själva på Set:et.
  const pendingIds = useApplicationPending();

  const byStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g])),
    [groups],
  );

  const items = useMemo<AttentionItem[]>(() => {
    const out: AttentionItem[] = [];
    for (const status of PIPELINE_ORDER) {
      const group = byStatus.get(status);
      if (group == null) continue;
      for (const application of group.applications) {
        const signal = application.attentionSignal;
        if (!isFiringSignal(signal)) continue;
        out.push({ key: application.id, signal, application });
      }
    }
    // Sortera på signalprioritet (backend-enumens deklarationsordning speglad i
    // ATTENTION_SIGNAL_ORDER). Pipelineordningen ovan ger stabil sekundär­ordning
    // inom samma signal.
    const rank = new Map(ATTENTION_SIGNAL_ORDER.map((s, i) => [s, i] as const));
    return out.sort(
      (a, b) =>
        (rank.get(a.signal) ?? Number.MAX_SAFE_INTEGER) -
        (rank.get(b.signal) ?? Number.MAX_SAFE_INTEGER),
    );
  }, [byStatus]);

  const overCap = items.length > VISIBLE_ROW_CAP;
  const visible = expanded ? items : items.slice(0, VISIBLE_ROW_CAP);
  const hiddenCount = items.length - visible.length;

  return (
    <section className="jp-attentionqueue" aria-labelledby="attention-heading">
      <div className="jp-section__head jp-section__head--strong">
        <h2 id="attention-heading" className="jp-section__title">
          {tUi("queue.title")}
        </h2>
        <span className="jp-section__count">{items.length}</span>
      </div>

      {items.length === 0 ? (
        <p className="jp-attentionqueue__empty">{tUi("queue.empty")}</p>
      ) : (
        <>
          <ol className="jp-attentionqueue__list">
            {visible.map((item) => (
              <AttentionQueueRow
                key={item.key}
                signal={item.signal}
                application={item.application}
                now={now}
                pending={pendingIds.has(item.application.id)}
              />
            ))}
          </ol>
          {overCap && (
            <button
              type="button"
              className="jp-btn jp-btn--secondary jp-attentionqueue__more"
              onClick={() => setExpanded((v) => !v)}
            >
              {expanded
                ? tUi("queue.showFewer")
                : tUi("queue.showMore", { count: hiddenCount })}
            </button>
          )}
        </>
      )}
    </section>
  );
});

// clientY-fallback för dialogankaret: ett programmatiskt klick (utan verklig
// pekare) faller tillbaka på knappens position, aldrig 0.
const anchorY = (e: React.MouseEvent<HTMLButtonElement>): number =>
  e.clientY > 0 ? e.clientY : e.currentTarget.getBoundingClientRect().top;

interface AttentionQueueRowProps {
  signal: FiringSignal;
  application: ApplicationDto;
  now: Date;
  pending: boolean;
}

/**
 * A row in the queue (#1827 M1). Line 1: the signal's kicker in its colour + the
 * value the urgency helper derives for the signal (a signal without a value shows
 * only the kicker). Line 2: the title (the row's only link, stretched over the row
 * with ::after) and the company. On the right, the row's §11 CTAs. Status, days in
 * the step, the event line and the urgency tag are carried by the Lista row, not
 * the queue.
 */
function AttentionQueueRow({
  signal,
  application,
  now,
  pending,
}: AttentionQueueRowProps) {
  const tUi = useTranslations("applications.ui");
  const tAttention = useTranslations("applications.ui.attention");
  const router = useRouter();
  const { transition, openFinishDraft, openLogFollowUp } =
    useApplicationActions();
  const contextId = useId();

  // #892: strukturell identitet + borttagen-markör (lockstep med Lista-raden).
  const { adRemoved, title: adTitle, company } = adIdentityOf(application.jobAd);
  const title =
    adTitle ?? tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });
  const kicker = tAttention(attentionReasonKey(signal));
  const value = useUrgencyValue(urgencyTagFor(application, now));
  const description = [
    kicker,
    value,
    company,
    adRemoved ? tUi("adRemoved.tag") : null,
  ]
    .filter((part): part is string => part != null)
    .join(", ");

  // §11-signal → primär/sekundär CTA (prototypens urgency()-karta = facit).
  // "Förbered intervjun" (interview-near) är deferrad med sin signal (ADR 0092 D5 —
  // datumfältet finns inte).
  const openDetail = (label: string): RowAction => ({
    label,
    // Samma väg som radklicket: soft-nav → den centrerade route-modalen.
    onClick: () => router.push(`/ansokningar/${application.id}`),
  });
  const followUp = (label: string): RowAction => ({
    label,
    onClick: (e) => openLogFollowUp(application, anchorY(e)),
  });
  const markGhosted = (label: string): RowAction => ({
    label,
    onClick: () => transition(application, "Ghosted"),
  });
  const actions = ((): { primary: RowAction; secondary?: RowAction } => {
    switch (signal) {
      case "OfferAwaitingReply":
        return {
          primary: openDetail(tUi("queueCta.readOffer")),
          secondary: {
            label: tUi("queueCta.accept"),
            onClick: () => transition(application, "Accepted"),
          },
        };
      case "OverdueFollowUp":
        // The signal is a Pending follow-up past its date, and logging a new
        // contact leaves it Pending; its outcome form lives in the detail.
        return { primary: openDetail(tUi("queueCta.recordOutcome")) };
      case "DraftDeadlineApproaching":
        return {
          primary: {
            label: tUi("row.finishAndSend"),
            onClick: (e) => openFinishDraft(application, anchorY(e)),
          },
        };
      case "GhostSuggested":
        return {
          primary: markGhosted(tUi("queueCta.markGhosted")),
          secondary: followUp(tUi("queueCta.followUpAgain")),
        };
      case "NoResponseNudge":
        return {
          primary: followUp(tUi("queueCta.followUp")),
          secondary: markGhosted(tUi("queueCta.markGhosted")),
        };
      case "SilentAfterInterview":
        return { primary: followUp(tUi("queueCta.followUp")) };
    }
  })();

  return (
    <li className="jp-attentionqueue__row">
      <div className="jp-attentionqueue__body">
        <p className="jp-attentionqueue__signal">
          <span
            className="jp-attentionqueue__kicker"
            data-signal={ATTENTION_SIGNAL_BUCKET[signal]}
          >
            {kicker}
          </span>
          {value != null && (
            <span className="jp-attentionqueue__value">{value}</span>
          )}
        </p>
        <div className="jp-attentionqueue__line">
          <h3 className="jp-app__title">
            <Link
              href={`/ansokningar/${application.id}`}
              className="jp-app__rowlink"
              aria-describedby={contextId}
            >
              {title}
            </Link>
          </h3>
          {company != null && (
            <span className="jp-attentionqueue__company">{company}</span>
          )}
          {adRemoved && (
            <span className="jp-tag jp-tag--neutral">{tUi("adRemoved.tag")}</span>
          )}
        </div>
        <span id={contextId} className="sr-only">
          {description}
        </span>
      </div>

      <div className="jp-attentionqueue__actions">
        <button
          type="button"
          className="jp-rowbtn jp-rowbtn--emphasis"
          disabled={pending}
          onClick={actions.primary.onClick}
        >
          {actions.primary.label}
        </button>
        {actions.secondary != null && (
          <button
            type="button"
            className="jp-rowbtn"
            disabled={pending}
            onClick={actions.secondary.onClick}
          >
            {actions.secondary.label}
          </button>
        )}
      </div>
    </li>
  );
}
