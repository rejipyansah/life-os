import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const backend = path.join(root, 'backend');
const frontend = path.join(root, 'frontend', 'LifeOS.Web');
const apiPort = process.env.LIFEOS_E2E_API_PORT ?? '5272';
const frontendPort = process.env.LIFEOS_E2E_FRONTEND_PORT ?? '5174';
const repetitions = Number.parseInt(process.env.LIFEOS_MOBILE_PERF_REPEATS ?? '3', 10);
const mockApi = process.env.LIFEOS_MOBILE_PERF_MOCK === '1';

if (!Number.isInteger(repetitions) || repetitions <= 0) {
  console.error('LIFEOS_MOBILE_PERF_REPEATS must be a positive integer.');
  process.exit(1);
}
const connection = process.env.LIFEOS_E2E_POSTGRES_CONNECTION
  ?? process.env.LIFEOS_TEST_POSTGRES_CONNECTION;

if (!mockApi && !connection) {
  console.error('Set LIFEOS_E2E_POSTGRES_CONNECTION or LIFEOS_TEST_POSTGRES_CONNECTION to a disposable PostgreSQL database.');
  process.exit(1);
}

if (!mockApi) {
  const databaseName = /(?:Database|Initial Catalog)=([^;]+)/i.exec(connection)?.[1];
  if (!databaseName || !/(test|e2e)/i.test(databaseName)) {
    console.error('The mobile performance test database name must include "test" or "e2e".');
    process.exit(1);
  }
}

const sharedEnv = {
  ...process.env,
  LIFEOS_TEST_POSTGRES_CONNECTION: connection,
  LIFEOS_E2E_POSTGRES_CONNECTION: connection,
  ConnectionStrings__DefaultConnection: connection,
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
  LIFEOS_E2E_API_PORT: apiPort,
  LIFEOS_E2E_FRONTEND_PORT: frontendPort,
};

function run(label, command, args, cwd, env = sharedEnv) {
  console.log(`\n==> ${label}`);
  const result = spawnSync(command, args, {
    cwd,
    env,
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
  if (result.error) {
    console.error(`${label} could not start: ${result.error.message}`);
    process.exit(1);
  }
  if (result.status !== 0) process.exit(result.status ?? 1);
}

if (!mockApi) {
  run('Restore EF tooling', 'dotnet', ['tool', 'restore'], backend);
  run('Apply migrations to disposable test database', 'dotnet', [
    'tool', 'run', 'dotnet-ef', '--', 'database', 'update',
    '--project', 'LifeOS.Api/LifeOS.Api.csproj',
    '--startup-project', 'LifeOS.Api/LifeOS.Api.csproj',
    '--configuration', 'Release',
  ], backend);
}
run('Build production frontend pointing to local API', 'npm', ['run', 'build', '--prefix', frontend], root, {
  ...sharedEnv,
  VITE_API_BASE_URL: `http://127.0.0.1:${apiPort}`,
});
run(`Run Pixel 7 mobile performance scenario (${mockApi ? 'mock API' : 'disposable PostgreSQL API'})`, 'npx', [
  'playwright', 'test', '--grep', '@mobile-perf', '--repeat-each', String(repetitions),
], frontend, {
  ...sharedEnv,
  LIFEOS_E2E_MOBILE: '1',
  LIFEOS_E2E_PRODUCTION: '1',
  LIFEOS_MOBILE_PERF_MOCK: mockApi ? '1' : '0',
});
