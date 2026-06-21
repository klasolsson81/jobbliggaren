import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { WaitlistForm } from "@/components/forms/WaitlistForm";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("landing");
  return {
    title: t("meta.waitlistTitle"),
    description: t("meta.waitlistDescription"),
  };
}

export default async function VantelistaPage() {
  const t = await getTranslations("landing");
  return (
    <>
      <header className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <p className="jp-pagehero__kicker">{t("waitlist.kicker")}</p>
            <h1 id="vantelista-heading" className="jp-pagehero__title">
              {t("waitlist.title")}
            </h1>
            <p className="jp-pagehero__lede">{t("waitlist.lede")}</p>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-2xl px-6 py-12">
        <section aria-labelledby="vantelista-heading" className="flex flex-col gap-8">
          <WaitlistForm />

          <div className="border-t border-border pt-6">
            <p className="text-body-sm text-text-secondary">
              {t("waitlist.privacyNote")}
            </p>
          </div>
        </section>
      </main>
    </>
  );
}
