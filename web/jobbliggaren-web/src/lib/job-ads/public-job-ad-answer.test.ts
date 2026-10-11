import { describe, expect, it } from "vitest";
import {
  PUBLIC_JOB_AD_BUSY_PATH,
  PUBLIC_JOB_AD_GONE_PATH,
  PUBLIC_JOB_AD_MISSING_PATH,
  RETRY_AFTER_DEFAULT_SECONDS,
  RETRY_AFTER_MAX_SECONDS,
  answerForPublicJobAdProbe,
  boundedRetryAfter,
  classifyPublicJobAdProbe,
  isInternalPublicJobAdPath,
  publicJobAdIdFromPath,
} from "./public-job-ad-answer";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";

describe("publicJobAdIdFromPath", () => {
  it("takes exactly /jobb/<guid>", () => {
    expect(publicJobAdIdFromPath(`/jobb/${ID}`)).toBe(ID);
    expect(publicJobAdIdFromPath(`/jobb/${ID.toUpperCase()}`)).toBe(ID.toUpperCase());
  });

  it.each([
    "/jobb",
    "/jobb/",
    "/jobb/abc",
    `/jobb/${ID}/`,
    `/jobb/${ID}/extra`,
    `/jobb/${ID}x`,
    `/jobb//${ID}`,
    `/jobb/%30b0b0b0b-1111-4222-8333-444444444444`,
    `/oversikt/${ID}`,
    `/foretag/${ID}`,
    `/jobb/${ID}%2f..`,
  ])("rejects %s, which keeps today's route", (pathname) => {
    expect(publicJobAdIdFromPath(pathname)).toBeNull();
  });
});

describe("isInternalPublicJobAdPath", () => {
  it.each(["/publik", "/publik/jobb/" + ID, "/publik/borttagen", "/publik/saknas"])("%s is internal", (p) => {
    expect(isInternalPublicJobAdPath(p)).toBe(true);
  });

  it.each(["/%70ublik/jobb/x", "/%70%75blik", "/publik%2Fjobb", "/%2570ublik/x", "/%252570ublik/x"])(
    "%s decodes to the internal prefix",
    (p) => {
      expect(isInternalPublicJobAdPath(p)).toBe(true);
    }
  );

  it("treats a path that does not decode as internal (a refusal costs a 404, a miss costs a duplicate URL)", () => {
    expect(isInternalPublicJobAdPath("/%E0%A4%A")).toBe(true);
  });

  it.each(["/", "/jobb/" + ID, "/publiktips", "/om", "/cv-granskning", "/jobb/%61bc"])("%s is not internal", (p) => {
    expect(isInternalPublicJobAdPath(p)).toBe(false);
  });
});

describe("boundedRetryAfter", () => {
  it("keeps an integer in range", () => {
    expect(boundedRetryAfter("17")).toBe(17);
    expect(boundedRetryAfter(" 45 ")).toBe(45);
  });

  it("caps a large number and never relays it raw", () => {
    expect(boundedRetryAfter("999999")).toBe(RETRY_AFTER_MAX_SECONDS);
    expect(boundedRetryAfter("1000000000")).toBe(RETRY_AFTER_DEFAULT_SECONDS);
  });

  it.each([null, "", "0", "-5", "abc", "12.5", "Wed, 21 Oct 2026 07:28:00 GMT", "30 seconds"])(
    "falls back to the default for %s",
    (value) => {
      expect(boundedRetryAfter(value)).toBe(RETRY_AFTER_DEFAULT_SECONDS);
    }
  );
});

describe("classifyPublicJobAdProbe", () => {
  it("reads the public endpoint's statuses (ADR 0160 D3)", () => {
    expect(classifyPublicJobAdProbe(200, null)).toEqual({ kind: "listed" });
    expect(classifyPublicJobAdProbe(410, null)).toEqual({ kind: "gone" });
    expect(classifyPublicJobAdProbe(404, null)).toEqual({ kind: "notFound" });
  });

  it("reads a 400 as a 404 (a deterministic client answer, so a 503 would make a crawler retry it)", () => {
    expect(classifyPublicJobAdProbe(400, null)).toEqual({ kind: "notFound" });
  });

  it("reads a 429 as the budget, with a bounded Retry-After", () => {
    expect(classifyPublicJobAdProbe(429, "12")).toEqual({ kind: "busy", retryAfterSeconds: 12 });
    expect(classifyPublicJobAdProbe(429, null)).toEqual({ kind: "busy", retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS });
  });

  it.each([301, 302, 307, 308, 401, 403, 405, 408, 500, 502, 503, 504, 0, 204])(
    "reads %i as an outage, never a removal",
    (status) => {
      expect(classifyPublicJobAdProbe(status, "5")).toEqual({ kind: "unavailable" });
    }
  );
});

describe("answerForPublicJobAdProbe", () => {
  it("rewrites a listed ad to its internal page, with no status of its own", () => {
    expect(answerForPublicJobAdProbe(ID, { kind: "listed" })).toEqual({
      path: `/publik/jobb/${ID}`,
      noindex: false,
    });
  });

  it("answers an ended and an erased ad with the one 410 page, which has no id in scope", () => {
    const answer = answerForPublicJobAdProbe(ID, { kind: "gone" });
    expect(answer).toEqual({ path: PUBLIC_JOB_AD_GONE_PATH, status: 410, noindex: true });
    expect(JSON.stringify(answer)).not.toContain(ID);
  });

  it("answers an unknown or never-published ad with a path no route matches", () => {
    expect(answerForPublicJobAdProbe(ID, { kind: "notFound" })).toEqual({
      path: PUBLIC_JOB_AD_MISSING_PATH,
      noindex: false,
    });
  });

  it("answers the budget and every outage with a 503 and a bounded Retry-After", () => {
    expect(answerForPublicJobAdProbe(ID, { kind: "busy", retryAfterSeconds: 12 })).toEqual({
      path: PUBLIC_JOB_AD_BUSY_PATH,
      status: 503,
      retryAfterSeconds: 12,
      noindex: true,
    });
    expect(answerForPublicJobAdProbe(ID, { kind: "unavailable" })).toEqual({
      path: PUBLIC_JOB_AD_BUSY_PATH,
      status: 503,
      retryAfterSeconds: RETRY_AFTER_DEFAULT_SECONDS,
      noindex: true,
    });
  });

  it("never answers an outage or the budget with a 404 or a 410", () => {
    for (const probe of [{ kind: "busy", retryAfterSeconds: 9 }, { kind: "unavailable" }] as const) {
      const { status } = answerForPublicJobAdProbe(ID, probe);
      expect(status).not.toBe(404);
      expect(status).not.toBe(410);
    }
  });
});
