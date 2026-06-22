import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react/pure";
import "@testing-library/jest-dom/vitest";

// The default `@testing-library/react` entry (which auto-registers cleanup) is
// aliased to `render-intl.tsx`, which re-exports from `/pure` — and `/pure` does
// NOT auto-clean. Register cleanup explicitly so the DOM is reset between tests.
afterEach(() => {
  cleanup();
});
