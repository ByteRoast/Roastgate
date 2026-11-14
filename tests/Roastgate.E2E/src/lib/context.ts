import type { Browser, Page } from 'playwright-core';

import type { pageHelper } from './page.ts';

export interface Context {
  browser: Browser;
  helpers: ReturnType<typeof pageHelper>;
  page: Page;
  token: string;
}
