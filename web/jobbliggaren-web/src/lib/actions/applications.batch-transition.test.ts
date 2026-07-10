import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import svApplications from "../../../messages/sv/applications.json";
import svValidation from "../../../messages/sv/validation.json";
import svErrors from "../../../messages/sv/errors.json";

// batchTransitionAction-kontrakt (#630 PR 10, Tabell-vyns bulkrad + grupp-ångra):
// per-item-par {applicationId, targetStatus} → EN atomär batch-endpoint (PR 9).
// Speglar create-payload-testets mock-setup: getSessionId / env / revalidatePath
// mockas; getTranslations → en RIKTIG translator över den svenska katalogen
// (source of truth), respekterar namespace-argumentet så alla tre namespaces
// (applications.ui / errors / validation) resolvar sina verbatim-strängar.

const getSessionId = vi.hoisted(() =>
  vi.fn<() => Promise<string | null>>(async () => "sess-1"),
);
vi.mock("@/lib/auth/session", () => ({ getSessionId }));

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://backend.test" },
}));

const revalidatePathMock = vi.fn();
vi.mock("next/cache", () => ({
  revalidatePath: (p: string) => revalidatePathMock(p),
}));

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: {
        applications: svApplications,
        validation: svValidation,
        errors: svErrors,
      },
      // Dynamiskt namespace (str) — casta så createTranslator inte kräver en
      // literal NamespaceKey; runtime resolvar rätt katalog per anrop.
      namespace: namespace as never,
    }),
}));

import { batchTransitionAction } from "./applications";

const GUID_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GUID_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

function guid(i: number): string {
  return `00000000-0000-0000-0000-${i.toString(16).padStart(12, "0")}`;
}

function okFetch() {
  return vi.fn(async () => ({ ok: true, status: 200 }));
}

beforeEach(() => {
  getSessionId.mockResolvedValue("sess-1");
  revalidatePathMock.mockReset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("batchTransitionAction", () => {
  it("(a) inte inloggad → {success:false} med svensk notLoggedIn-copy, ingen fetch", async () => {
    getSessionId.mockResolvedValueOnce(null);
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
    ]);

    expect(result).toEqual({ success: false, error: "Du är inte inloggad." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(b) tom items → valideringsfel 'Minst en ansökan måste anges.', ingen fetch", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([]);

    expect(result).toEqual({
      success: false,
      error: "Minst en ansökan måste anges.",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(c) 101 items → 'Max 100 ansökningar per bulkåtgärd.', ingen fetch", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const items = Array.from({ length: 101 }, (_, i) => ({
      applicationId: guid(i + 1),
      targetStatus: "Ghosted" as const,
    }));

    const result = await batchTransitionAction(items);

    expect(result).toEqual({
      success: false,
      error: "Max 100 ansökningar per bulkåtgärd.",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(d) samma id med olika målstatus → 'Samma ansökan förekommer med olika målstatus.', ingen fetch", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
      { applicationId: GUID_A, targetStatus: "Rejected" },
    ]);

    expect(result).toEqual({
      success: false,
      error: "Samma ansökan förekommer med olika målstatus.",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(e) ogiltigt guid avvisas före fetch (applicationIdInvalid)", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: "inte-ett-guid", targetStatus: "Ghosted" },
    ]);

    expect(result).toEqual({
      success: false,
      error: "Ogiltigt ansöknings-ID.",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(e) okänd status avvisas före fetch (statusInvalid)", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Blaha" },
    ]);

    expect(result).toEqual({ success: false, error: "Ogiltig status." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(f) happy path: POST till batch-endpointen med exakt {items} body, revalidatePath, success", async () => {
    const fetchMock = okFetch();
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
      { applicationId: GUID_B, targetStatus: "Rejected" },
    ]);

    expect(result).toEqual({ success: true });
    expect(fetchMock).toHaveBeenCalledTimes(1);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://backend.test/api/v1/applications/batch-transition");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({
      items: [
        { applicationId: GUID_A, targetStatus: "Ghosted" },
        { applicationId: GUID_B, targetStatus: "Rejected" },
      ],
    });
    expect(revalidatePathMock).toHaveBeenCalledWith("/ansokningar");
  });

  it("(g) res 404 → errors.notFound (uniform, aldrig body-läst)", async () => {
    const fetchMock = vi.fn(async () => ({ ok: false, status: 404 }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
    ]);

    expect(result).toEqual({ success: false, error: "Resursen hittades inte." });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("(h) res 400 → transitionFailed-fallback 'Statusbytet misslyckades.'", async () => {
    const fetchMock = vi.fn(async () => ({ ok: false, status: 400 }));
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
    ]);

    expect(result).toEqual({
      success: false,
      error: "Statusbytet misslyckades.",
    });
  });

  it("(i) fetch kastar → serverUnreachable-copy", async () => {
    const fetchMock = vi.fn(async () => {
      throw new Error("network down");
    });
    vi.stubGlobal("fetch", fetchMock);

    const result = await batchTransitionAction([
      { applicationId: GUID_A, targetStatus: "Ghosted" },
    ]);

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
    });
  });
});
