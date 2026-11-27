import { spawn } from 'node:child_process';
import { createServer } from 'node:net';

// One run of the page's tests at a time on the PC, whatever starts it: scripts/check.ps1 and scripts/test.ps1 wait for
// each other already, a plain `npx playwright test` in another worktree does not, and two runs' browsers starting
// together stall the user's keyboard and windows. Playwright starts its web server before anything else of a run, so
// the server is what takes the lock, before it takes its port: a run that waits holds neither. Held as a named pipe on
// Windows and as a socket of Linux's abstract names elsewhere, which each frees with the process that held it, even one
// that was killed, as Playwright kills the server's.
const pipe = process.platform === 'win32' ? '\\\\.\\pipe\\thermi-page-tests' : '\0thermi-page-tests';
const port = process.argv[2] ?? '4173';

const taken = (): Promise<boolean> =>
  new Promise((resolve) => {
    const server = createServer();
    server.once('error', () => resolve(false));
    server.listen(pipe, () => resolve(true));
  });

for (let tries = 0; !(await taken()); tries++) {
  if (tries === 0) console.error('waiting for another run of the page tests to end');
  await new Promise((resolve) => setTimeout(resolve, 1000));
}

const vite = spawn(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--host', '127.0.0.1', '--port', port, '--strictPort'], { stdio: 'inherit' });
vite.on('exit', (code) => process.exit(code ?? 1));

// The line playwright.config.ts waits for, once the page answers.
for (;;) {
  const answered = await fetch(`http://127.0.0.1:${port}`).then(
    (response) => response.ok,
    () => false,
  );
  if (answered) break;
  await new Promise((resolve) => setTimeout(resolve, 200));
}
console.log(`the page is served on port ${port}`);
