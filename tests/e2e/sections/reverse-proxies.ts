import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { sh } from '../lib/shell.ts';

import type { Context } from '../lib/context.ts';

/**
 * Reverse proxies
 */
export const reverseProxies = async ({
  helpers,
  page,
}: Context): Promise<void> => {
  await page.goto(`${BASE}/reverse-proxies`);
  await helpers.waitText('No reverse proxy yet');
  await helpers.shot('reverse-proxies-empty');
  await page.getByRole('link', { name: 'New from raw text' }).click();
  await page.waitForURL(/\/reverse-proxies\/edit$/);
  await helpers.waitMonaco('new-proxy');
  await page.waitForTimeout(800);

  const goodProxy: string =
    'server {\n    listen 8080;\n    server_name app.example.com;\n    location / {\n        proxy_pass http://127.0.0.1:3000;\n    }\n}';

  await page.getByLabel('File name').fill('app.example.com');
  await helpers.setEditor(goodProxy);
  await helpers.shot('editor-new');
  await helpers.button('Save').click();
  await helpers.waitText('Saved.');

  check(
    'creating a reverse proxy saves, validates and reloads',
    (await helpers.body()).includes('nginx reloaded.'),
  );

  check(
    'a successful save is a toast that names the file, not an inline alert',
    (await helpers.toasts()).includes('app.example.com.conf: Saved.') &&
      !(await helpers.alerts()).includes('Saved.'),
  );

  check(
    'the file really exists on the nginx host',
    sh('cat /etc/nginx/conf.d/app.example.com.conf').includes(
      'proxy_pass http://127.0.0.1:3000;',
    ),
  );

  check(
    'the new proxy is really listening after the reload',
    sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open',
  );

  await helpers.shot('editor-saved');

  // a change nginx refuses
  await helpers.openEditor('/reverse-proxies/edit?name=app.example.com.conf');

  check(
    'the editor opens the saved content',
    (await helpers.getEditor()).includes('proxy_pass http://127.0.0.1:3000;'),
  );

  await helpers.setEditor(
    'server {\n    listen 8080;\n    bogus_directive on;\n}',
  );
  await helpers.button('Save').click();
  await helpers.waitText('undone');

  const refused: string = await helpers.body();

  check(
    'a refused change shows the real nginx error and says it was undone',
    refused.includes('unknown directive "bogus_directive"') &&
      refused.includes('undone'),
  );

  check(
    'a refused change stays inline with the nginx output, not in a toast',
    (await helpers.alerts()).includes('unknown directive "bogus_directive"') &&
      !(await helpers.toasts()).includes('undone'),
  );

  check(
    'the file on the host is unchanged after the refusal',
    sh('cat /etc/nginx/conf.d/app.example.com.conf').includes(
      'proxy_pass http://127.0.0.1:3000;',
    ) && !sh('cat /etc/nginx/conf.d/app.example.com.conf').includes('bogus'),
  );

  check(
    'nginx still serves the old configuration',
    sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open',
  );

  await helpers.shot('refused');

  // disable and enable
  await page.goto(`${BASE}/reverse-proxies`);
  await helpers.waitText('app.example.com.conf');
  await page.getByLabel('Enable app.example.com.conf').click();
  await helpers.shot('reverse-proxy-disabled');
  await page.waitForTimeout(1500);

  check(
    'disabling renames the file to .conf.disabled',
    sh('ls /etc/nginx/conf.d').includes('app.example.com.conf.disabled') &&
      !sh('ls /etc/nginx/conf.d').split('\n').includes('app.example.com.conf'),
  );

  check(
    'a disabled proxy no longer listens',
    sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`).startsWith(
      'ERR',
    ),
  );

  check(
    'the switch in the list reports in a toast that names the file',
    (await helpers.toasts()).includes('app.example.com.conf: Disabled.'),
  );

  await page.getByLabel('Enable app.example.com.conf').click();
  await page.waitForTimeout(1500);

  check(
    'enabling renames it back and nginx listens again',
    sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open',
  );
};
