/**
 * Jobbliggaren's own pages on other sites (#2178). The home page's `Organization` names every one of them in `sameAs`.
 *
 * Only pages the project itself holds: never a person's profile or personal site, because `sameAs` asserts identity
 * with the Organization (senior-cto-advisor, stage-B B7(c), and #2178's ruling of 2026-10-23).
 */

/** The public repository: source-available under PolyForm Noncommercial 1.0.0 (ADR 0072). `/for-utvecklare` links it. */
export const REPOSITORY_URL = "https://github.com/klasolsson81/jobbliggaren";

/** Jobbliggaren's LinkedIn company page. */
export const LINKEDIN_PAGE_URL = "https://www.linkedin.com/company/jobbliggaren";

export const OFFICIAL_PROFILES = [REPOSITORY_URL, LINKEDIN_PAGE_URL] as const;
