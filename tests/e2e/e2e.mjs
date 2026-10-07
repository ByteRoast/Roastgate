// End to end scenario: the real setup wizard, then every config feature against a REAL nginx running in the test container.
// See README.md in this folder. Needs Docker, Node 20+ and an installed browser (Edge by default, see E2E_CHANNEL).
import { chromium } from 'playwright-core';
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import crypto from 'node:crypto';

// RFC 6238 (HMAC-SHA1, 30 s steps, 6 digits), written here independently of the server, as an authenticator app would compute it.
const base32Decode = (text) => {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const character of text.replace(/[\s=-]/g, '').toUpperCase()) bits += alphabet.indexOf(character).toString(2).padStart(5, '0');
  const bytes = [];
  for (let index = 0; index + 8 <= bits.length; index += 8) bytes.push(parseInt(bits.slice(index, index + 8), 2));
  return Buffer.from(bytes);
};
const totp = (secret, stepOffset = 0) => {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000) + stepOffset));
  const hash = crypto.createHmac('sha1', base32Decode(secret)).update(counter).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  const binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
  return String(binary % 1000000).padStart(6, '0');
};

const out = process.env.E2E_OUT ?? './out';
const shots = `${out}/screenshots`;
fs.rmSync(shots, { recursive: true, force: true });
fs.mkdirSync(shots, { recursive: true });

const container = process.env.E2E_CONTAINER ?? 'roastgate-e2e';
const base = process.env.E2E_BASE ?? 'http://127.0.0.1:8199';
const sh = (command) => {
  try { return execSync(`docker exec ${container} sh -c ${JSON.stringify(command)}`, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim(); }
  catch (e) { return `ERR ${e.status}: ${(e.stderr || e.stdout || '').toString().trim()}`; }
};
const logs = execSync(`docker logs ${container} 2>&1`, { encoding: 'utf8' });
const token = /setup token: (\w+)/.exec(logs)?.[1];

const results = [];
const problems = [];
const check = (name, ok, detail = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${!ok && detail ? '\n        ' + detail : ''}`); };

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 1300, height: 950 } });
page.on('pageerror', (e) => problems.push('pageerror: ' + e.message));
const trace = [];
const t0 = Date.now();
page.on('console', (m) => { trace.push(`+${Date.now() - t0}ms console.${m.type()}: ${m.text().slice(0, 200)}`); if (m.type() === 'error') problems.push('console: ' + m.text()); });
page.on('websocket', (ws) => { trace.push(`+${Date.now() - t0}ms websocket open ${ws.url()}`); ws.on('close', () => trace.push(`+${Date.now() - t0}ms websocket CLOSED`)); });
page.on('framenavigated', (f) => { if (f === page.mainFrame()) trace.push(`+${Date.now() - t0}ms navigated ${f.url()}`); });
page.on('requestfailed', (r) => trace.push(`+${Date.now() - t0}ms request failed ${r.url()} ${r.failure()?.errorText}`));

const shot = (name) => page.screenshot({ path: `${shots}/${name}.png`, fullPage: true });
const body = async () => page.locator('body').innerText();
// After the text shows up, give the Blazor circuit a moment to attach, the way a person reading the page would.
// (A click within ~50 ms of the WebSocket opening can leave Blazor's URL and page out of step: a framework race.)
const waitText = async (t, timeout = 10000) => {
  await page.getByText(t, { exact: false }).first().waitFor({ timeout });
  await page.waitForTimeout(700);
};
const button = (name) => page.getByRole('button', { name, exact: true });


const waitMonaco = async (label) => {
  try {
    await page.waitForFunction(() => window.monaco && window.monaco.editor.getModels().length > 0, null, { timeout: 40000 });
  } catch (error) {
    const state = await page.evaluate(() => ({
      url: location.href, monaco: typeof window.monaco, models: window.monaco?.editor ? window.monaco.editor.getModels().length : null,
      editorDom: document.querySelectorAll('.monaco-editor').length, containers: document.querySelectorAll('.rg-editor').length,
      text: document.body.innerText.replace(/\s+/g, ' ').slice(0, 300),
    }));
    console.log(`MONACO FAILED at ${label}:`, JSON.stringify(state));
    console.log('TRACE (last 25):\n' + trace.slice(-25).join('\n'));
    console.log('browser problems so far:', [...new Set(problems)].join(' | '));
    await page.screenshot({ path: `${shots}/failure-${label.replace(/[^a-z0-9]+/gi, '_')}.png`, fullPage: true });
    throw error;
  }
};

const setEditor = async (text) => {
  await waitMonaco('setEditor');
  await page.evaluate((value) => window.monaco.editor.getModels()[0].setValue(value), text);
};
const getEditor = async () => page.evaluate(() => window.monaco.editor.getModels()[0].getValue());
const openEditor = async (url) => {
  await page.goto(`${base}${url}`);
  await waitMonaco('openEditor ' + url);
  await page.waitForTimeout(800); // language and theme are applied once the editor has initialised
};

// ---- setup, through the real wizard ----
await page.goto(`${base}/setup`);
await waitText('Step 1 of 5');
await page.waitForTimeout(1500);
await page.getByLabel('Setup token').fill(token);
await button('Continue').click();
await waitText('Step 2 of 5');
await page.getByLabel('Password', { exact: true }).fill('Correct-Horse-9');
await page.getByLabel('Repeat the password').fill('Correct-Horse-9');
await button('Continue').click();
await waitText('Step 3 of 5');
await button('Continue').click();
await waitText('Step 4 of 5', 20000);
await page.getByText('host access').first().waitFor({ timeout: 25000 });
const diagnostics = await body();
check('diagnostics see the real nginx and its directories', diagnostics.includes('nginx 1.24') && diagnostics.includes('is visible to Roastgate'));
await shot('00-diagnostics');
await button('Continue').click();
await waitText('Step 5 of 5');
await button('Finish setup').click();
await page.waitForURL(`${base}/`, { timeout: 20000 });
await waitText('Dashboard');
check('setup finished and signed in', true);

// ---- dashboard ----
await waitText('nginx 1.24');
await button('Test configuration').click();
await waitText('The configuration is valid.');
check('dashboard: Test configuration runs the real nginx -t', true);
await button('Reload nginx').click();
await waitText('nginx reloaded.');
check('dashboard: Reload nginx reloads the real nginx', true);

// ---- sites: the existing default site (absolute symlink) ----
await page.goto(`${base}/sites`);
await waitText('default');
const defaultSwitch = page.getByLabel('Enable default');
check('an existing site enabled by an ABSOLUTE symlink shows as enabled', await defaultSwitch.isChecked());
await shot('01-sites-existing');

// ---- reverse proxy: create ----
await page.goto(`${base}/reverse-proxies`);
await waitText('No reverse proxy yet');
await page.getByRole('link', { name: 'New from raw text' }).click();
await page.waitForURL(/\/reverse-proxies\/edit$/);
await waitMonaco('new-proxy');
await page.waitForTimeout(800);
const goodProxy = 'server {\n    listen 8080;\n    server_name app.example.com;\n    location / {\n        proxy_pass http://127.0.0.1:3000;\n    }\n}';
await page.getByLabel('File name').fill('app.example.com');
await setEditor(goodProxy);
await shot('02-editor-new');
await button('Save').click();
await waitText('Saved.');
check('creating a reverse proxy saves, validates and reloads', (await body()).includes('nginx reloaded.'));
check('the file really exists on the nginx host', sh('cat /etc/nginx/conf.d/app.example.com.conf').includes('proxy_pass http://127.0.0.1:3000;'));
check('the new proxy is really listening after the reload', sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open');
await shot('03-editor-saved');

// ---- reverse proxy: a change nginx refuses ----
await openEditor('/reverse-proxies/edit?name=app.example.com.conf');
check('the editor opens the saved content', (await getEditor()).includes('proxy_pass http://127.0.0.1:3000;'));
await setEditor('server {\n    listen 8080;\n    bogus_directive on;\n}');
await button('Save').click();
await waitText('undone');
const refused = await body();
check('a refused change shows the real nginx error and says it was undone', refused.includes('unknown directive "bogus_directive"') && refused.includes('undone'));
check('the file on the host is unchanged after the refusal', sh('cat /etc/nginx/conf.d/app.example.com.conf').includes('proxy_pass http://127.0.0.1:3000;') && !sh('cat /etc/nginx/conf.d/app.example.com.conf').includes('bogus'));
check('nginx still serves the old configuration', sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open');
await shot('04-refused');

// ---- reverse proxy: disable and enable ----
await page.goto(`${base}/reverse-proxies`);
await waitText('app.example.com.conf');
await page.getByLabel('Enable app.example.com.conf').click();
await page.waitForTimeout(1500);
check('disabling renames the file to .conf.disabled', sh('ls /etc/nginx/conf.d').includes('app.example.com.conf.disabled') && !sh('ls /etc/nginx/conf.d').split('\n').includes('app.example.com.conf'));
check('a disabled proxy no longer listens', sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`).startsWith('ERR'));
await page.getByLabel('Enable app.example.com.conf').click();
await page.waitForTimeout(1500);
check('enabling renames it back and nginx listens again', sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && echo open'`) === 'open');

// ---- site: create, enable (symlink), serve, delete ----
await page.goto(`${base}/sites/edit`);
await page.waitForFunction(() => window.monaco && window.monaco.editor.getModels().length > 0, null, { timeout: 15000 });
await page.waitForTimeout(800);
await page.getByLabel('File name').fill('example.com');
await setEditor('server {\n    listen 8081;\n    location / { return 200 "hello from roastgate\\n"; }\n}');
await button('Save').click();
await waitText('Saved.');
check('a new site is saved but not enabled yet', sh('ls /etc/nginx/sites-available').includes('example.com') && !sh('ls /etc/nginx/sites-enabled').includes('example.com'));
await button('Enable').click();
await page.waitForTimeout(1500);
const link = sh('readlink /etc/nginx/sites-enabled/example.com');
check('enabling a site creates a relative symlink', link === '../sites-available/example.com', link);
const served = sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/8081; printf "GET / HTTP/1.0\\r\\n\\r\\n" >&3; cat <&3'`);
check('the enabled site is really served by nginx', served.includes('hello from roastgate'), served);
await page.goto(`${base}/sites`);
await waitText('example.com');
await page.getByLabel('Delete example.com').click();
await button('Delete').click();
await page.waitForTimeout(1500);
check('deleting a site removes the file AND its symlink', !sh('ls /etc/nginx/sites-available').includes('example.com') && !sh('ls /etc/nginx/sites-enabled').includes('example.com'));
check('the default site was not touched', sh('readlink /etc/nginx/sites-enabled/default') === '/etc/nginx/sites-available/default');

// ---- whitelists: the list editor, then a real nginx enforcing it ----
const requestFromLocalhost = (port) => sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/${port}; printf "GET / HTTP/1.0\\r\\n\\r\\n" >&3; head -1 <&3'`);
await page.goto(`${base}/whitelists/edit`);
await waitText('Allowed addresses');
await page.getByLabel('Name', { exact: true }).fill('alice');
await page.getByLabel('Address', { exact: true }).first().fill('203.0.113.7');
await page.getByLabel('Comment').first().fill('alice at home');
await button('Add address').click();
await page.getByLabel('Address', { exact: true }).nth(1).fill('999.1.1.1');
await page.waitForTimeout(500);
check('an invalid address is flagged on its own row', (await body()).includes('is not a valid IPv4 or IPv6 address'));
check('saving is blocked while an address is invalid', await button('Save').isDisabled());
await page.getByLabel('Address', { exact: true }).nth(1).fill('10.0.0.5/24');
await page.waitForTimeout(500);
check('host bits in a CIDR range are a warning that suggests the network, not an error', (await body()).includes('did you mean 10.0.0.0/24') && !(await button('Save').isDisabled()));
await page.getByLabel('Address', { exact: true }).nth(1).fill('');
await page.getByPlaceholder(/One per line/).fill('10.0.0.0/8, 192.168.1.0/24\nallow 2001:db8::/32;');
await button('Add them to the list').click();
await page.waitForTimeout(500);
check('pasting several addresses adds one row each', (await page.getByLabel('Address', { exact: true }).count()) >= 4);
check('the "add my address" shortcut shows what Roastgate sees', /add my address \(/i.test(await body()));
await shot('06-whitelist-editor');
await button('Save').click();
await waitText('Saved.');
const saved = sh('cat /etc/nginx/whitelists/alice.conf');
check('the whitelist is written as clean allow lines', saved.includes('allow 203.0.113.7; # alice at home') && saved.includes('allow 10.0.0.0/8;') && saved.includes('allow 2001:db8::/32;') && !saved.includes('999'), saved);
check('the page shows the include line to copy', (await body()).includes('include /etc/nginx/whitelists/alice.conf;'));
check('a whitelist nothing includes says so', (await body()).includes('Nothing includes this whitelist yet'));

// A proxy that includes it. It serves a static file on purpose: nginx runs "return" before allow/deny, which would hide the whitelist.
await page.goto(`${base}/reverse-proxies/edit`);
await waitMonaco('private-proxy');
await page.getByLabel('File name').fill('private.example.com');
await setEditor('server {\n    listen 8082;\n    include /etc/nginx/whitelists/alice.conf;\n    deny all;\n    location / { root /var/www/html; index index.nginx-debian.html; }\n}');
await button('Save').click();
await waitText('Saved.');
check('nginx really DENIES an address that is not in the whitelist', requestFromLocalhost(8082).includes('403'), requestFromLocalhost(8082));

await page.goto(`${base}/whitelists`);
await waitText('alice.conf');
const listText = await body();
check('the list shows how many addresses and who uses the whitelist', listText.includes('private.example.com.conf') && /alice\.conf\s+\d/.test(listText));
await shot('07-whitelist-list');

await page.goto(`${base}/whitelists/edit?name=alice.conf`);
await waitText('Used by');
check('the editor lists the including file and links to it', (await body()).includes('private.example.com.conf (line 3)'));
await button('Add address').click();
await page.getByLabel('Address', { exact: true }).last().fill('127.0.0.1');
await button('Save').click();
await waitText('Saved.');
check('after adding 127.0.0.1 nginx really ALLOWS it', requestFromLocalhost(8082).includes('200'), requestFromLocalhost(8082));

// deletion is blocked while the whitelist is in use
await page.goto(`${base}/whitelists`);
await waitText('alice.conf');
await page.getByLabel('Delete alice.conf').click();
await button('Delete').click();
await waitText('is still included by');
check('deleting a whitelist that is in use is refused and names the file', (await body()).includes('private.example.com.conf') && sh('ls /etc/nginx/whitelists').includes('alice.conf'));

// a file the list editor cannot represent is never rewritten
sh(`printf 'allow 198.51.100.1;\\ndeny 198.51.100.2;\\n' > /etc/nginx/whitelists/mixed.conf`);
await page.goto(`${base}/whitelists/edit?name=mixed.conf`);
await waitText('more than a list');
check('a whitelist with other directives shows a warning instead of the list editor', /open the raw editor/i.test(await body()));
check('the file on disk is untouched', sh('cat /etc/nginx/whitelists/mixed.conf').includes('deny 198.51.100.2;'));
await page.goto(`${base}/whitelists/raw?name=mixed.conf`);
await waitMonaco('whitelist-raw');
check('the raw editor opens it', (await getEditor()).includes('deny 198.51.100.2;'));

// an unused whitelist can be deleted from the list
await page.goto(`${base}/whitelists`);
await waitText('mixed.conf');
await page.getByLabel('Delete mixed.conf').click();
await button('Delete').click();
await page.waitForTimeout(1500);
check('an unused whitelist is deleted', !sh('ls /etc/nginx/whitelists').includes('mixed.conf'));

// ---- the wizard, against a real nginx ----
const httpGet = (port, host) => sh(`bash -c 'exec 3<>/dev/tcp/127.0.0.1/${port}; printf "GET / HTTP/1.0\\r\\nHost: ${host}\\r\\n\\r\\n" >&3; cat <&3'`);
const tab = (name) => page.getByRole('tab', { name, exact: true });
// Waits for the "Saved." message, and when the save failed shows why instead of just timing out.
const expectSaved = async () => {
  try {
    await page.getByText('Saved.', { exact: false }).first().waitFor({ timeout: 10000 });
  } catch (error) {
    const alert = await page.locator('.mud-alert').allInnerTexts();
    throw new Error('The save did not succeed. Alerts on the page: ' + JSON.stringify(alert));
  }
  await page.waitForTimeout(500);
};

// Waits for a form field by its label (the text of an outlined field also exists in a hidden legend).
const waitField = async (label) => {
  await page.getByLabel(label, { exact: true }).waitFor({ timeout: 10000 });
  await page.waitForTimeout(700);
};

sh("sh -c 'echo hello from the static site > /var/www/html/index.html'");
// A static site.
await page.goto(`${base}/sites/wizard`);
await waitField('Root directory');
await page.getByLabel('Domain names').fill('static.example.com');
await page.getByLabel('Root directory').fill('/var/www/html');
await tab('Advanced').click();
await page.getByLabel('HTTP port').fill('8091');
await button('Save').click();
await expectSaved();
const siteFile = sh('cat /etc/nginx/sites-available/static.example.com');
check('the wizard writes a static site with its header and settings', siteFile.includes('# roastgate-spec:') && siteFile.includes('root /var/www/html;') && siteFile.includes('listen 8091;'), siteFile.slice(0, 300));
await page.goto(`${base}/sites`);
await waitText('static.example.com');
check('the list marks wizard sites', /wizard/i.test(await body()));
await page.getByLabel('Enable static.example.com').click();
await page.waitForTimeout(1500);
check('the wizard site, once enabled, is served by nginx', httpGet(8091, 'static.example.com').includes('200 OK'), httpGet(8091, 'static.example.com').slice(0, 120));

// A reverse proxy in front of it, with the preview tab.
await page.goto(`${base}/reverse-proxies/wizard`);
await waitField('Forward to');
await page.getByLabel('Domain names').fill('proxied.example.com');
await page.getByLabel('Forward to').fill('not a url;');
await button('Save').click();
await waitText('Fix these first');
check('invalid input is explained and nothing is written', (await body()).includes('The destination must look like') && !sh('ls /etc/nginx/conf.d').includes('proxied'));
await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
await tab('Advanced').click();
await page.getByLabel('HTTP port').fill('8092');
await tab('Preview').click();
await waitText('This is exactly what will be written');
const preview = await body();
check('the preview shows the generated config before anything is saved', preview.includes('server_name proxied.example.com;') && preview.includes('proxy_pass http://127.0.0.1:8091;') && preview.includes('listen 8092;'));
await shot('08-wizard-preview');
check('and the preview wrote nothing', !sh('ls /etc/nginx/conf.d').includes('proxied'));
await button('Save').click();
await expectSaved();
check('the wizard proxy really forwards to the site behind it', httpGet(8092, 'proxied.example.com').includes('200 OK') && sh('cat /etc/nginx/conf.d/proxied.example.com.conf').includes('# roastgate-spec:'));

// Reopening it fills the form from the file, and a change regenerates it.
await page.goto(`${base}/reverse-proxies/wizard?name=proxied.example.com.conf`);
await waitField('Forward to');
check('reopening the wizard restores the settings', (await page.getByLabel('Forward to').inputValue()) === 'http://127.0.0.1:8091' && (await page.getByLabel('Domain names').inputValue()).includes('proxied.example.com'));
await page.getByLabel('Timeout (seconds)').fill('90');
await button('Save').click();
await expectSaved();
check('a change in the form is regenerated into the file', sh('cat /etc/nginx/conf.d/proxied.example.com.conf').includes('proxy_read_timeout 90s;'));

// A hand edit is noticed.
sh(`printf '# my own note\\n' >> /etc/nginx/conf.d/proxied.example.com.conf`);
await page.goto(`${base}/reverse-proxies/wizard?name=proxied.example.com.conf`);
await waitText('edited by hand');
check('a hand edit is detected and the user is warned before it would be replaced', (await body()).includes('replaces those edits'));
await page.goto(`${base}/reverse-proxies`);
await waitText('proxied.example.com.conf');
check('the list still opens an edited wizard file in the wizard', (await page.getByLabel('Edit proxied.example.com.conf', { exact: true }).getAttribute('href')).includes('/wizard'));

// A file the wizard did not create gets an explicit warning instead of being silently replaced.
await page.goto(`${base}/reverse-proxies/wizard?name=app.example.com.conf`);
await waitText('not created by the wizard');
check('a hand-made file is not silently replaced by the wizard', (await body()).includes('replaces the whole file'));

// HTTPS with a real certificate, a redirect and a whitelist.
sh('openssl req -x509 -nodes -newkey rsa:2048 -keyout /etc/ssl/private/rg-e2e.key -out /etc/ssl/certs/rg-e2e.crt -subj /CN=secure.example.com -days 2');
await page.goto(`${base}/reverse-proxies/wizard`);
await waitField('Forward to');
await page.getByLabel('Domain names').fill('secure.example.com');
await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
await tab('HTTPS').click();
await page.getByLabel('Serve the site over HTTPS').check();
await page.getByLabel('Certificate (full chain)').fill('/etc/ssl/certs/rg-e2e.crt');
await page.getByLabel('Private key').fill('/etc/ssl/private/rg-e2e.key');
await tab('Access').click();
await page.getByLabel('alice.conf').check();
await tab('Advanced').click();
await page.getByLabel('HTTP port').fill('8093');
await page.getByLabel('HTTPS port').fill('8443');
await button('Save').click();
await expectSaved();
const secureFile = sh('cat /etc/nginx/conf.d/secure.example.com.conf');
check('the HTTPS config is accepted by the real nginx -t and reloaded', (await body()).includes('nginx reloaded.') && secureFile.includes('ssl_certificate /etc/ssl/certs/rg-e2e.crt;') && secureFile.includes('include /etc/nginx/whitelists/alice.conf;'), secureFile.slice(0, 200));
const redirect = httpGet(8093, 'secure.example.com');
check('plain HTTP is redirected to HTTPS on the chosen port', redirect.includes('301') && redirect.includes('Location: https://secure.example.com:8443/'), redirect.slice(0, 200));
const secure = sh(`bash -c 'printf "GET / HTTP/1.0\\r\\nHost: secure.example.com\\r\\n\\r\\n" | openssl s_client -quiet -connect 127.0.0.1:8443 -servername secure.example.com 2>/dev/null | head -1'`);
check('HTTPS is really served with the certificate, behind the whitelist (127.0.0.1 is whitelisted)', secure.includes('200 OK'), secure);

// ---- certificates (against a stand-in for certbot: it cannot reach Let's Encrypt) ----
await page.goto(`${base}/certificates`);
await waitText('No certificate yet');
check('the page says certificates are handled by the embedded certbot', /embedded certbot/i.test(await body()));
await page.getByLabel('Domain names').fill('lets.example.com');
await button('Test first (dry run)').click();
await waitText('The dry run succeeded');
check('a dry run succeeds and issues nothing', !sh('ls /etc/letsencrypt/live 2>&1').includes('lets.example.com'));
await button('Get the certificate').click();
await waitText('was issued');
check('issuing creates the certificate where nginx will read it', sh('ls /etc/letsencrypt/live').includes('lets.example.com'));
const certificatesText = await body();
check('the page lists it with its domain and its expiry read from the file', certificatesText.includes('lets.example.com') && /in (89|90) days/.test(certificatesText), certificatesText.slice(0, 300));
await shot('09-certificates');

// The wizard picks it from a list and fills in the paths.
await page.goto(`${base}/reverse-proxies/wizard`);
await waitField('Forward to');
await page.getByLabel('Domain names').fill('lets.example.com');
await page.getByLabel('Forward to').fill('http://127.0.0.1:8091');
await tab('HTTPS').click();
await page.getByLabel('Serve the site over HTTPS').check();
await page.getByLabel("Use a Let's Encrypt certificate").click();
await page.getByRole('option', { name: /lets\.example\.com/ }).click();
await page.waitForTimeout(500);
check('choosing a certificate fills in the certificate and key paths', (await page.getByLabel('Certificate (full chain)').inputValue()).endsWith('/live/lets.example.com/fullchain.pem') && (await page.getByLabel('Private key').inputValue()).endsWith('/live/lets.example.com/privkey.pem'));
await tab('Advanced').click();
await page.getByLabel('HTTP port').fill('8094');
await page.getByLabel('HTTPS port').fill('8444');
await button('Save').click();
await expectSaved();
const servedCertificate = () => sh(`bash -c 'openssl s_client -connect 127.0.0.1:8444 -servername lets.example.com </dev/null 2>/dev/null | openssl x509 -noout -subject -enddate'`);
check('nginx serves the Let\'s Encrypt certificate that was chosen', servedCertificate().includes('lets.example.com'), servedCertificate());

// A certificate that a config uses cannot be deleted.
await page.goto(`${base}/certificates`);
await waitText('lets.example.com.conf');
check('the list shows which config uses the certificate', (await body()).includes('lets.example.com.conf'));
await page.getByLabel('Delete lets.example.com').click();
await button('Delete').click();
await waitText('is still used by');
check('deleting a certificate that is in use is refused', sh('ls /etc/letsencrypt/live').includes('lets.example.com'));

// Let it age, then renew: Roastgate sees the new file and reloads nginx so the new certificate is served.
sh(`openssl req -x509 -nodes -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -keyout /etc/letsencrypt/live/lets.example.com/privkey.pem -out /etc/letsencrypt/live/lets.example.com/fullchain.pem -subj /CN=lets.example.com -addext subjectAltName=DNS:lets.example.com -days 5`);
await page.goto(`${base}/certificates`);
await waitText('in 4 days');
check('a certificate close to expiry is shown with its few remaining days', /in [34] days/.test(await body()));
await page.getByLabel('Renew lets.example.com now').click();
await waitText('Renewed lets.example.com');
check('renewing replaces the certificate and reloads nginx', (await body()).includes('nginx was reloaded'));
check('nginx now serves a certificate valid for more than 60 days', sh(`bash -c 'openssl s_client -connect 127.0.0.1:8444 -servername lets.example.com </dev/null 2>/dev/null | openssl x509 -checkend 5184000 -noout'`).includes('will not expire'));

// An unused certificate can be deleted.
await page.getByLabel('Domain names').fill('unused.example.com');
await button('Get the certificate').click();
await waitText('was issued');
await page.getByLabel('Delete unused.example.com').click();
await button('Delete').click();
await waitText('was deleted');
check('an unused certificate is deleted through certbot', !sh('ls /etc/letsencrypt/live').includes('unused.example.com'));

// ---- backups: compare and restore ----
// The refused change earlier left no backup behind; a successful second edit creates the first one.
check('a refused change left no backup', sh('ls /etc/nginx/conf.d').includes('app.example.com.conf'));
await openEditor('/reverse-proxies/edit?name=app.example.com.conf');
await setEditor((await getEditor()) + '\n# second version');
await button('Save').click();
await expectSaved();
await page.goto(`${base}/backups?area=ReverseProxies&name=app.example.com.conf`);
await waitText('Taken before');
check('the backups of a file are listed', /a change/i.test(await body()));
await button('Compare').first().click();
await waitText('Restoring');
const comparison = await body();
check('comparing shows what restoring would add and remove', /adds \d+ line/.test(comparison) && /removes \d+ line/.test(comparison), comparison.slice(0, 300));
check('the differences are drawn line by line', (await page.locator('.rg-diff-added, .rg-diff-removed').count()) > 0);
await shot('10-backups');
const beforeRestore = sh('cat /etc/nginx/conf.d/app.example.com.conf');
await button('Restore this version').click();
await button('Restore').click();
await waitText('Restored.');
check('restoring puts the old version back and reloads nginx', (await body()).includes('nginx reloaded.') && sh('cat /etc/nginx/conf.d/app.example.com.conf') !== beforeRestore);

// A deleted site comes back (it was deleted earlier in the scenario).
await page.goto(`${base}/backups?area=Sites&name=example.com`);
await waitText('a deletion');
await button('Compare').first().click();
await waitText('does not exist any more');
check('a deleted file says restoring creates it again', (await body()).includes('restoring creates it again'));
await button('Restore this version').click();
await button('Restore').click();
await waitText('Restored.');
check('the deleted site is back in sites-available', sh('ls /etc/nginx/sites-available').includes('example.com'));

// ---- settings ----
await page.goto(`${base}/settings`);
await waitField('nginx directory');
await page.getByLabel('nginx directory').fill('relative/path');
await button('Save settings').click();
await waitText('must be an absolute path');
check('an unsafe path is refused with an explanation', true);
await page.getByLabel('nginx directory').fill('/etc/nginx');
await page.getByLabel("Let's Encrypt e-mail").fill('ops@example.com');
await button('Save settings').click();
await waitText('Settings saved.');
await page.goto(`${base}/certificates`);
await waitField('Domain names');
check('saved settings are used elsewhere (the e-mail is pre-filled when getting a certificate)', (await page.getByLabel('E-mail for expiry notices').inputValue()) === 'ops@example.com');
await page.goto(`${base}/settings`);
await waitField('nginx directory');
await button('Check the host with these values').click();
await waitText('host access');
check('the host can be checked from the settings page', /nginx/.test(await body()) && /Let's Encrypt directory/.test(await body()));
await shot('11-settings');

// Another browser, signed in with the current password.
const context2 = await browser.newContext({ viewport: { width: 1300, height: 900 } });
const page2 = await context2.newPage();
await page2.goto(`${base}/login`);
await page2.getByLabel('Username').fill('admin');
await page2.getByLabel('Password').fill('Correct-Horse-9');
await page2.getByRole('button', { name: 'Sign in' }).click();
await page2.waitForURL(`${base}/`);

await page.getByLabel('Current password').fill('definitely-not-it-1!');
await page.getByLabel('New password', { exact: true }).fill('A-brand-new-Passw0rd');
await page.getByLabel('Repeat the new password').fill('A-brand-new-Passw0rd');
await button('Change the password').click();
await waitText('current password is not correct');
check('a wrong current password is refused', true);

await page.getByLabel('Current password').fill('Correct-Horse-9');
await button('Change the password').click();
await page.waitForURL(/\/login\?notice=password-changed/, { timeout: 15000 });
check('changing the password sends the user to the sign-in page with an explanation', (await body()).includes('Your password was changed'));
await page2.goto(`${base}/`);
check('the other browser, still open, is signed out as well', page2.url().includes('/login'), page2.url());
await context2.close();

await page.getByLabel('Username').fill('admin');
await page.getByLabel('Password').fill('Correct-Horse-9');
await page.getByRole('button', { name: 'Sign in' }).click();
await waitText('Invalid username or password.');
check('the old password no longer works', true);
await page.getByLabel('Password').fill('A-brand-new-Passw0rd');
await page.getByRole('button', { name: 'Sign in' }).click();
await page.waitForURL(`${base}/`, { timeout: 10000 });
check('the new password works', true);

// ---- two-factor authentication ----
const NEW_PASSWORD = 'A-brand-new-Passw0rd';
const typePasswordStep = async (p, password) => {
  await p.getByLabel('Username').fill('admin');
  await p.getByLabel('Password', { exact: true }).fill(password);
  await p.getByRole('button', { name: 'Sign in' }).click();
};
const newBrowserAtLogin = async () => {
  const context = await browser.newContext({ viewport: { width: 1300, height: 900 } });
  const fresh = await context.newPage();
  await fresh.goto(`${base}/login`);
  await fresh.getByLabel('Username').waitFor();
  await fresh.waitForTimeout(700);
  return { context, fresh };
};
const enableTwoFactor = async () => {
  await page.goto(`${base}/settings`);
  await waitText('Two-factor authentication');
  await button('Set up two-factor authentication').click();
  await page.locator('.rg-qr svg').waitFor();
  const secret = (await page.locator('.rg-secret').innerText()).replace(/\s+/g, '');
  await page.getByLabel('Code from the app').fill(totp(secret));
  await button('Turn on two-factor authentication').click();
  await waitText('Save these recovery codes now');
  const recovery = (await page.locator('pre.rg-recovery-codes').innerText()).trim().split(/\s+/);
  return { secret, recovery };
};

await page.goto(`${base}/settings`);
await waitText('Two-factor authentication');
await button('Set up two-factor authentication').click();
await page.locator('.rg-qr svg').waitFor();
check('a QR code is shown to scan', (await page.locator('.rg-qr svg').count()) === 1);
await shot('12-two-factor-setup');
const secret = (await page.locator('.rg-secret').innerText()).replace(/\s+/g, '');
check('the secret is shown too, for apps that cannot scan', /^[A-Z2-7]{32}$/.test(secret), secret);
await page.getByLabel('Code from the app').fill('000000');
await button('Turn on two-factor authentication').click();
await waitText('That code is not valid');
check('a wrong code does not turn it on', (await body()).includes('Set up two-factor') === false);
await page.getByLabel('Code from the app').fill(totp(secret));
await button('Turn on two-factor authentication').click();
await waitText('Save these recovery codes now');
const recovery = (await page.locator('pre.rg-recovery-codes').innerText()).trim().split(/\s+/);
check('ten recovery codes are shown, once', recovery.length === 10 && recovery.every((code) => /^[a-z2-7]{4}(-[a-z2-7]{4}){3}$/.test(code)), recovery.join(' '));
await button('I have saved them, sign in again').click();
await page.waitForURL(/\/login\?notice=two-factor-changed/, { timeout: 15000 });
check('turning it on ends the session and says why', (await body()).includes('Two-factor authentication was changed'));

// The two steps, with a code computed by this script (so the server's TOTP is checked against an independent implementation).
await typePasswordStep(page, NEW_PASSWORD);
await waitText('Two-step verification');
check('the password alone leads to the second step, not into the application', page.url().includes('/login'));
await shot('13-two-step');
await page.getByLabel('Authentication code').fill('000000');
await button('Verify').click();
await waitText('That code is not valid');
check('a wrong code is refused', page.url().includes('/login'));
await page.getByLabel('Authentication code').fill(totp(secret, 1));
await button('Verify').click();
await page.waitForURL(`${base}/`, { timeout: 15000 });
check('the code computed independently signs in', true);

// A recovery code works once.
const first = await newBrowserAtLogin();
await typePasswordStep(first.fresh, NEW_PASSWORD);
await first.fresh.getByLabel('Authentication code').fill(recovery[0].toUpperCase());
await first.fresh.getByRole('button', { name: 'Verify', exact: true }).click();
await first.fresh.waitForURL(`${base}/`, { timeout: 15000 });
check('a recovery code signs in (case does not matter)', true);
await first.context.close();
const second = await newBrowserAtLogin();
await typePasswordStep(second.fresh, NEW_PASSWORD);
await second.fresh.getByLabel('Authentication code').fill(recovery[0]);
await second.fresh.getByRole('button', { name: 'Verify', exact: true }).click();
await second.fresh.getByText('That code is not valid').first().waitFor({ timeout: 10000 });
check('the same recovery code does not work twice', second.fresh.url().includes('/login'));
await second.context.close();

// New recovery codes replace the old ones.
await page.goto(`${base}/settings`);
await waitText('Two-factor authentication is');
await page.getByLabel('Code from the app', { exact: true }).fill(recovery[1]);
await button('Get new recovery codes').click();
await waitText('Save these recovery codes now');
const regenerated = (await page.locator('pre.rg-recovery-codes').innerText()).trim().split(/\s+/);
check('regenerating gives ten new codes, confirmed with a code', regenerated.length === 10 && !regenerated.includes(recovery[2]));
await button('I have saved them').click();
await waitText('10 recovery code(s) left');
check('regenerating does not sign the user out', !page.url().includes('/login'));
const third = await newBrowserAtLogin();
await typePasswordStep(third.fresh, NEW_PASSWORD);
await third.fresh.getByLabel('Authentication code').fill(recovery[2]);
await third.fresh.getByRole('button', { name: 'Verify', exact: true }).click();
await third.fresh.getByText('That code is not valid').first().waitFor({ timeout: 10000 });
check('an old recovery code no longer works', true);
await third.context.close();

// Turning it off needs the password and a code.
await page.getByLabel('Password', { exact: true }).fill('wrong-password-123');
await page.getByLabel('Code from the app (or a recovery code)').fill(regenerated[0]);
await button('Turn off two-factor authentication').click();
await waitText('The password is not correct');
check('turning it off with a wrong password is refused', true);
await page.getByLabel('Password', { exact: true }).fill(NEW_PASSWORD);
await button('Turn off two-factor authentication').click();
await page.waitForURL(/\/login\?notice=two-factor-changed/, { timeout: 15000 });
await typePasswordStep(page, NEW_PASSWORD);
await page.waitForURL(`${base}/`, { timeout: 15000 });
check('with two-factor off the password alone signs in again', true);

// The way out when the phone is lost: the console command.
const enabledAgain = await enableTwoFactor();
check('it can be turned on again with a new secret', enabledAgain.secret !== secret);
await button('I have saved them, sign in again').click();
await page.waitForURL(/\/login\?notice=two-factor-changed/, { timeout: 15000 });
await typePasswordStep(page, NEW_PASSWORD);
await waitText('Two-step verification');
const consoleOutput = sh('roastgate disable-2fa 2>&1');
check('the console command turns it off', consoleOutput.includes('is off'), consoleOutput);
await page.goto(`${base}/login`);
await typePasswordStep(page, NEW_PASSWORD);
await page.waitForURL(`${base}/`, { timeout: 20000 });
check('afterwards the password alone is enough', true);

// ---- audit ----
await page.goto(`${base}/audit-log`);
await waitText('config.save');
const audit = await body();
check('the audit log records saves, refusals, enables and deletes', ['config.save', 'config.enable', 'config.disable', 'config.delete', 'nginx.reload', 'nginx.test', 'certbot.issue', 'certbot.renew', 'certbot.delete'].every((a) => audit.includes(a)));
await shot('05-audit');

await browser.close();
console.log('\nbrowser problems:', problems.length ? '\n  ' + [...new Set(problems)].join('\n  ') : 'none');
const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length ? 1 : 0);
