import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { isIP } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Every address the web's DMARC tests and fixtures write is from the documentation ranges (RFC 5737, RFC 3849): a source
 * address in a report is a third party's data (#1981 PR 4b, test-writer A3). The backend's DmarcFixtureAddressGuardTests
 * reads only the .NET tests, so this reads the web's.
 */
const WEB = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SOURCES = [
  "src/test/fixtures/admin-email-dmarc.ts",
  "src/lib/dto/admin-email.test.ts",
  "src/lib/admin/email-dmarc.test.ts",
  "src/components/admin/admin-email-dmarc.test.tsx",
  "src/lib/admin-preview/fixtures/index.ts",
  "tests/admin/servers.ts",
  "tests/admin/admin-email-delivery.spec.ts",
];

const DOCUMENTATION_V4 = /^(192\.0\.2|198\.51\.100|203\.0\.113)\.\d{1,3}$/;
const DOCUMENTATION_V6 = /^2001:db8:/i;
/** The harness serves on loopback; that is its own host, not a source in a report. */
const HARNESS_HOST = "127.0.0.1";

function addressesIn(source: string): string[] {
  return (source.match(/[0-9a-fA-F:.]{7,}/g) ?? [])
    .map((token) => token.replace(/^\.+|\.+$/g, ""))
    .filter((token) => isIP(token) !== 0 && token !== HARNESS_HOST);
}

describe("the web's DMARC test addresses (#1981 PR 4b)", () => {
  it.each(SOURCES)("%s writes documentation-range addresses only", (file) => {
    const addresses = addressesIn(readFileSync(resolve(WEB, file), "utf8"));
    expect(addresses.filter((address) => !(isIP(address) === 4 ? DOCUMENTATION_V4 : DOCUMENTATION_V6).test(address))).toEqual([]);
  });

  it("finds the addresses it is to judge", () => {
    expect(addressesIn('"198.51.100.7" "2001:db8::5efe:192.0.2.1" "10.1.2.3" "::ffff:10.1.2.3" 08:23:09.123')).toEqual([
      "198.51.100.7", "2001:db8::5efe:192.0.2.1", "10.1.2.3", "::ffff:10.1.2.3",
    ]);
  });
});
