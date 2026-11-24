import { expect, test } from 'vitest';
import type { Bridge } from './bridge.g';
import { client } from './client.g';

interface Sent {
  id: unknown;
  method: string;
}

type Receive = (message: { data: unknown }) => void;

// The host's script over a WebView2 of the test's own, which answers each call with what C# sent.
function connect(): { bridge: Bridge; sent: Sent[]; answer: (data: unknown) => void } {
  const sent: Sent[] = [];
  let receive: Receive = () => {};
  const page: { chrome: unknown; addEventListener: () => void; thermi?: Bridge } = {
    addEventListener: () => {},
    chrome: {
      webview: {
        addEventListener: (_: string, listener: Receive) => {
          receive = listener;
        },
        postMessage: (message: Sent) => sent.push(message),
      },
    },
  };
  new Function('window', client)(page);
  if (page.thermi === undefined) {
    throw new Error('The script made no bridge.');
  }
  return { bridge: page.thermi, sent, answer: (data) => receive({ data }) };
}

// A chart C# has none of comes back null, as the page's types say, never undefined.
test.each([
  { result: null, answered: null },
  { result: 0, answered: 0 },
  { result: { stats: '1 %' }, answered: { stats: '1 %' } },
])('a call C# answers with $result resolves to $answered', async ({ result, answered }) => {
  const { bridge, sent, answer } = connect();

  const call = bridge.chart('cpu.power');
  answer({ id: sent[0]?.id, result });

  await expect(call).resolves.toStrictEqual(answered);
});

// A listener taken back, as a component that unmounts takes its own, hears no event after, and the others still do.
test('a listener taken back hears nothing more', () => {
  const { bridge, answer } = connect();
  const heard: string[] = [];

  const stop = bridge.changed(() => heard.push('gone'));
  bridge.changed(() => heard.push('kept'));
  stop();
  answer({ event: 'changed', value: null });

  expect(heard).toStrictEqual(['kept']);
});

// R4 smoke: the page loaded again while C# still owed its last load answers, and an answer to the old page's call resolved the
// new page's call of the same number with another method's value. A call is answered only by the answer to that call.
test('an answer to the page before a reload answers nothing of the page after it', async () => {
  const before = connect();
  void before.bridge.view();
  const after = connect();

  const call = after.bridge.snapshot();
  after.answer({ id: before.sent[0]?.id, result: { items: {} } });
  after.answer({ id: after.sent[0]?.id, result: { service: 'running', sensors: [] } });

  await expect(call).resolves.toStrictEqual({ service: 'running', sensors: [] });
});
