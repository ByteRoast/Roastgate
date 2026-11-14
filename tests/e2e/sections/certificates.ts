import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { sh } from '../lib/shell.ts';

import type { Context } from '../lib/context.ts';

/**
 * Certificates (against a stand-in for certbot: it cannot reach Let's Encrypt).
 */
export const certificates = async ({
  helpers,
  page,
}: Context): Promise<void> => {
  await page.goto(`${BASE}/certificates`);
  await helpers.waitText('No certificate yet');

  check(
    'the page says certificates are handled by the embedded certbot',
    /embedded certbot/i.test(await helpers.body()),
  );

  check(
    'the certificate Domain names text area can be resized',
    (await page
      .getByLabel('Domain names')
      .evaluate((element) => getComputedStyle(element).resize)) === 'vertical',
  );

  await page.getByLabel('Domain names').fill('lets.example.com');
  await helpers.button('Test first (dry run)').click();
  await helpers.waitText('The dry run succeeded');

  check(
    'a dry run succeeds and issues nothing',
    !sh('ls /etc/letsencrypt/live 2>&1').includes('lets.example.com'),
  );

  check(
    'a successful certbot run is a toast, with no certbot output left on the page',
    (await helpers.toasts()).includes('The dry run succeeded') &&
      !(await helpers.alerts()).includes('The dry run succeeded') &&
      (await page.locator('.rg-output').count()) === 0,
  );

  await helpers.button('Get the certificate').click();
  await helpers.waitText('was issued');

  check(
    'issuing creates the certificate where nginx will read it',
    sh('ls /etc/letsencrypt/live').includes('lets.example.com'),
  );

  const certificatesText: string = await helpers.body();

  check(
    'the page lists it with its domain and its expiry read from the file',
    certificatesText.includes('lets.example.com') &&
      /in (89|90) days/.test(certificatesText),
    certificatesText.slice(0, 300),
  );

  await helpers.shot('09-certificates');

  // The wizard picks it from a list and fills in the paths.
  await page.goto(`${BASE}/reverse-proxies/wizard`);
  await helpers.waitField('Forward to');
  await page.getByLabel('Domain names').fill('lets.example.com');
  await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
  await helpers.tab('HTTPS').click();
  await page.getByLabel('Serve the site over HTTPS').check();
  await page.getByLabel("Use a Let's Encrypt certificate").click();
  await page.getByRole('option', { name: /lets\.example\.com/ }).click();
  await page.waitForTimeout(500);

  check(
    'choosing a certificate fills in the certificate and key paths',
    (await page.getByLabel('Certificate (full chain)').inputValue()).endsWith(
      '/live/lets.example.com/fullchain.pem',
    ) &&
      (await page.getByLabel('Private key').inputValue()).endsWith(
        '/live/lets.example.com/privkey.pem',
      ),
  );

  await helpers.tab('Advanced').click();
  await page.getByLabel('HTTP port').fill('8094');
  await page.getByLabel('HTTPS port').fill('8444');
  await helpers.button('Save').click();
  await helpers.expectSaved();

  const servedCertificate = (): string =>
    sh(
      `bash -c 'openssl s_client -connect 127.0.0.1:8444 -servername lets.example.com </dev/null 2>/dev/null | openssl x509 -noout -subject -enddate'`,
    );
  check(
    "nginx serves the Let's Encrypt certificate that was chosen",
    servedCertificate().includes('lets.example.com'),
    servedCertificate(),
  );

  // A certificate that a config uses cannot be deleted.
  await page.goto(`${BASE}/certificates`);
  await helpers.waitText('lets.example.com.conf');

  check(
    'the list shows which config uses the certificate',
    (await helpers.body()).includes('lets.example.com.conf'),
  );

  await page.getByLabel('Delete lets.example.com').click();
  await helpers.button('Delete').click();
  await helpers.waitText('is still used by');

  check(
    'deleting a certificate that is in use is refused',
    sh('ls /etc/letsencrypt/live').includes('lets.example.com'),
  );

  check(
    'a refused certbot action stays inline, not in a toast',
    (await helpers.alerts()).includes('is still used by') &&
      !(await helpers.toasts()).includes('is still used by'),
  );

  // Let it age, then renew: Roastgate sees the new file and reloads nginx so the new certificate is served.
  sh(
    `openssl req -x509 -nodes -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -keyout /etc/letsencrypt/live/lets.example.com/privkey.pem -out /etc/letsencrypt/live/lets.example.com/fullchain.pem -subj /CN=lets.example.com -addext subjectAltName=DNS:lets.example.com -days 5`,
  );

  await page.goto(`${BASE}/certificates`);
  await helpers.waitText('in 4 days');

  check(
    'a certificate close to expiry is shown with its few remaining days',
    /in [34] days/.test(await helpers.body()),
  );

  await page.getByLabel('Renew lets.example.com now').click();
  await helpers.waitText('Renewed lets.example.com');

  check(
    'renewing replaces the certificate and reloads nginx',
    (await helpers.body()).includes('nginx was reloaded'),
  );

  check(
    'nginx now serves a certificate valid for more than 60 days',
    sh(
      `bash -c 'openssl s_client -connect 127.0.0.1:8444 -servername lets.example.com </dev/null 2>/dev/null | openssl x509 -checkend 5184000 -noout'`,
    ).includes('will not expire'),
  );

  // An unused certificate can be deleted.
  await page.getByLabel('Domain names').fill('unused.example.com');
  await helpers.button('Get the certificate').click();
  await helpers.waitText('was issued');
  await page.getByLabel('Delete unused.example.com').click();
  await helpers.button('Delete').click();
  await helpers.waitText('was deleted');

  check(
    'an unused certificate is deleted through certbot',
    !sh('ls /etc/letsencrypt/live').includes('unused.example.com'),
  );
};
