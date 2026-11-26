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
  }
  export function createServer(): Server;
}

declare module 'node:child_process' {
  export function spawn(command: string, args: string[], options: { stdio: 'inherit' }): { on(event: 'exit', listener: (code: number | null) => void): void };
}

declare module 'node:os' {
  export function availableParallelism(): number;
  export function setPriority(priority: number): void;
  export const constants: { priority: { PRIORITY_BELOW_NORMAL: number } };
}

declare const process: {
  pid: number;
  argv: string[];
  execPath: string;
  exit(code: number): never;
  env: Record<string, string | undefined>;
  on(event: 'exit', listener: () => void): void;
};
