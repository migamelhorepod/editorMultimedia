import { test, expect, run } from './support';

test('POC-003.2: an active import blocks switching and deactivation until completion', async () => {
  const results = await run([
    { op: 'beginImportLifecycle' },
    { op: 'changeLibraryLifecycle' },
    { op: 'deactivateLibraryLifecycle' },
    { op: 'finishImportLifecycle' },
    { op: 'changeLibraryLifecycle' },
  ]);
  expect(results.every(r => r.error === null)).toBeTruthy();
  expect((results[0].value as { active: number }).active).toBe(1);
  const blocked = results[1].value as { allowed: boolean; popupMessage: string };
  expect(blocked.allowed).toBe(false);
  expect(blocked.popupMessage).toBe('Aguarde o processamento ser finalizado');
  expect((results[2].value as { allowed: boolean }).allowed).toBe(false);
  expect((results[4].value as { allowed: boolean }).allowed).toBe(true);
});

test('POC-003.2: unavailable storage stops affected tasks and forbids new imports', async () => {
  const results = await run([
    { op: 'beginImportLifecycle' },
    { op: 'diskOfflineLifecycle' },
    { op: 'beginImportLifecycle' },
    { op: 'diskOnlineLifecycle' },
    { op: 'beginImportLifecycle' },
  ]);
  const lost = results[1].value as { available: boolean; terminated: number; active: number };
  expect(lost).toEqual({ available: false, terminated: 1, active: 0 });
  expect(results[2].error?.type).toBe('IOException');
  expect((results[3].value as { available: boolean }).available).toBe(true);
  expect(results[4].error).toBeNull();
});
