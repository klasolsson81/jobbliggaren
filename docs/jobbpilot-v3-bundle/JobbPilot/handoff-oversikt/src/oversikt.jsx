// oversikt.jsx — Översiktsbild (notiser + sammanfattning) i civic-utility-stil
(() => {
  const { useState, useMemo } = React;
  const I = window.JpIcons;
  const { MOCK_CVS, SAVED_SEARCHES, STATUS_META } = window.JpData;

  // ── Liten datum-hjälpare (svensk lång form) ───
  const SV_WEEKDAYS = ["söndag","måndag","tisdag","onsdag","torsdag","fredag","lördag"];
  const SV_MONTHS = ["januari","februari","mars","april","maj","juni","juli","augusti","september","oktober","november","december"];
  function svDate(d) {
    return `${SV_WEEKDAYS[d.getDay()]} ${d.getDate()} ${SV_MONTHS[d.getMonth()]} ${d.getFullYear()}`;
  }

  // ── Notis-rad ─────────────────────────────────
  function NoticeRow({ n, onDismiss }) {
    return (
      <li className={`jp-notice jp-notice--${n.kind}`}>
        <span className="jp-notice__strip" aria-hidden="true" />
        <span className="jp-notice__label">{n.label}</span>
        <span className="jp-notice__text">{n.text}</span>
        <button className="jp-notice__cta" onClick={n.action}>
          {n.cta} <I.ArrowRight size={13} />
        </button>
        <span className="jp-notice__time">{n.time}</span>
        <button
          className="jp-notice__dismiss"
          aria-label="Markera som läst"
          title="Markera som läst"
          onClick={onDismiss}
        >
          <I.X size={16} />
        </button>
      </li>
    );
  }

  // ── Summary-rad ───────────────────────────────
  function SummaryRow({ label, value, onClick, highlight, hint }) {
    const isBtn = !!onClick;
    const Cmp = isBtn ? "button" : "div";
    return (
      <Cmp
        className={
          "jp-summary__row"
          + (isBtn ? " jp-summary__row--btn" : "")
          + (highlight ? " jp-summary__row--highlight" : "")
        }
        onClick={onClick}
        type={isBtn ? "button" : undefined}
      >
        <span className="jp-summary__row__label">
          {label}
          {hint && <span className="jp-summary__row__hint"> · {hint}</span>}
        </span>
        <span className="jp-summary__row__leader" aria-hidden="true" />
        <span className="jp-summary__row__value">{value}</span>
        <I.ChevronRight
          size={14}
          className="jp-summary__row__chev"
          style={isBtn ? null : { visibility: "hidden" }}
        />
      </Cmp>
    );
  }

  // ── Översiktssida ─────────────────────────────
  function OversiktPage({ applications, savedSet, onNav }) {
    const today = new Date(2026, 4, 23); // lördag 23 maj 2026
    const [dismissed, setDismissed] = useState(() => new Set());

    // Dagens planerade händelser — mock; senare från Google Calendar
    const todaysEvents = [
      {
        id: "ev-1",
        time: "10:30",
        title: "Telefonscreening — Klarna",
        where: "Rebecca Lind, rekryterare",
        source: "jobbpilot",
      },
      {
        id: "ev-2",
        time: "14:00",
        title: "Förbered intervju med Folksam IT",
        where: "30 min",
        source: "jobbpilot",
      },
    ];

    // Stats från riktig pipeline-data
    const counts = useMemo(() => {
      const inactive = new Set(["Rejected", "Withdrawn", "Accepted"]);
      return {
        active:     applications.filter((a) => !inactive.has(a.status)).length,
        submitted:  applications.filter((a) => a.status === "Submitted").length,
        ack:        applications.filter((a) => a.status === "Acknowledged").length,
        interviews: applications.filter((a) => a.status === "InterviewScheduled" || a.status === "Interviewing").length,
        offers:     applications.filter((a) => a.status === "OfferReceived").length,
        drafts:     applications.filter((a) => a.status === "Draft").length,
        rejected:   applications.filter((a) => a.status === "Rejected").length,
        ghosted:    applications.filter((a) => a.status === "Ghosted").length,
        saved:      savedSet.size,
        cvs:        MOCK_CVS.length,
        savedSearches: SAVED_SEARCHES.length,
      };
    }, [applications, savedSet]);

    // Notis-data — uppdelat i "Kräver åtgärd" och "Information"
    // (civic register-logik: prioriterade ärenden först, info sen)
    const noticesAction = [
      {
        id: "n-offer",
        kind: "success",
        label: "Erbjudande",
        text: (
          <>
            <b>Bonnier News</b> — verksamhetsutvecklare. Erbjudande väntar svar
            senast <b>27 maj</b>.
          </>
        ),
        cta: "Granska erbjudande",
        action: () => onNav("ansokningar"),
        time: "3 dagar sedan",
      },
      {
        id: "n-followup",
        kind: "warning",
        label: "Uppföljning",
        text: (
          <>
            Du har <b>{Math.max(2, counts.submitted + counts.ack)} ansökningar</b> som
            inte fått svar på över 14 dagar. Överväg att höra av dig.
          </>
        ),
        cta: "Visa ansökningar",
        action: () => onNav("ansokningar"),
        time: "i dag · 07:00",
      },
      {
        id: "n-deadline",
        kind: "warning",
        label: "Deadline",
        text: (
          <>
            <b>2 sparade annonser</b> har sista ansökningsdag denna vecka
            (<span className="jp-mono">25 maj</span>, <span className="jp-mono">27 maj</span>).
          </>
        ),
        cta: "Visa sparade",
        action: () => onNav("jobb"),
        time: "denna vecka",
      },
    ];

    const noticesInfo = [
      {
        id: "n-match",
        kind: "info",
        label: "Matchning",
        text: (
          <>
            Det finns <b>143 nya annonser</b> som matchar din profil sedan i tisdags —
            de flesta inom <em>Mjukvaru- och systemutvecklare</em>.
          </>
        ),
        cta: "Visa annonser",
        action: () => onNav("jobb"),
        time: "i dag · 08:12",
      },
      {
        id: "n-interview-confirmed",
        kind: "brand",
        label: "Intervju",
        text: (
          <>
            <b>Folksam IT</b> har bekräftat intervjutid <b>tisdag 26 maj 14:00</b>,
            digitalt möte.
          </>
        ),
        cta: "Öppna ärende",
        action: () => onNav("ansokningar"),
        time: "i går",
      },
      {
        id: "n-saved-search",
        kind: "info",
        label: "Sparad sökning",
        text: (
          <>
            <b>Remote / Distansjobb</b> har <b>4 nya träffar</b> sedan din senaste körning.
          </>
        ),
        cta: "Kör sökning",
        action: () => onNav("sokningar"),
        time: "i går",
      },
    ];

    const allNotices = [...noticesAction, ...noticesInfo];
    const visibleAction = noticesAction.filter((n) => !dismissed.has(n.id));
    const visibleInfo = noticesInfo.filter((n) => !dismissed.has(n.id));
    const visibleNotices = [...visibleAction, ...visibleInfo];

    const dismissOne = (id) =>
      setDismissed((s) => {
        const next = new Set(s);
        next.add(id);
        return next;
      });
    const dismissAll = () =>
      setDismissed(new Set(allNotices.map((n) => n.id)));

    return (
      <div className="jp-container jp-page">
        {/* Title block — civic dossier-rubrik */}
        <div className="jp-page__title-block">
          <div className="jp-oversikt__head">
            <div>
              <div className="jp-oversikt__kicker">Inloggad som Klas Olsson</div>
              <h1 className="jp-page__title">Översikt</h1>
              <p className="jp-page__lede">
                Senaste händelser och status för dina ansökningar.
              </p>
            </div>
            <div className="jp-oversikt__today">
              <div className="jp-oversikt__today__head">
                <div className="jp-oversikt__today__kicker">I dag</div>
                <div className="jp-oversikt__today__date">
                  <span className="jp-oversikt__today__day">{today.getDate()}</span>
                  <span className="jp-oversikt__today__rest">
                    <span className="jp-oversikt__today__weekday">{SV_WEEKDAYS[today.getDay()]}</span>
                    <span className="jp-oversikt__today__month">{SV_MONTHS[today.getMonth()]} {today.getFullYear()}</span>
                  </span>
                </div>
              </div>
              {todaysEvents.length === 0 ? (
                <div className="jp-oversikt__today__empty">Inget planerat i dag.</div>
              ) : (
                <ul className="jp-oversikt__today__list">
                  {todaysEvents.map((ev) => (
                    <li key={ev.id} className={`jp-oversikt__today__event jp-oversikt__today__event--${ev.source}`}>
                      <span className="jp-oversikt__today__time">{ev.time}</span>
                      <span className="jp-oversikt__today__title">{ev.title}</span>
                      {ev.where && <span className="jp-oversikt__today__where">{ev.where}</span>}
                    </li>
                  ))}
                </ul>
              )}
              <div className="jp-oversikt__today__foot">
                <I.Calendar size={12} />
                <span>Google Calendar inte synkad — visar endast JobbPilot-händelser.</span>
              </div>
            </div>
          </div>
        </div>

        {/* ── Notiser ─────────────────────────── */}
        <section className="jp-section" aria-labelledby="o-notiser">
          <div className="jp-section__head">
            <h2 className="jp-section__title" id="o-notiser">Notiser</h2>
            <span className="jp-section__count">
              senast uppdaterad <span className="jp-mono">2026-05-23 · 08:42</span>
            </span>
            <span style={{ flex: 1 }} />
            {visibleNotices.length > 0 && (
              <button
                className="jp-btn jp-btn--ghost jp-btn--sm"
                onClick={dismissAll}
              >
                <I.Check size={14} /> Markera alla som lästa
              </button>
            )}
          </div>

          {visibleNotices.length === 0 ? (
            <div className="jp-empty">
              <div className="jp-empty__title">Inga olästa notiser</div>
              Inkorgen är tom. Du får besked så snart det händer något i ditt ärende.
            </div>
          ) : (
            <>
              {visibleAction.length > 0 && (
                <>
                  <div className="jp-notice-group">
                    <span className="jp-notice-group__title">Kräver åtgärd</span>
                    <span className="jp-notice-group__count">{visibleAction.length}</span>
                  </div>
                  <ul className="jp-notice-list">
                    {visibleAction.map((n) => (
                      <NoticeRow key={n.id} n={n} onDismiss={() => dismissOne(n.id)} />
                    ))}
                  </ul>
                </>
              )}
              {visibleInfo.length > 0 && (
                <>
                  <div className="jp-notice-group jp-notice-group--info">
                    <span className="jp-notice-group__title">Information</span>
                    <span className="jp-notice-group__count">{visibleInfo.length}</span>
                  </div>
                  <ul className="jp-notice-list">
                    {visibleInfo.map((n) => (
                      <NoticeRow key={n.id} n={n} onDismiss={() => dismissOne(n.id)} />
                    ))}
                  </ul>
                </>
              )}
            </>
          )}
        </section>

        {/* ── Sammanfattning ──────────────────── */}
        <section className="jp-section" aria-labelledby="o-summary" style={{ marginTop: 40 }}>
          <div className="jp-section__head">
            <h2 className="jp-section__title" id="o-summary">Sammanfattning</h2>
            <span className="jp-section__count">
              registrerat per <span className="jp-mono">2026-05-23</span>
            </span>
          </div>

          <div className="jp-summary">
            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Ansökningar</div>
              <SummaryRow
                label="Aktiva ansökningar"
                value={counts.active}
                onClick={() => onNav("ansokningar")}
              />
              <SummaryRow
                label="Utkast"
                value={counts.drafts}
                onClick={() => onNav("ansokningar")}
              />
              <SummaryRow
                label="Intervjuer bokade"
                value={counts.interviews}
                highlight
                onClick={() => onNav("ansokningar")}
              />
              <SummaryRow
                label="Erbjudanden"
                value={counts.offers}
                highlight
                onClick={() => onNav("ansokningar")}
              />
              <SummaryRow
                label="Avslag"
                value={counts.rejected}
              />
              <SummaryRow
                label="Inget svar"
                value={counts.ghosted}
                hint="över 30 dagar"
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Bevakning</div>
              <SummaryRow
                label="Sparade annonser"
                value={counts.saved}
                onClick={() => onNav("jobb")}
              />
              <SummaryRow
                label="Sparade sökningar"
                value={counts.savedSearches}
                onClick={() => onNav("sokningar")}
              />
              <SummaryRow
                label="Nya matchningar i dag"
                value={28}
                hint="profil"
                onClick={() => onNav("jobb")}
              />
              <SummaryRow
                label="Aktiva annonser totalt"
                value={"45 580"}
              />
              <SummaryRow
                label="Senaste sökning"
                value="Backend Sthlm"
                onClick={() => onNav("sokningar")}
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Underlag</div>
              <SummaryRow
                label="CV-varianter"
                value={counts.cvs}
                onClick={() => onNav("cv")}
              />
              <SummaryRow
                label="Personliga brev"
                value={4}
              />
              <SummaryRow
                label="Senast uppdaterat CV"
                value={"13 maj"}
                onClick={() => onNav("cv")}
              />
              <SummaryRow
                label="Sökstart"
                value={"6 apr"}
                hint="46 dagar"
              />
            </div>
          </div>
        </section>

        {/* Notiser & sammanfattning slut. Åtgärder finns redan i headern. */}
      </div>
    );
  }

  window.JpOversikt = { OversiktPage };
})();
