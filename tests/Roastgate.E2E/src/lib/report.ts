import type {
  ConsoleMessage,
  Frame,
  Page,
  Request,
  WebSocket,
} from 'playwright-core';

export const RESULTS: { name: string; ok: boolean }[] = [];
export const PROBLEMS: string[] = [];
export const TRACE: string[] = [];

export const check = (name: string, ok: boolean, detail = '') => {
  RESULTS.push({ name, ok });
  console.log(
    `${ok ? 'PASS' : 'FAIL'}  ${name}${!ok && detail ? '\n        ' + detail : ''}`,
  );
};

export const watchPage = (page: Page): void => {
  page.on('pageerror', (e: Error): void => {
    PROBLEMS.push('pageerror: ' + e.message);
  });

  const t0: number = Date.now();

  page.on('console', (m: ConsoleMessage): void => {
    TRACE.push(
      `+${Date.now() - t0}ms console.${m.type()}: ${m.text().slice(0, 200)}`,
    );

    if (m.type() === 'error') {
      PROBLEMS.push('console: ' + m.text());
    }
  });

  page.on('websocket', (ws: WebSocket): void => {
    TRACE.push(`+${Date.now() - t0}ms websocket open ${ws.url()}`);

    ws.on('close', (): void => {
      TRACE.push(`+${Date.now() - t0}ms websocket CLOSED`);
    });
  });

  page.on('framenavigated', (f: Frame): void => {
    if (f === page.mainFrame()) {
      TRACE.push(`+${Date.now() - t0}ms navigated ${f.url()}`);
    }
  });

  page.on('requestfailed', (r: Request): void => {
    TRACE.push(
      `+${Date.now() - t0}ms request failed ${r.url()} ${r.failure()?.errorText}`,
    );
  });
};

// Prints the browser problems and the count of passed checks, and returns the exit code of the run.
export const printSummary = (): number => {
  console.log(
    '\nbrowser problems:',
    PROBLEMS.length ? '\n  ' + [...new Set(PROBLEMS)].join('\n  ') : 'none',
  );

  const failed: { name: string; ok: boolean }[] = RESULTS.filter((r) => !r.ok);

  console.log(
    `\n${RESULTS.length - failed.length}/${RESULTS.length} checks passed`,
  );

  return failed.length ? 1 : 0;
};
