import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { NewApplicationForm } from "@/components/applications/new-application-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("ansokningar.new.meta.title") };
}

export default async function NyAnsokningPage() {
  const t = await getTranslations("pages");

  return (
    // /ny-ansokan is in V3_NATIVE_ROUTES (top-level, moved out of the
    // /ansokningar/[id] sibling space so the application-detail modal intercept
    // can't catch it on soft-nav — #332). No transitional shell container → the
    // page owns its own jp-container/jp-page (design-reviewer F5 Major #1).
    <div className="jp-container jp-page flex flex-col gap-6">
      <header className="flex flex-col gap-1">
        <h1 className="jp-h1">{t("ansokningar.new.title")}</h1>
        <p className="jp-lede">{t("ansokningar.new.lede")}</p>
      </header>

      <NewApplicationForm />
    </div>
  );
}
