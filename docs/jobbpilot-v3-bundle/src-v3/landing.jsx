// landing.jsx — Marketing landing page i v3-stil
(() => {
  const { useState, Suspense } = React;
  const I = window.JpIcons;
  const { ThemeToggle, LangToggle } = window.JpShell;

  const FEATURES = [
    {
      k: "Sökning",
      v: "Aktiva platsannonser — just nu från Platsbanken, fler källor planerade. Sortera efter CV-match, nyhet eller deadline.",
    },
    {
      k: "Pipeline",
      v: "Spåra varje ansökan från utkast till svar — intervjuer, erbjudanden, avslag. Inget tappas mellan stolarna.",
    },
    {
      k: "CV och brev",
      v: "Skapa och organisera flera CV-varianter. Generera personliga brev som matchar din ton. Du äger varje rad.",
    },
    {
      k: "Påminnelser",
      v: "Intervjuer och sista ansökningsdag fångas automatiskt från dina ansökningar — du missar inga deadlines.",
    },
  ];

  function LandingTopbar({ onNav, mode, setMode }) {
    return (
      <header className="jp-land-top">
        <div className="jp-land-top__inner">
          <a
            href="#"
            className="jp-brand"
            onClick={(e) => { e.preventDefault(); onNav("landing"); }}
          >
            <span className="jp-brand__mark">J</span>
            <span className="jp-brand__word">JobbPilot</span>
          </a>
          <div className="jp-land-top__stats" aria-label="Liveräkning">
            <div className="jp-land-top__stat">
              <span className="jp-land-top__stat__num">45 580</span>
              <span className="jp-land-top__stat__label">aktiva annonser</span>
            </div>
            <span className="jp-land-top__stat__sep" />
            <div className="jp-land-top__stat">
              <span className="jp-land-top__stat__num">312</span>
              <span className="jp-land-top__stat__label">nya idag</span>
            </div>
          </div>
        </div>
      </header>
    );
  }

  function AuthCard({ mode, setMode, onNav }) {
    return (
      <div className="jp-land-auth">
        <div className="jp-land-auth__tabs" role="tablist">
          {["login", "register"].map((m) => (
            <button
              key={m}
              role="tab"
              aria-selected={mode === m}
              className="jp-land-auth__tab"
              data-active={mode === m}
              onClick={() => setMode(m)}
            >
              {m === "login" ? "Logga in" : "Skapa konto"}
            </button>
          ))}
        </div>
        <form
          className="jp-land-auth__form"
          onSubmit={(e) => { e.preventDefault(); onNav("jobb"); }}
        >
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
          {mode === "login" && (
            <a
              href="#"
              style={{ fontSize: 13.5, alignSelf: "flex-end", textDecoration: "underline" }}
              onClick={(e) => e.preventDefault()}
            >
              Glömt lösenord?
            </a>
          )}
          <button type="submit" className="jp-btn jp-btn--primary jp-btn--lg jp-btn--block" style={{ marginTop: 4 }}>
            {mode === "login" ? "Logga in" : "Skapa konto"}
          </button>
        </form>

        <div className="jp-land-auth__sep">
          <span>eller {mode === "login" ? "logga in med" : "fortsätt med"}</span>
        </div>

        <div className="jp-land-auth__oauth">
          {[
            { id: "google", label: "Google" },
            { id: "linkedin", label: "LinkedIn" },
            { id: "microsoft", label: "Microsoft" },
          ].map((p) => (
            <button
              key={p.id}
              type="button"
              className="jp-btn jp-btn--secondary"
              style={{ height: 42, justifyContent: "center" }}
              onClick={() => onNav("jobb")}
              title={`Fortsätt med ${p.label}`}
            >
              <OAuthMark kind={p.id} />
              {p.label}
            </button>
          ))}
        </div>

        {mode === "register" && (
          <p style={{ fontSize: 13.5, color: "var(--jp-ink-2)", margin: 0, lineHeight: 1.5 }}>
            Genom att skapa konto godkänner du våra användarvillkor och vår datapolicy. JobbPilot säljer aldrig din data.
          </p>
        )}
      </div>
    );
  }

  function OAuthMark({ kind }) {
    if (kind === "google") {
      return (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
          <path d="M12 4a8 8 0 1 0 7.6 10.5" />
          <path d="M12 11h8" />
        </svg>
      );
    }
    if (kind === "linkedin") {
      return (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <rect x="3" y="9" width="3" height="11" />
          <circle cx="4.5" cy="5" r="1.4" fill="currentColor" stroke="none" />
          <path d="M10 9v11M10 13a3.5 3.5 0 0 1 7 0v7" />
        </svg>
      );
    }
    return (
      <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
        <rect x="3" y="3" width="8" height="8" />
        <rect x="13" y="3" width="8" height="8" />
        <rect x="3" y="13" width="8" height="8" />
        <rect x="13" y="13" width="8" height="8" />
      </svg>
    );
  }

  function LandingPage({ onNav }) {
    const [mode, setMode] = useState("login");
    return (
      <div className="jp-shell">
        <LandingTopbar onNav={onNav} mode={mode} setMode={setMode} />

        <section className="jp-land-hero">
          <div className="jp-land-hero__inner">
            <div className="jp-land-hero__copy">
              <h1 className="jp-land-hero__title">
                Verktyg för svenska jobbsökare
              </h1>
              <p className="jp-land-hero__lede">
                Skapa professionella CV, sök bland aktiva annonser och följ upp varje ansökan — från utkast till svar.
              </p>
              <div className="jp-land-hero__ctas">
                <button
                  className="jp-btn jp-btn--lg"
                  style={{ background: "#fff", color: "#0A2647", borderColor: "#fff" }}
                  onClick={() => { setMode("register"); window.scrollTo({ top: 0, behavior: "smooth" }); }}
                >
                  <I.Plus size={16} /> Skapa konto
                </button>
                <button
                  className="jp-btn jp-btn--lg"
                  style={{
                    background: "var(--jp-leaf-600)",
                    color: "#FFFFFF",
                    borderColor: "var(--jp-leaf-600)",
                  }}
                  onClick={() => onNav("jobb")}
                >
                  Utforska som gäst <I.ArrowRight size={16} />
                </button>
              </div>
            </div>

            <AuthCard mode={mode} setMode={setMode} onNav={onNav} />
          </div>
        </section>

        <section className="jp-land-section">
          <div className="jp-container">
            <div className="jp-land-section__head">
              <div className="jp-land-kicker">Funktioner</div>
              <h2 className="jp-land-section__title">Allt du behöver för att hålla ordning</h2>
            </div>
            <div className="jp-land-features">
              {FEATURES.map((f) => (
                <div key={f.k} className="jp-land-feature">
                  <div className="jp-land-feature__key">{f.k}</div>
                  <div className="jp-land-feature__val">{f.v}</div>
                </div>
              ))}
            </div>
          </div>
        </section>

        <footer className="jp-land-foot">
          <div className="jp-land-foot__inner">
            <nav className="jp-land-foot__links">
              {[
                "Om JobbPilot", "Användarvillkor", "Integritetspolicy",
                "Cookies", "Tillgänglighet", "Kontakt",
              ].map((label, i, arr) => (
                <React.Fragment key={label}>
                  <a href="#" onClick={(e) => e.preventDefault()}>{label}</a>
                  {i < arr.length - 1 && <span className="jp-land-foot__dot">·</span>}
                </React.Fragment>
              ))}
            </nav>
            <div className="jp-land-foot__settings">
              <ThemeToggle />
              <LangToggle />
            </div>
          </div>
        </footer>
      </div>
    );
  }

  window.JpLanding = { LandingPage };
})();
