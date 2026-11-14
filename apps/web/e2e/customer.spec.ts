import { expect, Page, test, TestInfo } from '@playwright/test';
import type { AuthSession } from '../src/app/auth/auth-service';
import type { Dispute, DisputePage } from '../src/app/disputes/dispute-api';
import type { TransactionDetails, TransactionDisputeEligibility, TransactionPage } from '../src/app/transactions/transaction-api';

const transactionId = (index: number) => `00000000-0000-0000-0000-${String(index).padStart(12, '0')}`;
const journeyTransactions = [1, 2, 6, 7, 5, 9].map(transactionId);
const description = 'Please investigate this synthetic transaction for the assessment.';
test.setTimeout(120000);

async function login(page: Page, username = 'customer.demo') {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(username);
  await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const heading = page.getByRole('heading', { name: 'Your transactions', exact: true });
  await expect(heading).toBeVisible();
  await expect(heading).toBeFocused();
}

async function capture(page: Page, info: TestInfo, name: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width);
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(name, { path, contentType: 'image/png' });
}

test('customer completes the persisted journey for all categories, with filtering and duplicate protection', async ({ page }, info) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const firstPage = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/transactions');
  await login(page);
  const first: TransactionPage = await (await firstPage).json();
  await expect(page.locator('tbody tr')).toHaveCount(first.items.length);
  expect(first.totalCount).toBe(30);
  await capture(page, info, 'transactions');
  const secondPage = page.waitForResponse(response => new URL(response.url()).searchParams.get('pageNumber') === '2');
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  const second: TransactionPage = await (await secondPage).json();
  await expect(page.locator('tbody tr')).toHaveCount(second.items.length);
  await expect(page.locator('main h1')).toBeFocused();
  expect(second.items.every(item => !first.items.some(previous => previous.id === item.id))).toBe(true);

  await page.getByRole('combobox', { name: 'Transaction type', exact: true }).selectOption('BankCharge');
  await page.getByLabel('From date', { exact: true }).fill('2026-11-01');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-10');
  const filteredResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('transactionType') === 'BankCharge');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  const filtered: TransactionPage = await (await filteredResponse).json();
  expect(filtered.pageNumber).toBe(1);
  expect(filtered.items.length).toBeGreaterThan(0);
  expect(filtered.items.every(item => item.transactionType === 'BankCharge'
    && item.transactionDate.slice(0, 10) >= '2026-11-01' && item.transactionDate.slice(0, 10) <= '2026-11-10')).toBe(true);
  await expect(page.locator('tbody tr')).toHaveCount(filtered.items.length);

  await page.getByLabel('From date', { exact: true }).fill('2030-01-01');
  await page.getByLabel('To date', { exact: true }).fill('2030-01-02');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  await expect(page.getByRole('status')).toContainText('No transactions found');

  const indexes = info.project.name === 'customer-mobile' ? [7, 5, 9] : [1, 2, 6];
  for (const index of indexes) {
    const id = transactionId(index);
    const detailResponse = page.waitForResponse(response => response.url().endsWith(`/api/v1/transactions/${id}`));
    const eligibilityResponse = page.waitForResponse(response => response.url().endsWith(`/${id}/dispute-eligibility`));
    await page.goto(`/transactions/${id}`);
    const transaction: TransactionDetails = await (await detailResponse).json();
    const eligibility: TransactionDisputeEligibility = await (await eligibilityResponse).json();
    await expect(page.getByRole('heading', { name: transaction.description, exact: true })).toBeVisible();
    await expect(page.getByText(eligibility.explanation, { exact: true })).toBeVisible();
    expect(eligibility.outcome).not.toBe('Ineligible');
    const reasons = page.getByRole('combobox', { name: 'Dispute reason' });
    expect(await reasons.locator('option').evaluateAll(options => options.slice(1).map(option => ({
      code: (option as HTMLOptionElement).value, label: option.textContent?.trim(),
    })))).toEqual(eligibility.availableReasons);
    await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
    await expect(reasons).toHaveAttribute('aria-invalid', 'true');
    await reasons.selectOption(eligibility.availableReasons[0].code);
    await page.getByRole('textbox', { name: 'Describe the issue' }).fill(description);
    await capture(page, info, `transaction-${transaction.transactionType}`);
    const submitted = page.waitForResponse(response => response.url().endsWith('/api/v1/disputes')
      && response.request().method() === 'POST');
    await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
    const response = await submitted;
    expect(response.status()).toBe(201);
    expect(response.request().headers()['x-csrf-token']).toBeTruthy();
    const dispute: Dispute = await response.json();
    expect(dispute.id).toMatch(/^[0-9a-f-]{36}$/);
    expect(dispute.reference).toMatch(/^DSP-/);
    expect(dispute.reasonCode).toBe(eligibility.availableReasons[0].code);
    expect(dispute.financialStatus).toBe('NotInitiated');
    await expect(page).toHaveURL(new RegExp(`/disputes/${dispute.id}$`));
    await expect(page.locator('main h1')).toHaveText(`Dispute ${dispute.reference}`);
    await page.reload();
    await expect(page.getByText(description, { exact: true })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Dispute timeline' }).locator('li p')).toHaveText(
      [...dispute.timeline].sort((left, right) => left.sequenceNumber - right.sequenceNumber).map(event => event.description));
    await expect(page.getByLabel('Case summary')).toContainText('Financial status: Not initiated');
    await capture(page, info, `timeline-${transaction.transactionType}`);
    await page.getByRole('link', { name: 'Back to my disputes' }).click();
    await expect(page.getByRole('link', { name: dispute.reference, exact: true })).toBeVisible();

    await page.goto(`/transactions/${id}`);
    await page.getByRole('combobox', { name: 'Dispute reason' }).selectOption(dispute.reasonCode);
    await page.getByRole('textbox', { name: 'Describe the issue' }).fill(description);
    const duplicate = page.waitForResponse(result => result.url().endsWith('/api/v1/disputes') && result.request().method() === 'POST');
    await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
    expect((await duplicate).status()).toBe(409);
    await expect(page.getByRole('alert')).toContainText('already has an open dispute');
  }

  await page.goto(`/transactions/${transactionId(4)}`);
  await expect(page.getByRole('status')).toContainText('Currently ineligible');
  await expect(page.getByRole('button', { name: 'Submit dispute', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  expect((await page.request.get('/api/v1/disputes')).status()).toBe(401);
  expect(errors).toEqual([]);
});

test('My disputes consumes the opaque API cursor and recovers from an offline request', async ({ page, context }, info) => {
  await login(page);
  const session: AuthSession = await (await page.request.get('/api/v1/auth/me')).json();
  const initial: DisputePage = await (await page.request.get('/api/v1/disputes?pageSize=100')).json();
  const transactions: TransactionPage = await (await page.request.get('/api/v1/transactions?pageSize=100')).json();
  let count = initial.items.length;
  for (const transaction of transactions.items) {
    if (count > 20) break;
    if (journeyTransactions.includes(transaction.id) || initial.items.some(item => item.transactionId === transaction.id)) continue;
    const eligibility: TransactionDisputeEligibility = await (await page.request.get(`/api/v1/transactions/${transaction.id}/dispute-eligibility`)).json();
    if (eligibility.outcome === 'Ineligible') continue;
    const created = await page.request.post('/api/v1/disputes', {
      headers: { 'X-CSRF-TOKEN': session.csrfToken },
      data: { transactionId: transaction.id, reasonCode: eligibility.availableReasons[0].code, description },
    });
    expect(created.status()).toBe(201);
    count++;
  }
  expect(count).toBeGreaterThan(20);
  const firstResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/disputes');
  await page.goto('/disputes');
  const first: DisputePage = await (await firstResponse).json();
  expect(first.pageInfo.hasNextPage).toBe(true);
  const nextResponse = page.waitForResponse(response => new URL(response.url()).searchParams.get('cursor') === first.pageInfo.nextCursor);
  await page.getByRole('button', { name: 'Load more disputes' }).click();
  const next: DisputePage = await (await nextResponse).json();
  await expect(page.locator('.case-list > li')).toHaveCount(first.items.length + next.items.length);
  for (const item of [...first.items, ...next.items]) {
    await expect(page.getByRole('link', { name: item.reference, exact: true })).toHaveCount(1);
  }
  await expect(page.getByRole('button', { name: 'Load more disputes' })).toHaveCount(0);
  await capture(page, info, 'my-disputes');
  await context.setOffline(true);
  await page.getByRole('button', { name: 'Refresh list' }).click();
  await expect(page.getByRole('alert')).toContainText('Unable to load disputes');
  await context.setOffline(false);
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.locator('.case-list > li')).toHaveCount(20);
});

test('customer navigation, session expiry and ownership remain enforced', async ({ page, context }, info) => {
  await login(page);
  if (info.project.name === 'customer-mobile') {
    await page.getByRole('button', { name: 'Open navigation' }).tap();
    const navigation = page.getByRole('navigation', { name: 'Customer navigation', exact: true });
    await expect(navigation.getByRole('link', { name: 'Transactions', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/disputes$/);
  } else {
    await page.getByRole('navigation', { name: 'Customer navigation', exact: true })
      .getByRole('link', { name: 'Your disputes', exact: true }).click();
  }
  const cases: DisputePage = await (await page.request.get('/api/v1/disputes')).json();
  let owned = cases.items[0];
  if (owned === undefined) {
    const session: AuthSession = await (await page.request.get('/api/v1/auth/me')).json();
    const eligibility: TransactionDisputeEligibility = await (await page.request.get(`/api/v1/transactions/${transactionId(12)}/dispute-eligibility`)).json();
    const response = await page.request.post('/api/v1/disputes', {
      headers: { 'X-CSRF-TOKEN': session.csrfToken },
      data: { transactionId: transactionId(12), reasonCode: eligibility.availableReasons[0].code, description },
    });
    expect(response.status()).toBe(201);
    owned = await response.json();
  }
  await expect(page.getByRole('button', { name: 'Refresh list' })).toBeVisible();
  await context.clearCookies();
  await page.getByRole('button', { name: 'Refresh list' }).click();
  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fdisputes$/);
  await expect(page.locator('main')).not.toContainText(owned.reference);
  await page.getByLabel('Username', { exact: true }).fill('customer.other');
  await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByText('No disputes submitted yet', { exact: true })).toBeVisible();
  expect((await page.request.get(`/api/v1/disputes/${owned.id}`)).status()).toBe(404);
  expect((await page.request.get(`/api/v1/transactions/${owned.transactionId}`)).status()).toBe(404);
  expect((await page.request.get(`/api/v1/transactions/${owned.transactionId}/dispute-eligibility`)).status()).toBe(404);
  const other: AuthSession = await (await page.request.get('/api/v1/auth/me')).json();
  const crossCustomer = await page.request.post('/api/v1/disputes', {
    headers: { 'X-CSRF-TOKEN': other.csrfToken },
    data: { transactionId: owned.transactionId, reasonCode: 'duplicateCharge', description },
  });
  expect(crossCustomer.status()).toBe(422);
  const withoutCsrf = await page.request.post('/api/v1/disputes', {
    data: { transactionId: transactionId(99), reasonCode: 'duplicateCharge', description },
  });
  expect(withoutCsrf.status()).toBe(400);
  expect((await withoutCsrf.json()).code).toBe('auth.csrfInvalid');
  await page.goto(`/disputes/${owned.id}`);
  await expect(page.getByRole('status')).toHaveText('Dispute not found.');
  await page.goto('/');
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await expect(page.locator('tbody')).toContainText('Other synthetic customer');
  await capture(page, info, 'isolated-customer');
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
});
