/**
 * The server Lighthouse CI measures: `collect.startServerCommand` in `lighthouserc.json` (SEO epic #2090 S6, CTO B10).
 *
 * `/jobb/<uuid>` and `/lediga-jobb` read the API on every request, and the other URLs in that file do not need it.
 * This starts the stub backend the public-page smoke asserts on (`public-job-stub.mjs`), then `next start` with
 * `BACKEND_URL` pointing at it, so a measured page is the page the smoke checks. One command, so lhci can start it and
 * kill the whole tree. The stub answers any other path with a 401, which is what the landing's stats read gets.
 *
 * `next start` with no `-H`, deliberately. The job page is a proxy rewrite (`src/proxy.ts`), and Next compares the
 * rewrite's origin with the one it derived for itself: on `-H 127.0.0.1` it calls a same-origin rewrite external and the
 * request hangs (ADR 0165 D2). `public-job-smoke.mjs` runs the standalone server behind `X-Forwarded-Proto` for that
 * reason; here the default host answers `Host: localhost:3000`, the host lhci's URLs name, with no header.
 *
 * It does not print the ready line until the server has answered both public pages with a 200 AND the stub has seen the
 * API read each one makes. That second condition is the one that matters on Windows, where an IPv4 and a dual-stack
 * listener can share port 3000 and `localhost` reaches the other one: the 200 would then be somebody else's page.
 */
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { ID, seen, startStub } from "./public-job-stub.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** The port `lighthouserc.json`'s URLs name. */
const PORT = 3000;
const ORIGIN = `http://localhost:${PORT}`;
/** Shorter than `startServerReadyTimeout` in `lighthouserc.json`, so the launcher says why before lhci gives up. */
const READY_DEADLINE_MS = 45_000;

/** What each public page asks the API for, so a 200 can be told from a 200 this server's own render produced. */
const PUBLIC_PAGES = [
  { path: `/jobb/${ID.listed}`, backendRead: `/api/v1/public/job-ads/${ID.listed}` },
  { path: "/lediga-jobb", backendRead: "/api/v1/public/job-ads/catalogue?page=1" },
];

const { server: stub, port: stubPort } = await startStub();

const app = spawn(process.execPath, [createRequire(import.meta.url).resolve("next/dist/bin/next"), "start", "-p", String(PORT)], {
  cwd: ROOT,
  env: { ...process.env, BACKEND_URL: `http://127.0.0.1:${stubPort}`, NODE_ENV: "production" },
  stdio: "inherit",
});

function stop() {
  app.kill();
  stub.close();
}
for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    stop();
    process.exit(0);
  });
}
// `next start` exiting is a failure whatever its code: lhci reads this process's exit before the ready line as "no server".
app.on("exit", (code) => {
  stub.close();
  process.exit(code ?? 1);
});

/** Null when the page answered 200 and the stub saw its read, otherwise what was wrong. */
async function problemWith({ path, backendRead }) {
  seen.length = 0;
  let res;
  try {
    res = await fetch(`${ORIGIN}${path}`, { redirect: "manual", signal: AbortSignal.timeout(5_000) });
  } catch (error) {
    return `${path}: ${error instanceof Error ? error.name : error}`;
  }
  await res.arrayBuffer();
  if (res.status !== 200) return `${path}: HTTP ${res.status}`;
  if (!seen.some((request) => request.path === backendRead)) return `${path}: answered without reading ${backendRead} from this stub`;
  return null;
}

async function untilReady() {
  const deadline = Date.now() + READY_DEADLINE_MS;
  let problem = "no attempt";
  while (Date.now() < deadline) {
    problem = null;
    for (const page of PUBLIC_PAGES) {
      problem = await problemWith(page);
      if (problem !== null) break;
    }
    if (problem === null) return;
    await new Promise((done) => setTimeout(done, 500));
  }
  throw new Error(problem);
}

try {
  await untilReady();
  console.log(`lighthouse-server ready: ${ORIGIN} (stub backend on 127.0.0.1:${stubPort})`);
} catch (error) {
  console.error(`lighthouse-server did not become ready in ${READY_DEADLINE_MS / 1000} s: ${error instanceof Error ? error.message : error}`);
  stop();
  process.exit(1);
}
