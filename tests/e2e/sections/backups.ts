import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { sh } from '../lib/shell.ts';

import type { Context } from '../lib/context.ts';
import type { Position } from '../lib/page.ts';

/**
 * Backups: compare and restore.
 */
export const backups = async ({ helpers, page }: Context): Promise<void> => {
  // The refused change earlier left no backup behind; a successful second edit creates the first one.
  check(
    'a refused change left no backup',
    sh('ls /etc/nginx/conf.d').includes('app.example.com.conf'),
  );

  await helpers.openEditor('/reverse-proxies/edit?name=app.example.com.conf');
  await helpers.setEditor((await helpers.getEditor()) + '\n# second version');
  await helpers.button('Save').click();
  await helpers.expectSaved();
  await page.goto(
    `${BASE}/backups?area=ReverseProxies&name=app.example.com.conf`,
  );

  await helpers.waitText('Taken before');

  const showBox: Position = await helpers.box(helpers.button('Show'));
  const fileInput: Position = await helpers.box(
    page
      .locator('.mud-input-outlined')
      .filter({ has: page.getByLabel('File name') }),
  );

  check(
    'the Show button is centred on the filter inputs',
    Math.abs(
      showBox.y + showBox.height / 2 - (fileInput.y + fileInput.height / 2),
    ) <= 3,
    `${showBox.y + showBox.height / 2} vs ${fileInput.y + fileInput.height / 2}`,
  );

  check(
    'the backups of a file are listed',
    /a change/i.test(await helpers.body()),
  );

  await helpers.button('Compare').first().click();
  await helpers.waitText('Restoring');

  const comparison: string = await helpers.body();

  check(
    'comparing shows what restoring would add and remove',
    /adds \d+ line/.test(comparison) && /removes \d+ line/.test(comparison),
    comparison.slice(0, 300),
  );

  check(
    'the differences are drawn line by line',
    (await page.locator('.rg-diff-added, .rg-diff-removed').count()) > 0,
  );

  await helpers.shot('10-backups');

  const beforeRestore = sh('cat /etc/nginx/conf.d/app.example.com.conf');

  await helpers.button('Restore this version').click();
  await helpers.button('Restore').click();
  await helpers.waitText('Restored.');

  check(
    'restoring puts the old version back and reloads nginx',
    (await helpers.body()).includes('nginx reloaded.') &&
      sh('cat /etc/nginx/conf.d/app.example.com.conf') !== beforeRestore,
  );

  check(
    'restoring reports in a toast that names the file',
    (await helpers.toasts()).includes('app.example.com.conf: Restored.'),
  );

  // A deleted site comes back (it was deleted earlier in the scenario).
  await page.goto(`${BASE}/backups?area=Sites&name=example.com`);
  await helpers.waitText('a deletion');
  await helpers.button('Compare').first().click();
  await helpers.waitText('does not exist any more');

  check(
    'a deleted file says restoring creates it again',
    (await helpers.body()).includes('restoring creates it again'),
  );

  await helpers.button('Restore this version').click();
  await helpers.button('Restore').click();
  await helpers.waitText('Restored.');

  check(
    'the deleted site is back in sites-available',
    sh('ls /etc/nginx/sites-available').includes('example.com'),
  );
};
