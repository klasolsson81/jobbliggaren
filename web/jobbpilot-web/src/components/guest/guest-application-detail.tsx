import type {
  GuestApplicationStatus,
  GuestMockApplication,
} from "@/lib/guest/mock-data";

// F-Pre Punkt 5b 2026-05-24 — egen gäst-variant av ApplicationDetail (CTO
// Beslut 6). Live `<ApplicationDetail>` exponerar muterande knappar
// (StatusEditCard, AddNoteForm, AddFollowUpForm, RecordFollowUpOutcomeForm)
// som anropar BE. Gäst får INTE mutera (Klas-direktiv §F) — adapter med
// "passa undefined"-mönster funkar inte här (komponenten har ingen sådan
// prop). Egen presentational variant utan mutationsformulär.

const STATUS_BADGE: Record<GuestApplicationStatus, { label: string; pill: string }> = {
  Draft: { label: "Utkast", pill: "jp-pill jp-pill--neutral" },
  Submitted: { label: "Inskickad", pill: "jp-pill jp-pill--info" },
  Interview: { label: "Intervju", pill: "jp-pill jp-pill--success" },
  Offer: { label: "Erbjudande", pill: "jp-pill jp-pill--success" },
  Rejected: { label: "Avslag", pill: "jp-pill jp-pill--warning" },
};

export function GuestApplicationDetail({
  application,
}: {
  application: GuestMockApplication;
}) {
  const status = STATUS_BADGE[application.status];

  return (
    <div className="jp-modal__body">
      <span className={status.pill} style={{ alignSelf: "flex-start" }}>
        <span className="jp-pill__dot" aria-hidden="true" />
        {status.label}
      </span>

      <dl className="jp-modal__metarow">
        <div className="jp-modal__metaitem">
          <dt>Företag</dt>
          <dd>{application.company}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>Roll</dt>
          <dd>{application.role}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>Källa</dt>
          <dd>{application.source}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>Senast uppdaterad</dt>
          <dd>{application.updatedAtLabel}</dd>
        </div>
      </dl>

      <p className="text-body-sm text-text-secondary" style={{ marginTop: 16 }}>
        Detta är en exempelansökan i demoläget. Logga in eller anmäl dig till
        väntelistan för att skapa, redigera och följa upp egna ansökningar
        med statusbyten, anteckningar och uppföljningar.
      </p>
    </div>
  );
}
