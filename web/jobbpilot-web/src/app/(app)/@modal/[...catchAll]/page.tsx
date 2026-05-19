// Catch-all för @modal-slotten. Next-docs (Parallel Routes §Closing the
// modal): client-side-navigering till en route som inte längre matchar
// slotten lämnar slot-innehållet synligt — en catch-all som returnerar
// null behövs för att stänga modalen vid soft-nav bort från /jobb/[id]
// (utöver router.back() från modal-chromet).
export default function ModalCatchAll() {
  return null;
}
