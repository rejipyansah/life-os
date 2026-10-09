import { spawnSync } from 'node:child_process';

const integrationProject = 'backend/LifeOS.IntegrationTests/LifeOS.IntegrationTests.csproj';
const checks = [
  ['Backend unit/service tests', 'dotnet', ['test', 'backend/LifeOS.Tests/LifeOS.Tests.csproj', '--configuration', 'Release']],
  ['Build projection benchmark tool', 'dotnet', ['build', 'backend/FinanceProjectionBenchmark/FinanceProjectionBenchmark.csproj', '--configuration', 'Release']],
  ['Frontend tests', 'npm', ['run', 'test', '--prefix', 'frontend/LifeOS.Web']],
  ['Frontend lint', 'npm', ['run', 'lint', '--prefix', 'frontend/LifeOS.Web']],
  ['Frontend production build', 'npm', ['run', 'build', '--prefix', 'frontend/LifeOS.Web']],
];

if (process.env.LIFEOS_TEST_POSTGRES_CONNECTION) {
  checks.splice(1, 0, [
    'PostgreSQL integration tests',
    'dotnet',
    ['test', integrationProject, '--configuration', 'Release'],
  ]);
} else {
  checks.splice(1, 0, [
    'HTTP/API integration tests',
    'dotnet',
    ['test', integrationProject, '--configuration', 'Release', '--filter', 'FullyQualifiedName~FinanceEndpointTests'],
  ]);
  console.log('Skipping PostgreSQL-provider tests (LIFEOS_TEST_POSTGRES_CONNECTION is not set).');
}

if (process.env.LIFEOS_E2E_POSTGRES_CONNECTION) {
  checks.push([
    'Playwright end-to-end tests',
    'npm',
    ['run', 'test:e2e', '--prefix', 'frontend/LifeOS.Web'],
  ]);
} else {
  console.log('Skipping browser E2E tests (LIFEOS_E2E_POSTGRES_CONNECTION is not set).');
}

for (const [label, command, args] of checks) {
  console.log(`\n==> ${label}`);
  const result = spawnSync(command, args, { stdio: 'inherit', env: process.env, shell: process.platform === 'win32' });
  if (result.error) {
    console.error(`${label} could not start: ${result.error.message}`);
    process.exit(1);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

console.log('\nAll configured checks passed.');
