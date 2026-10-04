"use strict";

/**
 * The admin preview's build gate (ADR 0150 D5, #1973): the one place that decides whether a build
 * contains the preview. `next.config.ts` reads it for the compile lock, the preview's layout and
 * pages read it at request time, and `scripts/assert-admin-preview-build.mjs` reads it after
 * `next build`. CommonJS, so a transpiled config, a server bundle and an ES module script can all
 * load it on any Node.
 */

/** The flag. Only the exact string "true" turns the preview on; absent, empty or anything else is off. */
const ADMIN_PREVIEW_FLAG = "ADMIN_PREVIEW_ENABLED";

/** Every preview route lives under this path. */
const ADMIN_PREVIEW_ROUTE = "/admin/forhandsvisning";

/** The page extension that makes `page.preview.tsx` and `layout.preview.tsx` route files. */
const ADMIN_PREVIEW_EXTENSION = "preview.tsx";

/**
 * The reserved domain every fixture value carries (RFC 6761 `.invalid`). A build output that
 * contains it contains fixture data, whichever fixture row was imported.
 */
const ADMIN_PREVIEW_SENTINEL = "forhandsvisning.invalid";

/** Next's defaults, kept as they are. */
const DEFAULT_PAGE_EXTENSIONS = ["tsx", "ts", "jsx", "js"];

/** @param {Record<string, string | undefined>} env */
function adminPreviewEnabled(env) {
  return env[ADMIN_PREVIEW_FLAG] === "true";
}

/** @param {Record<string, string | undefined>} env */
function pageExtensionsFor(env) {
  return adminPreviewEnabled(env)
    ? [ADMIN_PREVIEW_EXTENSION, ...DEFAULT_PAGE_EXTENSIONS]
    : [...DEFAULT_PAGE_EXTENSIONS];
}

exports.ADMIN_PREVIEW_FLAG = ADMIN_PREVIEW_FLAG;
exports.ADMIN_PREVIEW_ROUTE = ADMIN_PREVIEW_ROUTE;
exports.ADMIN_PREVIEW_EXTENSION = ADMIN_PREVIEW_EXTENSION;
exports.ADMIN_PREVIEW_SENTINEL = ADMIN_PREVIEW_SENTINEL;
exports.DEFAULT_PAGE_EXTENSIONS = DEFAULT_PAGE_EXTENSIONS;
exports.adminPreviewEnabled = adminPreviewEnabled;
exports.pageExtensionsFor = pageExtensionsFor;
