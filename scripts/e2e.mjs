import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const backend = path.join(root, 'backend');
const frontend = path.join(root, 'frontend', 'LifeOS.Web');
const connection = process.env.LIFEOS_E2E_POSTGRES_CONNECTION;

if (!connection) {
  console.error('Set LIFEOS_E2E_POSTGRES_CONNECTION to a dedicated, disposable E2E database.');
  process.exit(1);
}

const databaseName = /(?:Database|Initial Catalog)=([^;]+)/i.exec(connection)?.[1];
if (!databaseName || !/(test|e2e)/i.test(databaseName)) {
  console.error('The E2E connection string database name must include "test" or "e2e".');
  process.exit(1);
}

const env = {
  ...process.env,
  LIFEOS_E2E_POSTGRES_CONNECTION: connection,
  ConnectionStrings__DefaultConnection: connection,
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: 'http://localhost:5271',
};

function run(label, command, args, cwd) {
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

run('Restore pinned EF CLI tool', 'dotnet', ['tool', 'restore'], backend);
run('Apply API migrations to the isolated E2E database', 'dotnet', [
  'tool', 'run', 'dotnet-ef', '--', 'database', 'update',
  '--project', 'LifeOS.Api/LifeOS.Api.csproj',
  '--startup-project', 'LifeOS.Api/LifeOS.Api.csproj',
  '--configuration', 'Release',
], backend);
run('Run Playwright browser tests', 'npx', ['playwright', 'test'], frontend);
