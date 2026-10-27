import { test, expect, run, wave } from './support';
import { mkdir, access, readdir } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

test('BDD: Given external WAV, When imported into active physical library, Then file and timeline reference use library path', async ({ folder }) => {
  const source = await wave(folder, 'physical-source.wav', 5);
  const library = path.join(folder, 'library');
  await mkdir(library);
  const id = randomUUID();
  const states = await run([
    { op: 'importPhysical', path: source, library, seconds: 0, id },
    { op: 'importPhysical', path: source, library, seconds: 0, id: randomUUID() },
    { op: 'deleteCatalogFirst' },
    { op: 'undo' },
  ]);
  for (const state of states) expect(state.error).toBeNull();
  expect(states[0].project.media).toHaveLength(1);
  expect(states[0].project.clips).toHaveLength(1);
  console.log('Physical import path check:', JSON.stringify({ library, stored: states[0].project.media[0].sourcePath, importResult: states[0].value }));
  expect(states[0].project.media[0].sourcePath.toLowerCase().startsWith(library.toLowerCase())).toBe(true);
  expect(states[1].value).toBe('duplicate');
  expect(await readdir(library)).toEqual(['physical-source.wav']);
  expect(states[2].project.media).toHaveLength(0);
  expect(states[2].project.clips).toHaveLength(0);
  expect(states[3].project.media).toHaveLength(1);
  await access(source);
  await access(path.join(library, 'physical-source.wav'));
});
