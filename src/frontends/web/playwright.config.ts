import { defineConfig, devices } from '@playwright/test';

// Verificação visual e de teclado no navegador real (Chromium). As respostas do backend
// são simuladas por page.route apenas para estas checagens de interface; o roteiro
// integrado com o backend real está descrito em docs/verification/p10.md.
const port = Number(process.env.INKLINK_E2E_PORT ?? 4173);
const executablePath = process.env.PLAYWRIGHT_CHROMIUM_PATH;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.INKLINK_E2E_BASE_URL ?? `http://localhost:${port}`,
    trace: 'retain-on-failure',
    launchOptions: executablePath ? { executablePath } : undefined,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: process.env.INKLINK_E2E_BASE_URL
    ? undefined
    : { command: `npx vite --port ${port} --strictPort`, port, reuseExistingServer: true, env: { INKLINK_DEV_CERT: '', INKLINK_DEV_KEY: '' } },
});
