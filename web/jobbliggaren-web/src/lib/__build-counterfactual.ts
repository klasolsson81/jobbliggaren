// TEMPORARY — #1053 AC 2 counterfactual. Reverted in the next commit.
// Imported by a page so it is in the production build graph, and referencing a
// module that does not exist, so `next build` must fail.
export { nothing } from "./this-module-does-not-exist";
