import { z } from "zod";

// Zod probes `new Function` to decide whether it may JIT-compile object parsing. The production
// CSP carries no 'unsafe-eval', so the probe raised a `script-src` violation, although Zod catches
// the throw (#2139). `jitless` skips the probe. Next runs this file before hydration in every document.
z.config({ jitless: true });
