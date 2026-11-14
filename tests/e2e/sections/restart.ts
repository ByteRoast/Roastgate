import { execSync } from 'node:child_process';

import { BASE, CONTAINER } from '../lib/config.ts';
import { check } from '../lib/report.ts';

import type { Context } from '../lib/context.ts';

/**
 * The account survives a restart of the container.
 */
export const restart = async ({ page }: Context): Promise<void> => {
  execSync(`docker restart ${CONTAINER}`, { stdio: 'ignore' });

  for (let i: number = 0; i < 60; i += 1) {
    try {
      if ((await fetch(`${BASE}/login`)).ok) {
        break;
      }
    } catch {
      /* not up yet */
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  const restartLogs: string = execSync(`docker logs ${CONTAINER} 2>&1`, {
    encoding: 'utf8',
  });

  check(
    'after a restart the existing database is reused',
    restartLogs.includes('Using the existing database'),
  );

  await page.goto(`${BASE}/`);

  check(
    'after a restart the account and the session are still there (no setup)',
    !page.url().includes('/setup') && !page.url().includes('/login'),
    page.url(),
  );
};
