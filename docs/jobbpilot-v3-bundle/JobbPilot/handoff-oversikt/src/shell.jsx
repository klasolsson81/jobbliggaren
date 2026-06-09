// shell.jsx — Header, mobile drawer, theme/lang controls
(() => {
  const { useState, useEffect, useRef } = React;
  const I = window.JpIcons;
  const { NOTIFICATIONS } = window.JpData;

  // ── Theme hook ────────────────────────────────
  function useTheme() {
    const [theme, setTheme] = useState(() => {
      const stored = localStorage.getItem("jp-theme");
      if (stored === "dark" || stored === "light") return stored;
      return window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light";
    });
    useEffect(() => {
      if (theme === "dark") document.documentElement.setAttribute("data-theme", "dark");
      else document.documentElement.removeAttribute("data-theme");
      localStorage.setItem("jp-theme", theme);
    }, [theme]);
    return [theme, setTheme];
  }
  window.useJpTheme = useTheme;

  // ── Outside click hook ────────────────────────
  function useOutside(open, onClose) {
    const ref = useRef(null);
    useEffect(() => {
      if (!open) return;
      const onDoc = (e) => {
        if (ref.current && !ref.current.contains(e.target)) onClose();
      };
      const onKey = (e) => { if (e.key === "Escape") onClose(); };
      document.addEventListener("mousedown", onDoc);
      document.addEventListener("keydown", onKey);
      return () => {
        document.removeEventListener("mousedown", onDoc);
        document.removeEventListener("keydown", onKey);
      };
    }, [open, onClose]);
    return ref;
  }
  window.useJpOutside = useOutside;

  // ── Notifications popover ─────────────────────
  function NotifBell() {
    const [open, setOpen] = useState(false);
    const ref = useOutside(open, () => setOpen(false));
    return (
      <div ref={ref} style={{ position: "relative" }}>
        <button
          className="jp-icon-btn"
          aria-label="Aviseringar"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
        >
          <I.Bell size={18} />
        </button>
        {open && (
          <div className="jp-notif" role="dialog" aria-label="Aviseringar">
            <div className="jp-notif__head">Aviseringar</div>
            <div className="jp-notif__list">
              {NOTIFICATIONS.map((n) => (
                <div key={n.id} className="jp-notif__item">
                  <div>{n.text}</div>
                  <div className="jp-notif__item__time">{n.time}</div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    );
  }

  // ── Theme toggle ──────────────────────────────
  function ThemeToggle() {
    const [theme, setTheme] = useTheme();
    return (
      <button
        className="jp-icon-btn"
        aria-label={theme === "dark" ? "Byt till ljust läge" : "Byt till mörkt läge"}
        onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
      >
        {theme === "dark" ? <I.Sun size={18} /> : <I.Moon size={18} />}
      </button>
    );
  }

  // ── Language toggle (SV/EN, SV aktiv) ─────────
  function LangToggle() {
    const [lang, setLang] = useState("sv");
    return (
      <div className="jp-lang" role="group" aria-label="Språk">
        <button
          className="jp-lang__btn"
          aria-pressed={lang === "sv"}
          onClick={() => setLang("sv")}
        >SV</button>
        <button
          className="jp-lang__btn"
          aria-pressed={lang === "en"}
          onClick={() => setLang("en")}
          title="Engelska är ännu inte implementerat"
        >EN</button>
      </div>
    );
  }

  // ── User menu ─────────────────────────────────
  function UserMenu({ onNav }) {
    const [open, setOpen] = useState(false);
    const ref = useOutside(open, () => setOpen(false));
    const initials = "KO";
    return (
      <div ref={ref} style={{ position: "relative" }}>
        <button
          className="jp-avatar"
          aria-label="Användarmeny"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
        >
          {initials}
        </button>
        {open && (
          <div className="jp-usermenu" role="menu">
            <div className="jp-usermenu__head">
              <div className="jp-usermenu__name">Klas Olsson</div>
              <div className="jp-usermenu__email">klas@example.se</div>
            </div>
            <button className="jp-usermenu__item" onClick={() => { onNav("konto"); setOpen(false); }}>
              <I.Settings size={16} /> Inställningar
            </button>
            <button className="jp-usermenu__item" onClick={() => { onNav("sokningar"); setOpen(false); }}>
              <I.Clock size={16} /> Senaste sökningar
            </button>
            <button className="jp-usermenu__item" onClick={() => { onNav("cv"); setOpen(false); }}>
              <I.ScrollText size={16} /> Mina CV
            </button>
            <div className="jp-usermenu__sep" />
            <button className="jp-usermenu__item" onClick={() => { onNav("login"); setOpen(false); }}>
              <I.LogOut size={16} /> Logga ut
            </button>
          </div>
        )}
      </div>
    );
  }

  // ── Mobile drawer ─────────────────────────────
  function Drawer({ open, onClose, route, onNav }) {
    if (!open) return null;
    const items = [
      { id: "oversikt", label: "Översikt", icon: <I.Inbox size={18} /> },
      { id: "jobb", label: "Jobb", icon: <I.Briefcase size={18} /> },
      { id: "ansokningar", label: "Mina ansökningar", icon: <I.Send size={18} /> },
      { id: "cv", label: "CV", icon: <I.ScrollText size={18} /> },
      { id: "sokningar", label: "Senaste sökningar", icon: <I.Clock size={18} /> },
      { id: "konto", label: "Inställningar", icon: <I.Settings size={18} /> },
    ];
    return (
      <>
        <div className="jp-drawer-scrim" onClick={onClose} />
        <aside className="jp-drawer" role="dialog" aria-label="Meny">
          <div className="jp-drawer__head">
            <span style={{ fontSize: 17, fontWeight: 700 }}>Meny</span>
            <button className="jp-drawer__close" onClick={onClose} aria-label="Stäng">
              Stäng <I.X size={18} />
            </button>
          </div>
          <nav className="jp-drawer__list">
            {items.map((it) => (
              <a
                key={it.id}
                href="#"
                className="jp-drawer__item"
                aria-current={route === it.id ? "page" : undefined}
                onClick={(e) => { e.preventDefault(); onNav(it.id); onClose(); }}
              >
                {it.icon} {it.label}
              </a>
            ))}
          </nav>
        </aside>
      </>
    );
  }

  // ── Header ────────────────────────────────────
  function Header({ route, onNav }) {
    const [drawerOpen, setDrawerOpen] = useState(false);
    const nav = [
      { id: "oversikt", label: "Översikt" },
      { id: "jobb", label: "Jobb" },
      { id: "ansokningar", label: "Mina ansökningar" },
      { id: "cv", label: "CV" },
    ];
    return (
      <header className="jp-header" role="banner">
        <div className="jp-header__inner">
          <a
            href="#"
            className="jp-brand"
            onClick={(e) => { e.preventDefault(); onNav("oversikt"); }}
          >
            <span className="jp-brand__mark">J</span>
            <span className="jp-brand__word">JobbPilot</span>
          </a>
          <nav className="jp-nav" aria-label="Huvudnavigation">
            {nav.map((n) => (
              <a
                key={n.id}
                href="#"
                className="jp-nav__link"
                aria-current={route === n.id ? "page" : undefined}
                onClick={(e) => { e.preventDefault(); onNav(n.id); }}
              >
                {n.label}
              </a>
            ))}
          </nav>
          <span className="jp-header__spacer" />
          <div className="jp-header__actions">
            <NotifBell />
            <UserMenu onNav={onNav} />
            <button
              className="jp-icon-btn jp-drawer-trigger"
              aria-label="Öppna meny"
              onClick={() => setDrawerOpen(true)}
            >
              <I.Menu size={20} />
            </button>
          </div>
        </div>
        <Drawer
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          route={route}
          onNav={onNav}
        />
      </header>
    );
  }

  window.JpShell = { Header, ThemeToggle, LangToggle };
})();
