import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { requestFromLocalhost, sh } from '../lib/shell.ts';

import type { Context } from '../lib/context.ts';

/**
 * Whitelists.
 */
export const whitelists = async ({ helpers, page }: Context): Promise<void> => {
  await page.goto(`${BASE}/whitelists/edit`);
  await helpers.waitText('Allowed addresses');
  await page.getByLabel('Name', { exact: true }).fill('alice');
  await page.getByLabel('Address', { exact: true }).first().fill('203.0.113.7');
  await page.getByLabel('Comment').first().fill('alice at home');
  await helpers.button('Add address').click();
  await page.getByLabel('Address', { exact: true }).nth(1).fill('999.1.1.1');
  await page.waitForTimeout(500);

  check(
    'an invalid address is flagged on its own row',
    (await helpers.body()).includes('is not a valid IPv4 or IPv6 address'),
  );

  check(
    'saving is blocked while an address is invalid',
    await helpers.button('Save').isDisabled(),
  );

  await page.getByLabel('Address', { exact: true }).nth(1).fill('10.0.0.5/24');
  await page.waitForTimeout(500);

  check(
    'host bits in a CIDR range are a warning that suggests the network, not an error',
    (await helpers.body()).includes('did you mean 10.0.0.0/24') &&
      !(await helpers.button('Save').isDisabled()),
  );

  await page.getByLabel('Address', { exact: true }).nth(1).fill('');
  await page
    .getByPlaceholder(/One per line/)
    .fill('10.0.0.0/8, 192.168.1.0/24\nallow 2001:db8::/32;');

  await helpers.button('Add them to the list').click();
  await page.waitForTimeout(500);

  check(
    'pasting several addresses adds one row each',
    (await page.getByLabel('Address', { exact: true }).count()) >= 4,
  );

  check(
    'the "add my address" shortcut shows what Roastgate sees',
    /add my address \(/i.test(await helpers.body()),
  );

  await helpers.shot('whitelist-editor');
  await helpers.button('Save').click();
  await helpers.waitText('Saved.');

  const saved: string = sh('cat /etc/nginx/whitelists/alice.conf');

  check(
    'the whitelist is written as clean allow lines',
    saved.includes('allow 203.0.113.7; # alice at home') &&
      saved.includes('allow 10.0.0.0/8;') &&
      saved.includes('allow 2001:db8::/32;') &&
      !saved.includes('999'),
    saved,
  );

  check(
    'the page shows the include line to copy',
    (await helpers.body()).includes(
      'include /etc/nginx/whitelists/alice.conf;',
    ),
  );

  check(
    'a whitelist nothing includes says so',
    (await helpers.body()).includes('Nothing includes this whitelist yet'),
  );

  // A proxy that includes it. It serves a static file on purpose: nginx runs "return" before allow/deny, which would hide the whitelist.
  await page.goto(`${BASE}/reverse-proxies/edit`);
  await helpers.waitMonaco('private-proxy');
  await page.getByLabel('File name').fill('private.example.com');
  await helpers.setEditor(
    'server {\n    listen 8082;\n    include /etc/nginx/whitelists/alice.conf;\n    deny all;\n    location / { root /var/www/html; index index.nginx-debian.html; }\n}',
  );

  await helpers.button('Save').click();
  await helpers.waitText('Saved.');

  check(
    'nginx really DENIES an address that is not in the whitelist',
    requestFromLocalhost(8082).includes('403'),
    requestFromLocalhost(8082),
  );

  await page.goto(`${BASE}/whitelists`);
  await helpers.waitText('alice.conf');

  const listText: string = await helpers.body();

  check(
    'the list shows how many addresses and who uses the whitelist',
    listText.includes('private.example.com.conf') &&
      /alice\.conf\s+\d/.test(listText),
  );

  await helpers.shot('whitelist-list');
  await page.goto(`${BASE}/whitelists/edit?name=alice.conf`);
  await helpers.waitText('Used by');

  check(
    'the editor lists the including file and links to it',
    (await helpers.body()).includes('private.example.com.conf (line 3)'),
  );

  await helpers.button('Add address').click();
  await page.getByLabel('Address', { exact: true }).last().fill('127.0.0.1');
  await helpers.button('Save').click();
  await helpers.waitText('Saved.');

  check(
    'after adding 127.0.0.1 nginx really ALLOWS it',
    requestFromLocalhost(8082).includes('200'),
    requestFromLocalhost(8082),
  );

  // deletion is blocked while the whitelist is in use
  await page.goto(`${BASE}/whitelists`);
  await helpers.waitText('alice.conf');
  await page.getByLabel('Delete alice.conf').click();
  await helpers.button('Delete').click();
  await helpers.waitText('is still included by');

  check(
    'deleting a whitelist that is in use is refused and names the file',
    (await helpers.body()).includes('private.example.com.conf') &&
      sh('ls /etc/nginx/whitelists').includes('alice.conf'),
  );

  check(
    'the refusal stays inline, not in a toast',
    (await helpers.alerts()).includes('is still included by') &&
      !(await helpers.toasts()).includes('is still included by'),
  );

  // a file the list editor cannot represent is never rewritten
  sh(
    `printf 'allow 198.51.100.1;\\ndeny 198.51.100.2;\\n' > /etc/nginx/whitelists/mixed.conf`,
  );

  await page.goto(`${BASE}/whitelists/edit?name=mixed.conf`);
  await helpers.waitText('more than a list');

  check(
    'a whitelist with other directives shows a warning instead of the list editor',
    /open the raw editor/i.test(await helpers.body()),
  );

  check(
    'the file on disk is untouched',
    sh('cat /etc/nginx/whitelists/mixed.conf').includes('deny 198.51.100.2;'),
  );

  await page.goto(`${BASE}/whitelists/raw?name=mixed.conf`);
  await helpers.waitMonaco('whitelist-raw');

  check(
    'the raw editor opens it',
    (await helpers.getEditor()).includes('deny 198.51.100.2;'),
  );

  // an unused whitelist can be deleted from the list
  await page.goto(`${BASE}/whitelists`);
  await helpers.waitText('mixed.conf');
  await page.getByLabel('Delete mixed.conf').click();
  await helpers.button('Delete').click();
  await page.waitForTimeout(1500);

  check(
    'an unused whitelist is deleted',
    !sh('ls /etc/nginx/whitelists').includes('mixed.conf'),
  );
};
