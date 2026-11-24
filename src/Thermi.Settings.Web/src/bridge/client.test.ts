import { expect, test } from 'vitest';
import type { Bridge } from './bridge.g';
import { client } from './client.g';

interface Sent {
  id: number;
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
