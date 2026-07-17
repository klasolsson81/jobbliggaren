import { useTranslations } from "next-intl";
import type { AdContactDto } from "@/lib/dto/job-ads";

/**
 * RecruiterContactBlock — #842 PR4. Pure presentational Server Component (no
 * "use client", zero interactivity), rendered on EXACTLY the two DETAIL surfaces
 * the re-bind (R2) allows: the job-ad detail and the application detail's
 * preserved-ad panel. NEVER on a list/browse card — a card renders ~20 ads per
 * page over the whole corpus, and structured recruiter contacts there would be a
 * bulk-harvest surface (the schema split in dto/job-ads.ts makes that
 * structurally impossible; this component simply must never be imported by a card).
 *
 * ## The derived-contact truth rule (R1(b), hard)
 * An `isDerived` contact was extracted by us from the ad body, not declared by
 * the advertiser. We must NEVER present our extraction as the advertiser's own
 * statement, so each derived entry carries a neutral "Från annonstexten" tag
 * (rectangular, uppercase, no icon — the .jp-tag--neutral idiom; the visible
 * label IS its accessible name). Derived entries always have `name === null` (the
 * backend never guesses a name), so the entry LEADS with its email/phone value
 * itself, with the provenance tag inline beside that lead so value and marker are
 * read together.
 *
 * Renders nothing when `contacts` is empty — the caller need not gate (both
 * surfaces pass [] when the ad holds none). Null fields are omitted (no
 * placeholder dashes), matching the rest of the detail. Civic tone: no emoji, no
 * exclamation marks, high contrast (content ink, never gray body text). Phone
 * text is shown verbatim from the wire — we never re-format the number.
 */

interface ContactMethod {
  kind: "email" | "phone";
  value: string;
  href: string;
}

function methods(contact: AdContactDto): ContactMethod[] {
  const list: ContactMethod[] = [];
  if (contact.email) {
    list.push({
      kind: "email",
      value: contact.email,
      href: `mailto:${contact.email}`,
    });
  }
  if (contact.phone) {
    // Verbatim href — tel: carries the number exactly as it arrived; we do not
    // normalise or strip spaces (no re-formatting, per R2/design copy).
    list.push({
      kind: "phone",
      value: contact.phone,
      href: `tel:${contact.phone}`,
    });
  }
  return list;
}

export interface RecruiterContactBlockProps {
  contacts: readonly AdContactDto[];
}

export function RecruiterContactBlock({ contacts }: RecruiterContactBlockProps) {
  // Synchronous next-intl translator — keeps this a non-async RSC (shared by the
  // full page, the @modal serialized slot and the application detail, with sync
  // tests), parity with JobAdMatchSection / JobTags.
  const t = useTranslations("jobads.ui.contact");

  if (contacts.length === 0) return null;

  return (
    <section aria-labelledby="jp-recruiter-contacts-title">
      <div className="jp-section-label" id="jp-recruiter-contacts-title">
        {t("title")}
      </div>

      <ul className="flex flex-col gap-4">
        {contacts.map((contact, i) => {
          const contactMethods = methods(contact);
          // A named (declared) contact headlines with its name; a nameless one
          // (always the case for a derived contact) headlines with its first
          // contact value, so the value leads. Remaining methods render below as
          // labelled rows.
          const leadMethod = contact.name ? undefined : contactMethods[0];
          const restMethods = contact.name
            ? contactMethods
            : contactMethods.slice(1);

          return (
            <li
              key={`contact-${i}-${contact.email ?? contact.phone ?? contact.name ?? "derived"}`}
              className="flex flex-col gap-1"
            >
              {(contact.name || leadMethod || contact.isDerived) && (
                <div className="flex flex-wrap items-center gap-2">
                  {contact.name ? (
                    <span className="text-body font-semibold text-text-primary">
                      {contact.name}
                    </span>
                  ) : leadMethod ? (
                    <a
                      href={leadMethod.href}
                      className="text-body font-semibold underline underline-offset-2"
                    >
                      {leadMethod.value}
                    </a>
                  ) : null}
                  {contact.isDerived && (
                    <span className="jp-tag jp-tag--neutral" data-tag="derived">
                      {t("derived")}
                    </span>
                  )}
                </div>
              )}

              {contact.role && (
                <p className="text-body-sm text-text-primary">{contact.role}</p>
              )}

              {restMethods.length > 0 && (
                <dl className="flex flex-col gap-1">
                  {restMethods.map((method) => (
                    <div
                      key={method.kind}
                      className="flex flex-wrap items-baseline gap-x-2"
                    >
                      <dt className="text-body-sm text-text-secondary">
                        {method.kind === "email" ? t("email") : t("phone")}
                      </dt>
                      <dd className="text-body-sm">
                        <a
                          href={method.href}
                          className="underline underline-offset-2"
                        >
                          {method.value}
                        </a>
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
