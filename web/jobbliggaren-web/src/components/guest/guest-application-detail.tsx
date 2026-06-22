import { useTranslations } from "next-intl";
import type { ApplicationStatus } from "@/lib/types/applications";
import {
  applicationStatusLabel,
  getStatusPillClass,
} from "@/lib/applications/status";
import type {
  GuestApplicationStatus,
  GuestMockApplication,
} from "@/lib/guest/mock-data";

// F-Pre Punkt 5b 2026-05-24 — egen gäst-variant av ApplicationDetail (CTO
// Beslut 6). Live `<ApplicationDetail>` exponerar muterande knappar
// (StatusEditCard, AddNoteForm, AddFollowUpForm, RecordFollowUpOutcomeForm)
// som anropar BE. Gäst får INTE mutera (Klas-direktiv §F). Egen
// presentational variant utan mutationsformulär.
//
// design-reviewer M1 2026-05-24: status-pill mappar nu till live
// `ApplicationStatus` + använder `getStatusPillClass` + `getStatusLabel` så
// färgkodningen är identisk live/gäst. Tidigare hand-roll-mappning gav
// "warning" för Rejected (live = "danger") och "info" för Submitted
// (live = "brand") — funktionell felsignalering bröt
// memory `project_crossref_badge_status`. SECTION_LABEL-rubrik tillagd för
// typografisk paritet med live <ApplicationDetail> (m6).

const SECTION_LABEL_STYLE: React.CSSProperties = {
  fontSize: 13,
  fontWeight: 700,
  textTransform: "uppercase",
  letterSpacing: "0.06em",
  color: "var(--jp-ink-2)",
  marginBottom: 10,
  marginTop: 16,
};

// GuestApplicationStatus är subset av live ApplicationStatus, mappad så
// färg + etikett blir identiska (design-reviewer M1).
const GUEST_TO_LIVE_STATUS: Record<GuestApplicationStatus, ApplicationStatus> = {
  Draft: "Draft",
  Submitted: "Submitted",
  Interview: "InterviewScheduled",
  Offer: "OfferReceived",
  Rejected: "Rejected",
};

export function GuestApplicationDetail({
  application,
}: {
  application: GuestMockApplication;
}) {
  // Synchronous next-intl translators — keeps this a non-async RSC.
  // `t` bär enum-etiketten (applicationStatusLabel), `tg` bär gäst-detaljens copy.
  const t = useTranslations("applications.enums");
  const tg = useTranslations("guest");
  const liveStatus = GUEST_TO_LIVE_STATUS[application.status];

  return (
    <div className="jp-modal__body">
      <span
        className={getStatusPillClass(liveStatus)}
        style={{ alignSelf: "flex-start" }}
      >
        <span className="jp-pill__dot" aria-hidden="true" />
        {applicationStatusLabel(t, liveStatus)}
      </span>

      <dl className="jp-modal__metarow">
        <div className="jp-modal__metaitem">
          <dt>{tg("detail.company")}</dt>
          <dd>{application.company}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>{tg("detail.role")}</dt>
          <dd>{application.role}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>{tg("detail.source")}</dt>
          <dd>{application.source}</dd>
        </div>
        <div className="jp-modal__metaitem">
          <dt>{tg("detail.lastUpdated")}</dt>
          <dd>{application.updatedAtLabel}</dd>
        </div>
      </dl>

      <div style={SECTION_LABEL_STYLE}>{tg("detail.aboutHeading")}</div>
      <p className="text-body-sm text-text-secondary">{tg("detail.aboutBody")}</p>
    </div>
  );
}
