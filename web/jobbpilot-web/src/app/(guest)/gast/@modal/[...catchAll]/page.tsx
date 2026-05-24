// @modal catch-all-fallback i gäst-tree. Renderar null för okända modal-
// segment så soft-nav inte triggar 404 mot @modal-slotten.
// Speglar `(app)/@modal/[...catchAll]/page.tsx`.
export default function GuestModalCatchAll() {
  return null;
}
