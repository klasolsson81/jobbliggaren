import { describe, expect, it, vi } from "vitest";
import { type JobAdShareData, type SharePort, copyLink, hasNativeShare, shareNatively } from "./share-job-ad";

const DATA: JobAdShareData = {
  title: "Sjuksköterska till vårdcentral – Region Exempel",
  url: "https://jobbliggaren.se/jobb/0b0b0b0b-1111-4222-8333-444444444444",
};

const refusal = (name: string) => Object.assign(new Error(name), { name });

describe("hasNativeShare", () => {
  it("is true where the browser has a share sheet", () => {
    expect(hasNativeShare({ share: vi.fn() }, DATA)).toBe(true);
  });

  it("is false where it has none (desktop Firefox, Android WebView)", () => {
    expect(hasNativeShare({}, DATA)).toBe(false);
  });

  it("follows canShare where the browser has it", () => {
    expect(hasNativeShare({ share: vi.fn(), canShare: () => false }, DATA)).toBe(false);
    expect(hasNativeShare({ share: vi.fn(), canShare: () => true }, DATA)).toBe(true);
  });
});

describe("shareNatively", () => {
  it("hands the sheet exactly the title and the link, and nothing to the clipboard", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    const writeText = vi.fn();
    const port: SharePort = { share, clipboard: { writeText } };

    await expect(shareNatively(port, { ...DATA, extra: "x" } as JobAdShareData)).resolves.toBe("shared");
    expect(share).toHaveBeenCalledTimes(1);
    expect(share.mock.calls[0]?.[0]).toStrictEqual({ title: DATA.title, url: DATA.url });
    expect(writeText).not.toHaveBeenCalled();
  });

  it("reports a cancelled sheet as cancelled", async () => {
    await expect(shareNatively({ share: vi.fn().mockRejectedValue(refusal("AbortError")) }, DATA)).resolves.toBe("cancelled");
  });

  it.each(["NotAllowedError", "TypeError", "DataError", "InvalidStateError"])(
    "reports %s as failed and never copies in its place",
    async (name) => {
      const writeText = vi.fn();
      const port: SharePort = { share: vi.fn().mockRejectedValue(refusal(name)), clipboard: { writeText } };

      await expect(shareNatively(port, DATA)).resolves.toBe("failed");
      expect(writeText).not.toHaveBeenCalled();
    }
  );

  it("does not open a sheet that refuses this data", async () => {
    const share = vi.fn();

    await expect(shareNatively({ share, canShare: () => false }, DATA)).resolves.toBe("failed");
    expect(share).not.toHaveBeenCalled();
  });
});

describe("copyLink", () => {
  it("copies the link, and only the link", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    await expect(copyLink({ clipboard: { writeText } }, DATA.url)).resolves.toBe("copied");
    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText).toHaveBeenCalledWith(DATA.url);
  });

  it("reports a refused copy as failed", async () => {
    const writeText = vi.fn().mockRejectedValue(refusal("NotAllowedError"));

    await expect(copyLink({ clipboard: { writeText } }, DATA.url)).resolves.toBe("failed");
  });

  it("reports failed where the browser has no clipboard", async () => {
    await expect(copyLink({}, DATA.url)).resolves.toBe("failed");
  });
});
