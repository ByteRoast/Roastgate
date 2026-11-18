import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';
import { appendHostFile, sh, writeHostFile } from '../lib/shell.ts';

import type { Locator, Page } from 'playwright-core';
import type { Context } from '../lib/context.ts';

// Addresses of MaxMind's test database, which the image uses as its GeoIP database (see the Dockerfile). The two London
// addresses share the same coordinates, so they make one place with two addresses.
const LONDON_A: string = '81.2.69.142';
const LONDON_B: string = '81.2.69.144';
const MILTON: string = '216.160.83.56';
const PRIVATE: string = '10.0.0.1';

const LOGS: string = '/var/log/nginx';

const MONTHS: string[] = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/**
 * `count` requests from `address`, made `minutesAgo` minutes ago, in nginx's combined format.
 */
const logLines = (
  address: string,
  minutesAgo: number,
  count: number = 1,
): string => {
  const t: Date = new Date(Date.now() - minutesAgo * 60_000);
  const pad = (n: number): string => String(n).padStart(2, '0');

  const time: string =
    `${pad(t.getUTCDate())}/${MONTHS[t.getUTCMonth()]}/${t.getUTCFullYear()}` +
    `:${pad(t.getUTCHours())}:${pad(t.getUTCMinutes())}:${pad(t.getUTCSeconds())} +0000`;

  return `${address} - - [${time}] "GET / HTTP/1.1" 200 612 "-" "roastgate-e2e"\n`.repeat(
    count,
  );
};

/**
 * Seeds the three kinds of files the page reads. Located requests per period:
 *
 * | Place  | 1 h | 24 h | 7 days |
 * |--------|-----|------|--------|
 * | London | 3   | 6    | 11     |
 * | Milton | 2   | 3    | 3      |
 *
 * plus one request from a private address, and whatever nginx logged for 127.0.0.1 earlier in the scenario.
 */
const seedLogs = (): void => {
  const outputs: string[] = [
    // nginx keeps writing to the current file: append, do not replace it.
    appendHostFile(
      `${LOGS}/access.log`,
      logLines(LONDON_A, 30, 3) +
        logLines(MILTON, 20, 2) +
        logLines(PRIVATE, 15),
    ),
    writeHostFile(
      `${LOGS}/access.log.1`,
      logLines(LONDON_B, 5 * 60, 2) +
        logLines(MILTON, 6 * 60) +
        logLines(LONDON_A, 2 * 24 * 60, 5),
    ),
    writeHostFile(`${LOGS}/access.log.2`, logLines(LONDON_A, 10 * 60)),
    sh(`gzip -f ${LOGS}/access.log.2`),
  ];

  const failed: string | undefined = outputs.find((o) => o.startsWith('ERR'));

  if (failed) {
    throw new Error(`Could not seed the access logs: ${failed}`);
  }
};

/**
 * The number under a key figure ("Requests", "Countries"...). Table headers share some of these labels but have no
 * h5 next to them.
 */
const figure = async (page: Page, label: string): Promise<number> => {
  const value: string = await page
    .getByText(label, { exact: true })
    .locator('xpath=following-sibling::h5')
    .innerText();

  return Number(value.replace(/\D/g, ''));
};

const located = async (page: Page): Promise<number> => {
  return (
    (await figure(page, 'Requests')) -
    (await figure(page, 'Unlocated requests'))
  );
};

/**
 * Waits until the page shows `expected` located requests: a new period loads in the background, without a navigation
 * to wait for.
 */
const waitLocated = async (page: Page, expected: number): Promise<number> => {
  const deadline: number = Date.now() + 15000;
  let current: number = await located(page);

  while (current !== expected && Date.now() < deadline) {
    await page.waitForTimeout(250);
    current = await located(page);
  }

  return current;
};

/**
 * Traffic: the access logs, located with the GeoIP database and placed on the map.
 */
export const traffic = async ({ helpers, page }: Context): Promise<void> => {
  seedLogs();

  await page.goto(`${BASE}/traffic`);
  await helpers.waitText('Top places');

  // The map is drawn after the first render, then the countries fade to their shade.
  const points: Locator = page.locator('.traffic-point');

  await points.nth(1).waitFor({ timeout: 10000 });
  await page.waitForTimeout(600);

  const lastDay: number = await waitLocated(page, 9);

  check(
    'traffic: the last 24 hours count the current, rotated and gzipped logs',
    lastDay === 9,
    `${lastDay} located requests`,
  );

  const addresses: number = await figure(page, 'Addresses');
  const countries: number = await figure(page, 'Countries');

  check(
    'traffic: the located addresses and countries are counted',
    addresses === 3 && countries === 2,
    `${addresses} addresses, ${countries} countries`,
  );

  check(
    'traffic: a private address is counted as unlocated',
    (await figure(page, 'Unlocated requests')) >= 1,
  );

  const places: string[] = await page.locator('.traffic-place').allInnerTexts();

  check(
    'traffic: the top places list London first, then Milton, with their flags',
    places.length === 2 &&
      places[0].includes('London') &&
      places[0].includes('United Kingdom') &&
      places[1].includes('Milton') &&
      (await page.locator('.traffic-place .fi-gb').count()) === 1 &&
      (await page.locator('.traffic-place .fi-us').count()) === 1,
    JSON.stringify(places),
  );

  const london: string[] = (
    await page
      .locator('.mud-table tbody tr')
      .filter({ hasText: 'London' })
      .locator('td')
      .allInnerTexts()
  ).map((cell) => cell.trim());

  check(
    'traffic: the table merges the two London addresses into one place',
    london.join('|') === 'London|United Kingdom|6|2',
    JSON.stringify(london),
  );

  const shaded: string[] = await page
    .locator('.traffic-map path[data-country]')
    .evaluateAll((paths: Element[]) => [
      ...new Set(
        paths
          .filter((path) => (path as SVGElement).style.fill !== '')
          .map((path) => (path as SVGElement).dataset.country ?? ''),
      ),
    ]);

  check(
    'traffic: the map shades the countries with requests, and only them',
    shaded.sort().join(',') === 'GB,US',
    JSON.stringify(shaded),
  );

  check(
    'traffic: the map draws a point for each place',
    (await points.count()) === 2,
  );

  check(
    'traffic: the map credits DB-IP',
    (await helpers.body()).includes('IP Geolocation by DB-IP'),
  );

  await helpers.shot('traffic');

  const tooltip: Locator = page.locator('.traffic-tooltip--visible');
  const country: Locator = page.locator('.traffic-map path[data-country="GB"]');

  // A country's shape is not a reliable place to put the mouse (its middle can be sea or another country).
  await country.dispatchEvent('pointerenter');

  const countryTip: string = await tooltip.innerText();

  check(
    'traffic: hovering a country shows its flag and its requests',
    countryTip.includes('United Kingdom') &&
      countryTip.includes('6 requests') &&
      (await tooltip.locator('.fi-gb').count()) === 1,
    countryTip,
  );

  await country.dispatchEvent('pointerleave');

  // The points are drawn in the order of the places, so London comes first.
  await points.first().hover();

  const placeTip: string = await tooltip.innerText();

  check(
    'traffic: hovering a place shows its requests and its addresses',
    placeTip.includes('London') &&
      placeTip.includes('6 requests · 2 addresses'),
    placeTip,
  );

  await helpers.shot('traffic-tooltip', page.locator('.traffic-map-frame'));

  await page
    .locator('.traffic-periods')
    .getByText('1 h', { exact: true })
    .click();

  const lastHour: number = await waitLocated(page, 5);

  check(
    'traffic: the last hour leaves out the older requests',
    lastHour === 5,
    `${lastHour} located requests`,
  );

  await page
    .locator('.traffic-periods')
    .getByText('7 days', { exact: true })
    .click();

  const lastWeek: number = await waitLocated(page, 14);

  check(
    'traffic: the last 7 days include the requests of two days ago',
    lastWeek === 14,
    `${lastWeek} located requests`,
  );
};
