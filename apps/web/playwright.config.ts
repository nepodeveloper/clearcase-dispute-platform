import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';

const password = process.env['CLEARCASE_E2E_PASSWORD'];
const database = process.env['CLEARCASE_E2E_DATABASE'];
if (!password || !database) throw new Error('Run browser tests through npm run test:e2e for isolated credentials and database cleanup.');

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env['CI']),
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:4302',
    trace: 'off',
    screenshot: 'only-on-failure',
    reducedMotion: 'reduce',
  },
  projects: [
    { name: 'authentication', testMatch: 'auth.spec.ts', use: { viewport: { width: 1440, height: 1000 } } },
    { name: 'customer-desktop', testMatch: 'customer.spec.ts',
      use: { viewport: { width: 1440, height: 1000 }, timezoneId: 'Africa/Johannesburg' } },
    { name: 'customer-mobile', testMatch: 'customer.spec.ts',
      use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, timezoneId: 'Africa/Johannesburg' } },
    { name: 'agent-desktop', testMatch: 'agent.spec.ts', use: { viewport: { width: 1440, height: 1000 } } },
    { name: 'agent-mobile', testMatch: 'agent.spec.ts',
      use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } },
  ],
  webServer: [{
    command: `dotnet run --project "${resolve('..', 'api', 'src', 'ClearCase.Api')}" --configuration Release --no-launch-profile --urls http://127.0.0.1:5101`,
    url: 'http://127.0.0.1:5101/health/ready',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      Persistence__DatabasePath: database,
      Authentication__EnableDevelopmentCompatibilityPath: 'false',
      Authentication__SeedPasswords__Customer: password,
      Authentication__SeedPasswords__OtherCustomer: password,
      Authentication__SeedPasswords__Agent: password,
      Authentication__SeedPasswords__Supervisor: password,
      // Fixed seed dates must not make browser journeys expire; policy boundaries have clocked API tests.
      DisputeEligibility__Card__DemoWindowEnabled: 'false',
    },
    reuseExistingServer: false,
    timeout: 120000,
  }, {
    command: `npm start -- --host 127.0.0.1 --port 4302 --prebundle=false --proxy-config "${resolve('e2e', 'proxy.conf.json')}"`,
    url: 'http://127.0.0.1:4302/login',
    reuseExistingServer: false,
    timeout: 120000,
  }],
});
