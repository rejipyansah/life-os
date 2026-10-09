import { spawn, spawnSync } from 'node:child_process';

const backendDirectory = 'backend';
const migrations = [
  ['Restore .NET tools', ['tool', 'restore']],
  [
    'Apply database migrations',
    [
      'tool',
      'run',
      'dotnet-ef',
      '--',
      'database',
      'update',
      '--project',
      'LifeOS.Api/LifeOS.Api.csproj',
      '--startup-project',
      'LifeOS.Api/LifeOS.Api.csproj',
    ],
  ],
];

for (const [label, args] of migrations) {
  console.log(`\n==> ${label}`);
  const result = spawnSync('dotnet', args, {
    cwd: backendDirectory,
    stdio: 'inherit',
    env: process.env,
    shell: process.platform === 'win32',
  });

  if (result.error) {
    console.error(`${label} could not start: ${result.error.message}`);
    process.exit(1);
  }
  if (result.status !== 0) process.exit(result.status ?? 1);
}

const processes = [
  ['Backend API', 'dotnet', ['run', '--project', 'backend/LifeOS.Api/LifeOS.Api.csproj']],
  ['Frontend', 'npm', ['run', 'dev', '--prefix', 'frontend/LifeOS.Web']],
];

const children = processes.map(([name, command, args]) => {
  console.log(`Starting ${name}...`);
  const child = spawn(command, args, {
    stdio: 'inherit',
    env: process.env,
    shell: process.platform === 'win32',
  });

  child.on('error', (error) => {
    console.error(`${name} could not start: ${error.message}`);
    stopAll(1);
  });

  child.on('exit', (code) => {
    if (!shuttingDown) {
      console.log(`${name} stopped${code === 0 ? '.' : ` with exit code ${code}.`}`);
      stopAll(code ?? 1);
    }
  });

  return child;
});

let shuttingDown = false;

function stopAll(exitCode = 0) {
  if (shuttingDown) return;
  shuttingDown = true;

  for (const child of children) {
    if (!child.killed) child.kill('SIGTERM');
  }

  process.exitCode = exitCode;
}

process.on('SIGINT', () => stopAll(0));
process.on('SIGTERM', () => stopAll(0));
