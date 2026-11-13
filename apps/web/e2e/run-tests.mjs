import { randomUUID } from 'node:crypto';
import { rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const database = join(tmpdir(), `clearcase-e2e-${randomUUID()}.db`);
try {
  const result = spawnSync(process.execPath, [
    resolve('node_modules', '@playwright', 'test', 'cli.js'), 'test', ...process.argv.slice(2),
  ], {
    stdio: 'inherit',
    env: { ...process.env, CLEARCASE_E2E_DATABASE: database, CLEARCASE_E2E_PASSWORD: `Cc1!${randomUUID()}` },
  });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
} finally {
  // Playwright has stopped its owned web servers before this process returns.
  for (const suffix of ['', '-wal', '-shm', '-journal']) rmSync(database + suffix, { force: true });
}
