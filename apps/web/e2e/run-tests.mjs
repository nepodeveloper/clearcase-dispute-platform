import { randomUUID } from 'node:crypto';
import { rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const cliArgs = process.argv.slice(2);
const projectNames = [];
const otherArgs = [];
for (let index = 0; index < cliArgs.length; index++) {
  const arg = cliArgs[index];
  if (arg === '--project' || arg === '-p') {
    const project = cliArgs[++index];
    if (project) projectNames.push(project);
  } else if (arg.startsWith('--project=')) {
    projectNames.push(arg.slice('--project='.length));
  } else {
    otherArgs.push(arg);
  }
}

const selectedProjects = projectNames.length > 0
  ? projectNames
  : ['authentication', 'customer-desktop', 'customer-mobile', 'agent-desktop', 'agent-mobile',
    'agent-decision-desktop', 'agent-decision-mobile', 'ux-journeys'];

// One process and database per project keeps login rate limits and test data from leaking between them.
for (const project of selectedProjects) {
  const database = join(tmpdir(), `clearcase-e2e-${randomUUID()}.db`);
  const output = resolve('test-results', project);
  try {
    const result = spawnSync(process.execPath, [
      resolve('node_modules', '@playwright', 'test', 'cli.js'), 'test',
      '--output', output, '--project', project, ...otherArgs,
    ], {
      stdio: 'inherit',
      env: { ...process.env, CLEARCASE_E2E_DATABASE: database, CLEARCASE_E2E_PASSWORD: `Cc1!${randomUUID()}` },
    });
    if (result.error) throw result.error;
    if (result.status !== 0) process.exitCode = result.status ?? 1;
  } finally {
    // Playwright has stopped its owned web servers before this process returns.
    for (const suffix of ['', '-wal', '-shm', '-journal']) rmSync(database + suffix, { force: true });
  }
}
