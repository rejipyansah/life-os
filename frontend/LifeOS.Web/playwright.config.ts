import { defineConfig, devices } from '@playwright/test';

const isMobileRun = process.env.LIFEOS_E2E_MOBILE === '1';
const isProductionRun = process.env.LIFEOS_E2E_PRODUCTION === '1';
const isMockApiRun = process.env.LIFEOS_MOBILE_PERF_MOCK === '1';
const apiPort = process.env.LIFEOS_E2E_API_PORT ?? '5271';
const frontendPort = process.env.LIFEOS_E2E_FRONTEND_PORT ?? '5173';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${frontendPort}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...(isMobileRun ? devices['Pixel 7'] : devices['Desktop Chrome']),
  },
  webServer: [
    ...(isMockApiRun ? [] : [
    {
      command: 'dotnet run --no-build --no-launch-profile --project ../../backend/LifeOS.Api/LifeOS.Api.csproj --configuration Release',
      cwd: process.cwd(),
      port: Number(apiPort),
      reuseExistingServer: false,
      timeout: 120_000,
    },
    ]),
    {
      command: isProductionRun
        ? `node ../../scripts/mobile-static-server.mjs`
        : `npm run dev -- --host 127.0.0.1 --port ${frontendPort} --strictPort`,
      cwd: process.cwd(),
      port: Number(frontendPort),
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
});
