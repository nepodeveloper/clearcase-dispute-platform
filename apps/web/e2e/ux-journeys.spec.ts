import { expect, Page, test } from '@playwright/test';

type Role = 'customer.demo' | 'customer.other' | 'agent.demo';

const pdf = {
  name: 'receipt.pdf',
  mimeType: 'application/pdf',
  buffer: Buffer.from('%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n'),
};

async function signIn(page: Page, username: Role) {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(username);
  await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', {
    name: username === 'agent.demo' ? 'Dispute work queue' : 'Your transactions',
    exact: true,
  })).toBeVisible();
}

async function signOut(page: Page) {
  const response = page.waitForResponse(item => item.url().endsWith('/api/v1/auth/logout')
    && item.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  expect((await response).status()).toBe(204);
  await expect(page).toHaveURL(/\/login$/);
}

async function submitDispute(page: Page, transactionNumber: number) {
  await page.goto(`/transactions/00000000-0000-0000-0000-00000000000${transactionNumber}`);
  await page.getByRole('combobox', { name: 'Dispute reason' }).selectOption({ index: 1 });
  await page.getByRole('textbox', { name: 'Describe the issue' })
    .fill('Please investigate this synthetic transaction for the journey test.');
  const submission = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/disputes') && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Submit dispute', exact: true }).click();
  const response = await submission;
  expect(response.status()).toBe(201);
  const dispute = await response.json() as { id: string; reference: string };
  await expect(page).toHaveURL(new RegExp(`/disputes/${dispute.id}$`));
  return dispute;
}

test('customer journey: expected date, evidence, notifications, support and withdrawal', async ({ page }) => {
  await signIn(page, 'customer.demo');
  const kept = await submitDispute(page, 1);

  await expect(page.getByTestId('expected-resolution')).toContainText('Expected resolution by');
  await expect(page.getByRole('heading', { name: 'Contact support' })).toBeVisible();
  await expect(page.getByRole('link', { name: /@/ }).first()).toBeVisible();

  // Layout regression: documents sit directly under the timeline and support under the summary column,
  // not in a separate row that leaves a blank gap beneath a shorter column.
  const timeline = (await page.locator('section[aria-labelledby="case-timeline-heading"]').boundingBox())!;
  const documents = (await page.locator('section[aria-labelledby="evidence-heading"]').boundingBox())!;
  expect(documents.y - (timeline.y + timeline.height)).toBeLessThan(40);
  const goodToKnow = (await page.locator('aside.info-panel').boundingBox())!;
  const support = (await page.locator('aside[aria-labelledby="support-heading"]').boundingBox())!;
  expect(support.y - (goodToKnow.y + goodToKnow.height)).toBeLessThan(40);

  // Evidence: the pre-check rejects unsupported files, a real PDF is scanned, stored and listed.
  await expect(page.getByTestId('evidence-empty')).toBeVisible();
  await page.locator('#evidence-file').setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('plain') });
  await page.getByRole('button', { name: /^Upload/ }).click();
  await expect(page.locator('#evidence-error').getByRole('alert')).toBeVisible();

  await page.locator('#evidence-file').setInputFiles(pdf);
  await page.locator('#evidence-note').fill('Merchant receipt');
  const upload = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/disputes/${kept.id}/evidence`) && response.request().method() === 'POST');
  await page.getByRole('button', { name: /^Upload/ }).click();
  const uploaded = await upload;
  expect(uploaded.status()).toBe(201);
  expect(uploaded.request().headers()['x-csrf-token']).toBeTruthy();
  await expect(page.getByLabel('Uploaded documents')).toContainText('receipt.pdf');
  const download = await page.request.get(`/api/v1/disputes/${kept.id}/evidence/${(await uploaded.json() as { id: string }).id}/content`);
  expect(download.status()).toBe(200);
  expect(download.headers()['x-content-type-options']).toBe('nosniff');
  expect(download.headers()['content-disposition']).toContain('attachment');

  // Notifications: the submission produced an unread update that can be cleared.
  await page.goto('/');
  await expect(page.getByTestId('notification-badge')).toBeVisible();
  await page.getByTestId('notification-bell').click();
  await expect(page).toHaveURL(/\/notifications$/);
  await expect(page.getByLabel('Notifications, newest first')).toContainText(kept.reference);
  const marked = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/notifications/read') && response.request().method() === 'POST');
  await page.getByTestId('mark-all-read').click();
  expect((await marked).status()).toBe(204);
  await expect(page.getByTestId('notification-badge')).toHaveCount(0);

  // Withdrawal on a second case; the first stays open for the staff journey.
  const withdrawn = await submitDispute(page, 2);
  await page.getByTestId('withdraw-dispute').click();
  const dialog = page.getByRole('dialog', { name: 'Withdraw this dispute?' });
  await expect(dialog).toBeVisible();
  const withdrawal = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/disputes/${withdrawn.id}/withdrawals`) && response.request().method() === 'POST');
  await dialog.locator('button[type="submit"]').click();
  expect((await withdrawal).status()).toBe(200);
  await expect(page.getByText('You withdrew this dispute.').first()).toBeVisible();
  await expect(page.getByTestId('withdraw-dispute')).toHaveCount(0);
  await expect(page.getByTestId('evidence-closed')).toBeVisible();

  // Ownership: another customer cannot see or download this customer's evidence.
  await signOut(page);
  await signIn(page, 'customer.other');
  const evidenceList = await page.request.get(`/api/v1/disputes/${kept.id}/evidence`);
  expect(evidenceList.status()).toBe(404);
  await signOut(page);

  // Staff can read the customer's evidence.
  await signIn(page, 'agent.demo');
  await page.request.get(`/api/v1/agent/disputes/${kept.id}/evidence`).then(async response => {
    expect(response.status()).toBe(200);
    expect(JSON.stringify(await response.json())).toContain('receipt.pdf');
  });
});

test('staff journey: claim, filter, shortcuts, operation status and customer evidence', async ({ page }) => {
  // Create a case with evidence as the customer, then work it as an agent.
  await signIn(page, 'customer.demo');
  const dispute = await submitDispute(page, 3);
  await page.locator('#evidence-file').setInputFiles(pdf);
  const upload = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/disputes/${dispute.id}/evidence`) && response.request().method() === 'POST');
  await page.getByRole('button', { name: /^Upload/ }).click();
  expect((await upload).status()).toBe(201);
  await signOut(page);

  await signIn(page, 'agent.demo');
  const row = page.locator('tbody tr', { hasText: dispute.reference });
  await expect(row).toBeVisible();
  await expect(row.getByTestId('sla-cell')).toContainText(/On track|Due soon|Breached/);
  await expect(row.getByTestId('assignee-cell')).toContainText('Unassigned');

  const claim = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}/assignment`) && response.request().method() === 'POST');
  await row.getByTestId('claim').click();
  expect((await claim).status()).toBe(200);
  await expect(row.getByTestId('assignee-cell')).toContainText('agent.demo');

  const mine = page.waitForResponse(response => response.url().includes('assignment=mine'));
  await page.getByTestId('assignment-filter').selectOption('mine');
  await mine;
  await expect(page.locator('tbody tr', { hasText: dispute.reference })).toBeVisible();
  const unassigned = page.waitForResponse(response => response.url().includes('assignment=unassigned'));
  await page.getByTestId('assignment-filter').selectOption('unassigned');
  await unassigned;
  await expect(page.locator('tbody tr', { hasText: dispute.reference })).toHaveCount(0);

  // Keyboard shortcut: "/" focuses the search box when focus is not already in a field.
  await page.locator('h1').focus();
  await page.keyboard.press('/');
  await expect(page.getByRole('searchbox', { name: 'Search cases' })).toBeFocused();

  await page.goto(`/agent/disputes/${dispute.id}`);
  await expect(page.getByTestId('assignee')).toContainText('agent.demo');
  await expect(page.getByRole('heading', { name: 'Operation status' })).toBeVisible();
  await expect(page.getByTestId('operations-empty')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Customer evidence' })).toBeVisible();
  await expect(page.getByLabel('Customer evidence', { exact: true }).last()).toContainText('receipt.pdf');
  const release = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}/assignment`) && response.request().method() === 'DELETE');
  await page.getByTestId('release').click();
  expect((await release).status()).toBe(200);
  await expect(page.getByTestId('assignee')).toContainText('Unassigned');
});

test('theme preference is applied and remembered, including on the sign-in page', async ({ page }) => {
  // No sign-in: the toggle is available before authentication and the login endpoint allows 5 attempts a minute.
  await page.goto('/login');
  await page.getByRole('group', { name: 'Colour theme' }).getByRole('button', { name: 'Dark', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await page.evaluate(() => Object.keys(localStorage).sort())).toEqual(['clearcase.theme']);
  await page.getByRole('group', { name: 'Colour theme' }).getByRole('button', { name: 'System', exact: true }).click();
  await expect(page.locator('html')).not.toHaveAttribute('data-theme', /.+/);
});
