// The one-time code every flow mails, in a module without Zod: a page that checks a code in the browser imports it, and
// importing it from a module that builds schemas puts all of Zod into that page's bundle (#1975, /adressbyte).

/** MIRROR of the backend `LoginChallengePolicy.CodeLength`: six ASCII digits, after the trim every code field gets. */
export const CODE_PATTERN = /^[0-9]{6}$/;

/**
 * How many wrong codes end a code: the login's, the re-authentication's, and the address change an administrator
 * starts (#1975), which takes the same count. MIRROR of the backend `LoginChallengePolicy.MaxAttempts`; `/adressbyte`
 * states it in its one refusal.
 */
export const CODE_MAX_ATTEMPTS = 3;
