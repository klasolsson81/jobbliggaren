"use client";

import {
  createContext,
  useCallback,
  useContext,
  useSyncExternalStore,
  type ReactNode,
} from "react";

/**
 * Civic-utility tema-hantering — light/dark.
 *
 * Strategi (CTO-beslut 2026-05-16, Variant A): pre-paint applicering via
 * inline blocking-script (ThemeScript) FÖRE hydration → ingen flash-of-wrong-theme.
 * Runtime-läsning sker via useSyncExternalStore (sanktionerad API för att
 * synka mot extern store — undviker setState-in-effect och hydration-mismatch).
 * CSS-resolution via `@custom-variant dark ([data-theme="dark"])` i globals.css.
 *
 * Ingen extern dependency (next-themes ej tillåtet per batch-förbud). Mönstret
 * är webbplattformens standardlösning på FOUC/FOWT, inte en paket-uppfinning.
 */

type Theme = "light" | "dark";

const STORAGE_KEY = "jp-theme";
const CHANGE_EVENT = "jp-theme-change";

const ThemeContext = createContext<
  { theme: Theme; setTheme: (t: Theme) => void } | undefined
>(undefined);

function applyTheme(theme: Theme): void {
  const root = document.documentElement;
  if (theme === "dark") {
    root.setAttribute("data-theme", "dark");
  } else {
    root.removeAttribute("data-theme");
  }
}

function resolveTheme(): Theme {
  // Källan är DOM-attributet som inline-scriptet redan satt — håller
  // läsningen i synk med det som faktiskt renderas.
  if (document.documentElement.getAttribute("data-theme") === "dark") {
    return "dark";
  }
  return "light";
}

function subscribe(onChange: () => void): () => void {
  const mql = window.matchMedia("(prefers-color-scheme: dark)");
  const onMedia = () => {
    let explicit = false;
    try {
      const stored = window.localStorage.getItem(STORAGE_KEY);
      explicit = stored === "light" || stored === "dark";
    } catch {
      // localStorage blockerat — följ systemet
    }
    if (!explicit) {
      applyTheme(mql.matches ? "dark" : "light");
      onChange();
    }
  };
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) {
      applyTheme(
        e.newValue === "dark"
          ? "dark"
          : e.newValue === "light"
            ? "light"
            : mql.matches
              ? "dark"
              : "light",
      );
      onChange();
    }
  };
  mql.addEventListener("change", onMedia);
  window.addEventListener("storage", onStorage);
  window.addEventListener(CHANGE_EVENT, onChange);
  return () => {
    mql.removeEventListener("change", onMedia);
    window.removeEventListener("storage", onStorage);
    window.removeEventListener(CHANGE_EVENT, onChange);
  };
}

/**
 * Inline blocking-script. Renderas som första barn i `<body>` så att
 * `data-theme` är satt FÖRE first paint. Statisk string-literal utan
 * user input — XSS-ytan är noll (jfr CLAUDE.md §5.2: DOM-mutation-undantag
 * dokumenterat, samma kompromiss som next-themes / Tailwind-docs gör).
 */
export function ThemeScript() {
  const script = `(function(){try{var t=localStorage.getItem("${STORAGE_KEY}");if(t!=="light"&&t!=="dark"){t=window.matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light";}if(t==="dark"){document.documentElement.setAttribute("data-theme","dark");}}catch(e){}})();`;
  return <script dangerouslySetInnerHTML={{ __html: script }} />;
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const theme = useSyncExternalStore<Theme>(
    subscribe,
    resolveTheme,
    () => "light", // server snapshot — inline-scriptet korrigerar före paint
  );

  const setTheme = useCallback((next: Theme) => {
    applyTheme(next);
    try {
      window.localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // localStorage blockerat — temat gäller ändå för sessionen.
    }
    window.dispatchEvent(new Event(CHANGE_EVENT));
  }, []);

  return (
    <ThemeContext.Provider value={{ theme, setTheme }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): { theme: Theme; setTheme: (t: Theme) => void } {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error("useTheme måste användas inom en ThemeProvider");
  }
  return ctx;
}
