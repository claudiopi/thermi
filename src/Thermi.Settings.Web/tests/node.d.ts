// The tests run in Node, whose types the page leaves out: only what they read from the repo is declared.
declare module 'node:fs' {
  export function readFileSync(path: URL, encoding: 'utf8'): string;
}
