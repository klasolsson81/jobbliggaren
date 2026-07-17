import { describe, it, expect, vi, beforeEach } from "vitest";
import CvGapFillPage from "./page";

/**
 * CV-pivot 5c (R4) — komplettera-barnrouten är RETIRERAD tillsammans med
 * Slutför-guiden: rutten svarar 404 på route-nivå (notFound, INTE redirect —
 * se page.tsx). Auth-grinden körs FÖRE 404:an så en oinloggad besökare landar
 * på /logga-in (rutt-existens är ingen auth-oracle åt något håll). Både
 * `redirect` och `notFound` mockas att kasta (som i produktion) så att flödet
 * avbryts och dispositionen kan pinnas.
 */

const redirect = vi.fn((url: string) => {
  throw new Error(`NEXT_REDIRECT:${url}`);
});
const notFound = vi.fn(() => {
  throw new Error("NEXT_NOT_FOUND");
});
const getServerSession = vi.fn();

vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
  notFound: () => notFound(),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

function invoke() {
  return CvGapFillPage({ params: Promise.resolve({ parsedId: PARSED_ID }) });
}

describe("/cv/granska/[parsedId]/komplettera (retirerad — 404 på route-nivå)", () => {
  beforeEach(() => {
    redirect.mockClear();
    notFound.mockClear();
    getServerSession.mockReset();
  });

  it("svarar notFound för inloggad — aldrig en redirect till den retirerade guiden", async () => {
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });

    await expect(invoke()).rejects.toThrow("NEXT_NOT_FOUND");

    expect(notFound).toHaveBeenCalledTimes(1);
    expect(redirect).not.toHaveBeenCalled();
  });

  it("redirectar till /logga-in FÖRE 404:an när session saknas", async () => {
    getServerSession.mockResolvedValue(null);

    await expect(invoke()).rejects.toThrow("NEXT_REDIRECT:/logga-in");

    expect(redirect).toHaveBeenCalledWith("/logga-in");
    expect(notFound).not.toHaveBeenCalled();
  });
});
