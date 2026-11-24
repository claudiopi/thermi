import { useState } from 'react';
import type { About } from '../bridge/bridge.g';
import { bridge, useBridgeRead, useEdit, type WindowData } from '../bridge/useWindow';
import { Creature } from '../app/Creature';
import { Expander } from '../app/Expander';
import { SwitchLine } from '../app/SwitchLine';
import { Button, Card, CardRow, CardStack, Dialog, Dropdown, LinkButton, Segmented } from '../kit';
import { enumChoices } from '../overlay/lookModel';
import { useText } from '../texts/Texts';
import { HotkeysCard } from './HotkeysCard';
import './settings.css';

/** The language choice that follows Windows', which the file writes as no language at all. */
const fromWindows = 'windows';

/** Units, which no language translates. */
const units = [
  { value: 'c', text: '°C' },
  { value: 'f', text: '°F' },
];

const readAbout = (from: NonNullable<ReturnType<typeof bridge>>): Promise<About> => from.about();

function General({ data, about }: { data: WindowData; about: About | null }) {
  const t = useText();
  const edit = useEdit();
  const languages = [
    { value: fromWindows, text: t('UiText.LanguageFromWindows', about?.windows ?? '') },
    ...(about?.languages ?? []).map((one) => ({ value: one.code, text: one.name })),
  ];
  return (
    <CardStack>
      <Card>
        <CardRow label={t('UiText.LanguageField')}>
          <Dropdown
            className="t-language-box"
            label={t('UiText.LanguageField')}
            choices={languages}
            value={data.settings.language ?? fromWindows}
            onChange={(value) => void edit({ kind: 'thermi', field: 'language', value: value === fromWindows ? null : value })}
          />
        </CardRow>
      </Card>
      <Card>
        <CardRow label={t('UiText.TemperatureField')}>
          <Segmented
            label={t('UiText.TemperatureField')}
            choices={units}
            value={data.settings.temperature ?? 'c'}
            onChange={(value) => void edit({ kind: 'thermi', field: 'temperature', value })}
          />
        </CardRow>
      </Card>
      <Card>
        <CardRow label={t('UiText.ThemeField')}>
          <Segmented
            label={t('UiText.ThemeField')}
            choices={enumChoices(t, 'WindowTheme', ['system', 'light', 'dark'])}
            value={data.settings.windowTheme ?? 'system'}
            onChange={(value) => void edit({ kind: 'thermi', field: 'windowTheme', value })}
          />
        </CardRow>
      </Card>
      <Card>
        <SwitchLine label={t('UiText.StartWithWindows')} checked={data.startWithWindows} onChange={(on) => void bridge()?.setStartWithWindows(on)} />
      </Card>
    </CardStack>
  );
}

/** "Settings file and reset": the file in Windows' editor, and Reset… behind a dialog whose Cancel is the default. */
function FileCard({ open, onOpen }: { open: boolean; onOpen: (open: boolean) => void }) {
  const t = useText();
  const edit = useEdit();
  const [asking, setAsking] = useState(false);
  const reset = (): void => {
    setAsking(false);
    void edit({ kind: 'reset' });
  };
  return (
    <Expander label={t('WindowText.FileAndReset')} roomy open={open} onOpen={onOpen}>
      <CardRow label={t('WindowText.SettingsFile')}>
        <Button className="t-small-button" onClick={() => void bridge()?.open('settings-file')}>
          {t('WindowText.OpenInEditor')}
        </Button>
      </CardRow>
      <CardRow label={t('UiText.ResetToDefaults')} description={t('WindowText.ResetKeeps')}>
        <Button className="t-small-button" label={t('UiText.ResetToDefaults')} onClick={() => setAsking(true)}>
          {t('WindowText.ResetAsk')}
        </Button>
      </CardRow>
      <Dialog
        open={asking}
        title={t('WindowText.ResetTitle')}
        main={t('WindowText.Reset')}
        onMain={reset}
        cancel={t('UiText.Cancel')}
        onCancel={() => setAsking(false)}
        destroys
      >
        <p className="t-dialog-text">{t('WindowText.ResetBody')}</p>
      </Dialog>
    </Expander>
  );
}

function AboutCard({ about, open, onOpen }: { about: About | null; open: boolean; onOpen: (open: boolean) => void }) {
  const t = useText();
  return (
    <Expander
      label={t('WindowText.AboutThermi')}
      icon={<Creature state="app" className="t-about-mark" />}
      hint={t('WindowText.AboutVersion', about?.version ?? '')}
      hintWhenOpen
      open={open}
      onOpen={onOpen}
    >
      <div className="t-about">
        <div className="t-about-links">
          <LinkButton onClick={() => void bridge()?.open('source')}>{t('WindowText.SourceCode')}</LinkButton>
          <LinkButton onClick={() => void bridge()?.open('report')}>{t('WindowText.ReportProblem')}</LinkButton>
          <LinkButton onClick={() => void bridge()?.open('notices')}>{t('WindowText.ThirdPartyNotices')}</LinkButton>
        </div>
        <div className="t-about-license">{about?.updates}</div>
        <div className="t-about-license">{t('WindowText.License')}</div>
      </div>
    </Expander>
  );
}

export function SettingsPage({ data }: { data: WindowData }) {
  const t = useText();
  // Read again in a new language, which About's line on updates is worded in.
  const about = useBridgeRead(readAbout, data.settings.language ?? '', 0);
  const [hotkeysOpen, setHotkeysOpen] = useState(false);
  const [fileOpen, setFileOpen] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  return (
    <div className="t-page">
      <div className="t-page-head">
        <h2 className="t-heading" tabIndex={-1}>
          {t('UiText.SettingsPage')}
        </h2>
      </div>
      <div className="t-cols">
        <section className="t-col" aria-label={t('UiText.GeneralTab')}>
          <h3 className="t-pane-h">{t('UiText.GeneralTab')}</h3>
          <General data={data} about={about} />
          <HotkeysCard data={data} open={hotkeysOpen} onOpen={setHotkeysOpen} />
          <FileCard open={fileOpen} onOpen={setFileOpen} />
        </section>
        <section className="t-col" aria-label={t('WindowText.AboutSection')}>
          <h3 className="t-pane-h">{t('WindowText.AboutSection')}</h3>
          <AboutCard about={about} open={aboutOpen} onOpen={setAboutOpen} />
        </section>
      </div>
    </div>
  );
}
