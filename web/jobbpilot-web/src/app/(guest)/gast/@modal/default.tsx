// @modal-slot fallback i gäst-tree. Returnerar null så modalen INTE renderas
// när slotten är omatchad (initial load / hard-nav utan aktiv modal).
// Speglar `(app)/@modal/default.tsx` (ADR 0053).
export default function GuestModalDefault() {
  return null;
}
