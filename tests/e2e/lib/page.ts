import type { Locator, Page } from 'playwright-core';

import { BASE, SHOTS } from './config.ts';
import { PROBLEMS, TRACE } from './report.ts';

type Position = { x: number; y: number; width: number; height: number };

/**
 * // The helpers the scenario uses, bound to the page they act on.
 */
export const pageHelper = (page: Page) => {
  /**
   * What an action reports: a success is a toast (MudBlazor snackbar), a failure stays inline (MudAlert) with nginx's
   * output.
   */
  const alerts = async (): Promise<string> => {
    return (await page.locator('.mud-alert').allInnerTexts()).join('\n');
  };

  const body = async (): Promise<string> => {
    return page.locator('body').innerText();
  };

  /**
   * Where an element is on the page; fails with its locator when it is not visible, instead of reading from null.
   */
  const box = async (locator: Locator): Promise<Position> => {
    const found: Position | null = await locator.boundingBox();

    if (!found) {
      throw new Error(`Not visible, so it has no position: ${locator}`);
    }

    return found;
  };

  const button = (name: string): Locator => {
    return page.getByRole('button', { name, exact: true });
  };

  const centerY = async (locator: Locator): Promise<number> => {
    const position: Position = await box(locator);

    return position.y + position.height / 2;
  };

  /**
   * Waits for the "Saved." message, and when the save failed shows why instead of just timing out.
   */
  const expectSaved = async (): Promise<void> => {
    try {
      await page
        .getByText('Saved.', { exact: false })
        .first()
        .waitFor({ timeout: 10000 });
    } catch {
      const alert: string[] = await page.locator('.mud-alert').allInnerTexts();

      throw new Error(
        'The save did not succeed. Alerts on the page: ' +
          JSON.stringify(alert),
      );
    }

    await page.waitForTimeout(500);
  };

  const getEditor = async (): Promise<string> => {
    return page.evaluate(() => window.monaco.editor.getModels()[0].getValue());
  };

  const openEditor = async (url: string): Promise<void> => {
    await page.goto(`${BASE}${url}`);
    await waitMonaco('openEditor ' + url);
    await page.waitForTimeout(800); // language and theme are applied once the editor has initialised
  };

  const setEditor = async (text: string): Promise<void> => {
    await waitMonaco('setEditor');
    await page.evaluate(
      (value: string) => window.monaco.editor.getModels()[0].setValue(value),
      text,
    );
  };

  const shot = async (name: string): Promise<void> => {
    await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: true });
  };

  const tab = (name: string): Locator => {
    return page.getByRole('tab', { name, exact: true });
  };

  const toasts = async (): Promise<string> => {
    return (await page.locator('.mud-snackbar').allInnerTexts()).join('\n');
  };

  /**
   * Waits for a form field by its label (the text of an outlined field also exists in a hidden legend).
   */
  const waitField = async (label: string): Promise<void> => {
    await page.getByLabel(label, { exact: true }).waitFor({ timeout: 10000 });
    await page.waitForTimeout(700);
  };

  /**
   * When the editor never shows up, prints what the page looked like and what the browser did before failing.
   */
  const waitMonaco = async (label: string): Promise<void> => {
    try {
      await page.waitForFunction(
        () => window.monaco && window.monaco.editor.getModels().length > 0,
        null,
        { timeout: 40000 },
      );
    } catch (error) {
      const state = await page.evaluate(() => ({
        url: location.href,
        monaco: typeof window.monaco,
        models: window.monaco?.editor
          ? window.monaco.editor.getModels().length
          : null,
        editorDom: document.querySelectorAll('.monaco-editor').length,
        containers: document.querySelectorAll('.rg-editor').length,
        text: document.body.innerText.replace(/\s+/g, ' ').slice(0, 300),
      }));

      console.log(`MONACO FAILED at ${label}:`, JSON.stringify(state));
      console.log('TRACE (last 25):\n' + TRACE.slice(-25).join('\n'));
      console.log(
        'browser problems so far:',
        [...new Set(PROBLEMS)].join(' | '),
      );

      await page.screenshot({
        path: `${SHOTS}/failure-${label.replace(/[^a-z0-9]+/gi, '_')}.png`,
        fullPage: true,
      });

      throw error;
    }
  };

  /**
   * After the text shows up, give the Blazor circuit a moment to attach, the way a person reading the page would. (A
   * click within ~50 ms of the WebSocket opening can leave Blazor's URL and page out of step: a framework race.)
   */
  const waitText = async (t: string, timeout = 10000): Promise<void> => {
    await page.getByText(t, { exact: false }).first().waitFor({ timeout });
    await page.waitForTimeout(700);
  };

  return {
    alerts,
    body,
    box,
    button,
    centerY,
    expectSaved,
    getEditor,
    openEditor,
    setEditor,
    shot,
    tab,
    toasts,
    waitField,
    waitMonaco,
    waitText,
  };
};
