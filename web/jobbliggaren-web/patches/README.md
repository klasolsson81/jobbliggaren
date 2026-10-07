# Dependency security patches

`pnpm.patchedDependencies` and the lockfile bind these repairs to exact package
versions. Frozen installs and the Docker dependency stage apply the same files.
`dependency-security.test.mjs` exercises the installed packages through their
ESLint, shadcn and Lighthouse dependency paths.

## braces 3.0.3

[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
has no published fixed release as of 2026-10-07. Following the
[upstream recommendation](https://github.com/micromatch/braces/issues/70), the
patch rejects parser nesting deeper than 100, counting braces and parentheses.
The direct AST walkers enforce the same bound. Escaped and quoted literals are
not counted as nested nodes. Rejection uses `SyntaxError`, matching the existing
input-length guard; callers must handle invalid patterns as before.

The npm audit and GitHub alert remain version-based and continue to report this
package. No advisory is ignored or dismissed. Remove this patch only after an
upstream release repairs the nesting paths and the installed-package regressions
pass with that release.

## Lighthouse CI utils 0.15.1

LHCI uses `yaml.safeLoad`, which js-yaml 4 removed. This patch changes that one
call to `yaml.load`, whose default schema rejects executable JavaScript tags.
The parent-and-version-gated override upgrades only LHCI's js-yaml 3 dependency
to the already-used js-yaml 4.3.2, removing argparse 1 and vulnerable sprintf-js
from the lockfile. YAML and repository JSON configuration loading are tested.

Remove the patch and its override together when LHCI supports js-yaml 4 upstream,
after rerunning the configuration-loading regressions.