import { BASE } from '../lib/config.ts';
import { check } from '../lib/report.ts';

import type { Context } from '../lib/context.ts';

/**
 * Audit.
 */
export const audit = async ({ helpers, page }: Context): Promise<void> => {
  await page.goto(`${BASE}/audit-log`);
  await helpers.waitText('config.save');

  const auditLog: string = await helpers.body();

  check(
    'the audit log records saves, refusals, enables and deletes',
    [
      'config.save',
      'config.enable',
      'config.disable',
      'config.delete',
      'nginx.reload',
      'nginx.test',
      'certbot.issue',
      'certbot.renew',
      'certbot.delete',
    ].every((a) => auditLog.includes(a)),
  );

  await helpers.shot('audit');
};
