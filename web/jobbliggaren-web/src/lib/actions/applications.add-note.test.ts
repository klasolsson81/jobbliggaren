import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import svApplications from "../../../messages/sv/applications.json";
import svValidation from "../../../messages/sv/validation.json";
import svErrors from "../../../messages/sv/errors.json";

// addNoteAction's failure contract. The echo is the load-bearing half: `AddNoteForm` is an
// uncontrolled `<form action={…}>`, and React 19 resets it after EVERY action, so without `values`
// on the returned state a failed save destroys a note that can run to several paragraphs.
//
// All four failure arms are pinned, not a representative one — the echo is attached per return, so
// a dropped `values` on a single arm is exactly the regression a one-arm test would miss.
//
// Mock setup mirrors applications.batch-transition.test.ts: getSessionId / env / revalidatePath are
// mocked, and getTranslations resolves through a REAL translator over the Swedish catalogue, so the
// asserted copy is the shipped string rather than a key.

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
      namespace: namespace as never,
    }),
}));

import { addNoteAction } from "./applications";

const GUID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const NOTE = "Ringde rekryteraren och fick besked på fredag.";

function form(content: string): FormData {
  const fd = new FormData();
  fd.set("content", content);
  return fd;
}

beforeEach(() => {
  getSessionId.mockResolvedValue("sess-1");
  revalidatePathMock.mockReset();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("addNoteAction — every failure arm echoes the submitted note back", () => {
  it("(a) no session: the note survives a sign-out as much as any other failure", async () => {
    getSessionId.mockResolvedValueOnce(null);
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await addNoteAction(GUID, form(NOTE));

    expect(result).toEqual({
      success: false,
      error: "Du är inte inloggad.",
      values: { content: NOTE },
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(b) validation refusal: the rejected text comes back, not an empty field", async () => {
    // An over-long note is the arm where the echo matters most — the user has to EDIT what they
    // wrote, which is impossible if the refusal also deleted it.
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const tooLong = "x".repeat(5001);

    const result = await addNoteAction(GUID, form(tooLong));

    expect(result).toEqual({
      success: false,
      error: "Notering får vara max 5 000 tecken.",
      values: { content: tooLong },
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("(c) backend rejects: the mapped error copy plus the echo", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({ ok: false, status: 500 })),
    );

    const result = await addNoteAction(GUID, form(NOTE));

    expect(result).toEqual({
      success: false,
      error: "Kunde inte spara noteringen. Försök igen.",
      values: { content: NOTE },
    });
  });

  it("(d) transport throws: the network copy plus the echo", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("fetch failed");
      }),
    );

    const result = await addNoteAction(GUID, form(NOTE));

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
      values: { content: NOTE },
    });
  });

  it("carries no echo on success — there is nothing left to re-seed", async () => {
    // The counterfactual: the form clears itself on a successful save, so an echo here would be
    // state nothing reads. The success arm is `{ success: true }` and the type admits nothing else.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({ ok: true, status: 201 })),
    );

    const result = await addNoteAction(GUID, form(NOTE));

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledWith(`/ansokningar/${GUID}`);
  });
});
