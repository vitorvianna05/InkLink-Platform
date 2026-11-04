import { defineConfig } from '@playwright/test';

// Opt-in: uses only the isolated P09 backend and synthetic accounts. Never intercepts APIs.
if (process.env.INKLINK_RUN_REAL_E2E !== '1') throw new Error('Set INKLINK_RUN_REAL_E2E=1 after preparing the isolated P09 environment.');
export default defineConfig({
  testDir: './e2e-real',
  workers: 1,
  fullyParallel: false,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  reporter: 'list',
  outputDir: 'test-results/integration',
  use: {
    baseURL: 'https://localhost:5173',
    ignoreHTTPSErrors: false,
    trace: 'off', // OIDC/token/password traffic must not be persisted in test artifacts.
    screenshot: 'off',
    video: 'off',
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_PATH ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_PATH } : undefined,
  },
});
