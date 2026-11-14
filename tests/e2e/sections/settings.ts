import { BASE, INITIAL_PASSWORD, NEW_PASSWORD } from '../lib/config.ts';
import { check } from '../lib/report.ts';

import type { BrowserContext, Page } from 'playwright-core';
import type { Context } from '../lib/context.ts';

/**
 * Settings.
 */
export const settings = async ({
  browser,
  helpers,
  page,
}: Context): Promise<void> => {
  await page.goto(`${BASE}/settings`);
  await helpers.waitField('nginx directory');
  await page.getByLabel('nginx directory').fill('relative/path');
  await helpers.button('Save settings').click();
  await helpers.waitText('must be an absolute path');

  check('an unsafe path is refused with an explanation', true);

  await page.getByLabel('nginx directory').fill('/etc/nginx');
  await page.getByLabel("Let's Encrypt e-mail").fill('ops@example.com');
  await helpers.button('Save settings').click();
  await helpers.waitText('Settings saved.');

  check(
    'saving the settings is a toast, no longer an inline alert',
    (await helpers.toasts()).includes('Settings saved.') &&
      !(await helpers.alerts()).includes('Settings saved.'),
  );

  await page.goto(`${BASE}/certificates`);
  await helpers.waitField('Domain names');

  check(
    'saved settings are used elsewhere (the e-mail is pre-filled when getting a certificate)',
    (await page.getByLabel('E-mail for expiry notices').inputValue()) ===
      'ops@example.com',
  );

  await page.goto(`${BASE}/settings`);
  await helpers.waitField('nginx directory');
  await helpers.button('Check the host with these values').click();
  await helpers.waitText('host access');

  check(
    'the host can be checked from the settings page',
    /nginx/.test(await helpers.body()) &&
      /Let's Encrypt directory/.test(await helpers.body()),
  );

  await helpers.shot('settings');

  // Another browser, signed in with the current password.
  const context2: BrowserContext = await browser.newContext({
    viewport: { width: 1300, height: 900 },
  });

  const page2: Page = await context2.newPage();

  await page2.goto(`${BASE}/login`);
  await page2.getByLabel('Username').fill('admin');
  await page2.getByLabel('Password').fill(INITIAL_PASSWORD);
  await page2.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page2.waitForURL(`${BASE}/`);
  await page.getByLabel('Current password').fill('definitely-not-it-1!');
  await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD);
  await page.getByLabel('Repeat the new password').fill(NEW_PASSWORD);
  await helpers.button('Change the password').click();
  await helpers.waitText('current password is not correct');

  check('a wrong current password is refused', true);

  await page.getByLabel('Current password').fill(INITIAL_PASSWORD);
  await helpers.button('Change the password').click();
  await page.waitForURL(/\/login\?notice=password-changed/, { timeout: 15000 });
  await helpers.shot('password-changed');

  check(
    'changing the password sends the user to the sign-in page with an explanation',
    (await helpers.body()).includes('Your password was changed'),
  );

  await page2.goto(`${BASE}/`);

  check(
    'the other browser, still open, is signed out as well',
    page2.url().includes('/login'),
    page2.url(),
  );

  await context2.close();
  await page.getByLabel('Username').fill('admin');
  await page.getByLabel('Password').fill(INITIAL_PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await helpers.waitText('Invalid username or password.');

  check('the old password no longer works', true);

  await page.getByLabel('Password').fill(NEW_PASSWORD);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.waitForURL(`${BASE}/`, { timeout: 10000 });

  check('the new password works', true);
};
