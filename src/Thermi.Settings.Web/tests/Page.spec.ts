import { expect, openWindow, select, test, text } from './fakeBridge';

// A test's title is one PascalCase word, like a .NET test's name, so a spec's acceptance step can name it.
test('ThePageStartsWithoutErrorsOrTheNetwork', async ({ page, baseURL }) => {
  const errors: string[] = [];
  const outside: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error' || message.type() === 'warning') {
      errors.push(message.text());
    }
  });
  page.on('request', (request) => {
    if (!request.url().startsWith(baseURL ?? '')) {
      outside.push(request.url());
    }
  });

  await page.goto('/');
  await page.waitForLoadState('networkidle');
  // With its data, the page's controls warn of nothing either, its sliders included.
  await openWindow(page);
  await select(page, 'Taskbar');
  await expect(page.getByRole('slider', { name: text('UiText.SizeField') })).toBeVisible();

  expect(errors).toEqual([]);
  expect(outside).toEqual([]);
});
