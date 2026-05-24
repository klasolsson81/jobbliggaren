"use client";

import { useRouter } from "next/navigation";
import { ArrowRight } from "lucide-react";
import { AuthCard } from "./auth-card";

/**
 * LandingHeroSection — navy hero med copy-kolumn vänster + AuthCard höger
 * (HANDOVER §7.1 punkt 2).
 *
 * Klient-island eftersom CTA-knapparna använder `useRouter`. Per Klas-direktiv
 * 2026-05-24 (Steg 5 closed-beta-disciplin) är "Skapa konto"-CTA borttagen.
 * Två CTA:er återstår: "Anmäl till väntelista" (→ `/vantelista`) och
 * "Utforska som gäst" (→ `/jobb`, middleware hanterar auth-redirect).
 *
 * Civic-utility-disciplin: inga Sparkles-ikoner, inga gradient-bg, inga
 * trust-pills. CTA-ikon (ArrowRight) är funktionell `lucide-react` monogram,
 * ingen "AI"-konnotation.
 */
export function LandingHeroSection() {
  const router = useRouter();

  return (
    <section className="jp-land-hero">
      <div className="jp-land-hero__inner">
        <div className="jp-land-hero__copy">
          <h1 className="jp-land-hero__title">
            Håll ordning i ditt jobbsökande
          </h1>
          <p className="jp-land-hero__lede">
            Sök bland aktiva annonser från Platsbanken, skapa CV och brev, och följ upp
            varje ansökan, från utkast till svar.
          </p>
          <div className="jp-land-hero__ctas">
            <button
              type="button"
              className="jp-btn jp-btn--lg"
              style={{ background: "#fff", color: "#0A2647", borderColor: "#fff" }}
              onClick={() => router.push("/vantelista")}
            >
              Anmäl till väntelista
            </button>
            <button
              type="button"
              className="jp-btn jp-btn--lg"
              style={{
                background: "var(--jp-leaf-600)",
                color: "#FFFFFF",
                borderColor: "var(--jp-leaf-600)",
              }}
              onClick={() => router.push("/jobb")}
            >
              Utforska som gäst <ArrowRight size={16} aria-hidden="true" />
            </button>
          </div>
        </div>

        <AuthCard />
      </div>
    </section>
  );
}
