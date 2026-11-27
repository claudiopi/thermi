import type { LaunchOptions, PlaywrightWorkerArgs, PlaywrightWorkerOptions } from '@playwright/test';

// How the tests launch Edge, and Chromium beside WebKit on Linux, in the config and in the specs that change its launch.
// Without the GPU, a run's browsers draw in software and take nothing of the graphics the user's desktop draws with.
export const edge = { args: ['--disable-gpu'] };

type Launch = (worker: PlaywrightWorkerArgs & PlaywrightWorkerOptions, use: (options: LaunchOptions) => Promise<void>) => Promise<void>;

/**
 * The launch of a spec whose scrollbars show as the window draws them, where a headless Chromium hides them; WebKit's
 * draws them as it is, and takes none of Chromium's switches.
 */
export const scrollbarsShown: [Launch, { scope: 'worker' }] = [
  async ({ browserName }, use) => use(browserName === 'chromium' ? { ...edge, ignoreDefaultArgs: ['--hide-scrollbars'] } : {}),
  { scope: 'worker' },
];
