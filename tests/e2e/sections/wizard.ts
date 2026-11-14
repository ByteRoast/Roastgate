import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { httpGet, sh, writeHostFile } from '../lib/shell.ts';

import type { Context } from '../lib/context.ts';

/**
 * The wizard, against a real nginx.
 */
export const wizard = async ({ helpers, page }: Context): Promise<void> => {
  sh("sh -c 'echo hello from the static site > /var/www/html/index.html'");

  // A static site.
  await page.goto(`${BASE}/sites/wizard`);
  await helpers.waitField('Root directory');
  await page.getByLabel('Domain names').fill('static.example.com');
  await page.getByLabel('Root directory').fill('/var/www/html');
  await helpers.tab('Advanced').click();
  await page.getByLabel('HTTP port').fill('8091');
  await helpers.button('Save').click();
  await helpers.expectSaved();

  const siteFile: string = sh(
    'cat /etc/nginx/sites-available/static.example.com',
  );

  check(
    'the wizard writes a static site with its header and settings',
    siteFile.includes('# roastgate-spec:') &&
      siteFile.includes('root /var/www/html;') &&
      siteFile.includes('listen 8091;'),
    siteFile.slice(0, 300),
  );

  await page.goto(`${BASE}/sites`);
  await helpers.waitText('static.example.com');

  check('the list marks wizard sites', /wizard/i.test(await helpers.body()));

  await page.getByLabel('Enable static.example.com').click();
  await page.waitForTimeout(1500);

  check(
    'the wizard site, once enabled, is served by nginx',
    httpGet(8091, 'static.example.com').includes('200 OK'),
    httpGet(8091, 'static.example.com').slice(0, 120),
  );

  // A directory listing through the fancyindex module, which the test image installs.
  sh(
    "sh -c 'mkdir -p /var/www/files && echo listed > /var/www/files/listed-file.txt'",
  );

  await page.goto(`${BASE}/sites/wizard`);
  await helpers.waitField('Root directory');
  await page.getByLabel('Domain names').fill('files.example.com');
  await page.getByLabel('Root directory').fill('/var/www/files');
  await page.getByLabel('Browsable directory listing (fancyindex)').click();
  await page
    .getByRole('combobox', { name: 'Default sort' })
    .waitFor({ timeout: 10000 });

  await helpers.tab('Advanced').click();
  await page.getByLabel('HTTP port').fill('8097');
  await helpers.button('Save').click();
  await helpers.expectSaved();

  const filesSite: string = sh(
    'cat /etc/nginx/sites-available/files.example.com',
  );

  check(
    'the wizard writes the fancyindex directives',
    filesSite.includes('fancyindex on;') &&
      filesSite.includes('fancyindex_default_sort name;'),
    filesSite.slice(0, 400),
  );

  await page.goto(`${BASE}/sites`);
  await helpers.waitText('files.example.com');
  await page.getByLabel('Enable files.example.com').click();
  await page.waitForTimeout(1500);

  const listing: string = httpGet(8097, 'files.example.com');

  check(
    'nginx lists the directory through fancyindex',
    listing.includes('200 OK') && listing.includes('listed-file.txt'),
    listing.slice(0, 300),
  );

  await page.goto(`${BASE}/sites/wizard?name=files.example.com`);
  await helpers.waitField('Root directory');

  check(
    'reopening the site keeps the fancyindex option on',
    await page.getByRole('combobox', { name: 'Default sort' }).isVisible(),
  );

  // The image's fancyindex is Ubuntu's 0.5.2 package, which has no fancyindex_case_sensitive: the wizard asked nginx and
  // hides the option. If the BASE image changes, this build may have it and the check below must be revisited.
  check(
    'the case-sensitive sort is not offered by a fancyindex build without it',
    (await page.getByLabel('Case-sensitive sort').count()) === 0,
  );

  // The folder only exists if the probe ran for real; an error from ls (folder missing) fails the check as well.
  const probeLeftovers: string = sh('ls -A /etc/nginx/.roastgate-probe');

  check(
    'the directive probe leaves no file in the nginx root',
    probeLeftovers === '',
    probeLeftovers,
  );

  // A reverse proxy in front of it, with the preview tab.
  await page.goto(`${BASE}/reverse-proxies/wizard`);
  await helpers.waitField('Forward to');
  await page.getByLabel('Domain names').fill('proxied.example.com');
  await page.getByLabel('Forward to').fill('not a url;');
  await helpers.button('Save').click();
  await helpers.waitText('Fix these first');

  check(
    'invalid input is explained and nothing is written',
    (await helpers.body()).includes('The destination must look like') &&
      !sh('ls /etc/nginx/conf.d').includes('proxied'),
  );

  await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
  await helpers.tab('Advanced').click();
  await page.getByLabel('HTTP port').fill('8092');
  await helpers.tab('Preview').click();
  await helpers.waitText('This is exactly what will be written');

  const preview: string = await helpers.body();

  check(
    'the preview shows the generated config before anything is saved',
    preview.includes('server_name proxied.example.com;') &&
      preview.includes('proxy_pass http://127.0.0.1:8091;') &&
      preview.includes('listen 8092;'),
  );

  await helpers.shot('08-wizard-preview');

  check(
    'and the preview wrote nothing',
    !sh('ls /etc/nginx/conf.d').includes('proxied'),
  );

  await helpers.button('Save').click();
  await helpers.expectSaved();

  check(
    'the wizard proxy really forwards to the site behind it',
    httpGet(8092, 'proxied.example.com').includes('200 OK') &&
      sh('cat /etc/nginx/conf.d/proxied.example.com.conf').includes(
        '# roastgate-spec:',
      ),
  );

  // Reopening it fills the form from the file, and a change regenerates it.
  await page.goto(
    `${BASE}/reverse-proxies/wizard?name=proxied.example.com.conf`,
  );

  await helpers.waitField('Forward to');

  check(
    'reopening the wizard restores the settings',
    (await page.getByLabel('Forward to').inputValue()) ===
      'http://127.0.0.1:8091' &&
      (await page.getByLabel('Domain names').inputValue()).includes(
        'proxied.example.com',
      ),
  );

  await page.getByLabel('Timeout (seconds)').fill('90');

  // Layout: the WebSockets switch lines up with the timeout input, and the switches start where the inputs start.
  const switchY: number = await helpers.centerY(
    page.locator('.rg-switch-row .mud-switch').first(),
  );

  const timeoutY: number = await helpers.centerY(
    page.locator('.rg-switch-row .mud-input-outlined').first(),
  );

  check(
    'the WebSockets switch is vertically centred on the timeout input',
    Math.abs(switchY - timeoutY) <= 3,
    `${switchY} vs ${timeoutY}`,
  );

  await helpers.tab('Advanced').click();

  const trackX: number = (
    await helpers.box(page.locator('.rg-switch-grid .mud-switch-track').first())
  ).x;

  const portX: number = (await helpers.box(page.getByLabel('HTTP port'))).x;

  check(
    'in Advanced the switches start at the same edge as the port inputs',
    Math.abs(trackX - portX) <= 3,
    `${trackX} vs ${portX}`,
  );

  const httpsX: number = (await helpers.box(page.getByLabel('HTTPS port'))).x;
  const secondTrackX: number = (
    await helpers.box(page.locator('.rg-switch-grid .mud-switch-track').nth(1))
  ).x;

  check(
    'the second switch column lines up with the HTTPS port input',
    Math.abs(secondTrackX - httpsX) <= 3,
    `${secondTrackX} vs ${httpsX}`,
  );

  await helpers.tab('Essentials').click();

  check(
    'the Domain names text area can be resized by the user',
    (await page
      .getByLabel('Domain names')
      .evaluate((element) => getComputedStyle(element).resize)) === 'vertical',
  );

  await helpers.button('Save').click();
  await helpers.expectSaved();

  check(
    'a change in the form is regenerated into the file',
    sh('cat /etc/nginx/conf.d/proxied.example.com.conf').includes(
      'proxy_read_timeout 90s;',
    ),
  );

  // A hand edit is noticed.
  sh(`printf '# my own note\\n' >> /etc/nginx/conf.d/proxied.example.com.conf`);

  await page.goto(
    `${BASE}/reverse-proxies/wizard?name=proxied.example.com.conf`,
  );

  await helpers.waitText('edited by hand');

  check(
    'a hand edit is detected and the user is warned before it would be replaced',
    (await helpers.body()).includes('replaces those edits'),
  );

  await page.goto(`${BASE}/reverse-proxies`);
  await helpers.waitText('proxied.example.com.conf');

  check(
    'the list still opens an edited wizard file in the wizard',
    (
      (await page
        .getByLabel('Edit proxied.example.com.conf', { exact: true })
        .getAttribute('href')) ?? ''
    ).includes('/wizard'),
  );

  // A file the wizard did not create is read into the form, and the user is told that saving regenerates it.
  await page.goto(`${BASE}/reverse-proxies/wizard?name=app.example.com.conf`);
  await helpers.waitText('not created by the wizard');

  check(
    'a hand-made file is imported and the user is told saving regenerates it',
    (await helpers.body()).includes('read from the existing config') &&
      (await helpers.body()).includes('regenerates the whole file'),
  );

  check(
    'the imported form shows what the file said',
    (await page.getByLabel('Forward to').inputValue()) ===
      'http://127.0.0.1:3000',
  );

  // A richer hand-made file: what the form has no field for must survive a save through the wizard, checked by the real nginx.
  writeHostFile(
    '/etc/nginx/conf.d/legacy.example.com.conf',
    `server {
      listen 8095;
      server_name legacy.example.com;
      gzip on;
      location /health {
          return 200 "ok";
      }
      location / {
          proxy_pass http://127.0.0.1:9000;
          proxy_buffering off;
      }
  }
  `,
  );

  await helpers.openEditor(
    '/reverse-proxies/edit?name=legacy.example.com.conf',
  );

  await page.getByTestId('import-link').click();
  await helpers.waitText('read from the existing config');

  check(
    'the import fills the fields of a richer file',
    (await page.getByLabel('Forward to').inputValue()) ===
      'http://127.0.0.1:9000',
  );

  await helpers.button('Save').click();
  await helpers.expectSaved();

  const legacyFile: string = sh(
    'cat /etc/nginx/conf.d/legacy.example.com.conf',
  );

  check(
    'free directives survive a save through the wizard and the real nginx accepts the result',
    legacyFile.includes('proxy_buffering off;') &&
      legacyFile.includes('return 200 "ok";') &&
      legacyFile.includes('gzip on;') &&
      legacyFile.includes('# roastgate-spec:'),
    legacyFile.slice(0, 300),
  );

  const legacyAnswer: string = httpGet(8095, 'legacy.example.com', '/health');

  check(
    'the imported site still answers on its own port',
    legacyAnswer.includes('200') && legacyAnswer.endsWith('ok'),
    legacyAnswer.slice(0, 200),
  );

  // A file the wizard could not reproduce faithfully is refused with the reason, and nothing is rewritten unless the user insists.
  writeHostFile(
    '/etc/nginx/conf.d/blocked.example.com.conf',
    `upstream backend {
      server 127.0.0.1:9100;
  }
  server {
      listen 8096;
      server_name blocked.example.com;
      location / {
          proxy_pass http://backend;
      }
  }
  `,
  );

  await page.goto(
    `${BASE}/reverse-proxies/wizard?name=blocked.example.com.conf`,
  );

  await helpers.waitText('cannot be imported without changing what it does');

  check(
    'a file with an upstream block is refused with the reason',
    (await helpers.body()).includes('outside a server block would be lost') &&
      (await helpers.body()).includes('replaces the whole file'),
  );

  // HTTPS with a real certificate, a redirect and a whitelist.
  sh(
    'openssl req -x509 -nodes -newkey rsa:2048 -keyout /etc/ssl/private/rg-e2e.key -out /etc/ssl/certs/rg-e2e.crt -subj /CN=secure.example.com -days 2',
  );

  await page.goto(`${BASE}/reverse-proxies/wizard`);
  await helpers.waitField('Forward to');
  await page.getByLabel('Domain names').fill('secure.example.com');
  await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
  await helpers.tab('HTTPS').click();
  await page.getByLabel('Serve the site over HTTPS').check();
  await page
    .getByLabel('Certificate (full chain)')
    .fill('/etc/ssl/certs/rg-e2e.crt');

  await page.getByLabel('Private key').fill('/etc/ssl/private/rg-e2e.key');
  await helpers.tab('Access').click();
  await page.getByLabel('alice.conf').check();
  await helpers.tab('Advanced').click();
  await page.getByLabel('HTTP port').fill('8093');
  await page.getByLabel('HTTPS port').fill('8443');
  await helpers.button('Save').click();
  await helpers.expectSaved();
  const secureFile = sh('cat /etc/nginx/conf.d/secure.example.com.conf');

  check(
    'the HTTPS config is accepted by the real nginx -t and reloaded',
    (await helpers.body()).includes('nginx reloaded.') &&
      secureFile.includes('ssl_certificate /etc/ssl/certs/rg-e2e.crt;') &&
      secureFile.includes('include /etc/nginx/whitelists/alice.conf;'),
    secureFile.slice(0, 200),
  );

  const redirect: string = httpGet(8093, 'secure.example.com');

  check(
    'plain HTTP is redirected to HTTPS on the chosen port',
    redirect.includes('301') &&
      redirect.includes('Location: https://secure.example.com:8443/'),
    redirect.slice(0, 200),
  );

  const secure: string = sh(
    `bash -c 'printf "GET / HTTP/1.0\\r\\nHost: secure.example.com\\r\\n\\r\\n" | openssl s_client -quiet -connect 127.0.0.1:8443 -servername secure.example.com 2>/dev/null | head -1'`,
  );

  check(
    'HTTPS is really served with the certificate, behind the whitelist (127.0.0.1 is whitelisted)',
    secure.includes('200 OK'),
    secure,
  );
};
