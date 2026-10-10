import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { JsonLdScript } from "./json-ld-script";

describe("JsonLdScript", () => {
  it("writes no literal `<`, so a string in the data cannot close the script, and parses back unchanged", () => {
    const data = { "@type": "Thing", name: "</script><script>alert(1)</script>" };
    const { container } = render(<JsonLdScript data={data} />);
    const script = container.querySelector('script[type="application/ld+json"]');

    expect(script).not.toBeNull();
    expect(script?.innerHTML).not.toContain("<");
    expect(JSON.parse(script?.innerHTML ?? "")).toEqual(data);
  });
});
