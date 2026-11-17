import { expect, Page, test } from '@playwright/test';

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
  const response = page.waitForResponse(item => item.url().endsWith('/api/v1/auth/logout')
    && item.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  expect((await response).status()).toBe(204);
}

test('Agent processes a card dispute through decision, mock settlement and closure', async ({ page }) => {
  await signIn(page, 'customer.demo');
  // Use a different seeded card transaction from the escalation test to preserve isolation.
  await page.goto('/transactions/00000000-0000-0000-0000-000000000007');
  await page.getByRole('combobox', { name: 'Dispute reason' }).selectOption({ index: 1 });
  await page.getByRole('textbox', { name: 'Describe the issue' })
    .fill('Please review the synthetic card payment through to an outcome.');
  const submission = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/disputes') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
  const response = await submission;
  expect(response.status()).toBe(201);
  const dispute = await response.json() as { id: string };

  await signOut(page);
  await signIn(page, 'agent.demo');
  await page.goto(`/agent/disputes/${dispute.id}`);
  await expect(page.getByRole('heading', { name: 'Case investigation' })).toBeVisible();

  for (const label of [
    'Initiate chargeback', 'Send to acquirer', 'Merchant accepts',
    'Request settlement', 'Process mock settlement', 'Close case',
  ]) {
    const commandButton = page.getByRole('button', { name: label, exact: true });
    await expect(commandButton).toBeEnabled();
    await commandButton.click();
    const commandResponse = page.waitForResponse(item =>
      new URL(item.url()).pathname === `/api/v1/agent/disputes/${dispute.id}/commands`
      && item.request().method() === 'POST');
    await page.getByRole('button', { name: `Confirm ${label}`, exact: true }).click();
    expect((await commandResponse).status()).toBe(200);
    await expect(page.getByText(`${label} recorded.`, { exact: false })).toBeVisible();
  }
  await expect(page.getByText('Closed', { exact: true }).first()).toBeVisible();

  await signOut(page);
  await signIn(page, 'customer.demo');
  await page.goto(`/disputes/${dispute.id}`);
  await expect(page.getByText('Case closed')).toBeVisible();
  await expect(page.getByText('Card dispute started')).toBeVisible();
  await expect(page.getByText('Refund result received')).toBeVisible();
});
