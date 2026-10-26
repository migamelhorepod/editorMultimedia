import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 60_000,
  reporter: [['list'], ['html', { open: 'never' }], ['junit', { outputFile: 'test-results/integration.xml' }]],
});
