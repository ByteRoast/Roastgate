import { check } from '../lib/report.ts';

import type { Context } from '../lib/context.ts';

export const dashboard = async ({ helpers }: Context): Promise<void> => {
  await helpers.waitText('nginx 1.24');
  await helpers.button('Test configuration').click();
  await helpers.waitText('The configuration is valid.');

  check('dashboard: Test configuration runs the real nginx -t', true);

  await helpers.button('Reload nginx').click();
  await helpers.waitText('nginx reloaded.');

  check('dashboard: Reload nginx reloads the real nginx', true);

  await helpers.shot('dashboard');
};
