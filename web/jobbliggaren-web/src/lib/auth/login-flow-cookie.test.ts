import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { cookieSetMock, cookieGetMock, cookieDeleteMock } = vi.hoisted(() => ({
  cookieSetMock: vi.fn(),
  cookieGetMock: vi.fn(),
  cookieDeleteMock: vi.fn(),
}));

vi.mock("next/headers", () => ({
  cookies: vi.fn(async () => ({
    set: cookieSetMock,
    get: cookieGetMock,
    delete: cookieDeleteMock,
  })),
}));

import { LOGIN_FLOW_COOKIE_NAME } from "./cookie-names";
import { encodeLoginFlow, type LoginFlow } from "./login-flow";
import { clearLoginFlow, readLoginFlow, writeLoginFlow } from "./login-flow-cookie";

const NOW_MS = 1_800_000_000_000;
const NOW_S = NOW_MS / 1000;

const code: LoginFlow = {
  phase: "code",
  challengeId: "sample-challenge",
  email: "anna@example.com",
  next: "",
  sentAt: NOW_S,
};

const HOST_PREFIX_ATTRIBUTES = { httpOnly: true, secure: true, sameSite: "strict", path: "/" };

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW_MS);
  cookieSetMock.mockReset();
  cookieGetMock.mockReset();
  cookieDeleteMock.mockReset();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("writeLoginFlow", () => {
  it("sets the cookie with the attributes the __Host- prefix requires, httpOnly and Strict", async () => {
    await writeLoginFlow(code);

    // toEqual on the whole options object: a `domain` key would break the __Host- prefix.
    expect(cookieSetMock).toHaveBeenCalledTimes(1);
    expect(cookieSetMock).toHaveBeenCalledWith(LOGIN_FLOW_COOKIE_NAME, encodeLoginFlow(code), {
      ...HOST_PREFIX_ATTRIBUTES,
      maxAge: 900,
    });
  });

  it("gives a code phase rewritten ten minutes in only the five minutes it has left", async () => {
    const dead: LoginFlow = { ...code, sentAt: NOW_S - 600, dead: "burned" };
    await writeLoginFlow(dead);

    expect(cookieSetMock).toHaveBeenCalledWith(LOGIN_FLOW_COOKIE_NAME, encodeLoginFlow(dead), {
      ...HOST_PREFIX_ATTRIBUTES,
      maxAge: 300,
    });
  });

  it("gives the consent phase the grant's ten minutes", async () => {
    const consent: LoginFlow = { phase: "consent", grantToken: "sample-grant", next: "" };
    await writeLoginFlow(consent);

    expect(cookieSetMock).toHaveBeenCalledWith(LOGIN_FLOW_COOKIE_NAME, encodeLoginFlow(consent), {
      ...HOST_PREFIX_ATTRIBUTES,
      maxAge: 600,
    });
  });
});

describe("readLoginFlow", () => {
  it("reads the phase back out of the cookie", async () => {
    cookieGetMock.mockReturnValue({ name: LOGIN_FLOW_COOKIE_NAME, value: encodeLoginFlow(code) });

    expect(await readLoginFlow()).toEqual(code);
    expect(cookieGetMock).toHaveBeenCalledWith(LOGIN_FLOW_COOKIE_NAME);
  });

  it("reads a missing cookie as no flow", async () => {
    cookieGetMock.mockReturnValue(undefined);

    expect(await readLoginFlow()).toBeNull();
  });

  it("reads a value that does not parse as no flow", async () => {
    cookieGetMock.mockReturnValue({ name: LOGIN_FLOW_COOKIE_NAME, value: "not-a-flow" });

    expect(await readLoginFlow()).toBeNull();
  });
});

describe("clearLoginFlow", () => {
  it("overwrites with the full attribute set and Max-Age 0, never delete()", async () => {
    await clearLoginFlow();

    expect(cookieSetMock).toHaveBeenCalledWith(LOGIN_FLOW_COOKIE_NAME, "", {
      ...HOST_PREFIX_ATTRIBUTES,
      maxAge: 0,
    });
    expect(cookieDeleteMock).not.toHaveBeenCalled();
  });
});
