import { expect, Page, test, TestInfo } from '@playwright/test';

const transactionId = '00000000-0000-0000-0000-000000000001';
const customerStatement = 'Please investigate this synthetic card transaction for the assessment.';
const privateNote = 'Merchant verification is pending before an investigation decision.';

async function signIn(page: Page, username: 'customer.demo' | 'agent.demo' | 'supervisor.demo') {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(username);
  await page.getByLabel('Password', { exact: true }).fill(process.env['CLEARCASE_E2E_PASSWORD']!);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', {
    name: username === 'customer.demo' ? 'Your transactions'
      : username === 'agent.demo' ? 'Dispute work queue' : 'Escalations queue',
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
  const layout = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
    overflowing: Array.from(document.querySelectorAll('body *'))
      .map(element => {
        const bounds = element.getBoundingClientRect();
        return {
          element: `${element.tagName.toLowerCase()}${typeof element.className === 'string' && element.className
            ? `.${element.className.trim().replace(/\s+/g, '.')}` : ''}`,
          right: Math.round(bounds.right),
          scrollWidth: element.scrollWidth,
          clientWidth: element.clientWidth,
        };
      })
      .filter(element => element.right > window.innerWidth || element.scrollWidth > element.clientWidth + 1)
      .slice(0, 8),
  }));
  expect(layout.scrollWidth, JSON.stringify(layout))
    .toBeLessThanOrEqual(page.viewportSize()!.width);
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(name, { path, contentType: 'image/png' });
}

test('Agent escalation is independently reviewed and private notes remain customer-safe', async ({ page }, info) => {
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
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(page.getByRole('link', { name: dispute.reference, exact: true })).toBeVisible();
  await capture(page, info, `${info.project.name}-agent-queue`);

  const detailResponse = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}`));
  await page.getByRole('link', { name: dispute.reference, exact: true }).click();
  expect((await detailResponse).status()).toBe(200);
  await expect(page.getByText(customerStatement, { exact: true })).toBeVisible();
  await page.getByLabel('Add an internal note').fill(privateNote);
  const noteResponsePromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}/notes`)
      && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Save internal note', exact: true }).click();
  const noteResponse = await noteResponsePromise;
  expect(noteResponse.status()).toBe(201);
  expect(noteResponse.request().headers()['x-csrf-token']).toBeTruthy();
  expect(noteResponse.request().headers()['idempotency-key']).toBeTruthy();
  await expect(page.getByText(privateNote, { exact: true })).toBeVisible();

  const escalationNote = 'Please complete an independent authorization review for this case.';
  await page.getByRole('button', { name: 'Escalate case', exact: true }).click();
  await page.getByLabel('Internal investigation note').fill(escalationNote);
  const escalationResponsePromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/agent/disputes/${dispute.id}/escalations`)
      && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Submit escalation', exact: false }).click();
  const escalationResponse = await escalationResponsePromise;
  expect(escalationResponse.status()).toBe(201);
  expect(escalationResponse.request().headers()['x-csrf-token']).toBeTruthy();
  expect(escalationResponse.request().headers()['idempotency-key']).toBeTruthy();
  const escalation = await escalationResponse.json() as { id: string; reference: string };
  await expect(page.getByText(escalation.reference, { exact: false })).toBeVisible();
  await page.reload();
  await expect(page.getByText(privateNote, { exact: true })).toBeVisible();
  await capture(page, info, `${info.project.name}-investigation`);

  await signOut(page);
  await signIn(page, 'supervisor.demo');
  const supervisorQueue = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/v1/supervisor/escalations');
  await page.goto('/agent/escalations');
  expect((await supervisorQueue).status()).toBe(200);
  await page.getByRole('link', { name: escalation.reference, exact: true }).click();
  await expect(page.getByText(escalationNote, { exact: true })).toBeVisible();
  await capture(page, info, `${info.project.name}-supervisor-detail`);

  const acknowledgementPromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/supervisor/escalations/${escalation.id}/acknowledgements`)
      && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Acknowledge escalation', exact: true }).click();
  const acknowledgement = await acknowledgementPromise;
  expect(acknowledgement.status()).toBe(200);
  expect(acknowledgement.request().headers()['x-csrf-token']).toBeTruthy();
  expect(acknowledgement.request().headers()['idempotency-key']).toBeTruthy();

  const resolutionNote = 'Independent checks are complete; investigation may continue.';
  await page.getByLabel('Resolution note').fill(resolutionNote);
  const resolutionPromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/supervisor/escalations/${escalation.id}/resolutions`)
      && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Resolve escalation', exact: true }).click();
  const resolution = await resolutionPromise;
  expect(resolution.status()).toBe(200);
  expect(resolution.request().headers()['x-csrf-token']).toBeTruthy();
  expect(resolution.request().headers()['idempotency-key']).toBeTruthy();
  const resolved = await resolution.json() as {
    status: string; caseStatus: string; decisionStatus: string; financialStatus: string;
  };
  expect(resolved).toMatchObject({
    status: 'Resolved',
    caseStatus: 'UnderReview',
    decisionStatus: 'Pending',
    financialStatus: 'NotInitiated',
  });
  await expect(page.getByText(resolutionNote, { exact: true })).toBeVisible();

  await signOut(page);
  await signIn(page, 'customer.demo');
  const supervisorCase = await page.request.get(`/api/v1/disputes/${dispute.id}`);
  expect(supervisorCase.status()).toBe(200);
  const customerProjection = await supervisorCase.text();
  expect(customerProjection).not.toContain(privateNote);
  expect(customerProjection).not.toContain(escalationNote);
  expect(customerProjection).not.toContain(resolutionNote);
  expect((await page.request.get(`/api/v1/agent/disputes/${dispute.id}`)).status()).toBe(403);
  const customerCase = await page.request.get(`/api/v1/disputes/${dispute.id}`);
  expect(customerCase.status()).toBe(200);
  await page.goto(`/disputes/${dispute.id}`);
  await expect(page.getByText(customerStatement, { exact: true })).toBeVisible();
  await expect(page.getByText(privateNote, { exact: true })).toHaveCount(0);
  await expect(page.getByText(resolutionNote, { exact: true })).toHaveCount(0);

  await signOut(page);
  expect((await page.request.get('/api/v1/agent/disputes')).status()).toBe(401);
  await page.goto('/agent/disputes');
  await expect(page.getByRole('heading', { name: 'Sign in to ClearCase' })).toBeVisible();
});


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
