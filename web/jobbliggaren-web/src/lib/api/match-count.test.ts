import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
}));

import { getDraftMatchCount } from "./match-count";

const originalFetch = global.fetch;

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

/**
 * #551 punkt 4 — den här filen hade inget testhem alls, och det är där PR:ens
 * andra defekt bodde: bodyn byggs som ett EXPLICIT whitelist-literal, så ett nytt
 * fält som inte skrivs in försvinner tyst mellan hooken och backend. Symtomet var
 * inte ett fel utan ett fel TAL — wizardens live-siffra räknad mot ett annat WHERE
 * än det som sparas, vilket bryter "samma siffra"-harmonin (ADR 0089/0079 H2).
 * Whitelist-formen är rätt (ingen oavsiktlig genomsläppning); den kräver bara en
 * pin per fält.
 */
describe("getDraftMatchCount — hela draften når wire:n (#551 punkt 4)", () => {
  function okResponse() {
    return {
      ok: true,
      status: 200,
      json: async () => ({ count: 7 }),
    } as unknown as Response;
  }

  async function bodyFor(over: Partial<Parameters<typeof getDraftMatchCount>[0]> = {}) {
    const fetchMock = vi.fn().mockResolvedValue(okResponse());
    global.fetch = fetchMock;
    await getDraftMatchCount({
      occupationGroups: [],
      regions: [],
      municipalities: [],
      employmentTypes: [],
      remote: false,
      ...over,
    });
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    return JSON.parse(String(init.body)) as Record<string, unknown>;
  }

  it("remote=true når bodyn", async () => {
    expect(await bodyFor({ remote: true })).toMatchObject({ remote: true });
  });

  it("remote=false når bodyn som false — aldrig utelämnad", async () => {
    const body = await bodyFor({ remote: false });
    expect(body).toMatchObject({ remote: false });
    expect("remote" in body).toBe(true);
  });

  it("distans reser bredvid de två id-axlarna, aldrig i stället för dem", async () => {
    const body = await bodyFor({
      regions: ["CifL_Rzy_Mku"],
      municipalities: ["AvNB_uwa_6n6"],
      remote: true,
    });
    expect(body).toMatchObject({
      regions: ["CifL_Rzy_Mku"],
      municipalities: ["AvNB_uwa_6n6"],
      remote: true,
    });
  });
});
