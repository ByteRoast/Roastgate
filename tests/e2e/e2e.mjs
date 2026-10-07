// End to end scenario: the real setup wizard, then every config feature against a REAL nginx running in the test container.
// See README.md in this folder. Needs Docker, Node 20+ and an installed browser (Edge by default, see E2E_CHANNEL).
import { chromium } from 'playwright-core';
import { execSync } from 'node:child_process';
import fs from 'node:fs';

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
await page.getByRole('link', { name: 'New reverse proxy' }).click();
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

// ---- whitelist: create ----
await page.goto(`${base}/whitelists/edit`);
await page.waitForFunction(() => window.monaco && window.monaco.editor.getModels().length > 0, null, { timeout: 15000 });
await page.waitForTimeout(800);
await page.getByLabel('File name').fill('alice');
await setEditor('allow 203.0.113.7; # alice at home');
await button('Save').click();
await waitText('Saved.');
check('a whitelist is created in the whitelists directory', sh('cat /etc/nginx/whitelists/alice.conf').includes('allow 203.0.113.7;'));

// ---- audit ----
await page.goto(`${base}/audit-log`);
await waitText('config.save');
const audit = await body();
check('the audit log records saves, refusals, enables and deletes', ['config.save', 'config.enable', 'config.disable', 'config.delete', 'nginx.reload', 'nginx.test'].every((a) => audit.includes(a)));
await shot('05-audit');

await browser.close();
console.log('\nbrowser problems:', problems.length ? '\n  ' + [...new Set(problems)].join('\n  ') : 'none');
const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length ? 1 : 0);
