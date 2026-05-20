"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowRight, Plus } from "lucide-react";
import { AuthCard, type AuthMode } from "./auth-card";

/**
 * LandingHeroSection — navy hero med copy-kolumn vänster + AuthCard höger
 * (HANDOVER §7.1 punkt 2).
 *
 * Klient-island eftersom det håller mode-state delat mellan hero-CTA
 * "Skapa konto" och AuthCard. När CTA:n klickas flippas AuthCard till
 * register-tab + sidan scrollar till toppen (matchar prototyp-beteende
 * `setMode("register"); window.scrollTo({ top: 0, behavior: "smooth" })`).
 *
 * "Utforska som gäst" navigerar till `/jobb` (samma route som auth-gated
 * användare landar på efter login). Backend-middleware hanterar
 * autentisering-redirecten om användaren inte är inloggad — landing-koden
 * gör ingen client-side auth-check.
 *
 * Civic-utility-disciplin: inga Sparkles-ikoner, inga gradient-bg, inga
 * trust-pills. CTA-ikoner (Plus, ArrowRight) är funktionella `lucide-react`
 * monogram, ingen "AI"-konnotation.
 */
export function LandingHeroSection() {
  const router = useRouter();
  const [mode, setMode] = useState<AuthMode>("login");

  const onSkapaKonto = () => {
    setMode("register");
    if (typeof window !== "undefined") {
      window.scrollTo({ top: 0, behavior: "smooth" });
    }
  };

  return (
    <section className="jp-land-hero">
      <div className="jp-land-hero__inner">
        <div className="jp-land-hero__copy">
          <h1 className="jp-land-hero__title">
            Verktyg för svenska jobbsökare
          </h1>
          <p className="jp-land-hero__lede">
            Skapa professionella CV, sök bland aktiva annonser och följ upp
            varje ansökan — från utkast till svar.
          </p>
          <div className="jp-land-hero__ctas">
            <button
              type="button"
              className="jp-btn jp-btn--lg"
              style={{ background: "#fff", color: "#0A2647", borderColor: "#fff" }}
              onClick={onSkapaKonto}
            >
              <Plus size={16} aria-hidden="true" /> Skapa konto
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

        <AuthCard mode={mode} onModeChange={setMode} />
      </div>
    </section>
  );
}
