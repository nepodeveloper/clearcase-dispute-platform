import { expect, Page, test, TestInfo } from '@playwright/test';

const transactionId = '00000000-0000-0000-0000-000000000001';
const customerStatement = 'Please investigate this synthetic card transaction for the assessment.';
const privateNote = 'Merchant verification is pending before an investigation decision.';

async function signIn(page: Page, username: 'customer.demo' | 'agent.demo') {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(username);
  await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', {
    name: username === 'customer.demo' ? 'Your transactions' : 'Dispute work queue',
    exact: true,
  })).toBeVisible();
}

async function signOut(page: Page) {
  const loggedOut = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/auth/logout') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  expect((await loggedOut).status()).toBe(204);
  await expect(page).toHaveURL(/\/login$/);
}

async function capture(page: Page, info: TestInfo, name: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth))
    .toBeLessThanOrEqual(page.viewportSize()!.width);
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(name, { path, contentType: 'image/png' });
}

test('customer dispute is investigated with private notes and remains customer-safe', async ({ page }, info) => {
  await signIn(page, 'customer.demo');
  await page.goto(`/transactions/${transactionId}`);
  await page.getByRole('combobox', { name: 'Dispute reason' }).selectOption({ index: 1 });
  await page.getByRole('textbox', { name: 'Describe the issue' }).fill(customerStatement);
  const submission = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/disputes') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
  const submitted = await submission;
  expect(submitted.status()).toBe(201);
  const dispute = await submitted.json() as { id: string; reference: string };

  await signOut(page);
  await signIn(page, 'agent.demo');
  const queueResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/v1/agent/disputes');
  await page.goto('/agent/disputes');
  await queueResponse;
  await page.getByRole('searchbox', { name: 'Search cases' }).fill(dispute.reference);
  await page.getByRole('button', { name: 'Apply filters' }).click();
  await expect(page.getByRole('link', { name: dispute.reference, exact: true })).toBeVisible();
  await capture(page, info, `${info.project.name}-agent-queue`);

  const detailResponse = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}`));
  await page.getByRole('link', { name: dispute.reference, exact: true }).click();
  expect((await detailResponse).status()).toBe(200);
  await expect(page.getByText(customerStatement, { exact: true })).toBeVisible();
  await page.getByLabel('Add a private note').fill(privateNote);
  const noteResponsePromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}/notes`)
      && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Save private note', exact: true }).click();
  const noteResponse = await noteResponsePromise;
  expect(noteResponse.status()).toBe(201);
  expect(noteResponse.request().headers()['x-csrf-token']).toBeTruthy();
  expect(noteResponse.request().headers()['idempotency-key']).toBeTruthy();
  await expect(page.getByText(privateNote, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText(privateNote, { exact: true })).toBeVisible();
  await capture(page, info, `${info.project.name}-investigation`);

  await signOut(page);
  await signIn(page, 'customer.demo');
  expect((await page.request.get(`/api/v1/agent/disputes/${dispute.id}`)).status()).toBe(403);
  const customerCase = await page.request.get(`/api/v1/disputes/${dispute.id}`);
  expect(customerCase.status()).toBe(200);
  expect(await customerCase.text()).not.toContain(privateNote);
  await page.goto(`/disputes/${dispute.id}`);
  await expect(page.getByText(customerStatement, { exact: true })).toBeVisible();
  await expect(page.getByText(privateNote, { exact: true })).toHaveCount(0);

  await signOut(page);
  expect((await page.request.get('/api/v1/agent/disputes')).status()).toBe(401);
  await page.goto('/agent/disputes');
  await expect(page.getByRole('heading', { name: 'Sign in to ClearCase' })).toBeVisible();
});
