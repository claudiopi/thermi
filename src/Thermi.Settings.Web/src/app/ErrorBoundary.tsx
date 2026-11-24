import { Component, type ErrorInfo, type ReactNode } from 'react';
import { bridge } from '../bridge/useWindow';
import { Button } from '../kit';
import { useText } from '../texts/Texts';

function Failed() {
  const t = useText();
  const reload = (): void => window.location.reload();
  return (
    <main className="t-failed">
      <p>{t('UiText.SomethingWentWrong')}</p>
      <Button onClick={reload}>{t('UiText.Reload')}</Button>
    </main>
  );
}

/**
 * What the page draws when drawing it threw: a line that says so and a way to load it again, never a blank window. The
 * error goes to thermi's log with where in the page it happened.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    void bridge()
      ?.report(`${error.stack ?? String(error)}${info.componentStack ?? ''}`)
      .catch(() => {});
  }

  override render(): ReactNode {
    return this.state.failed ? <Failed /> : this.props.children;
  }
}
