import { useEffect, useRef, useState } from 'react';
import { bridge, type WindowData } from '../bridge/useWindow';
import { Button, Dialog, Icon, InfoBar, LinkButton, Progress } from '../kit';
import { useText } from '../texts/Texts';
import './status.css';

/**
 * Where the driver's install is: offered, asked of Windows, refused, done for a moment, or waiting for Windows to
 * restart, which stays, since nothing reads until then.
 */
type DriverStep = 'offered' | 'installing' | 'refused' | 'installed' | 'restart';

/** How long "Driver installed" stays before its bar goes. */
const installedFor = 8_000;

/** The driver's dialog: what it is, what installing it does, and the games that may mind it. */
function DriverDialog({ open, onInstall, onClose }: { open: boolean; onInstall: () => void; onClose: () => void }) {
  const t = useText();
  return (
    <Dialog
      open={open}
      title={t('WindowText.DriverTitle')}
      main={t('WindowText.InstallDriver')}
      onMain={onInstall}
      cancel={t('UiText.Close')}
      onCancel={onClose}
      shield
    >
      <p className="t-dialog-text">{t('WindowText.DriverReads')}</p>
      <p className="t-dialog-text">
        <b>{t('WindowText.DriverInstallLead')}</b> {t('WindowText.DriverInstallText')}
      </p>
      <p className="t-dialog-text">
        <b>{t('WindowText.DriverAntiCheatLead')}</b> {t('WindowText.DriverAntiCheat')}
      </p>
    </Dialog>
  );
}

/** Try again takes the focus as it comes, from the bar that held it while Windows asked. */
const focusOnMount = (button: HTMLButtonElement | null): void => button?.focus();

/** The driver's bar through its install: offered with its dialog, then installing, refused or installed. */
function DriverBar({ needed, onInstalled }: { needed: boolean; onInstalled: () => void }) {
  const t = useText();
  const [step, setStep] = useState<DriverStep>('offered');
  const [asking, setAsking] = useState(false);
  const bar = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (step !== 'installed') {
      return;
    }
    const done = setTimeout(() => setStep('offered'), installedFor);
    return () => clearTimeout(done);
  }, [step]);
  const install = async (): Promise<void> => {
    setAsking(false);
    setStep('installing');
    // The button goes while Windows asks, so the focus waits on the bar.
    bar.current?.focus();
    // A call that fails installed nothing either, so it is refused too, and Try again stays offered.
    const ended = (await bridge()?.installDriver().catch(() => undefined)) ?? 'not-installed';
    setStep(ended === 'installed' ? 'installed' : ended === 'restart-windows' ? 'restart' : 'refused');
    if (ended === 'installed') {
      onInstalled();
    }
  };
  if (step === 'offered' && !needed) {
    return null;
  }
  return (
    <div ref={bar} tabIndex={-1} className="t-bar">
      {step === 'offered' && (
        <InfoBar
          intent="warning"
          title={t('WindowText.DriverNeeded')}
          message={t('WindowText.DriverAsks')}
          actions={
            <>
              <Button shield onClick={() => void install()}>
                {t('WindowText.InstallDriver')}
              </Button>
              <LinkButton onClick={() => setAsking(true)}>{t('WindowText.WhatIsThis')}</LinkButton>
            </>
          }
        />
      )}
      {step === 'installing' && (
        <InfoBar
          intent="info"
          title={t('WindowText.DriverInstalling')}
          actions={
            <span className="t-bar-progress">
              <Progress label={t('WindowText.DriverInstalling')} />
            </span>
          }
        />
      )}
      {step === 'restart' && (
        <InfoBar intent="warning" title={t('WindowText.DriverRestart')} message={t('WindowText.DriverRestartMore')} />
      )}
      {step === 'refused' && (
        <InfoBar
          intent="error"
          title={t('WindowText.DriverRefused')}
          message={t('WindowText.DriverRefusedMore')}
          actions={
            <>
              <Button ref={focusOnMount} shield onClick={() => void install()}>
                {t('WindowText.TryAgain')}
              </Button>
              <LinkButton onClick={() => setAsking(true)}>{t('WindowText.WhatIsThis')}</LinkButton>
            </>
          }
        />
      )}
      {step === 'installed' && (
        <InfoBar
          intent="success"
          title={t('WindowText.DriverInstalled')}
          message={t('WindowText.DriverReading')}
          actions={
            <Button
              kind="subtle"
              className="t-bar-dismiss"
              icon={<Icon name="close" />}
              label={t('CatalogText.Dismiss')}
              onClick={() => {
                setStep('offered');
                onInstalled();
              }}
            />
          }
        />
      )}
      <DriverDialog open={asking} onInstall={() => void install()} onClose={() => setAsking(false)} />
    </div>
  );
}

/**
 * The sensor service's bar: stopped, with Start it, and why when another program holds its pipe, or readings late,
 * which come back on their own.
 */
function ServiceBar({ data }: { data: WindowData }) {
  const t = useText();
  const service = data.sensors.service;
  if (service === 'stale') {
    return <InfoBar intent="warning" title={t('WindowText.ReadingsStale')} message={t('WindowText.ReadingsStaleMore')} />;
  }
  if (service !== 'stopped') {
    return null;
  }
  return (
    <InfoBar
      intent="error"
      title={t('WindowText.ServiceStopped')}
      message={t(data.sensors.taken ? 'WindowText.PipeTaken' : 'WindowText.ServiceStoppedMore')}
      actions={
        <Button shield onClick={() => void bridge()?.startService()}>
          {t('WindowText.StartIt')}
        </Button>
      }
    />
  );
}

export interface BarsProps {
  data: WindowData;
  /** The driver installed: the focus goes back to the list's selected row. */
  onDriverInstalled: () => void;
}

/**
 * The bars under the title row, for what keeps the window from working fully, each with its fix: the overlay not
 * running, the sensor service stopped or late, and the CPU's driver.
 */
export function Bars({ data, onDriverInstalled }: BarsProps) {
  const t = useText();
  return (
    <div className="t-bars" aria-live="polite">
      {!data.status.overlay && (
        <InfoBar
          intent="info"
          title={t('UiText.OverlayNotRunning')}
          actions={<Button onClick={() => void bridge()?.startOverlay()}>{t('UiText.StartOverlay')}</Button>}
        />
      )}
      <ServiceBar data={data} />
      <DriverBar needed={data.status.driver} onInstalled={onDriverInstalled} />
    </div>
  );
}
