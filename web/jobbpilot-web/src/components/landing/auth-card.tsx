"use client";

import { Suspense } from "react";
import { LoginForm } from "@/components/forms/LoginForm";
import { RegisterForm } from "@/components/forms/RegisterForm";
import { OAuthMark, type OAuthProvider } from "./oauth-mark";

export type AuthMode = "login" | "register";

interface AuthCardProps {
  mode: AuthMode;
  onModeChange: (mode: AuthMode) => void;
}

const OAUTH_PROVIDERS: ReadonlyArray<{
  id: OAuthProvider;
  label: string;
}> = [
  { id: "google", label: "Google" },
  { id: "linkedin", label: "LinkedIn" },
  { id: "microsoft", label: "Microsoft" },
];

/**
 * AuthCard — höger-kort i landing-hero. Tabs (Logga in / Skapa konto) +
 * formulär + OAuth-knappar.
 *
 * Mode-state lyfts till parent `<LandingHeroSection />` så hero-CTA "Skapa
 * konto" kan flippa kortet till register-tab. Tab-knapparna här delegerar
 * till samma `onModeChange`.
 *
 * Formerna är de befintliga delade `<LoginForm />` / `<RegisterForm />`
 * (inga duplicerade valideringsscheman, samma actions). Suspense-wrap
 * eftersom de använder `useSearchParams` (Next App Router-mönster).
 *
 * OAuth-knapparna är **stubs** denna fas (FAS-DEFERRAL per Klas Prompt 1):
 * de leder till `/logga-in?provider=<id>` så befintlig auth-route kan
 * upptäcka query-paramet och hantera när fullt OAuth-flöde wires:as.
 * Idag är det ett no-op-redirect till login-routen — ingen regression.
 *
 * Auth-kortet har scoped token-overrides via `.jp-land-auth` så det
 * renderas i ljust läge även i dark mode (HANDOVER §2.4).
 */
export function AuthCard({ mode, onModeChange }: AuthCardProps) {
  return (
    <div className="jp-land-auth">
      <div className="jp-land-auth__tabs" role="tablist" aria-label="Logga in eller skapa konto">
        {(["login", "register"] as const).map((m) => (
          <button
            key={m}
            type="button"
            role="tab"
            aria-selected={mode === m}
            className="jp-land-auth__tab"
            data-active={mode === m}
            onClick={() => onModeChange(m)}
          >
            {m === "login" ? "Logga in" : "Skapa konto"}
          </button>
        ))}
      </div>

      <Suspense fallback={null}>
        {mode === "login" ? <LoginForm /> : <RegisterForm />}
      </Suspense>

      <div
        className="jp-land-auth__sep"
        role="separator"
        aria-orientation="horizontal"
      >
        <span>
          {mode === "login" ? "eller logga in med" : "eller fortsätt med"}
        </span>
      </div>

      <div className="jp-land-auth__oauth">
        {OAUTH_PROVIDERS.map((p) => (
          <a
            key={p.id}
            // TODO: Fas 7 — wire mot riktig OAuth-flöde (ADR-spår behövs)
            href={`/logga-in?provider=${p.id}`}
            className="jp-btn jp-btn--secondary"
            style={{ height: 42, justifyContent: "center" }}
            title={`Fortsätt med ${p.label}`}
          >
            <OAuthMark provider={p.id} />
            <span>{p.label}</span>
          </a>
        ))}
      </div>

      {mode === "register" && (
        <p
          style={{
            fontSize: 13.5,
            color: "var(--jp-ink-2)",
            margin: 0,
            lineHeight: 1.5,
          }}
        >
          Genom att skapa konto godkänner du våra användarvillkor och vår
          datapolicy. JobbPilot säljer aldrig din data.
        </p>
      )}
    </div>
  );
}
