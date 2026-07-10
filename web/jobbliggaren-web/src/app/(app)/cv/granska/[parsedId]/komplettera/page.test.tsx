import { describe, it, expect, vi, beforeEach } from "vitest";
import CvGapFillPage from "./page";

/**
 * Fas 4b PR-8.3 (CTO Q7(c)) — komplettera-barnrouten är superseded av Slutför-
 * guiden och 308-redirectar (permanent) till /cv/slutfor/[parsedId]. Auth-grinden
 * körs FÖRE redirecten så en oinloggad besökare landar på /logga-in. Både
 * `redirect` och `permanentRedirect` mockas att kasta (som i produktion) så att
 * flödet avbryts och destinationen kan pinnas. Speglar RSC-sid-testmönstret i
 * `@modal/(.)cv/importera/page.test.tsx`.
 */

const redirect = vi.fn((url: string) => {
  throw new Error(`NEXT_REDIRECT:${url}`);
});
const permanentRedirect = vi.fn((url: string) => {
  throw new Error(`NEXT_PERMANENT_REDIRECT:${url}`);
});
const getServerSession = vi.fn();

vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
  permanentRedirect: (url: string) => permanentRedirect(url),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

function invoke() {
  return CvGapFillPage({ params: Promise.resolve({ parsedId: PARSED_ID }) });
}

describe("/cv/granska/[parsedId]/komplettera (permanent redirect till Slutför-guiden)", () => {
  beforeEach(() => {
    redirect.mockClear();
    permanentRedirect.mockClear();
    getServerSession.mockReset();
  });

  it("permanentRedirectar till /cv/slutfor/{parsedId} för inloggad", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });

    await expect(invoke()).rejects.toThrow();

    expect(permanentRedirect).toHaveBeenCalledWith(`/cv/slutfor/${PARSED_ID}`);
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirectar till /logga-in FÖRE permanent-redirecten när session saknas", async () => {
    getServerSession.mockResolvedValue(null);

    await expect(invoke()).rejects.toThrow();

    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(permanentRedirect).not.toHaveBeenCalled();
  });
});
