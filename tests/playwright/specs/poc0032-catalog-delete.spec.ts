import { test, expect, run, wave } from './support';
import { access } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';

test('BDD: Given a catalog media on timeline, When excluded, Then all occurrences leave catalog and timeline without deleting source', async ({ folder }) => {
  const source = await wave(folder, 'delete-reference.wav', 12);
  const clipId = randomUUID(), repeatedId = randomUUID();
  const states = await run([
    { op: 'import', path: source, seconds: 0, id: clipId },
    { op: 'insertFirstCatalog', seconds: 13, id: repeatedId },
    { op: 'deleteCatalogFirst' },
    { op: 'undo' },
    { op: 'redo' },
  ]);
  expect(states[0].error).toBeNull();
  expect(states[1].error).toBeNull();
  expect(states[1].project.clips.length).toBe(2);
  expect(states[2].error).toBeNull();
  expect(states[2].project.media).toHaveLength(0);
  expect(states[2].project.clips).toHaveLength(0);
  expect(states[3].project.media).toHaveLength(1);
  expect(states[3].project.clips).toHaveLength(2);
  expect(states[4].project.media).toHaveLength(0);
  await access(source);
});
