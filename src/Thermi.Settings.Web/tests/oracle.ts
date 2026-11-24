import { test as base, type Page } from '@playwright/test';
import { createHash } from 'node:crypto';
import { appendFileSync, mkdirSync, readFileSync } from 'node:fs';
import type { FakeState, OracleAnswer, OracleCall, OracleStart, Sensor } from './bridgeTypes';
import { pseudo, recording, texts } from './pageChecks';

// The C# side's answers to the page's edits, undos and looks, which the fake replays instead of deciding them: a table
// of calls from a start, each with what the real bridge answered, the settings it then showed, its saved looks and its
// undo steps. Thermi.Windows.Tests' BridgeOracleTests runs every call through the real WindowBridge and fails when an
// answer here is stale; a call the table lacks fails the test that made it, and `pwsh scripts/oracle.ps1` asks C#.

interface OracleCase {
  start: string;
  calls: (OracleCall & { answer: OracleAnswer })[];
}

interface Golden {
  starts: Record<string, OracleStart & { shown: FakeState['settings'] }>;
  cases: OracleCase[];
}

const golden = JSON.parse(readFileSync(new URL('./oracle/edits.json', import.meta.url), 'utf8')) as Golden;
const misses = new URL('../test-results/oracle-misses/', import.meta.url);
const missed = new Map<string, string[]>();
const used = new Map<number, number>();
const starts = new Set<string>();

/** JSON with every object's keys in order, so the same call reads the same however its object was built. */
function canonical(value: unknown): string {
  if (Array.isArray(value)) {
    return `[${value.map((one) => (one === undefined ? 'null' : canonical(one))).join(',')}]`;
  }
  if (value !== null && typeof value === 'object') {
    const fields = Object.entries(value).filter(([, one]) => one !== undefined).sort(([one], [other]) => (one < other ? -1 : one > other ? 1 : 0));
    return `{${fields.map(([key, one]) => `${JSON.stringify(key)}:${canonical(one)}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

const recordings = ['admin', 'no-admin', 'admin-net-disk'] as const;

function languageOf(shown: Record<string, string>): OracleStart['texts'] {
  const said = canonical(shown);
  const known = ([['en', texts()], ['es', texts('es')], ['pseudo', pseudo(texts())]] as const).find(([, one]) => canonical(one) === said);
  if (known === undefined) {
    throw new Error('The oracle knows English, Spanish and the pseudo-language only.');
  }
  return known[0];
}

/** What the C# side starts from: the settings file, the saved looks, the readings and the window's language. */
function startOf(state: FakeState): OracleStart {
  const sensors = canonical(state.sensors);
  const named = recordings.find((name) => canonical(recording(name)) === sensors);
  return {
    settings: state.settings,
    looks: state.looks,
    sensors: named ?? (state.sensors as Sensor[]),
    service: state.service,
    texts: languageOf(state.texts),
    problem: state.problem,
  };
}

function record(file: string, line: unknown): void {
  mkdirSync(misses, { recursive: true });
  appendFileSync(new URL(`${file}-${process.pid}.jsonl`, misses), `${JSON.stringify(line)}\n`);
}

// Which call of which case the run used, for `scripts/oracle.ps1 -Prune` to drop the rest.
process.on('exit', () => {
  if (starts.size > 0) {
    record('used', { cases: [...used.entries()], starts: [...starts] });
  }
});

/**
 * Answers the page's calls on <paramref name="page"/> from the table, for a fake starting at <paramref name="state"/>,
 * and gives that state the settings as C# shows them. A call the table lacks answers that it is missing, and fails the test.
 */
export async function askOracle(page: Page, state: FakeState): Promise<FakeState> {
  const start = startOf(state);
  const id = createHash('sha256').update(canonical(start)).digest('hex').slice(0, 16);
  const shown = golden.starts[id]?.shown;
  starts.add(id);
  const cases = golden.cases.flatMap((one, index) =>
    one.start === id
      ? [{ index, keys: one.calls.map((call) => canonical({ method: call.method, args: call.args })), calls: one.calls }]
      : [],
  );
  const info = base.info();
  const log: OracleCall[] = [];
  const keys: string[] = [];
  let lost = shown === undefined;
  const miss = (why: string): { missing: string } => {
    if (!lost) {
      record('misses', { start: id, state: start, calls: log });
    }
    lost = true;
    missed.set(info.testId, [...(missed.get(info.testId) ?? []), why]);
    return { missing: why };
  };
  if (shown === undefined) {
    record('misses', { start: id, state: start, calls: [] });
  }
  await page.exposeBinding('thermiOracle', (_source, call: OracleCall): OracleAnswer | { missing: string } => {
    const key = canonical(call);
    const at = keys.length;
    keys.push(key);
    log.push(call);
    if (lost) {
      return miss(`${call.method} after a call the oracle lacks`);
    }
    const found = cases.find((one) => one.keys.length > at && one.keys[at] === key && keys.slice(0, at).every((each, before) => one.keys[before] === each));
    if (found === undefined) {
      return miss(`${call.method}(${JSON.stringify(call.args).slice(0, 200)}) as call ${at + 1}`);
    }
    used.set(found.index, Math.max(used.get(found.index) ?? 0, at + 1));
    const answer = found.calls[at]?.answer;
    return answer ?? miss(`${call.method} as call ${at + 1}`);
  });
  return shown === undefined ? state : { ...state, settings: shown };
}

/** The tests of the page: each fails when its fake asked the oracle for a call it lacks. */
export const test = base.extend<{ oracle: undefined }>({
  oracle: [
    async ({}, use, info) => { // eslint-disable-line no-empty-pattern -- Playwright reads what a fixture needs from this pattern, and it needs no other.
      await use(undefined);
      const lacks = missed.get(info.testId);
      if (lacks !== undefined) {
        missed.delete(info.testId);
        throw new Error(`The C# side's answers lack what this test asked (${lacks[0]}): run pwsh scripts/oracle.ps1, then check the new answers.`);
      }
    },
    { auto: true },
  ],
});
