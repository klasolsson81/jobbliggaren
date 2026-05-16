"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { ThemeToggle } from "@/components/theme-toggle";
import { LoginForm } from "@/components/forms/LoginForm";
import { RegisterForm } from "@/components/forms/RegisterForm";

type Mode = "login" | "register";

const FEATURES: { k: string; v: string }[] = [
  {
    k: "Sökning",
    v: "Aktiva annonser från Platsbanken, indexerade dygnet runt.",
  },
  {
    k: "Pipeline",
    v: "Spåra varje ansökan från skickad till svar — utkast, intervjuer, erbjudanden.",
  },
  {
    k: "CV-anpassning",
    v: "Förslag på vad du kan justera för en specifik annons. Du godkänner varje ändring.",
  },
  {
    k: "Kalender",
    v: "Intervjuer, deadlines och uppföljningar — synkas automatiskt från dina ansökningar.",
  },
];

function ProviderMonogram({ kind }: { kind: "google" | "linkedin" | "microsoft" }) {
  const common = {
    width: 14,
    height: 14,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.8,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
  };
  if (kind === "google") {
    return (
      <svg {...common}>
        <path d="M12 4a8 8 0 1 0 7.6 10.5" />
        <path d="M12 11h8" />
      </svg>
    );
  }
  if (kind === "linkedin") {
    return (
      <svg {...common}>
        <rect x="3" y="9" width="3" height="11" />
        <circle cx="4.5" cy="5" r="1.4" fill="currentColor" stroke="none" />
        <path d="M10 9v11M10 13a3.5 3.5 0 0 1 7 0v7" />
      </svg>
    );
  }
  return (
    <svg {...common} strokeWidth={0} fill="currentColor">
      <rect x="3" y="3" width="8" height="8" />
      <rect x="13" y="3" width="8" height="8" />
      <rect x="3" y="13" width="8" height="8" />
      <rect x="13" y="13" width="8" height="8" />
    </svg>
  );
}

const CONTAINER = "mx-auto w-full max-w-[1200px]";

export default function LandingPage() {
  const [mode, setMode] = useState<Mode>("login");

  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      {/* ── Topbar ── */}
      <header className="flex-none border-b border-border-default">
        <div
          className={`${CONTAINER} flex h-14 items-center justify-between px-14`}
        >
          <div className="flex items-center gap-2.5 text-base font-semibold tracking-tight">
            <span>JobbPilot</span>
            <span className="jp-sidebar__tag">v2</span>
          </div>
          <nav className="flex items-center gap-5">
            <button
              type="button"
              onClick={() => setMode("login")}
              className={`text-body-sm ${mode === "login" ? "font-medium text-text-primary" : "text-text-secondary hover:text-text-primary"}`}
            >
              Logga in
            </button>
            <button
              type="button"
              onClick={() => setMode("register")}
              className={`text-body-sm ${mode === "register" ? "font-medium text-text-primary" : "text-text-secondary hover:text-text-primary"}`}
            >
              Skapa konto
            </button>
            <ThemeToggle />
            <div
              role="group"
              aria-label="Språk"
              className="inline-flex h-6.5 overflow-hidden rounded-sm border border-border-default"
            >
              <span className="font-mono flex items-center bg-text-primary px-2.5 text-[10.5px] font-medium tracking-wide text-surface-primary">
                SV
              </span>
              <span
                aria-disabled="true"
                title="Engelska kommer senare"
                className="font-mono flex cursor-not-allowed items-center border-l border-border-default bg-surface-primary px-2.5 text-[10.5px] font-medium tracking-wide text-text-tertiary"
              >
                EN
              </span>
            </div>
          </nav>
        </div>
      </header>

      {/* ── Main ── */}
      <main className="flex-1">
        <div
          className={`${CONTAINER} grid grid-cols-1 items-start gap-20 px-14 py-18 lg:grid-cols-[minmax(0,1fr)_380px]`}
        >
          {/* Vänster: copy + features */}
          <div className="min-w-0">
            <div className="font-mono mb-7 text-[11.5px] font-medium uppercase tracking-[0.12em] text-text-secondary">
              Version 2 · Maj 2026
            </div>
            <h1 className="m-0 max-w-160 text-[clamp(40px,5vw,56px)] font-semibold leading-[1.05] tracking-tight">
              Sök jobb.
              <br />
              Spåra ansökningar.
              <br />
              Behåll kontrollen.
            </h1>
            <p className="mt-7 max-w-140 text-[16.5px] leading-relaxed text-text-secondary">
              En arbetsmarknadsapp byggd på publik data från Arbetsförmedlingen.
              Inga svarta lådor, ingen försäljning av din profil — bara verktyg
              för att hitta jobb och hålla ordning på dina ansökningar.
            </p>

            <div className="mt-14 max-w-140 border-t border-border-default">
              {FEATURES.map((f) => (
                <div
                  key={f.k}
                  className="grid grid-cols-[160px_1fr] items-baseline gap-6 border-b border-border-default py-5"
                >
                  <div className="font-mono text-[11px] font-semibold uppercase tracking-[0.08em] text-text-primary">
                    {f.k}
                  </div>
                  <div className="text-body leading-relaxed text-text-secondary">
                    {f.v}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Höger: riktig login / register-panel (ingen mellansida) */}
          <div className="sticky top-18 flex flex-col border border-border-default bg-surface-primary">
            <div className="flex border-b border-border-default">
              {(["login", "register"] as Mode[]).map((m, i) => {
                const active = mode === m;
                return (
                  <button
                    key={m}
                    type="button"
                    aria-pressed={active}
                    onClick={() => setMode(m)}
                    className={[
                      "relative flex-1 py-4 text-body-sm font-medium",
                      i > 0 ? "border-l border-border-default" : "",
                      active
                        ? "bg-surface-primary text-text-primary"
                        : "bg-surface-sunken text-text-secondary",
                    ].join(" ")}
                  >
                    {m === "login" ? "Logga in" : "Skapa konto"}
                    {active && (
                      <span className="absolute inset-x-0 -bottom-px h-0.5 bg-brand-600" />
                    )}
                  </button>
                );
              })}
            </div>

            <div className="flex flex-col gap-5 px-6.5 pb-7 pt-6.5">
              <Suspense fallback={null}>
                {mode === "login" ? <LoginForm /> : <RegisterForm />}
              </Suspense>

              <div className="font-mono flex items-center gap-3 text-[11.5px] uppercase tracking-[0.12em] text-text-secondary">
                <span className="h-px flex-1 bg-border-default" />
                {mode === "login" ? "eller logga in med" : "eller fortsätt med"}
                <span className="h-px flex-1 bg-border-default" />
              </div>

              <div className="grid grid-cols-3 gap-2">
                {(
                  [
                    { id: "google", label: "Google" },
                    { id: "linkedin", label: "LinkedIn" },
                    { id: "microsoft", label: "Microsoft" },
                  ] as const
                ).map((p) => (
                  <Link
                    key={p.id}
                    href="/logga-in"
                    className="jp-btn jp-btn--secondary h-9.5 justify-center gap-1.5 px-1 text-caption"
                    title={`Fortsätt med ${p.label}`}
                  >
                    <ProviderMonogram kind={p.id} />
                    <span>{p.label}</span>
                  </Link>
                ))}
              </div>

              {mode === "register" && (
                <p className="text-body-sm leading-relaxed text-text-secondary">
                  Genom att skapa konto godkänner du våra användarvillkor och
                  vår datapolicy. JobbPilot säljer aldrig din data.
                </p>
              )}
            </div>
          </div>
        </div>
      </main>

      {/* ── Footer ── */}
      <footer className="flex-none border-t border-border-default bg-surface-sunken">
        <div
          className={`${CONTAINER} flex flex-wrap items-center justify-between gap-6 px-14 py-4.5`}
        >
          <nav className="flex flex-wrap items-center text-caption text-text-secondary">
            {[
              "Om JobbPilot",
              "Användarvillkor",
              "Integritetspolicy",
              "Cookies",
              "Tillgänglighet",
              "Kontakt",
            ].map((label, i, arr) => (
              <span key={label} className="flex items-center">
                <Link
                  href="/"
                  className="py-0.5 text-text-secondary hover:text-text-primary hover:underline hover:underline-offset-[3px]"
                >
                  {label}
                </Link>
                {i < arr.length - 1 && (
                  <span aria-hidden="true" className="px-3 text-border-default">
                    ·
                  </span>
                )}
              </span>
            ))}
          </nav>
          <div className="font-mono flex items-center gap-3.5 text-[13px] text-text-secondary">
            <span className="inline-flex items-center gap-1.5">
              <span
                aria-hidden="true"
                className="size-1.5 rounded-full bg-success-600"
              />
              Drift
            </span>
            <span className="text-border-default">·</span>
            <span>v2 · 2026</span>
          </div>
        </div>
      </footer>
    </div>
  );
}
