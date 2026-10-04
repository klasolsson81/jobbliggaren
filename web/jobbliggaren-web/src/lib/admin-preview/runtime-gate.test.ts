import { afterEach, describe, expect, it, vi } from "vitest";
import { requireAdminPreview } from "./runtime-gate";

vi.mock("next/navigation", () => ({
  redirect: (path: string) => {
    throw new Error(`REDIRECT:${path}`);
  },
}));

describe("requireAdminPreview (ADR 0150 D5 (b))", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("sends a visitor to /admin when the running server lacks the flag", () => {
    vi.stubEnv("ADMIN_PREVIEW_ENABLED", "");
    expect(() => requireAdminPreview()).toThrow(/^REDIRECT:\/admin$/);
  });

  it("lets the preview render when the flag is exactly true", () => {
    vi.stubEnv("ADMIN_PREVIEW_ENABLED", "true");
    expect(() => requireAdminPreview()).not.toThrow();
  });
});
