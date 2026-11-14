import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { sh } from '../lib/shell.ts';

import type { Locator } from 'playwright-core';
import type { Context } from '../lib/context.ts';

/**
 * The existing default site, enabled by an absolute symlink.
 */
export const defaultSite = async ({
  helpers,
  page,
}: Context): Promise<void> => {
  await page.goto(`${BASE}/sites`);
  await helpers.waitText('default');

  const defaultSwitch: Locator = page.getByLabel('Enable default');

  check(
    'an existing site enabled by an ABSOLUTE symlink shows as enabled',
    await defaultSwitch.isChecked(),
  );

  await helpers.shot('sites-existing');
};

/**
 * A site: create, enable (symlink), serve, delete.
 */
export const sites = async ({ helpers, page }: Context): Promise<void> => {
  await page.goto(`${BASE}/sites/edit`);
  await page.waitForFunction(
    () => window.monaco && window.monaco.editor.getModels().length > 0,
    null,
    {
      timeout: 15000,
    },
  );

  await page.waitForTimeout(800);
  await page.getByLabel('File name').fill('example.com');
  await helpers.setEditor(
    'server {\n    listen 8081;\n    location / { return 200 "hello from roastgate\\n"; }\n}',
  );

  await helpers.button('Save').click();
  await helpers.waitText('Saved.');
  await helpers.shot('site-saved');

  check(
    'a new site is saved but not enabled yet',
    sh('ls /etc/nginx/sites-available').includes('example.com') &&
      !sh('ls /etc/nginx/sites-enabled').includes('example.com'),
  );

  await helpers.button('Enable').click();
  await page.waitForTimeout(1500);

  const link: string = sh('readlink /etc/nginx/sites-enabled/example.com');

  check(
    'enabling a site creates a relative symlink',
    link === '../sites-available/example.com',
    link,
  );

  check(
    'enabling from the editor reports in a toast',
    (await helpers.toasts()).includes('example.com: Enabled.'),
  );

  const served: string = sh(
    `bash -c 'exec 3<>/dev/tcp/127.0.0.1/8081; printf "GET / HTTP/1.0\\r\\n\\r\\n" >&3; cat <&3'`,
  );

  check(
    'the enabled site is really served by nginx',
    served.includes('hello from roastgate'),
    served,
  );

  await page.goto(`${BASE}/sites`);
  await helpers.waitText('example.com');
  await helpers.shot('sites-enabled');
  await page.getByLabel('Delete example.com').click();
  await helpers.button('Delete').click();
  await page.waitForTimeout(1500);

  check(
    'deleting a site removes the file AND its symlink',
    !sh('ls /etc/nginx/sites-available').includes('example.com') &&
      !sh('ls /etc/nginx/sites-enabled').includes('example.com'),
  );

  check(
    'deleting from the list reports in a toast that names the file',
    (await helpers.toasts()).includes('example.com: Deleted.'),
  );

  check(
    'the default site was not touched',
    sh('readlink /etc/nginx/sites-enabled/default') ===
      '/etc/nginx/sites-available/default',
  );

  // Deleting from the editor leaves the page: the toast must still say what happened once the list is shown.
  await page.goto(`${BASE}/sites/edit`);
  await helpers.waitMonaco('throwaway-site');
  await page.waitForTimeout(800);
  await page.getByLabel('File name').fill('throwaway.example.com');
  await helpers.setEditor(
    'server {\n    listen 8089;\n    location / { return 200 "bye\\n"; }\n}',
  );

  await helpers.button('Save').click();
  await helpers.waitText('Saved.');
  await helpers.button('Delete').click();
  await page
    .locator('.mud-dialog')
    .getByRole('button', { name: 'Delete', exact: true })
    .click();

  await page.waitForURL(`${BASE}/sites`);
  await helpers.waitText('Deleted.');
  await helpers.shot('site-deleted');

  check(
    'deleting from the editor goes back to the list with a toast naming the file',
    (await helpers.toasts()).includes('throwaway.example.com: Deleted.') &&
      !sh('ls /etc/nginx/sites-available').includes('throwaway'),
  );
};
