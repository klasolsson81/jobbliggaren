import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AddressChangeForm } from "@/components/auth/address-change-form";
import { FocusHeading } from "@/components/auth/focus-heading";

// Rendered per request and never cached: what it answers is about an account.
export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return {
    title: t("auth.addressChange.meta.title"),
    robots: { index: false, follow: false },
  };
}

/**
 * `/adressbyte` (#1975, ADR 0153): where an account's owner completes the address change an administrator started,
 * with the account's current address, the new one and the code mailed there. Public: it reads and issues no session,
 * so it answers without one. It asks not to be indexed and is not in the sitemap, and the code mail links here with
 * no parameter, so nothing about the account travels in a URL.
 *
 * The default Referrer-Policy stays: a form posted without JavaScript needs its Origin for Next to take the Server
 * Action, and `no-referrer` sends it as null (`LOGIN_LINK_REFERRER_POLICY` records the measurement).
 */
export default async function AddressChangePage() {
  const t = await getTranslations("pages");

  return (
    <div className="flex flex-col gap-8">
      <FocusHeading>{t("auth.addressChange.title")}</FocusHeading>
      <AddressChangeForm />
    </div>
  );
}
