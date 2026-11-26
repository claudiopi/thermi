import { createServer } from 'node:net';

// One run of the page's tests at a time on the PC, whatever starts it: scripts/check.ps1 and scripts/test.ps1 wait for
// each other already, a plain `npx playwright test` in another worktree does not, and two runs' browsers starting
// together stall the user's keyboard and windows. Held as a named pipe, which Windows frees with the process that
// held it, even one that was killed.
const pipe = '\\\\.\\pipe\\thermi-page-tests';

const take = (): Promise<(() => Promise<void>) | undefined> =>
  new Promise((resolve) => {
    const server = createServer();
    server.once('error', () => resolve(undefined));
    server.listen(pipe, () => resolve(() => new Promise((closed) => server.close(() => closed()))));
  });

export default async function oneRunAtATime(): Promise<() => Promise<void>> {
  for (let tries = 0; ; tries++) {
    const release = await take();
    if (release) return release;
    if (tries === 0) console.log('waiting for another run of the page tests to end');
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}
