"use client";

import { useTranslations } from "next-intl";
import { DeleteAccountSection } from "@/components/me/delete-account-section";

interface PrivacyCardProps {
  userEmail: string;
}

/**
 * Sekretess och data-kort. Använder befintliga `<DeleteAccountSection />`
 * (TD-28 typed-confirmation + re-auth-flöde, ej regression i denna prompt).
 *
 * FAS-DEFERRAL: "Exportera mina data" är stub — Klas-prompt anger no-op +
 * TODO tills GDPR-export-pipeline finns. Renderas som secondary-knapp med
 * `aria-disabled` så användaren ser möjligheten men inte triggar något än.
 */
export function PrivacyCard({ userEmail }: PrivacyCardProps) {
  const t = useTranslations("settings");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("privacy.title")}</h2>
      <p className="text-body-sm text-text-primary">
        {t("privacy.description")}
      </p>
      <div className="flex flex-wrap gap-3 mt-2">
        <button
          type="button"
          className="jp-btn jp-btn--secondary jp-btn--sm"
          aria-disabled="true"
          // TODO: Fas 7 — wire mot riktig GDPR-export-pipeline. Knappen är
          // synlig så GDPR-rätten kommuniceras, men flödet är inte byggt än.
          onClick={(e) => e.preventDefault()}
          title={t("privacy.exportTitle")}
        >
          {t("privacy.export")}
        </button>
      </div>
      <div className="mt-4">
        <DeleteAccountSection currentEmail={userEmail} />
      </div>
    </section>
  );
}
