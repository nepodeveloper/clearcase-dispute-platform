import { expect, Page, test, TestInfo } from '@playwright/test';

async function capture(page: Page, testInfo: TestInfo, name: string) {
  const path = testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await testInfo.attach(name, { path, contentType: 'image/png' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width);
}

for (const [role, username, home, heading] of [
  ['customer', 'customer.demo', '/', 'Your transactions'],
  ['agent', 'agent.demo', '/agent/disputes', 'Agent workspace'],
  ['supervisor', 'supervisor.demo', '/agent/escalations', 'Supervisor workspace'],
]) {
  test(`${role} signs in, restores, enforces roles and signs out with the real API`, async ({ page, context }, testInfo) => {
    await page.goto(role === 'customer' ? '/disputes' : '/login');
    await expect(page.getByRole('heading', { name: 'Sign in to ClearCase' })).toBeVisible();
    await page.getByLabel('Username', { exact: true }).fill(username);
    await capture(page, testInfo, `${role}-login-desktop`);
    await page.setViewportSize({ width: 390, height: 844 });
    await capture(page, testInfo, `${role}-login-mobile`);
    await page.getByLabel('Username', { exact: true }).fill('');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page.getByLabel('Username', { exact: true })).toBeFocused();
    await expect(page.getByText('Enter your password.', { exact: true })).toBeVisible();
    await page.getByLabel('Username', { exact: true }).fill(username);
    await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
    await page.getByRole('button', { name: 'Show password', exact: true }).click();
    await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('type', 'text');
    await page.getByRole('button', { name: 'Hide password', exact: true }).click();
    await page.getByLabel('Password', { exact: true }).press('Enter');
    await expect(page).toHaveURL(new RegExp(`${role === 'customer' ? '/disputes' : home}$`));
    await page.goto(home);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await capture(page, testInfo, `${role}-workspace-mobile`);
    await page.setViewportSize({ width: 1440, height: 1000 });
    await capture(page, testInfo, `${role}-workspace-desktop`);
    expect((await context.cookies()).find(cookie => cookie.name === 'clearcase.auth')?.httpOnly).toBe(true);
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
    await page.reload();
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();

    if (role === 'customer') {
      await page.goto('/transactions/00000000-0000-0000-0000-000000000003');
      await page.getByRole('combobox', { name: 'Dispute reason' }).selectOption({ index: 1 });
      await page.getByRole('textbox', { name: 'Describe the issue' }).fill('Please investigate this synthetic account service fee.');
      const submitted = page.waitForResponse(response => response.url().endsWith('/api/v1/disputes') && response.request().method() === 'POST');
      await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
      const response = await submitted;
      expect(response.status()).toBe(201);
      expect(response.request().headers()['x-csrf-token']).toBeTruthy();
      await page.getByRole('link', { name: 'Track this dispute' }).click();
      await expect(page.getByText('Please investigate this synthetic account service fee.')).toBeVisible();
    }
    await page.goto(role === 'customer' ? '/agent/disputes' : '/disputes');
    await expect(page.getByRole('heading', { name: 'Access denied', exact: true })).toBeVisible();
    const loggedOut = page.waitForResponse(response => response.url().endsWith('/api/v1/auth/logout'));
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    expect((await loggedOut).status()).toBe(204);
    await expect(page).toHaveURL(/\/login$/);
    expect((await page.request.get('/api/v1/auth/me')).status()).toBe(401);
    await page.goto(home);
    await expect(page.getByRole('heading', { name: 'Sign in to ClearCase' })).toBeVisible();
  });
}

test('invalid cookies never restore a synthetic customer and invalid credentials are explained', async ({ page, context }) => {
  await context.addCookies([{ name: 'clearcase.auth', value: 'expired-or-invalid', url: 'http://127.0.0.1:4302' }]);
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { name: 'Sign in to ClearCase' })).toBeVisible();
  await page.getByLabel('Username', { exact: true }).fill('not-a-user');
  await page.getByLabel('Password', { exact: true }).fill('invalid-input');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Username or password is incorrect');
  await expect(page.getByLabel('Password', { exact: true })).toHaveValue('');
});

test('rate limited login is explained without bypassing the real limiter', async ({ page }) => {
  await page.goto('/login');
  for (let attempt = 0; attempt < 6; attempt++) {
    await page.request.post('/api/v1/auth/login', { data: { username: 'not-a-user', password: 'invalid-input' } });
  }
  await page.getByLabel('Username', { exact: true }).fill('not-a-user');
  await page.getByLabel('Password', { exact: true }).fill('invalid-input');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Too many sign-in attempts');
});
