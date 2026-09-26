import { afterEach, describe, expect, it, vi } from "vitest";
import { isSameOriginRequest } from "./same-origin";

function request(origin: string | null, host: string | null, url = "http://internal:3000/api/cv/import") {
  const headers = new Headers();
  if (origin !== null) headers.set("origin", origin);
  if (host !== null) headers.set("host", host);
  return new Request(url, { method: "POST", headers });
}

afterEach(() => vi.unstubAllEnvs());

describe("isSameOriginRequest in production behind TLS termination", () => {
  it.each([
    ["https://app.example.test", "app.example.test"],
    ["https://app.example.test:443", "app.example.test"],
    ["https://app.example.test", "app.example.test:443"],
    ["https://APP.example.test:8443", "app.EXAMPLE.test:8443"],
    ["https://[::1]:8443", "[::1]:8443"],
  ])("accepts %s against preserved Host %s despite internal HTTP", (origin, host) => {
    vi.stubEnv("NODE_ENV", "production");
    expect(isSameOriginRequest(request(origin, host))).toBe(true);
  });

  it.each([
    null, "", "null", "https://foreign.test", "https://sibling.example.test",
    "https://app.example.test.evil.test", "http://app.example.test",
    "https://app.example.test:8443", "https://app.example.test/",
    "https://app.example.test/path", "https://app.example.test?x=1",
    "https://app.example.test#fragment", "https://user@app.example.test",
    "https://user:pass@app.example.test", "https://app.example.test:",
    "https://app.example.test:65536", "https://app.example.test,https://foreign.test",
    "https://app.example.test https://foreign.test", "https://app..example.test",
    "https://app.example.test\\", "https://%61pp.example.test",
    "https:///app.example.test", "https:app.example.test", "file://app.example.test",
  ])("rejects invalid or foreign Origin %s", (origin) => {
    vi.stubEnv("NODE_ENV", "production");
    expect(isSameOriginRequest(request(origin, "app.example.test"))).toBe(false);
  });

  it.each([
    null, "", "null", "app.example.test:", "app.example.test:65536",
    "app.example.test,foreign.test", "app.example.test foreign.test",
    "https://app.example.test", "user@app.example.test", "app.example.test/path",
    "app.example.test?x=1", "app.example.test#fragment", "app..example.test",
    "app.example.test\\", "[invalid]",
  ])("rejects invalid Host %s", (host) => {
    vi.stubEnv("NODE_ENV", "production");
    expect(isSameOriginRequest(request("https://app.example.test", host))).toBe(false);
  });

  it("ignores forwarded authority, forwarded protocol, SITE_URL and the internal URL host", () => {
    vi.stubEnv("NODE_ENV", "production");
    vi.stubEnv("SITE_URL", "https://foreign.test");
    const req = request("https://foreign.test", "app.example.test", "https://foreign.test/api/cv/import");
    req.headers.set("forwarded", "host=foreign.test;proto=https");
    req.headers.set("x-forwarded-host", "foreign.test");
    req.headers.set("x-forwarded-proto", "https");
    expect(isSameOriginRequest(req)).toBe(false);

    req.headers.set("origin", "http://app.example.test");
    req.headers.set("x-forwarded-proto", "http");
    expect(isSameOriginRequest(req)).toBe(false);

    req.headers.set("origin", "https://app.example.test");
    expect(isSameOriginRequest(req)).toBe(true);
  });

  it("refuses a null Host even when the Origin names it", () => {
    vi.stubEnv("NODE_ENV", "production");
    expect(isSameOriginRequest(request("https://null", "null"))).toBe(false);
  });

  it.each([
    ["127.1", "127.0.0.1"],
    ["2130706433", "127.0.0.1"],
    ["0x7f000001", "127.0.0.1"],
    ["0177.0.0.1", "127.0.0.1"],
    ["[0:0:0:0:0:0:0:1]", "[::1]"],
  ])("refuses parser normalization of %s to %s in either header", (raw, canonical) => {
    vi.stubEnv("NODE_ENV", "production");
    expect(isSameOriginRequest(request(`https://${raw}`, canonical))).toBe(false);
    expect(isSameOriginRequest(request(`https://${canonical}`, raw))).toBe(false);
    expect(isSameOriginRequest(request(`https://${raw}`, raw))).toBe(false);
    expect(isSameOriginRequest(request(`https://${canonical}`, canonical))).toBe(true);
  });
});

describe("isSameOriginRequest for direct development and tests", () => {
  it.each(["development", "test"])("uses the actual request scheme in %s", (environment) => {
    vi.stubEnv("NODE_ENV", environment);
    expect(isSameOriginRequest(request("http://localhost:3000", "localhost:3000", "http://localhost:3000/api/cv/import"))).toBe(true);
    expect(isSameOriginRequest(request("https://localhost:3000", "localhost:3000", "http://localhost:3000/api/cv/import"))).toBe(false);
    expect(isSameOriginRequest(request("https://localhost:3000", "localhost:3000", "https://localhost:3000/api/cv/import"))).toBe(true);
    expect(isSameOriginRequest(request("http://localhost:3000", "localhost:3000", "https://localhost:3000/api/cv/import"))).toBe(false);
    expect(isSameOriginRequest(request("http://localhost:80", "localhost", "http://localhost/api/cv/import"))).toBe(true);
    expect(isSameOriginRequest(request("http://localhost", "localhost:80", "http://localhost/api/cv/import"))).toBe(true);
    expect(isSameOriginRequest(request("http://localhost", "localhost", "ftp://localhost/api/cv/import"))).toBe(false);
  });
});
