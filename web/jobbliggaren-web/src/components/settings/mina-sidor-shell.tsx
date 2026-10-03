import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { MinaSidorNav, type MinaSidorSection } from "./mina-sidor-nav";

/**
 * The frame every /mina-sidor section renders (#1891): the pagehero band with the page title and
 * its static line (#1917), then the section menu beside the one section on show. Each page and
 * each `loading.tsx` renders it themselves, the /foretag pattern: there is no `mina-sidor/layout.tsx`,
 * because `v3-native-routes.test.ts` reads pages rather than layouts, and a layout would owe an
 * error boundary of its own (`route-boundaries.test.ts`).
 */
export function MinaSidorShell({
  active,
  children,
}: {
  active: MinaSidorSection;
  children: ReactNode;
}) {
  const t = useTranslations("pages");
  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("minaSidor.title")}</h1>
            <p className="jp-pagehero__lede">{t("minaSidor.lede")}</p>
          </div>
        </div>
      </section>
      <div className="jp-container jp-page">
        <div className="jp-settings-layout">
          <MinaSidorNav active={active} />
          <div className="jp-settings-section">{children}</div>
        </div>
      </div>
    </>
  );
}

/** Skeleton rows per section, roughly the rows the loaded card renders. */
const SKELETON_ROWS: Record<MinaSidorSection, number> = {
  matchning: 5,
  notiser: 3,
  konto: 2,
  sekretess: 2,
};

/**
 * The loading state of one section: the real band and menu, so the frame does not move when the
 * section arrives, and the card with its real title over flat grey rows (#739). The sr-only
 * `role="status"` announces; the shapes are decorative. It lives in this file because the route's
 * `loading.tsx` delegates here, and `v3-native-routes.test.ts` follows a delegation one hop only.
 */
export function MinaSidorLoading({ active }: { active: MinaSidorSection }) {
  const t = useTranslations("pages");
  const rows = Array.from({ length: SKELETON_ROWS[active] }, (_, i) => i);
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.minaSidor")}
      </span>
      <MinaSidorShell active={active}>
        <section className="jp-card" aria-hidden="true">
          <h2 className="jp-card__title">{t(`minaSidor.sections.${active}`)}</h2>
          <div className="flex flex-col gap-4">
            {rows.map((row) => (
              <span key={row} className="jp-skeleton block h-10 w-full" />
            ))}
          </div>
        </section>
      </MinaSidorShell>
    </>
  );
}

/**
 * A section that reads the profile, when the profile could not be read: the card keeps its title
 * and says so in one sentence. `getMyProfile` answers the backend's 404 as `error`, so a missing
 * profile lands here too. The menu and Logga ut stay, since they read nothing.
 */
export function ProfileUnavailable({
  title,
  retryAfterSeconds,
}: {
  title: string;
  /** Set when the read was rate limited; otherwise the read failed. */
  retryAfterSeconds?: number;
}) {
  const t = useTranslations("pages");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{title}</h2>
      <p className="text-body text-text-primary">
        {retryAfterSeconds === undefined
          ? t("minaSidor.profileLoadError")
          : t("minaSidor.rateLimited", { seconds: retryAfterSeconds })}
      </p>
    </section>
  );
}
