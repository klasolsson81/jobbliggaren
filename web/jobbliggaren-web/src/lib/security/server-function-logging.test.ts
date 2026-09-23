import { describe, expect, it } from "vitest";
import nextConfig from "../../../next.config";

// #1740 S6 (security-auditor). Next's own default is on (`config-shared.js`), so a config that drops the
// key logs again: the pin reads the value this app hands Next.
describe("next.config logging", () => {
  it("keeps Server Action arguments out of the dev server's output", () => {
    expect(nextConfig.logging).toMatchObject({ serverFunctions: false });
  });
});
