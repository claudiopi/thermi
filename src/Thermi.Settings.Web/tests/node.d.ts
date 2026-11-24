// The tests run in Node, whose types the page leaves out: only what they read from the repo and write for the oracle
// is declared.
declare module 'node:fs' {
  export function readFileSync(path: URL, encoding: 'utf8'): string;
  export function appendFileSync(path: URL, data: string): void;
  export function mkdirSync(path: URL, options: { recursive: true }): void;
}

declare module 'node:crypto' {
  export function createHash(algorithm: 'sha256'): { update(data: string): { digest(encoding: 'hex'): string } };
}

declare const process: {
  pid: number;
  env: Record<string, string | undefined>;
  on(event: 'exit', listener: () => void): void;
};
