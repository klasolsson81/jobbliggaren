// pages.jsx — Mina ansökningar, Sparade sökningar, CV, Konto, Auth
(() => {
  const { useState, useMemo } = React;
  const I = window.JpIcons;
  const {
    MOCK_APPLICATIONS, STATUS_META, STATUS_ORDER, MOCK_CVS, SAVED_SEARCHES,
  } = window.JpData;

  // ── Mina ansökningar ─────────────────────────────
  function AnsokningarPage({ applications, onNav }) {
    const apps = applications.length > 0 ? applications : MOCK_APPLICATIONS;
    const [active, setActive] = useState("All");
    const [openApp, setOpenApp] = useState(null);

    const counts = useMemo(() => {
      const c = { All: apps.length };
      STATUS_ORDER.forEach((s) => (c[s] = 0));
      apps.forEach((a) => { c[a.status] = (c[a.status] || 0) + 1; });
      return c;
    }, [apps]);

    const visible = active === "All" ? apps : apps.filter((a) => a.status === active);
    const grouped = useMemo(() => {
      const g = {};
      visible.forEach((a) => {
        if (!g[a.status]) g[a.status] = [];
        g[a.status].push(a);
      });
      return g;
    }, [visible]);

    const tabs = ["All", ...STATUS_ORDER.filter((s) => counts[s] > 0)];

    return (
      <>
      <div className="jp-container jp-page">
        <div className="jp-page__title-block" style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-end", gap: 16, flexWrap: "wrap" }}>
          <div>
            <h1 className="jp-page__title">Mina ansökningar</h1>
            <p className="jp-page__lede">
              Pipeline över alla ansökningar. Klicka på en rad för detaljer.
            </p>
          </div>
          <button className="jp-btn jp-btn--primary"><I.Plus size={16} /> Ny ansökan</button>
        </div>

        <div className="jp-statusbar" role="tablist" aria-label="Status">
          {tabs.map((t) => (
            <button
              key={t}
              role="tab"
              aria-selected={active === t}
              data-active={active === t}
              className="jp-statusbar__item"
              onClick={() => setActive(t)}
            >
              {t === "All" ? "Alla" : STATUS_META[t].sv}
              <span className="jp-statusbar__count">{counts[t] || 0}</span>
            </button>
          ))}
        </div>

        {visible.length === 0 ? (
          <div className="jp-empty">
            <div className="jp-empty__title">Inga ansökningar i den här statusen</div>
            Välj en annan flik eller skapa en ny ansökan.
          </div>
        ) : (
          STATUS_ORDER.filter((s) => grouped[s] && grouped[s].length > 0).map((s) => (
            <section key={s} className="jp-section">
              <div className="jp-section__head">
                <h2 className="jp-section__title">{STATUS_META[s].sv}</h2>
                <span className="jp-section__count">{grouped[s].length}</span>
              </div>
              <div className="jp-applist">
                {grouped[s].map((a) => (
                  <ApplicationRow key={a.id} app={a} onOpen={() => setOpenApp(a)} />
                ))}
              </div>
            </section>
          ))
        )}
      </div>
      <ApplicationModal
        app={openApp}
        onClose={() => setOpenApp(null)}
        onUpdateStatus={() => {}}
      />
      </>
    );
  }

  // ── Application modal ───────────────────────────
  function ApplicationModal({ app, onClose, onUpdateStatus }) {
    const [editStatus, setEditStatus] = useState(null);
    React.useEffect(() => {
      setEditStatus(null);
      if (!app) return;
      const onKey = (e) => { if (e.key === "Escape") onClose(); };
      document.addEventListener("keydown", onKey);
      document.body.style.overflow = "hidden";
      return () => {
        document.removeEventListener("keydown", onKey);
        document.body.style.overflow = "";
      };
    }, [app && app.id]);
    if (!app) return null;
    const status = editStatus ?? app.status;
    const meta = STATUS_META[status];
    const pillColor =
      meta.color === "brand" ? "var(--jp-navy-700)"
      : meta.color === "info" ? "var(--jp-info)"
      : meta.color === "success" ? "var(--jp-success)"
      : meta.color === "warning" ? "var(--jp-warning)"
      : meta.color === "danger" ? "var(--jp-danger)"
      : "var(--jp-ink-3)";
    const pillBg =
      meta.color === "brand" ? "var(--jp-navy-50)"
      : meta.color === "info" ? "var(--jp-info-bg)"
      : meta.color === "success" ? "var(--jp-success-bg)"
      : meta.color === "warning" ? "var(--jp-warning-bg)"
      : meta.color === "danger" ? "var(--jp-danger-bg)"
      : "var(--jp-surface-3)";

    const events = [
      { date: app.updated, label: meta.sv, prim: true },
      app.published && { date: app.published, label: "Annons publicerades" },
    ].filter(Boolean);

    return (
      <div
        className="jp-modal-scrim"
        role="dialog"
        aria-modal="true"
        aria-label={`Ansökan ${app.title}`}
        onClick={onClose}
      >
        <div className="jp-modal" onClick={(e) => e.stopPropagation()}>
          <div className="jp-modal__head">
            <div style={{ flex: 1 }}>
              <h2 className="jp-modal__title">{app.title}</h2>
              <p className="jp-modal__company">
                {app.company} · <span className="jp-mono">#{app.id}</span>
              </p>
            </div>
            <button className="jp-icon-btn" aria-label="Stäng" onClick={onClose}>
              <I.X size={20} />
            </button>
          </div>
          <div className="jp-modal__body">
            <div
              className="jp-modal__match"
              style={{ borderColor: pillColor, background: pillBg, color: pillColor }}
            >
              <div
                className="jp-modal__match__ring"
                style={{ background: "var(--jp-surface)", color: pillColor, border: `2px solid ${pillColor}` }}
              >
                <I.Inbox size={26} />
              </div>
              <div className="jp-modal__match__expl" style={{ color: "var(--jp-ink-1)" }}>
                <div style={{ fontSize: 13, color: pillColor, textTransform: "uppercase", letterSpacing: "0.06em", fontWeight: 700, marginBottom: 2 }}>
                  Status
                </div>
                <b style={{ fontSize: 16 }}>{meta.sv}</b>
                {app.nextDate && (
                  <div style={{ marginTop: 4, fontSize: 14, color: "var(--jp-ink-2)" }}>
                    Nästa: <span className="jp-mono" style={{ color: "var(--jp-ink-1)", fontWeight: 600 }}>{app.nextDate}</span>
                  </div>
                )}
              </div>
            </div>

            <div className="jp-field">
              <label className="jp-label">Uppdatera status</label>
              <select
                className="jp-select"
                value={status}
                onChange={(e) => { setEditStatus(e.target.value); onUpdateStatus && onUpdateStatus(app.id, e.target.value); }}
              >
                {STATUS_ORDER.map((s) => (
                  <option key={s} value={s}>{STATUS_META[s].sv}</option>
                ))}
              </select>
              <div className="jp-hint">Ändringar sparas direkt och syns i pipelinen.</div>
            </div>

            <div>
              <div style={{
                fontSize: 13, fontWeight: 700, textTransform: "uppercase",
                letterSpacing: "0.06em", color: "var(--jp-ink-2)", marginBottom: 10,
              }}>
                Tidslinje
              </div>
              <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "flex", flexDirection: "column", gap: 12 }}>
                {events.map((e, i) => (
                  <li key={i} style={{ display: "flex", gap: 14, alignItems: "baseline" }}>
                    <span className="jp-mono" style={{ fontSize: 12, color: "var(--jp-ink-3)", width: 92 }}>{e.date}</span>
                    <span style={{ color: "var(--jp-ink-1)", fontWeight: e.prim ? 600 : 400 }}>{e.label}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div>
              <div style={{
                fontSize: 13, fontWeight: 700, textTransform: "uppercase",
                letterSpacing: "0.06em", color: "var(--jp-ink-2)", marginBottom: 8,
              }}>
                Anteckningar
              </div>
              <textarea
                className="jp-textarea"
                placeholder="t.ex. förberedelse inför intervju, lön, kontaktperson…"
              />
            </div>
          </div>
          <div className="jp-modal__foot">
            <button className="jp-btn jp-btn--ghost" style={{ color: "var(--jp-danger)" }}>
              <I.Trash size={14} /> Ta bort ansökan
            </button>
            <span className="jp-modal__foot__spacer" />
            <button className="jp-btn jp-btn--secondary" onClick={onClose}>Stäng</button>
            <button className="jp-btn jp-btn--primary"><I.Check size={14} /> Spara</button>
          </div>
        </div>
      </div>
    );
  }

  function ApplicationRow({ app, onOpen }) {
    const meta = STATUS_META[app.status];
    const StatusIcon =
      app.status === "Draft" ? I.Edit
      : app.status === "Submitted" ? I.Send
      : app.status === "Acknowledged" ? I.Check
      : app.status === "InterviewScheduled" ? I.Calendar
      : app.status === "Interviewing" ? I.User
      : app.status === "OfferReceived" ? I.Star
      : app.status === "Accepted" ? I.Check
      : app.status === "Rejected" ? I.X
      : app.status === "Withdrawn" ? I.X
      : app.status === "Ghosted" ? I.Clock
      : I.Inbox;

    const badgeColorVar =
      meta.color === "brand"   ? "var(--jp-navy-700)"
      : meta.color === "info"  ? "var(--jp-info)"
      : meta.color === "success" ? "var(--jp-success)"
      : meta.color === "warning" ? "var(--jp-warning)"
      : meta.color === "danger"  ? "var(--jp-danger)"
      : "var(--jp-ink-3)";
    const badgeBg =
      meta.color === "brand"   ? "var(--jp-navy-50)"
      : meta.color === "info"  ? "var(--jp-info-bg)"
      : meta.color === "success" ? "var(--jp-success-bg)"
      : meta.color === "warning" ? "var(--jp-warning-bg)"
      : meta.color === "danger"  ? "var(--jp-danger-bg)"
      : "var(--jp-surface-3)";

    return (
      <article className="jp-app" onClick={onOpen}>
        <div>
          <h3 className="jp-app__title">{app.title}</h3>
          <div className="jp-app__company">{app.company}</div>
          <div className="jp-app__meta">
            <span className="jp-app__id">#{app.id}</span>
            <span>Uppdaterad <b>{app.updated}</b></span>
            {app.nextDate && (
              <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
                <I.Calendar size={12} /> <b>{app.nextDate}</b>
              </span>
            )}
          </div>
        </div>
        <div className="jp-app__actions">
          <span className={`jp-pill jp-pill--${meta.color}`}>
            <span className="jp-pill__dot" /> {meta.sv}
          </span>
          <I.ChevronRight size={20} style={{ color: "var(--jp-ink-3)" }} />
        </div>
      </article>
    );
  }

  // ── Detalj-sida för en ansökan ───────────────────
  function AnsokanDetailPage({ app, onNav }) {
    if (!app) return (
      <div className="jp-container jp-page">
        <button className="jp-btn jp-btn--ghost jp-btn--sm" onClick={() => onNav("ansokningar")}>
          <I.ChevronLeft size={14} /> Tillbaka
        </button>
        <div className="jp-empty" style={{ marginTop: 24 }}>
          <div className="jp-empty__title">Ansökan hittades inte</div>
        </div>
      </div>
    );
    const meta = STATUS_META[app.status];
    const [status, setStatus] = useState(app.status);
    return (
      <div className="jp-container jp-page">
        <button className="jp-btn jp-btn--ghost jp-btn--sm" onClick={() => onNav("ansokningar")}>
          <I.ChevronLeft size={14} /> Tillbaka till ansökningar
        </button>

        <div className="jp-page__title-block" style={{ marginTop: 16 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
            <h1 className="jp-page__title">{app.title}</h1>
            <span className={`jp-pill jp-pill--${meta.color}`}>
              <span className="jp-pill__dot" /> {meta.sv}
            </span>
          </div>
          <p className="jp-page__lede" style={{ fontSize: 17 }}>
            {app.company} · <span className="jp-mono" style={{ color: "var(--jp-ink-2)" }}>#{app.id}</span>
          </p>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 20, alignItems: "start" }}>
          <div className="jp-card">
            <h3 className="jp-card__title">Statushistorik</h3>
            <div className="jp-field" style={{ marginBottom: 14 }}>
              <label className="jp-label">Uppdatera status</label>
              <select className="jp-select" value={status} onChange={(e) => setStatus(e.target.value)}>
                {STATUS_ORDER.map((s) => (
                  <option key={s} value={s}>{STATUS_META[s].sv}</option>
                ))}
              </select>
              <div className="jp-hint">Ändringar sparas direkt och visas i pipelinen.</div>
            </div>
            <div style={{ borderTop: "1px solid var(--jp-border-soft)", paddingTop: 14 }}>
              <div style={{ fontSize: 13, color: "var(--jp-ink-2)", marginBottom: 8 }}>Tidigare händelser</div>
              <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "flex", flexDirection: "column", gap: 10 }}>
                <li style={{ display: "flex", gap: 10, alignItems: "baseline" }}>
                  <span className="jp-mono" style={{ fontSize: 12, color: "var(--jp-ink-3)", width: 88 }}>{app.updated}</span>
                  <span>{meta.sv}</span>
                </li>
                {app.published && (
                  <li style={{ display: "flex", gap: 10, alignItems: "baseline" }}>
                    <span className="jp-mono" style={{ fontSize: 12, color: "var(--jp-ink-3)", width: 88 }}>{app.published}</span>
                    <span>Annons publicerades</span>
                  </li>
                )}
              </ul>
            </div>
          </div>

          <div className="jp-card">
            <h3 className="jp-card__title">Anteckningar</h3>
            <div className="jp-field" style={{ marginBottom: 12 }}>
              <textarea className="jp-textarea" placeholder="t.ex. förberedelse inför intervju, lön, kontaktperson…" />
            </div>
            <button className="jp-btn jp-btn--primary jp-btn--sm">
              <I.Plus size={14} /> Spara anteckning
            </button>
          </div>
        </div>

        <div className="jp-card" style={{ marginTop: 20 }}>
          <h3 className="jp-card__title">Uppföljningar</h3>
          <div style={{ color: "var(--jp-ink-2)", fontSize: 14.5, marginBottom: 12 }}>
            Inga uppföljningar registrerade. Lägg till en påminnelse om att kontakta arbetsgivaren.
          </div>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            <button className="jp-btn jp-btn--secondary jp-btn--sm">
              <I.Clock size={14} /> Påminn om 1 vecka
            </button>
            <button className="jp-btn jp-btn--secondary jp-btn--sm">
              <I.Clock size={14} /> Påminn om 2 veckor
            </button>
            <button className="jp-btn jp-btn--ghost jp-btn--sm">
              <I.Plus size={14} /> Anpassa
            </button>
          </div>
        </div>
      </div>
    );
  }

  // ── Senaste sökningar ──────────────────────────
  function SokningarPage({ onNav }) {
    const items = window.JpData.RECENT_SEARCHES;
    return (
      <div className="jp-container jp-page">
        <div className="jp-page__title-block">
          <h1 className="jp-page__title">Senaste sökningar</h1>
          <p className="jp-page__lede">
            Dina senaste sökningar sparas automatiskt här. Kör om en sökning eller rensa de du inte behöver.
          </p>
        </div>
        {items.length === 0 ? (
          <div className="jp-empty">
            <div className="jp-empty__title">Inga senaste sökningar</div>
            Gör en sökning under Jobb — den sparas här automatiskt.
          </div>
        ) : (
          <div className="jp-jobs">
            {items.map((s) => (
              <article key={s.id} className="jp-job" style={{ gridTemplateColumns: "auto 1fr auto", cursor: "default" }}>
                <div
                  className="jp-job__match"
                  style={{ background: "var(--jp-surface-3)", borderColor: "var(--jp-border)", color: "var(--jp-ink-2)" }}
                  aria-hidden="true"
                >
                  <I.Clock size={20} />
                </div>
                <div className="jp-job__body">
                  <h3 className="jp-job__title">{s.label}</h3>
                  <div className="jp-job__meta" style={{ marginTop: 8 }}>
                    <span><b>{s.count}</b> träffar</span>
                    {s.isNew && (
                      <span style={{
                        background: "var(--jp-leaf-50)", color: "var(--jp-leaf-600)",
                        padding: "2px 8px", borderRadius: 4, fontSize: 12, fontWeight: 700,
                        letterSpacing: "0.04em", textTransform: "uppercase",
                      }}>
                        Nya
                      </span>
                    )}
                  </div>
                </div>
                <div className="jp-job__actions" style={{ flexDirection: "row" }}>
                  <button
                    className="jp-btn jp-btn--primary jp-btn--sm"
                    onClick={() => onNav("jobb")}
                  >
                    <I.Search size={14} /> Kör igen
                  </button>
                  <button className="jp-icon-btn" aria-label="Ta bort sökning">
                    <I.Trash size={16} />
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </div>
    );
  }

  // ── CV-lista ─────────────────────────────────────
  function CvPage({ onNav }) {
    return (
      <div className="jp-container jp-page">
        <div className="jp-page__title-block" style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-end", gap: 16, flexWrap: "wrap" }}>
          <div>
            <h1 className="jp-page__title">CV</h1>
            <p className="jp-page__lede">
              Hantera dina CV-varianter. AI-stöd hjälper dig anpassa innehållet per ansökan — du behåller alltid kontrollen.
            </p>
          </div>
          <button className="jp-btn jp-btn--primary"><I.Plus size={16} /> Nytt CV</button>
        </div>

        <div className="jp-cvgrid">
          {MOCK_CVS.map((cv) => (
            <article key={cv.id} className="jp-cv">
              <div className="jp-cv__head">
                <div>
                  <h3 className="jp-cv__title">{cv.name}</h3>
                  <div className="jp-cv__role">{cv.role}</div>
                </div>
                {cv.primary && (
                  <span className="jp-pill jp-pill--brand">
                    <span className="jp-pill__dot" /> Standard
                  </span>
                )}
              </div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 5 }}>
                {cv.skills.slice(0, 5).map((s) => (
                  <span key={s} style={{
                    padding: "3px 9px", background: "var(--jp-surface-2)",
                    border: "1px solid var(--jp-border-soft)", borderRadius: 4,
                    fontSize: 12.5, color: "var(--jp-ink-2)",
                  }}>{s}</span>
                ))}
              </div>
              <div className="jp-cv__meta">
                <span>{cv.sections} sektioner</span>
                <span>{cv.language.toUpperCase()}</span>
                <span>Uppd. {cv.updated}</span>
              </div>
              <div className="jp-cv__actions">
                <button className="jp-btn jp-btn--secondary jp-btn--sm" onClick={() => onNav("cv-detalj", cv)}>
                  <I.Edit size={14} /> Redigera
                </button>
                <button className="jp-btn jp-btn--ghost jp-btn--sm">
                  <I.Eye size={14} /> Förhandsgranska
                </button>
              </div>
            </article>
          ))}
        </div>

        <div style={{ marginTop: 32, padding: 20, background: "var(--jp-navy-50)", border: "1px solid var(--jp-navy-100)", borderRadius: 6, display: "flex", gap: 14, alignItems: "flex-start" }}>
          <I.Edit size={20} style={{ color: "var(--jp-navy-700)", flexShrink: 0, marginTop: 2 }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontWeight: 600, color: "var(--jp-ink-1)", marginBottom: 4 }}>
              Anpassa CV mot en annons
            </div>
            <div style={{ fontSize: 14.5, color: "var(--jp-ink-2)" }}>
              Klistra in en annons så ger vi förslag på vad du kan lyfta fram. Inget skickas vidare — du godkänner varje ändring.
            </div>
          </div>
          <button className="jp-btn jp-btn--primary jp-btn--sm">
            <I.Edit size={14} /> Öppna
          </button>
        </div>
      </div>
    );
  }

  // ── CV-detalj (förenklad) ────────────────────────
  function CvDetailPage({ cv, onNav }) {
    if (!cv) return (
      <div className="jp-container jp-page">
        <div className="jp-empty"><div className="jp-empty__title">CV hittades inte</div></div>
      </div>
    );
    return (
      <div className="jp-container jp-page">
        <button className="jp-btn jp-btn--ghost jp-btn--sm" onClick={() => onNav("cv")}>
          <I.ChevronLeft size={14} /> Tillbaka till CV
        </button>
        <div className="jp-page__title-block" style={{ marginTop: 16 }}>
          <h1 className="jp-page__title">{cv.name}</h1>
          <p className="jp-page__lede">{cv.role}</p>
        </div>
        <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: 20 }}>
          <div className="jp-card">
            <h3 className="jp-card__title">Sektioner</h3>
            {["Sammanfattning", "Arbetslivserfarenhet", "Utbildning", "Kompetenser", "Språk", "Referenser", "Övrigt"].slice(0, cv.sections).map((s, i) => (
              <div key={s} style={{
                padding: "14px 0",
                borderTop: i === 0 ? 0 : "1px solid var(--jp-border-soft)",
                display: "flex", justifyContent: "space-between", alignItems: "center",
              }}>
                <span style={{ fontWeight: 500 }}>{s}</span>
                <button className="jp-btn jp-btn--ghost jp-btn--sm"><I.Edit size={14} /> Redigera</button>
              </div>
            ))}
          </div>
          <div className="jp-card">
            <h3 className="jp-card__title">Egenskaper</h3>
            <dl style={{ display: "flex", flexDirection: "column", gap: 12, margin: 0 }}>
              <div>
                <dt style={{ fontSize: 13, color: "var(--jp-ink-2)", marginBottom: 2 }}>Roll</dt>
                <dd style={{ margin: 0, fontWeight: 500 }}>{cv.role}</dd>
              </div>
              <div>
                <dt style={{ fontSize: 13, color: "var(--jp-ink-2)", marginBottom: 2 }}>Språk</dt>
                <dd style={{ margin: 0, fontWeight: 500 }}>{cv.language.toUpperCase()}</dd>
              </div>
              <div>
                <dt style={{ fontSize: 13, color: "var(--jp-ink-2)", marginBottom: 2 }}>Uppdaterad</dt>
                <dd style={{ margin: 0, fontFamily: "var(--jp-font-mono)", fontWeight: 500 }}>{cv.updated}</dd>
              </div>
            </dl>
          </div>
        </div>
      </div>
    );
  }

  // ── Konto ────────────────────────────────────────
  function KontoPage({ onNav }) {
    const [name, setName] = useState("Klas Olsson");
    const [email, setEmail] = useState("klas@example.se");
    const [phone, setPhone] = useState("070-123 45 67");
    const [theme, setTheme] = window.useJpTheme();
    const [lang, setLang] = useState("sv");
    return (
      <div className="jp-container jp-page">
        <div className="jp-page__title-block">
          <h1 className="jp-page__title">Inställningar</h1>
          <p className="jp-page__lede">Hantera dina kontouppgifter och inställningar.</p>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 20, alignItems: "start" }}>
          <div className="jp-card">
            <h3 className="jp-card__title">Personuppgifter</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
              <div className="jp-field">
                <label className="jp-label">Namn</label>
                <input className="jp-input" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="jp-field">
                <label className="jp-label">E-postadress</label>
                <input className="jp-input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
              </div>
              <div className="jp-field">
                <label className="jp-label">Telefon</label>
                <input className="jp-input" value={phone} onChange={(e) => setPhone(e.target.value)} />
              </div>
              <div>
                <button className="jp-btn jp-btn--primary">Spara ändringar</button>
              </div>
            </div>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
            <div className="jp-card">
              <h3 className="jp-card__title">Visning</h3>
              <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
                <div className="jp-field">
                  <label className="jp-label">Tema</label>
                  <Segment
                    value={theme}
                    onChange={setTheme}
                    options={[
                      { value: "light", label: "Ljust", icon: <I.Sun size={14} /> },
                      { value: "dark",  label: "Mörkt", icon: <I.Moon size={14} /> },
                    ]}
                  />
                  <div className="jp-hint">Påverkar hela appen direkt. Sparas på din enhet.</div>
                </div>
                <div className="jp-field">
                  <label className="jp-label">Språk</label>
                  <Segment
                    value={lang}
                    onChange={setLang}
                    options={[
                      { value: "sv", label: "Svenska" },
                      { value: "en", label: "English", disabled: true },
                    ]}
                  />
                  <div className="jp-hint">Engelska är ännu inte tillgängligt.</div>
                </div>
              </div>
            </div>

            <div className="jp-card">
              <h3 className="jp-card__title">Aviseringar</h3>
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                {[
                  ["Nya matchningar på sparade sökningar", true],
                  ["Påminnelser om sista ansökningsdag", true],
                  ["Statusändringar på mina ansökningar", true],
                  ["Veckosammanfattning via e-post", false],
                ].map(([label, def]) => (
                  <ToggleRow key={label} label={label} def={def} />
                ))}
              </div>
            </div>

            <div className="jp-card">
              <h3 className="jp-card__title">Sekretess och data</h3>
              <p style={{ margin: "0 0 12px", color: "var(--jp-ink-2)", fontSize: 14.5 }}>
                Ladda ner all data vi har om dig, eller radera ditt konto permanent.
              </p>
              <div style={{ display: "flex", gap: 10 }}>
                <button className="jp-btn jp-btn--secondary jp-btn--sm">Exportera mina data</button>
                <button className="jp-btn jp-btn--ghost jp-btn--sm" style={{ color: "var(--jp-danger)" }}>
                  <I.Trash size={14} /> Radera konto
                </button>
              </div>
            </div>

            <div className="jp-card">
              <h3 className="jp-card__title">Logga ut</h3>
              <p style={{ margin: "0 0 12px", color: "var(--jp-ink-2)", fontSize: 14.5 }}>
                Du loggas ut från denna enhet.
              </p>
              <button className="jp-btn jp-btn--secondary" onClick={() => onNav("login")}>
                <I.LogOut size={14} /> Logga ut
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  function Segment({ value, onChange, options }) {
    return (
      <div role="radiogroup" style={{
        display: "inline-flex",
        border: "1.5px solid var(--jp-border-input)",
        borderRadius: "var(--jp-r-md)",
        overflow: "hidden",
        background: "var(--jp-surface)",
        alignSelf: "flex-start",
      }}>
        {options.map((o, i) => {
          const active = o.value === value;
          return (
            <button
              key={o.value}
              role="radio"
              aria-checked={active}
              disabled={o.disabled}
              onClick={() => onChange(o.value)}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                padding: "9px 18px",
                background: active ? "var(--jp-navy-700)" : "transparent",
                color: active ? "#fff" : (o.disabled ? "var(--jp-ink-3)" : "var(--jp-ink-1)"),
                border: 0,
                borderLeft: i > 0 ? "1px solid var(--jp-border)" : 0,
                fontFamily: "inherit",
                fontSize: 14,
                fontWeight: 600,
                cursor: o.disabled ? "not-allowed" : "pointer",
              }}
            >
              {o.icon} {o.label}
            </button>
          );
        })}
      </div>
    );
  }

  function ToggleRow({ label, def }) {
    const [on, setOn] = useState(def);
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
        <span style={{ fontSize: 14.5 }}>{label}</span>
        <button
          role="switch"
          aria-checked={on}
          onClick={() => setOn(!on)}
          style={{
            width: 44, height: 26, borderRadius: 999,
            background: on ? "var(--jp-navy-700)" : "var(--jp-border)",
            border: 0, cursor: "pointer", position: "relative",
            transition: "background 120ms",
          }}
        >
          <span style={{
            position: "absolute", top: 3, left: on ? 21 : 3,
            width: 20, height: 20, borderRadius: 999, background: "#fff",
            transition: "left 120ms", boxShadow: "0 1px 2px rgba(0,0,0,0.2)",
          }} />
        </button>
      </div>
    );
  }

  // ── Auth (Logga in / Registrera) ─────────────────
  function LoginPage({ onNav }) {
    const [mode, setMode] = useState("login"); // 'login' | 'register'
    return (
      <div className="jp-auth-wrap">
        <div className="jp-auth-card">
          <a
            href="#"
            className="jp-brand"
            onClick={(e) => { e.preventDefault(); onNav("jobb"); }}
            style={{ marginBottom: 24 }}
          >
            <span className="jp-brand__mark">J</span>
            <span className="jp-brand__word">JobbPilot</span>
          </a>
          <h1>{mode === "login" ? "Logga in" : "Skapa konto"}</h1>
          <p className="lede">
            {mode === "login"
              ? "Logga in för att se dina ansökningar, CV och sparade sökningar."
              : "Skapa ett konto för att börja söka jobb och spara annonser."}
          </p>
          <form className="jp-auth-form" onSubmit={(e) => { e.preventDefault(); onNav("jobb"); }}>
            {mode === "register" && (
              <div className="jp-field">
                <label className="jp-label">Namn</label>
                <input className="jp-input" placeholder="För- och efternamn" required />
              </div>
            )}
            <div className="jp-field">
              <label className="jp-label">E-postadress</label>
              <input className="jp-input" type="email" placeholder="namn@exempel.se" required />
            </div>
            <div className="jp-field">
              <label className="jp-label">Lösenord</label>
              <input className="jp-input" type="password" placeholder="Minst 8 tecken" required />
            </div>
            <button type="submit" className="jp-btn jp-btn--primary jp-btn--lg jp-btn--block" style={{ marginTop: 6 }}>
              {mode === "login" ? "Logga in" : "Skapa konto"}
            </button>
          </form>
          <div className="jp-auth-foot">
            {mode === "login" ? (
              <>Har du inget konto? <a href="#" onClick={(e) => { e.preventDefault(); setMode("register"); }}>Skapa ett</a></>
            ) : (
              <>Har du redan ett konto? <a href="#" onClick={(e) => { e.preventDefault(); setMode("login"); }}>Logga in</a></>
            )}
          </div>
        </div>
      </div>
    );
  }

  window.JpPages = {
    AnsokningarPage, AnsokanDetailPage, SokningarPage, CvPage, CvDetailPage, KontoPage, LoginPage,
  };
})();
