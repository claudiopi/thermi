import { useEffect, useRef, useState } from 'react';
import type { ExtensionState } from '../bridge/bridge.g';
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
function DriverDialog({ open, label, onInstall, onClose }: { open: boolean; label: string; onInstall: () => void; onClose: () => void }) {
  const t = useText();
  return (
    <Dialog
      open={open}
      title={t('WindowText.DriverTitle')}
      main={label}
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

/**
 * The driver's bar through its install: offered with its dialog, then installing, refused or installed. A per-user install
 * gets its driver with thermi installed for everyone, which the button says.
 */
function DriverBar({ needed, forEveryone, onInstalled }: { needed: boolean; forEveryone: boolean; onInstalled: () => void }) {
  const t = useText();
  const label = t(forEveryone ? 'WindowText.InstallForEveryone' : 'WindowText.InstallDriver');
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
                {label}
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
      <DriverDialog open={asking} label={label} onInstall={() => void install()} onClose={() => setAsking(false)} />
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
        <Button shield={!data.status.forEveryone} onClick={() => void bridge()?.startService()}>
          {t('WindowText.StartIt')}
        </Button>
      }
    />
  );
}

/**
 * The GNOME Shell extension's bar, while C# offers its fix: missing, to install behind GNOME's own prompt, or off, to
 * turn on; refused, it says so and offers it again. Once the Shell has it on, C# offers no fix and the bar goes.
 */
function ExtensionBar({ fix }: { fix: ExtensionState | null }) {
  const t = useText();
  const [refused, setRefused] = useState(false);
  if (fix === null || fix === 'enabled') {
    return null;
  }
  const install = async (): Promise<void> => {
    setRefused(false);
    setRefused(!((await bridge()?.installExtension().catch(() => false)) ?? false));
  };
  const missing = fix === 'missing';
  return (
    <InfoBar
      intent={refused ? 'error' : 'warning'}
      title={t(refused ? 'WindowText.ExtensionRefused' : missing ? 'WindowText.ExtensionMissing' : 'WindowText.ExtensionDisabled')}
      message={t(missing ? 'WindowText.ExtensionMissingMore' : 'WindowText.ExtensionDisabledMore')}
      actions={
        <Button ref={refused ? focusOnMount : null} onClick={() => void install()}>
          {t(refused ? 'WindowText.TryAgain' : missing ? 'WindowText.InstallExtension' : 'WindowText.EnableExtension')}
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
 * running, the sensor service stopped or late, the GNOME Shell extension missing or off, and the CPU's driver.
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
      <ExtensionBar fix={data.status.extension} />
      <DriverBar needed={data.status.driver} forEveryone={data.status.forEveryone} onInstalled={onDriverInstalled} />
    </div>
  );
}
