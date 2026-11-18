import { BASE, INITIAL_PASSWORD } from '../lib/config.ts';
import { check } from '../lib/report.ts';

import type { Context } from '../lib/context.ts';

/**
 * Setup.
 */
export const setup = async ({
  helpers,
  page,
  token,
}: Context): Promise<void> => {
  await page.goto(`${BASE}/setup`);
  await helpers.waitText('Step 1 of 5');
  await helpers.shot('setup-token');
  await page.waitForTimeout(1500);
  await page.getByLabel('Setup token').fill(token);
  await helpers.button('Continue').click();
  await helpers.waitText('Step 2 of 5');
  await helpers.shot('setup-password');
  await page.getByLabel('Password', { exact: true }).fill(INITIAL_PASSWORD);
  await page.getByLabel('Repeat the password').fill(INITIAL_PASSWORD);
  await helpers.button('Continue').click();
  await helpers.waitText('Step 3 of 5');
  await helpers.button('Continue').click();
  await helpers.waitText('Step 4 of 5', 20000);
  await page.getByText('host access').first().waitFor({ timeout: 25000 });

  const diagnostics: string = await helpers.body();

  check(
    'diagnostics see the real nginx and its directories',
    diagnostics.includes('nginx 1.24') &&
      diagnostics.includes('is visible to Roastgate'),
  );

  await helpers.shot('diagnostics');
  await helpers.button('Continue').click();
  await helpers.waitText('Step 5 of 5');
  await helpers.button('Finish setup').click();
  await page.waitForURL(`${BASE}/`, { timeout: 20000 });
  await helpers.waitText('Dashboard');

  check('setup finished and signed in', true);
};
