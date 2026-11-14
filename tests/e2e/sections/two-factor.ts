import { BASE, NEW_PASSWORD } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { sh } from '../lib/shell.ts';
import { totp } from '../lib/totp.ts';

import type { BrowserContext, Page } from 'playwright-core';
import type { Context } from '../lib/context.ts';
import type { Position } from '../lib/page.ts';

/**
 * Two-factor authentication.
 */
export const twoFactor = async ({
  browser,
  helpers,
  page,
}: Context): Promise<void> => {
  const typePasswordStep = async (p: Page, password: string) => {
    await p.getByLabel('Username').fill('admin');
    await p.getByLabel('Password', { exact: true }).fill(password);
    await p.getByRole('button', { name: 'Sign in', exact: true }).click();
  };

  const newBrowserAtLogin = async (): Promise<{
    context: BrowserContext;
    fresh: Page;
  }> => {
    const context: BrowserContext = await browser.newContext({
      viewport: { width: 1300, height: 900 },
    });

    const fresh: Page = await context.newPage();

    await fresh.goto(`${BASE}/login`);
    await fresh.getByLabel('Username').waitFor();
    await fresh.waitForTimeout(700);

    return { context, fresh };
  };

  const enableTwoFactor = async () => {
    await page.goto(`${BASE}/settings`);
    await helpers.waitText('Two-factor authentication');
    await helpers.button('Set up two-factor authentication').click();
    await page.locator('.rg-qr svg').waitFor();

    const secret: string = (
      await page.locator('.rg-secret').innerText()
    ).replace(/\s+/g, '');

    await page.getByLabel('Code from the app').fill(totp(secret));
    await helpers.button('Turn on two-factor authentication').click();
    await helpers.waitText('Save these recovery codes now');

    const recovery: string[] = (
      await page.locator('pre.rg-recovery-codes').innerText()
    )
      .trim()
      .split(/\s+/);

    return { secret, recovery };
  };

  await page.goto(`${BASE}/settings`);
  await helpers.waitText('Two-factor authentication');
  await helpers.button('Set up two-factor authentication').click();
  await page.locator('.rg-qr svg').waitFor();

  check(
    'a QR code is shown to scan',
    (await page.locator('.rg-qr svg').count()) === 1,
  );

  await helpers.shot('two-factor-setup');

  const secret: string = (await page.locator('.rg-secret').innerText()).replace(
    /\s+/g,
    '',
  );

  check(
    'the secret is shown too, for apps that cannot scan',
    /^[A-Z2-7]{32}$/.test(secret),
    secret,
  );

  await page.getByLabel('Code from the app').fill('000000');
  await helpers.button('Turn on two-factor authentication').click();
  await helpers.waitText('That code is not valid');

  check(
    'a wrong code does not turn it on',
    !(await helpers.body()).includes('Set up two-factor'),
  );

  await page.getByLabel('Code from the app').fill(totp(secret));
  await helpers.button('Turn on two-factor authentication').click();
  await helpers.waitText('Save these recovery codes now');
  await helpers.shot('two-factor-recovery-codes');

  const recovery: string[] = (
    await page.locator('pre.rg-recovery-codes').innerText()
  )
    .trim()
    .split(/\s+/);

  check(
    'ten recovery codes are shown, once',
    recovery.length === 10 &&
      recovery.every((code) => /^[a-z2-7]{4}(-[a-z2-7]{4}){3}$/.test(code)),
    recovery.join(' '),
  );

  await helpers.button('I have saved them, sign in again').click();
  await page.waitForURL(/\/login\?notice=two-factor-changed/, {
    timeout: 15000,
  });

  check(
    'turning it on ends the session and says why',
    (await helpers.body()).includes('Two-factor authentication was changed'),
  );

  // The two steps, with a code computed by this script (so the server's TOTP is checked against an independent implementation).
  await typePasswordStep(page, NEW_PASSWORD);
  await helpers.waitText('Two-step verification');

  check(
    'the password alone leads to the second step, not into the application',
    page.url().includes('/login'),
  );

  await helpers.shot('two-step');
  await page.getByLabel('Authentication code').fill('000000');
  await helpers.button('Verify').click();
  await helpers.waitText('That code is not valid');

  check('a wrong code is refused', page.url().includes('/login'));

  await page.getByLabel('Authentication code').fill(totp(secret, 1));
  await helpers.button('Verify').click();
  await page.waitForURL(`${BASE}/`, { timeout: 15000 });

  check('the code computed independently signs in', true);

  // A recovery code works once.
  const first: { context: BrowserContext; fresh: Page } =
    await newBrowserAtLogin();

  await typePasswordStep(first.fresh, NEW_PASSWORD);
  await first.fresh
    .getByLabel('Authentication code')
    .fill(recovery[0].toUpperCase());

  await first.fresh
    .getByRole('button', { name: 'Verify', exact: true })
    .click();

  await first.fresh.waitForURL(`${BASE}/`, { timeout: 15000 });

  check('a recovery code signs in (case does not matter)', true);

  await first.context.close();

  const second: { context: BrowserContext; fresh: Page } =
    await newBrowserAtLogin();

  await typePasswordStep(second.fresh, NEW_PASSWORD);
  await second.fresh.getByLabel('Authentication code').fill(recovery[0]);
  await second.fresh
    .getByRole('button', { name: 'Verify', exact: true })
    .click();

  await second.fresh
    .getByText('That code is not valid')
    .first()
    .waitFor({ timeout: 10000 });

  check(
    'the same recovery code does not work twice',
    second.fresh.url().includes('/login'),
  );

  await second.context.close();

  // New recovery codes replace the old ones.
  await page.goto(`${BASE}/settings`);
  await helpers.waitText('Two-factor authentication is');
  await page.getByLabel('Code from the app', { exact: true }).fill(recovery[1]);

  const inputMiddle: Position = await helpers.box(
    page
      .locator('.mud-input-outlined')
      .filter({ has: page.getByLabel('Code from the app', { exact: true }) }),
  );

  const buttonBox: Position = await helpers.box(
    helpers.button('Get new recovery codes'),
  );

  check(
    'the recovery codes button is centred on its code input',
    Math.abs(
      inputMiddle.y +
        inputMiddle.height / 2 -
        (buttonBox.y + buttonBox.height / 2),
    ) <= 3,
    `${inputMiddle.y + inputMiddle.height / 2} vs ${buttonBox.y + buttonBox.height / 2}`,
  );

  await helpers.button('Get new recovery codes').click();
  await helpers.waitText('Save these recovery codes now');

  const regenerated: string[] = (
    await page.locator('pre.rg-recovery-codes').innerText()
  )
    .trim()
    .split(/\s+/);

  check(
    'regenerating gives ten new codes, confirmed with a code',
    regenerated.length === 10 && !regenerated.includes(recovery[2]),
  );

  await helpers.button('I have saved them').click();
  await helpers.waitText('10 recovery code(s) left');

  check(
    'regenerating does not sign the user out',
    !page.url().includes('/login'),
  );

  const third: { context: BrowserContext; fresh: Page } =
    await newBrowserAtLogin();

  await typePasswordStep(third.fresh, NEW_PASSWORD);
  await third.fresh.getByLabel('Authentication code').fill(recovery[2]);
  await third.fresh
    .getByRole('button', { name: 'Verify', exact: true })
    .click();

  await third.fresh
    .getByText('That code is not valid')
    .first()
    .waitFor({ timeout: 10000 });

  check('an old recovery code no longer works', true);

  await third.context.close();

  // Turning it off needs the password and a code.
  await page.getByLabel('Password', { exact: true }).fill('wrong-password-123');
  await page
    .getByLabel('Code from the app (or a recovery code)')
    .fill(regenerated[0]);

  await helpers.button('Turn off two-factor authentication').click();
  await helpers.waitText('The password is not correct');

  check('turning it off with a wrong password is refused', true);

  await page.getByLabel('Password', { exact: true }).fill(NEW_PASSWORD);
  await helpers.button('Turn off two-factor authentication').click();
  await page.waitForURL(/\/login\?notice=two-factor-changed/, {
    timeout: 15000,
  });

  await typePasswordStep(page, NEW_PASSWORD);
  await page.waitForURL(`${BASE}/`, { timeout: 15000 });

  check('with two-factor off the password alone signs in again', true);

  // The way out when the phone is lost: the console command.
  const enabledAgain: { secret: string; recovery: string[] } =
    await enableTwoFactor();

  check(
    'it can be turned on again with a new secret',
    enabledAgain.secret !== secret,
  );

  await helpers.button('I have saved them, sign in again').click();
  await page.waitForURL(/\/login\?notice=two-factor-changed/, {
    timeout: 15000,
  });

  await typePasswordStep(page, NEW_PASSWORD);
  await helpers.waitText('Two-step verification');

  const consoleOutput: string = sh('roastgate disable-2fa 2>&1');

  check(
    'the console command turns it off',
    consoleOutput.includes('is off'),
    consoleOutput,
  );

  await page.goto(`${BASE}/login`);
  await typePasswordStep(page, NEW_PASSWORD);
  await page.waitForURL(`${BASE}/`, { timeout: 20000 });

  check('afterwards the password alone is enough', true);
};
