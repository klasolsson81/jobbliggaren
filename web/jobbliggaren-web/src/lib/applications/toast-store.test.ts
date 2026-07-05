import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  dismissApplicationToast,
  getApplicationToastServerSnapshot,
  getApplicationToastSnapshot,
  showApplicationToast,
  subscribeApplicationToast,
} from "./toast-store";

beforeEach(() => {
  // Modul-store: nollställ mellan tester (samma idiom som drawer-anchor).
  dismissApplicationToast();
});

describe("toast-store (#630 PR 7, design §10)", () => {
  it("publicerar en statusbyte-toast med ångra-payload (previous status, ADR 0092 D3)", () => {
    showApplicationToast({
      kind: "statusChange",
      applicationId: "app-1",
      company: "Volvo",
      from: "Submitted",
      to: "Acknowledged",
    });
    expect(getApplicationToastSnapshot()).toMatchObject({
      kind: "statusChange",
      applicationId: "app-1",
      from: "Submitted",
      to: "Acknowledged",
    });
  });

  it("single-toast-modell: en ny toast ERSÄTTER den gamla (prototyp-exakt)", () => {
    const first = showApplicationToast({
      kind: "followUpLogged",
      company: "Volvo",
    });
    const second = showApplicationToast({
      kind: "error",
      message: "Statusbytet misslyckades.",
    });
    expect(second).toBeGreaterThan(first);
    expect(getApplicationToastSnapshot()).toMatchObject({ kind: "error" });
  });

  it("token-skyddad dismissal: en inaktuell timer dödar aldrig efterföljaren", () => {
    const first = showApplicationToast({
      kind: "followUpLogged",
      company: "Volvo",
    });
    showApplicationToast({ kind: "followUpLogged", company: "Scania" });
    // Den gamla toastens auto-close fyrar sent → ska INTE stänga den nya.
    dismissApplicationToast(first);
    expect(getApplicationToastSnapshot()).toMatchObject({ company: "Scania" });
  });

  it("dismissal utan token stänger ovillkorligt", () => {
    showApplicationToast({ kind: "followUpLogged", company: "Volvo" });
    dismissApplicationToast();
    expect(getApplicationToastSnapshot()).toBeNull();
  });

  it("notifierar prenumeranter vid publish och dismissal; unsubscribe slutar lyssna", () => {
    const listener = vi.fn();
    const unsubscribe = subscribeApplicationToast(listener);

    const token = showApplicationToast({
      kind: "followUpLogged",
      company: "Volvo",
    });
    expect(listener).toHaveBeenCalledTimes(1);

    dismissApplicationToast(token);
    expect(listener).toHaveBeenCalledTimes(2);

    unsubscribe();
    showApplicationToast({ kind: "followUpLogged", company: "Scania" });
    expect(listener).toHaveBeenCalledTimes(2);
  });

  it("server-snapshot är alltid null (SSR har aldrig en toast)", () => {
    showApplicationToast({ kind: "followUpLogged", company: "Volvo" });
    expect(getApplicationToastServerSnapshot()).toBeNull();
  });
});
