"use client";

import { Suspense } from "react";
import Link from "next/link";
import { LoginForm } from "@/components/forms/LoginForm";
import { OAuthMark, type OAuthProvider } from "./oauth-mark";

const OAUTH_PROVIDERS: ReadonlyArray<{
  id: OAuthProvider;
  label: string;
}> = [
  { id: "google", label: "Google" },
  { id: "linkedin", label: "LinkedIn" },
  { id: "microsoft", label: "Microsoft" },
];

/**
 * AuthCard — höger-kort i landing-hero. Per Klas-direktiv 2026-05-24 och
 * Steg 5 closed-beta-disciplin är registrerings-tab borttagen — bara LoginForm
 * + OAuth-knappar. Väntelistan nås via separat hero-CTA till `/vantelista`.
 *
 * Formuläret är delade `<LoginForm />` (samma action som `/logga-in`-routen).
 * Suspense-wrap eftersom det använder `useSearchParams` (App Router-mönster).
 *
 * OAuth-knapparna är stubs denna fas (FAS-DEFERRAL per Klas Prompt 1): de
 * pekar mot `/logga-in?provider=<id>` så befintlig auth-route kan upptäcka
 * query-paramet när fullt OAuth-flöde wires:as.
 *
 * Auth-kortet har scoped token-overrides via `.jp-land-auth` så det
 * renderas i ljust läge även i dark mode (HANDOVER §2.4).
 */
export function AuthCard() {
  return (
    <div className="jp-land-auth">
      <h2
        style={{
          margin: 0,
          fontSize: 18,
          fontWeight: 600,
          // G1 (ADR 0068): navy → accent; resolvas mot .jp-land-auth:s
          // light-pin (kortet renderas alltid ljust).
          color: "var(--jp-accent-800)",
          letterSpacing: "-0.01em",
        }}
      >
        Logga in
      </h2>

      <Suspense fallback={null}>
        <LoginForm />
      </Suspense>

      <Link
        href="/logga-in?reset=1"
        style={{
          fontSize: 13.5,
          alignSelf: "flex-end",
          textDecoration: "underline",
          color: "var(--jp-accent-700)",
          marginTop: -4,
        }}
      >
        Glömt lösenord?
      </Link>

      <div
        className="jp-land-auth__sep"
        role="separator"
        aria-orientation="horizontal"
      >
        <span>eller logga in med</span>
      </div>

      <div className="jp-land-auth__oauth">
        {OAUTH_PROVIDERS.map((p) => (
          <a
            key={p.id}
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

      <p
        style={{
          fontSize: 13.5,
          color: "var(--jp-ink-2)",
          margin: 0,
          lineHeight: 1.5,
        }}
      >
        Inget konto?{" "}
        <Link
          href="/vantelista"
          style={{ color: "var(--jp-accent-700)", textDecoration: "underline" }}
        >
          Anmäl dig till väntelistan
        </Link>{" "}
        — JobbPilot är i sluten beta.
      </p>
    </div>
  );
}
