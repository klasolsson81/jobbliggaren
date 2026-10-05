import { useTranslations } from "next-intl";
import { InformationReturnLink } from "./InformationReturnLink";
import styles from "./information.module.css";
export function InformationPageFrame({ title, lede, headingId, sections = [], notice, children }: {
  title: string; lede: string; headingId: string;
  sections?: readonly { id: string; label: string }[];
  notice?: string;
  children: React.ReactNode;
}) {
  const t = useTranslations("information");
  return <main id="main" tabIndex={-1} className={`focus:outline-none ${styles.page}`}>
    <div className={styles.return}>{notice && <p className="mb-3 text-body">{notice}</p>}<InformationReturnLink /></div>
    <section className="jp-pagehero" aria-labelledby={headingId}>
      <div className="jp-pagehero__inner"><div className="jp-pagehero__main">
        <h1 id={headingId} tabIndex={-1} className="jp-pagehero__title">{title}</h1>
        <p className="jp-pagehero__lede">{lede}</p>
      </div></div>
    </section>
    <div className={styles.content}>
      {sections.length > 0 && <nav aria-label={t("contents")} className={styles.contents}>
        <h2 className="text-body-lg font-semibold">{t("contents")}</h2>
        <ul>{sections.map(section => <li key={section.id}><a href={`#${section.id}`}>{section.label}</a></li>)}</ul>
      </nav>}
      {children}
      {sections.length > 0 && <p className={styles.top}><a href={`#${headingId}`}>{t("top")}</a></p>}
      <div className={styles.end}><InformationReturnLink /></div>
    </div>
  </main>;
}
