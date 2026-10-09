import { describe, expect, it } from "vitest";
import { keyFor, newSubmissionKey, type FeedbackContent } from "./submission-key";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

const content = (overrides: Partial<FeedbackContent> = {}): FeedbackContent => ({
  page: "jobs",
  rating: 4,
  comment: "Filtren är bra.",
  screenshot: null,
  shareDeviceContext: false,
  ...overrides,
});

describe("submission keys", () => {
  it("keep the key for the same content, so a resend replays", () => {
    const first = keyFor(null, content());
    expect(keyFor(first, content({ comment: "  Filtren är bra.  " })).key).toBe(first.key);
  });

  it.each([
    ["the rating", { rating: 5 }],
    ["the comment", { comment: "Filtren är bra, men långsamma." }],
    ["the page", { page: "job-ad" as const }],
    ["the image", { screenshot: new Blob(["x"]) }],
    ["the device-context choice", { shareDeviceContext: true }],
  ])("give a new key when %s changes", (_, change) => {
    const first = keyFor(null, content());
    expect(keyFor(first, content(change)).key).not.toBe(first.key);
  });

  it("compare an image by identity", () => {
    const image = new Blob(["x"]);
    const first = keyFor(null, content({ screenshot: image }));
    expect(keyFor(first, content({ screenshot: image })).key).toBe(first.key);
    expect(keyFor(first, content({ screenshot: new Blob(["x"]) })).key).not.toBe(first.key);
  });

  it("are version-4 UUIDs, also where randomUUID is missing", () => {
    expect(newSubmissionKey()).toMatch(UUID);
    const original = Object.getOwnPropertyDescriptor(crypto, "randomUUID");
    Object.defineProperty(crypto, "randomUUID", { value: undefined, configurable: true, writable: true });
    try {
      expect(newSubmissionKey()).toMatch(UUID);
    } finally {
      if (original) Object.defineProperty(crypto, "randomUUID", original);
      else delete (crypto as { randomUUID?: unknown }).randomUUID;
    }
    expect(typeof crypto.randomUUID).toBe("function");
  });
});
