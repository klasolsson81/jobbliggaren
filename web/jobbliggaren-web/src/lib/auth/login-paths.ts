/** The login flow's pages (ADR 0142 "Page form"), where its actions and route handlers send a browser. */
export const LOGIN_ENTRY_PATH = "/logga-in";
export const LOGIN_CODE_PATH = "/logga-in/kod";
export const LOGIN_CONSENT_PATH = "/logga-in/villkor";

/**
 * The logout route handler (#1956). Every "Logga ut" form posts here natively, through `LogoutForm`. Open pages
 * of the previous build post here too, so a move must keep the old path answering for one rollout.
 */
export const LOGOUT_PATH = "/api/auth/logout";
