// @modal-slot fallback. Returnerar null så modalen INTE renderas när
// slotten är omatchad (initial load / full-page reload utan aktiv modal).
// Next-docs (Parallel Routes §default.js): krävs för @-slottar — utan
// default.js → 404 för omatchad slot vid hard-nav.
export default function ModalDefault() {
  return null;
}
