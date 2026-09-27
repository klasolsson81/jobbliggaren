import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import svApplications from "../../../messages/sv/applications.json";
import svValidation from "../../../messages/sv/validation.json";
import svErrors from "../../../messages/sv/errors.json";

// A saved follow-up outcome or a planned follow-up can change the list's attention signal
// (an overdue Pending follow-up is what puts an application in the queue), and the queue's
// "Registrera utfall" opens the detail over the list. Both actions therefore refresh the list
// as well as the detail. Mock setup mirrors applications.add-note.test.ts.

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

import { addFollowUpAction, recordFollowUpOutcomeAction } from "./applications";

const APP = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const FOLLOW_UP = "cccccccc-cccc-cccc-cccc-cccccccccccc";

beforeEach(() => {
  getSessionId.mockResolvedValue("sess-1");
  revalidatePathMock.mockReset();
  vi.stubGlobal("fetch", vi.fn(async () => ({ ok: true, status: 204 })));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("follow-up actions refresh the list and the detail", () => {
  it("recordFollowUpOutcomeAction revalidates /ansokningar and the application", async () => {
    const fd = new FormData();
    fd.set("outcome", "Responded");

    const result = await recordFollowUpOutcomeAction(APP, FOLLOW_UP, fd);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledWith("/ansokningar");
    expect(revalidatePathMock).toHaveBeenCalledWith(`/ansokningar/${APP}`);
  });

  it("addFollowUpAction revalidates /ansokningar and the application", async () => {
    const fd = new FormData();
    fd.set("channel", "Email");
    fd.set("scheduledAt", "2026-10-05");

    const result = await addFollowUpAction(APP, fd);

    expect(result).toEqual({ success: true });
    expect(revalidatePathMock).toHaveBeenCalledWith("/ansokningar");
    expect(revalidatePathMock).toHaveBeenCalledWith(`/ansokningar/${APP}`);
  });
});
