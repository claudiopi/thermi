// The tests run in Node, whose types the page leaves out: only what they read from the repo and write for the oracle,
// and what the config and the run's lock ask of the PC, is declared.
declare module 'node:fs' {
  export function readFileSync(path: URL, encoding: 'utf8'): string;
  export function appendFileSync(path: URL, data: string): void;
  export function mkdirSync(path: URL, options: { recursive: true }): void;
}

declare module 'node:crypto' {
  export function createHash(algorithm: 'sha256'): { update(data: string): { digest(encoding: 'hex'): string } };
}

declare module 'node:net' {
  interface Server {
    once(event: 'error', listener: () => void): Server;
    listen(path: string, listener: () => void): Server;
    close(listener: () => void): Server;
  }
  export function createServer(): Server;
}

declare module 'node:os' {
  export function availableParallelism(): number;
}

declare const process: {
  pid: number;
  env: Record<string, string | undefined>;
  on(event: 'exit', listener: () => void): void;
};
