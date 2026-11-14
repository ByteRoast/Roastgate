/**
 * End to end scenario: the real setup wizard, then every config feature against a REAL nginx running in the test
 * container. See README.md in this folder. Needs Docker, Node 22.18+ (it runs this TypeScript file as is) and an
 * installed browser (Edge by default, see E2E_CHANNEL).
 */

import { chromium } from 'playwright-core';
import fs from 'node:fs';

import { CHANNEL, CONTAINER, SHOTS } from './lib/config.ts';
import { pageHelper } from './lib/page.ts';
import { printSummary, watchPage } from './lib/report.ts';
import { readSetupToken } from './lib/shell.ts';
import { audit } from './sections/audit.ts';
import { backups } from './sections/backups.ts';
import { certificates } from './sections/certificates.ts';
import { dashboard } from './sections/dashboard.ts';
import { restart } from './sections/restart.ts';
import { reverseProxies } from './sections/reverse-proxies.ts';
import { settings } from './sections/settings.ts';
import { setup } from './sections/setup.ts';
import { defaultSite, sites } from './sections/sites.ts';
import { twoFactor } from './sections/two-factor.ts';
import { whitelists } from './sections/whitelists.ts';
import { wizard } from './sections/wizard.ts';

import type { Browser, Page } from 'playwright-core';
import type { Context } from './lib/context.ts';

fs.rmSync(SHOTS, { recursive: true, force: true });
fs.mkdirSync(SHOTS, { recursive: true });

const token: string | undefined = readSetupToken();

if (!token) {
  throw new Error(
    `No setup token in the logs of ${CONTAINER}: the scenario needs a fresh container.`,
  );
}

const browser: Browser = await chromium.launch({
  channel: CHANNEL,
  headless: true,
});

const page: Page = await browser.newPage({
  viewport: { width: 1300, height: 950 },
});

watchPage(page);

const context: Context = { browser, helpers: pageHelper(page), page, token };

/**
 * One scenario, not independent tests: each section relies on what the previous ones left on the server (127.0.0.1 in
 * the whitelist for the wizard's HTTPS, files to back up and restore, the password changed in the settings for
 * two-factor...).
 */
const sections: ((context: Context) => Promise<void>)[] = [
  setup,
  dashboard,
  defaultSite,
  reverseProxies,
  sites,
  whitelists,
  wizard,
  certificates,
  backups,
  settings,
  twoFactor,
  audit,
  restart,
];

for (const section of sections) {
  await section(context);
}

await browser.close();

process.exit(printSummary());
