import { useTranslations } from "next-intl";
import { ChangeEmailSetting } from "./change-email-setting";
import { LanguageSetting } from "./language-setting";

/**
 * The Konto section of /mina-sidor (#1891): the address and the language, as two groups in one card.
 *
 * Changing the address reads only the session's address, so it renders whatever the profile read
 * gave (design-reviewer Major 3, #1740). The language reads the profile; when that read failed, its
 * group says so in one sentence instead of showing a control it could not pre-fill.
 */
export function AccountSection({
  email,
  language,
  retryAfterSeconds,
}: {
  email: string;
  /** The saved language, or null when the profile could not be read. */
  language: string | null;
  /** Set when the profile read was rate limited. */
  retryAfterSeconds?: number;
}) {
  const t = useTranslations("settings");
  const tp = useTranslations("pages");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{tp("minaSidor.sections.konto")}</h2>
      <ChangeEmailSetting currentEmail={email} />
      <section className="jp-settings-group" aria-labelledby="mina-sidor-language">
        <h3 id="mina-sidor-language" className="jp-settings-group__title">
          {t("display.languageLabel")}
        </h3>
        {language !== null ? (
          <LanguageSetting initialLanguage={language} />
        ) : (
          <p className="text-body-sm text-text-primary">
            {retryAfterSeconds === undefined
              ? tp("minaSidor.profileLoadError")
              : tp("minaSidor.rateLimited", { seconds: retryAfterSeconds })}
          </p>
        )}
      </section>
    </section>
  );
}
