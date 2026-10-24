import type { JobAdShareData } from "./share-job-ad";

export type ShareTarget = "facebook" | "linkedin" | "whatsapp" | "email";

export interface ShareTargetLink {
  readonly target: ShareTarget;
  readonly href: string;
}

const WHITESPACE_RUN = /[\r\n\t]+/g;

/**
 * The share row's plain links (ADR 0183): the named platforms in alphabetical order, then e-mail. Each carries the
 * canonical link and nothing else, so the platform builds its preview from the page's own `og:title` and no copy of
 * the title or the employer is written into a post that outlives the ad. The mail subject is the page title, with any
 * run of line breaks and tabs made one space so an imported title cannot carry a header break into the mail.
 *
 * Every value goes through `encodeURIComponent`: a `+` is literal in a mailto value (RFC 6068), so a space is `%20`.
 * Facebook's `sharer.php` and LinkedIn's `share-offsite` are not in their owners' documentation; both need no
 * registered app and no script, and a broken one leaves the other routes.
 */
export function shareTargetLinks(data: JobAdShareData): readonly ShareTargetLink[] {
  const url = encodeURIComponent(data.url);
  const subject = encodeURIComponent(data.title.replace(WHITESPACE_RUN, " "));
  return [
    { target: "facebook", href: `https://www.facebook.com/sharer/sharer.php?u=${url}` },
    { target: "linkedin", href: `https://www.linkedin.com/sharing/share-offsite/?url=${url}` },
    { target: "whatsapp", href: `https://wa.me/?text=${url}` },
    { target: "email", href: `mailto:?subject=${subject}&body=${url}` },
  ];
}
