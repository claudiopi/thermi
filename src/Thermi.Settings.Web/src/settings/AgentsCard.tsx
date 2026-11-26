import { useState } from 'react';
import type { AgentsView, Bridge } from '../bridge/bridge.g';
import { bridge, useBridgeRead, useEdit, type WindowData } from '../bridge/useWindow';
import { SwitchLine } from '../app/SwitchLine';
import { Button, Card, CardRow, CardStack, LinkButton } from '../kit';
import { useText } from '../texts/Texts';

// Read again in a new language, which the changes' kinds and times are written in, and at each change of the settings,
// which an agent's change is.
const readAgents = (from: Bridge): Promise<AgentsView> => from.agents();

/** A row naming where a client's setup goes, its copy button, and the text it copies, as C# wrote it. */
function Setup({ label, description, text, copied, onCopy }: { label: string; description?: string; text: string; copied: boolean; onCopy: () => void }) {
  const t = useText();
  return (
    <div className="t-agent-setup">
      <CardRow label={label} {...(description === undefined ? {} : { description })}>
        <Button className="t-small-button" label={t('AgentText.CopyOf', label)} onClick={onCopy}>
          {t(copied ? 'AgentText.Copied' : 'AgentText.Copy')}
        </Button>
      </CardRow>
      <pre className="t-agent-code">{text}</pre>
    </div>
  );
}

/** "AI agents": whether agents may change the settings, how to add thermi to one, and what agents changed last. */
export function AgentsCard({ data }: { data: WindowData }) {
  const t = useText();
  const edit = useEdit();
  const agents = useBridgeRead(readAgents, data.settings.language ?? '', data.changes);
  const [copied, setCopied] = useState<string | null>(null);
  const copy = (text: string): void => {
    setCopied(text);
    void bridge()?.copy(text);
  };
  return (
    <CardStack>
      <Card>
        <SwitchLine
          label={t('AgentText.LetThemChange')}
          description={t('AgentText.WhatTheyDo')}
          checked={(data.settings.agents ?? 'change') === 'change'}
          onChange={(on) => void edit({ kind: 'thermi', field: 'agents', value: on ? 'change' : 'read' })}
        />
      </Card>
      {agents !== null && (
        <Card>
          <Setup label={t('AgentText.ClaudeCode')} text={agents.command} copied={copied === agents.command} onCopy={() => copy(agents.command)} />
          <Setup
            label={t('AgentText.ByFile')}
            description={t('AgentText.ByFileHow')}
            text={agents.file}
            copied={copied === agents.file}
            onCopy={() => copy(agents.file)}
          />
        </Card>
      )}
      {agents !== null && agents.changes.length > 0 && (
        <Card>
          <CardRow label={t('AgentText.LastChanges')}>
            <LinkButton onClick={() => void bridge()?.open('agents-log')}>{t('AgentText.OpenLog')}</LinkButton>
          </CardRow>
          <ul className="t-agent-changes">
            {agents.changes.map((change, at) => (
              // A change has no id of its own, and the list is read whole each time.
              <li key={`${at}:${change.what}`}>
                <span>{change.did}</span>
                <span className="t-agent-when">{change.when}</span>
                <code className="t-agent-what">{change.what}</code>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </CardStack>
  );
}
