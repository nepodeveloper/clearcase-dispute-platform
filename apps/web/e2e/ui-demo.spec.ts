import { expect, Page, test } from '@playwright/test';

async function screen(page: Page, name: string): Promise<void> {
  if ((page.viewportSize()?.width ?? 1440) <= 850) {
    await page.getByRole('button', { name: 'Open navigation' }).click();
  }
  await page.getByRole('navigation', { name: 'Screen navigation', exact: true })
    .getByRole('link', { name, exact: true }).click();
  await expect(page.locator('main h1')).toBeVisible();
}

test('all ten concepts render without overflow or API calls', async ({ page }, testInfo) => {
  const apiRequests: string[] = [];
  const errors: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).pathname.startsWith('/api/')) apiRequests.push(request.url());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/login');

  async function capture(name: string, heading: string | RegExp): Promise<void> {
    await expect(page.locator('main h1')).toHaveText(heading);
    await expect(page.locator('main h1')).toBeVisible();
    const dimensions = await page.evaluate(() => ({
      viewport: window.innerWidth, document: document.documentElement.scrollWidth,
    }));
    expect(dimensions.viewport).toBe(testInfo.project.use.viewport?.width);
    expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport);
    const path = testInfo.outputPath(`${name}-${testInfo.project.name}.png`);
    await page.screenshot({ path, fullPage: true, animations: 'disabled' });
    await testInfo.attach(`${name}-${testInfo.project.name}`, { path, contentType: 'image/png' });
  }

  await capture('login', /Manage disputes\s*with confidence\./);
  await page.getByRole('button', { name: 'Sign in as customer' }).click();
  await capture('transactions', 'Your transactions');
  await screen(page, 'Submit a dispute');
  await capture('transaction', 'Transaction details');
  await screen(page, 'My disputes');
  await capture('disputes', 'My disputes');
  await screen(page, 'Dispute timeline');
  await capture('timeline', 'Dispute CC-2026-01457');
  await page.getByRole('combobox', { name: 'Preview journey' }).selectOption('agent');
  await capture('agent-queue', 'Dispute work queue');
  await screen(page, 'Case investigation');
  await capture('investigation', 'Case investigation');
  await screen(page, 'Escalate case');
  await capture('escalate', 'Escalate a dispute');
  await page.getByRole('combobox', { name: 'Preview journey' }).selectOption('supervisor');
  await capture('supervisor', 'Escalations queue');
  await screen(page, 'Supervisor review');
  await capture('supervisor-detail', 'Escalation ESC-000437');
  expect(apiRequests).toEqual([]);
  expect(errors).toEqual([]);

  if (testInfo.project.name === 'desktop') {
    await page.setViewportSize({ width: 768, height: 1024 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(768);
  }
});

test('customer to agent to supervisor journey keeps private notes out of the public outcome', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('button', { name: 'Sign in as customer' }).click();
  await page.getByRole('link', { name: 'View and dispute Checkers Sandton' }).click();
  await page.getByRole('button', { name: 'Submit demo dispute' }).click();
  await expect(page.getByRole('alert')).toContainText('between 15 and 2000');
  await page.getByRole('textbox', { name: 'Describe the issue' }).fill('Please investigate the incorrect demo transaction amount.');
  await page.getByRole('button', { name: 'Submit demo dispute' }).click();
  await expect(page.locator('main h1')).toContainText('Dispute CC-2026-');
  const reference = (await page.locator('main h1').innerText()).replace('Dispute ', '');
  const customerTimeline = new URL(page.url()).pathname;
  await page.getByRole('combobox', { name: 'Preview journey' }).selectOption('agent');
  await page.getByRole('searchbox', { name: 'Search cases' }).fill(reference);
  await page.getByRole('link', { name: `Investigate ${reference}`, exact: true }).click();
  const open = page.getByRole('button', { name: 'Escalate case', exact: true });
  await open.click();
  const dialog = page.getByRole('dialog', { name: 'Escalate case', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('combobox', { name: 'Escalation reason' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await expect(open).toBeFocused();
  await open.click();
  await expect(dialog).toBeVisible();
  for (let count = 0; count < 8; count++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate(node => node.contains(document.activeElement))).toBe(true);
  }
  await dialog.getByRole('textbox', { name: 'Internal investigation note' }).fill('Private agent note: synthetic internal investigation finding.');
  await dialog.getByRole('button', { name: 'Submit demo escalation' }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('status')).toContainText('created in memory');
  await page.getByRole('button', { name: 'Preview supervisor review' }).click();
  await expect(page.locator('main h1')).toHaveText(/^Escalation ESC-/);
  await expect(page.locator('.soft-box')).toContainText('Private agent note');
  await expect(page.getByRole('button', { name: 'Resolve demo escalation' })).toBeDisabled();
  await page.getByRole('button', { name: 'Acknowledge demo escalation' }).click();
  await page.getByRole('textbox', { name: 'Resolution note' }).fill('Private supervisor note: the investigation can continue.');
  await page.getByRole('button', { name: 'Resolve demo escalation' }).click();
  await expect(page.getByRole('status')).toContainText('Demo escalation resolved');
  await page.getByRole('combobox', { name: 'Preview journey' }).selectOption('customer');
  await screen(page, 'My disputes');
  await page.locator(`a[href="${customerTimeline}"]`).click();
  await expect(page.getByRole('list', { name: 'Dispute timeline' })).toContainText('Specialist review complete');
  await expect(page.locator('.description-grid')).toContainText('Under review');
  await expect(page.locator('.description-grid')).toContainText('Pending');
  await expect(page.locator('.description-grid')).toContainText('Not initiated');
  await expect(page.locator('main')).not.toContainText('Private agent');
  await expect(page.locator('main')).not.toContainText('Private supervisor');
  await page.reload();
  await expect(page).toHaveURL(/\/login$/);
});

test('navigation and primary text colours meet keyboard and contrast requirements', async ({ page }) => {
  await page.goto('/login');
  const contrast = await page.getByRole('button', { name: 'Sign in as customer' }).evaluate(node => {
    const style = getComputedStyle(node);
    const luminance = (value: string) => {
      const [r, g, b] = (value.match(/\d+(\.\d+)?/g) ?? []).slice(0, 3).map(part => {
        const channel = Number(part) / 255;
        return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
      });
      return .2126 * r + .7152 * g + .0722 * b;
    };
    const foreground = luminance(style.color);
    const background = luminance(style.backgroundColor);
    return (Math.max(foreground, background) + .05) / (Math.min(foreground, background) + .05);
  });
  expect(contrast).toBeGreaterThanOrEqual(4.5);
  await page.getByRole('button', { name: 'Forgot password?' }).click();
  await expect(page.getByRole('status')).toContainText('No password is needed');
});