// Test render shim. `vitest.config.ts` aliases the bare `@testing-library/react`
// specifier to this module, so every test's `render`/`renderHook` is wrapped in
// `NextIntlClientProvider` with the Swedish catalog — components that call
// `useTranslations` render without a manual provider in each test file.
//
// Because the Swedish values are preserved verbatim and sv is the default
// locale, rendered output is byte-identical to the pre-i18n UI, so existing
// `getByText("Skickad")` / `getByRole({ name })` assertions keep passing.
//
// The real implementation is imported from `@testing-library/react/pure` (not
// rewritten by the alias, which is anchored with `$`), avoiding a resolve loop.
// `/pure` does not auto-register cleanup; `src/test/setup.ts` does it explicitly.
import type { ReactElement, ReactNode } from "react";
import {
  render as rtlRender,
  renderHook as rtlRenderHook,
  type RenderHookOptions,
  type RenderOptions,
} from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import messages from "../../messages/sv";

function IntlWrapper({ children }: { children: ReactNode }) {
  return (
    <NextIntlClientProvider
      locale="sv"
      messages={messages}
      timeZone="Europe/Stockholm"
    >
      {children}
    </NextIntlClientProvider>
  );
}

export function render(
  ui: ReactElement,
  options?: Omit<RenderOptions, "wrapper">,
) {
  return rtlRender(ui, { wrapper: IntlWrapper, ...options });
}

export function renderHook<Result, Props>(
  callback: (initialProps: Props) => Result,
  options?: Omit<RenderHookOptions<Props>, "wrapper">,
) {
  return rtlRenderHook(callback, { wrapper: IntlWrapper, ...options });
}

// Re-export the rest of the API (screen, within, waitFor, fireEvent, cleanup,
// act, …). The explicit `render`/`renderHook` above shadow the star-exported
// originals.
export * from "@testing-library/react/pure";
