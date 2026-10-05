export const LEGAL_SECTION_IDS = {
  integritet: ["controller", "data-protection-officer", "account-profile-applications", "operations", "recent-searches", "advertiser-contacts", "notifications", "recipients", "international-transfers", "retention", "rights", "complaints", "eligibility", "automated-decisions", "security", "cookies", "policy-changes"],
  villkor: ["provider", "service", "eligibility", "account-use", "ownership", "availability", "terms-changes", "account-closure", "disputes"],
  cookies: ["about-cookies", "essential-cookies", "functional-cookies", "browser-storage", "tracking", "cookie-controls", "future-cookies", "policy-changes"],
  tillganglighet: ["accessibility-status", "accessibility-work", "known-limitations", "report-barrier", "assessment"],
} as const;
export function legalSectionId(page: keyof typeof LEGAL_SECTION_IDS, index: number): string {
  const id = LEGAL_SECTION_IDS[page][index];
  if (!id) throw new Error("Legal section identifiers must match the content catalog.");
  return id;
}
