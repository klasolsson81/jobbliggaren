"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import {
  ATTENTION_SIGNAL_BUCKET,
  ATTENTION_SIGNAL_ORDER,
  attentionReasonKey,
  isFiringSignal,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import { ApplicationRow } from "./application-row";

type FiringSignal = Exclude<ApplicationAttentionSignal, "None">;

interface AttentionCard {
  key: string;
  signal: FiringSignal;
  application: ApplicationDto;
}

// Åtgärdskort synliga innan "Visa fler" (design 2a §4: "Max 4 kort synliga
// (tweakbart)"). Enkel konstant, ingen config — gäller bara den visuella
// kapningen; inget som kräver åtgärd döljs permanent (knappen expanderar).
const VISIBLE_CARD_CAP = 4;

interface AttentionQueueProps {
  // Hela pipelinen (alla 10 grupper). Kön byggs PURT ur `attentionSignal` som
  // backend (ApplicationAttentionEvaluator, SSOT) redan har beslutat — FE
  // återimplementerar aldrig fyrningsregeln (CLAUDE.md §5 / ADR 0071).
  groups: PipelineGroupDto[];
  // Server-beräknad referenstidpunkt (page.tsx, #336-determinism), rekonstruerad
  // en gång i containern och nedtrådt hit → ApplicationRow. Aldrig new Date() här.
  now: Date;
}

/**
 * "Kräver åtgärd"-kön (design 2a §3–4) — en alltid-synlig, prioritetssorterad
 * accelerator ovanför Lista-vyn. Varje ansökan med en fyrande `attentionSignal`
 * lyfts hit som ett åtgärdskort i ett rutnät (`minmax(600px, 1fr)`), sorterat på
 * signalprioritet (ATTENTION_SIGNAL_ORDER: erbjudande → förfallen uppföljning →
 * utkast-deadline → ghost-förslag → utan-svar → tyst-efter-intervju).
 *
 * PR 5-scope (CTO-bind A1): kortet visar orsaksrad + den återbrukade
 * ApplicationRow (roll/företag/status/relativ tid); hela raden klickar → befintlig
 * detaljmodal (ADR 0053). CTA-knappen ("Följ upp"/"Slutför"/"Läs erbjudandet"
 * osv., design §11) OMITTAD — den leder till PR 6-panelen / PR 7-dialogerna som
 * ännu inte finns; en knapp utan målyta vore en falsk affordans. Även den
 * daterade urgens-taggen ("14 DGR UTAN SVAR") och "N dagar i steget" utelämnas:
 * list-DTO:n bär inget dagar-i-status-fält och determinismen fabricerar aldrig
 * data (CLAUDE.md §5). Kort-CTA + dessa landar i PR 7 med sitt maskineri.
 *
 * 2a-doktrin (ADR 0092 supersederar ADR 0085 §343): kön DUPLICERAR — appen
 * ligger kvar i sin statusgrupp i "Alla ansökningar" (listan är komplett). Ingen
 * MOVE-semantik längre.
 */
export function AttentionQueue({ groups, now }: AttentionQueueProps) {
  const tUi = useTranslations("applications.ui");
  const tAttention = useTranslations("applications.ui.attention");
  const [expanded, setExpanded] = useState(false);

  const byStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g])),
    [groups],
  );

  const cards = useMemo<AttentionCard[]>(() => {
    const items: AttentionCard[] = [];
    for (const status of PIPELINE_ORDER) {
      const group = byStatus.get(status);
      if (group == null) continue;
      for (const application of group.applications) {
        const signal = application.attentionSignal;
        if (!isFiringSignal(signal)) continue;
        items.push({ key: application.id, signal, application });
      }
    }
    // Sortera på signalprioritet (backend-enumens deklarationsordning speglad i
    // ATTENTION_SIGNAL_ORDER). Pipelineordningen ovan ger stabil sekundär­ordning
    // inom samma signal.
    const rank = new Map(ATTENTION_SIGNAL_ORDER.map((s, i) => [s, i] as const));
    return items.sort(
      (a, b) =>
        (rank.get(a.signal) ?? Number.MAX_SAFE_INTEGER) -
        (rank.get(b.signal) ?? Number.MAX_SAFE_INTEGER),
    );
  }, [byStatus]);

  const overCap = cards.length > VISIBLE_CARD_CAP;
  const visible = expanded ? cards : cards.slice(0, VISIBLE_CARD_CAP);
  const hiddenCount = cards.length - visible.length;

  return (
    <section className="jp-attentionqueue" aria-labelledby="attention-heading">
      <div className="jp-section__head jp-section__head--strong">
        <h2 id="attention-heading" className="jp-section__title">
          {tUi("queue.title")}
        </h2>
        <span className="jp-section__count">{cards.length}</span>
        <span className="jp-section__hint">{tUi("queue.sortHint")}</span>
      </div>
      <p className="jp-attentionqueue__lede">{tUi("queue.lede")}</p>

      {cards.length === 0 ? (
        <div className="jp-attentionqueue__empty">{tUi("queue.empty")}</div>
      ) : (
        <>
          <div className="jp-actioncard-grid">
            {visible.map((card) => (
              <article key={card.key} className="jp-actioncard">
                <p
                  className="jp-actioncard__reason"
                  data-signal={ATTENTION_SIGNAL_BUCKET[card.signal]}
                >
                  <span className="jp-actioncard__dot" aria-hidden="true" />
                  <span className="jp-actioncard__text">
                    {tAttention(attentionReasonKey(card.signal))}
                  </span>
                </p>
                <ApplicationRow application={card.application} now={now} />
              </article>
            ))}
          </div>
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
}
