function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

export const env = {
  get BACKEND_URL() {
    return requireEnv("BACKEND_URL");
  },

  /**
   * DEV-ONLY throwaway tooling switch - REMOVE BEFORE LAUNCH with everything it gates
   * (docs/runbooks/release-checklist.md). Mirrors the backend's
   * `DevTools:EnableResetMyData`, and the two are set together or not at all: with only
   * this one on, the button renders and every press fails at the API.
   *
   * NOT `requireEnv`: absent must mean OFF, not a boot crash. It gates a DESTRUCTIVE
   * action, so anything other than the exact string "true" is off - a typo, an empty
   * value, "1", "yes" all fail closed. Deliberately not `NEXT_PUBLIC_`: this is read in
   * a Server Component only and has no business in the client bundle.
   */
  get DEV_TOOLS_RESET_ENABLED() {
    return process.env.DEV_TOOLS_RESET_ENABLED === "true";
  },
};
