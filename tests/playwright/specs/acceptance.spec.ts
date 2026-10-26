import { randomUUID as id, createHash } from 'node:crypto';
import { readFile, writeFile, rename, mkdir, readdir } from 'node:fs/promises';
import path from 'node:path';
import { test, expect, run, allOk, wave, media, ticks, exec, type Command } from './support';
const insert = (file: string, clipId: string, seconds = 0): Command => ({ op: 'import', path: file, id: clipId, seconds });
const hash = async (file: string) => createHash('sha256').update(await readFile(file)).digest('hex');

test('CT-001: Dado motor novo / Quando iniciado / Então permanece vazio', async () => {
  const [r] = await run([{ op: 'availability' }]); allOk([r]);
  expect(r.project.clips).toEqual([]); expect(r.project.media).toEqual([]);
  expect(r.canUndo).toBe(false); expect(r.displayDuration).toBe('00:00:00');
});
for (const extension of ['mp3', 'm4a', 'aac', 'flac', 'ogg']) {
  test(`CT-005/006/025: Dado ${extension} real / Quando importar e reabrir / Então conserva duração`, async ({ folder }) => {
    const file = await media(folder, extension), digest = await hash(file);
    const reference = await exec('ffprobe', ['-v', 'quiet', '-show_entries', 'format=duration', '-of', 'json', '-i', file]);
    const measured = ticks(Number(JSON.parse(reference.stdout).format.duration));
    const saved = path.join(folder, 'project.json');
    const results = await run([insert(file, id(), 3), { op: 'save', path: saved }, { op: 'load', path: saved }]); allOk(results);
    expect(results[0].project.clips[0].durationTicks).toBe(measured); expect(measured).toBeGreaterThan(ticks(10));
    expect(results[2].project).toEqual(results[0].project); expect(await hash(file)).toBe(digest);
  });
}
test('CT-002/003/009/018/019: Dado vídeos e áudios / Quando reordenar / Então mantém fontes e vínculos', async ({ folder }) => {
  const video = await media(folder, 'mp4', true), audio = await wave(folder);
  const a = id(), b = id(), child = id(), independent = id(), saved = path.join(folder, 'base.json');
  const initial = await run([insert(video, a), insert(audio, child, 2), insert(audio, id()), { op: 'save', path: saved }]); allOk(initial);
  expect(initial[2].value).toBe('duplicate'); expect(initial[2].project).toEqual(initial[1].project);
  const more = await run([{ op: 'load', path: saved }, { op: 'insert', mediaId: initial[0].project.media[0].id, id: b, seconds: 0 },
    { op: 'insert', mediaId: initial[1].project.media[1].id, id: independent, seconds: 4 },
    { op: 'independent', id: independent }, { op: 'reorder', id: b, index: 0 }]); allOk(more);
  const before = more[3].project.clips, after = more[4].project.clips;
  expect(before.find(c => c.id === b)!.startTicks).toBe(ticks(12)); expect(after.find(c => c.id === a)!.startTicks).toBe(ticks(12));
  expect(after.find(c => c.id === child)!.startTicks).toBe(ticks(14)); expect(after.find(c => c.id === independent)!.startTicks).toBe(ticks(4));
  for (const c of before) { const moved = after.find(x => x.id === c.id)!; expect(moved.sourceStartTicks).toBe(c.sourceStartTicks); expect(moved.durationTicks).toBe(c.durationTicks); }
});
test('CT-004/011–016/024: Dado áudio / Quando cortar, dividir e desfazer / Então restaura estados exatos', async ({ folder }) => {
  const file = await wave(folder), first = id(), right = id();
  const results = await run([insert(file, first, 2), { op: 'trimLeft', id: first, seconds: 4 }, { op: 'resize', id: first, seconds: 7 },
    { op: 'split', id: first, seconds: 7, rightId: right }, { op: 'marker', id: id(), seconds: 6 },
    ...Array.from({ length: 5 }, () => ({ op: 'undo' })), ...Array.from({ length: 5 }, () => ({ op: 'redo' }))]); allOk(results);
  expect(results[1].project.clips[0]).toMatchObject({ startTicks: ticks(4), durationTicks: ticks(10), sourceStartTicks: ticks(2) });
  const split = results[3].project.clips; expect(split[0].sourceStartTicks + split[0].durationTicks).toBe(split[1].sourceStartTicks);
  expect(split.reduce((sum, c) => sum + c.durationTicks, 0)).toBe(ticks(7));
  expect(results[9].project.clips).toEqual([]); expect(results[9].project.media).toEqual([]); expect(results[14].project).toEqual(results[4].project);
});
for (const op of ['resize', 'trimLeft']) {
  test(`CT-014/015: Dado redo / Quando ${op} ultrapassa fonte / Então falha sem mudar estado`, async ({ folder }) => {
    const file = await wave(folder), clip = id();
    const r = await run([insert(file, clip, 3), { op: 'marker', id: id(), seconds: 1 }, { op: 'undo' }, { op, id: clip, seconds: op === 'resize' ? 13 : 2 }, { op: 'redo' }]);
    allOk(r.filter((_, i) => i !== 3)); expect(r[3].error?.type).toBe('MediaReferenceException');
    expect(r[3].project).toEqual(r[2].project); expect(r[3].canRedo).toBe(true); expect(r[4].project).toEqual(r[1].project);
  });
}
test('CT-007/008: Dado mídia inválida ou ffprobe ausente / Quando importar / Então preserva edição', async ({ folder }) => {
  const good = await wave(folder), invalid = path.join(folder, 'invalid.mp3'); await writeFile(invalid, 'invalid media'); const mp3 = await media(folder, 'mp3');
  for (const [file, env] of [[invalid, {}], [mp3, { EDITOR_FFPROBE_PATH: path.join(folder, 'missing-ffprobe.exe') }]] as const) {
    const r = await run([insert(good, id()), { op: 'marker', id: id(), seconds: 1 }, { op: 'undo' }, insert(file, id()), { op: 'redo' }], env);
    allOk(r.filter((_, i) => i !== 3)); expect(r[3].error?.type).toBe('MediaReferenceException'); expect(r[3].error?.message).toContain('ffprobe');
    expect(r[3].project).toEqual(r[2].project); expect(r[3].canRedo).toBe(true); expect(r[4].project).toEqual(r[1].project);
  }
});
test('CT-010: Dado PNG / Quando cortar e dividir / Então origem fica zero', async ({ folder }) => {
  const file = path.join(folder, 'image.png'); await writeFile(file, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1cAAAAASUVORK5CYII=', 'base64'));
  const digest = await hash(file), clip = id(); const r = await run([insert(file, clip, 2), { op: 'trimLeft', id: clip, seconds: 3 }, { op: 'split', id: clip, seconds: 4, rightId: id() }]); allOk(r);
  expect(r[0].project.clips[0].durationTicks).toBe(ticks(4)); for (const c of r[2].project.clips) expect(c.sourceStartTicks).toBe(0); expect(await hash(file)).toBe(digest);
});
test('CT-021/022: Dado nove secundárias / Quando inserir décima / Então falha atômica', async ({ folder }) => {
  const files = await Promise.all(Array.from({ length: 10 }, (_, i) => wave(folder, `a${i}.wav`)));
  const r = await run([...files.slice(0, 9).map(file => insert(file, id())), insert(files[9], id()), { op: 'undo' }, { op: 'redo' }]); allOk(r.filter((_, i) => i !== 9));
  expect(r[9].error?.code).toBe('NO_FREE_TRACK'); expect(r[8].project.clips.map(c => c.track)).toEqual([1,2,3,4,5,6,7,8,9]);
  expect(r[9].project).toEqual(r[8].project); expect(r[10].project.clips).toHaveLength(8); expect(r[11].project).toEqual(r[8].project);
});
test('CT-028: Dado projeto salvo / Quando gravação ou leitura falha / Então preserva dados', async ({ folder }) => {
  const file = await wave(folder), saved = path.join(folder, 'saved.json'), bad = path.join(folder, 'bad.json'), blocked = path.join(folder, 'directory.json');
  await mkdir(blocked); await writeFile(bad, '{ invalid');
  const r = await run([insert(file, id()), { op: 'save', path: saved }, { op: 'marker', id: id(), seconds: 1 }, { op: 'save', path: blocked }, { op: 'load', path: bad }, { op: 'undo' }]);
  allOk(r.filter((_, i) => i !== 3 && i !== 4)); expect(r[3].error).not.toBeNull(); expect(r[4].error?.type).toBe('ProjectFileException');
  expect(r[3].project).toEqual(r[2].project); expect(r[4].project).toEqual(r[2].project); expect(JSON.parse(await readFile(saved, 'utf8'))).toEqual(r[0].project);
  expect(r[5].project).toEqual(r[0].project); expect((await readdir(folder)).some(name => name.endsWith('.tmp'))).toBe(false);
});
test('CT-027/029: Dado fonte ausente / Quando relocalizar e remover referência / Então preserva IDs e arquivo', async ({ folder }) => {
  const file = await wave(folder), moved = path.join(folder, 'moved.wav'), saved = path.join(folder, 'project.json'), clip = id();
  const initial = await run([insert(file, clip, 2), { op: 'trimLeft', id: clip, seconds: 4 }, { op: 'save', path: saved }]); allOk(initial);
  const mediaId = initial[0].project.media[0].id, digest = await hash(file); await rename(file, moved);
  const r = await run([{ op: 'load', path: saved }, { op: 'availability' }, { op: 'relink', id: mediaId, path: moved }, { op: 'undo' }, { op: 'redo' },
    { op: 'removeMedia', id: mediaId }, { op: 'delete', id: clip }, { op: 'removeMedia', id: mediaId }, { op: 'undo' }]);
  allOk(r.filter((_, i) => i !== 5)); expect(r[1].value).toEqual([{ id: mediaId, available: false }]); expect(r[2].project.clips).toEqual(initial[1].project.clips);
  expect(r[2].project.media[0].id).toBe(mediaId); expect(r[3].project).toEqual(r[0].project); expect(r[4].project).toEqual(r[2].project);
  expect(r[5].error?.type).toBe('MediaReferenceException'); expect(r[7].project.media).toEqual([]); expect(r[8].project.media).toEqual(r[2].project.media); expect(await hash(moved)).toBe(digest);
});
for (const format of [1, 2, 3]) {
  test(`CT-017/026: Dado projeto v${format} / Quando migrar e restaurar fonte / Então preserva edição e undo`, async ({ folder }) => {
    const file = await media(folder, 'mp4', true), saved = path.join(folder, 'project.json'), legacy = path.join(folder, 'legacy.json'), clip = id();
    const first = await run([insert(file, clip), { op: 'save', path: saved }]); allOk(first);
    const json = JSON.parse(await readFile(saved, 'utf8')); json.formatVersion = format; json.clips[0].durationTicks = ticks(5);
    delete json.clips[0].sourceStartTicks; delete json.clips[0].sourceRangeVerified;
    if (format === 1) delete json.media;
    if (format === 2) for (const asset of json.media) { delete asset.durationTicks; delete asset.pixelWidth; delete asset.pixelHeight; }
    await writeFile(legacy, JSON.stringify(json));
    const r = await run([{ op: 'load', path: legacy }, { op: 'save', path: saved }, { op: 'load', path: saved }]); allOk(r);
    expect(r[0].project.clips[0].durationTicks).toBe(ticks(5)); expect(r[2].project).toEqual(r[0].project);
    expect(JSON.parse(await readFile(saved, 'utf8')).formatVersion).toBe(4);
    // v1 legitimately has no catalog, so restoration is tested with catalog-bearing formats.
    if (format > 1) {
      const restored = await run([{ op: 'load', path: legacy }, { op: 'restore', id: clip }, { op: 'undo' }, { op: 'redo' }]); allOk(restored);
      expect(restored[1].project.clips[0]).toMatchObject({ durationTicks: ticks(12), sourceStartTicks: 0 });
      expect(restored[2].project).toEqual(restored[0].project); expect(restored[3].project).toEqual(restored[1].project);
    }
  });
}
test('CT-020: Dado secundário posicionado / Quando perde vínculo / Então aviso persiste até movimento', async ({ folder }) => {
  const video = await media(folder, 'mp4', true), audio = await wave(folder), parent = id(), child = id();
  const r = await run([insert(video, parent), insert(audio, child, 10), { op: 'resize', id: parent, seconds: 5 },
    { op: 'marker', id: id(), seconds: 1 }, { op: 'move', id: child, track: 1, seconds: 2 }, { op: 'undo' }, { op: 'redo' }]); allOk(r);
  expect(r[2].project.clips.find(c => c.id === child)!.orphanNotice).toBe(true);
  expect(r[3].project.clips.find(c => c.id === child)!.orphanNotice).toBe(true);
  expect(r[4].project.clips.find(c => c.id === child)!.orphanNotice).toBe(false);
  expect(r[5].project).toEqual(r[3].project); expect(r[6].project).toEqual(r[4].project);
});
test('CT-011/012/013/017: Dado vídeo real / Quando cortar, dividir e restaurar / Então ondula sem perder origem', async ({ folder }) => {
  const file = await media(folder, 'mp4', true), saved = path.join(folder, 'base.json'), first = id(), next = id(), right = id();
  const initial = await run([insert(file, first), { op: 'save', path: saved }]); allOk(initial);
  const r = await run([{ op: 'load', path: saved }, { op: 'insert', mediaId: initial[0].project.media[0].id, id: next, seconds: 0 },
    { op: 'trimLeft', id: first, seconds: 2 }, { op: 'resize', id: first, seconds: 7 },
    { op: 'split', id: first, seconds: 3, rightId: right }, { op: 'restore', id: first }, { op: 'undo' }, { op: 'redo' }]); allOk(r);
  expect(r[2].project.clips.find(c => c.id === first)!).toMatchObject({ startTicks: 0, sourceStartTicks: ticks(2), durationTicks: ticks(10) });
  expect(r[3].project.clips.find(c => c.id === first)!.sourceStartTicks).toBe(ticks(2));
  const left = r[4].project.clips.find(c => c.id === first)!, tail = r[4].project.clips.find(c => c.id === right)!;
  expect(left.sourceStartTicks + left.durationTicks).toBe(tail.sourceStartTicks);
  expect(r[4].project.clips.find(c => c.id === next)!.startTicks).toBe(ticks(7));
  expect(r[5].project.clips.find(c => c.id === first)!).toMatchObject({ sourceStartTicks: 0, durationTicks: ticks(12) });
  expect(r[5].project.clips.find(c => c.id === right)!.startTicks).toBe(ticks(12));
  expect(r[6].project).toEqual(r[4].project); expect(r[7].project).toEqual(r[5].project);
});


test('CT-014/015: Dado áudio cortado / Quando alongar até o fim da fonte / Então permite sem extrapolar', async ({ folder }) => {
  const file = await wave(folder), clip = id();
  const r = await run([
    insert(file, clip, 3), // WAV de 12 segundos
    { op: 'resize', id: clip, seconds: 8 },
    { op: 'trimLeft', id: clip, seconds: 5 }, // source offset 2s e duração 6s
    { op: 'resize', id: clip, seconds: 9 }, // intervalo da fonte [2s, 11s)
    { op: 'trimLeft', id: clip, seconds: 4 }, // intervalo [1s, 11s)
    { op: 'resize', id: clip, seconds: 12 }, // ultrapassa o fim original
    { op: 'trimLeft', id: clip, seconds: 2 }, // avançaria antes do início original
    { op: 'undo' },
    { op: 'redo' },
  ]);
  allOk(r.filter((_, i) => i !== 5 && i !== 6));
  expect(r[3].project.clips[0]).toMatchObject({
    startTicks: ticks(5), durationTicks: ticks(9), sourceStartTicks: ticks(2)
  });
  expect(r[4].project.clips[0]).toMatchObject({
    startTicks: ticks(4), durationTicks: ticks(10), sourceStartTicks: ticks(1)
  });
  for (const index of [5, 6]) {
    expect(r[index].error?.type).toBe('MediaReferenceException');
    expect(r[index].project).toEqual(r[4].project);
  }
  expect(r[7].project).toEqual(r[3].project);
  expect(r[8].project).toEqual(r[4].project);
});

test('CT-014/017: Dado vídeo cortado / Quando alongar e restaurar / Então aplica limites e histórico', async ({ folder }) => {
  const file = await media(folder, 'mp4', true), clip = id();
  const r = await run([
    insert(file, clip), // MP4 de 12 segundos
    { op: 'resize', id: clip, seconds: 8 },
    { op: 'resize', id: clip, seconds: 9 }, // permitido
    { op: 'resize', id: clip, seconds: 13 }, // ultrapassa a fonte
    { op: 'trimLeft', id: clip, seconds: -1 }, // posição negativa
    { op: 'restore', id: clip },
    { op: 'undo' },
    { op: 'redo' },
  ]);
  allOk(r.filter((_, i) => i !== 3 && i !== 4));
  expect(r[2].project.clips[0].durationTicks).toBe(ticks(9));
  expect(r[3].error?.type).toBe('MediaReferenceException');
  expect(r[4].error?.code).toBe('INVALID_RESIZE');
  expect(r[3].project).toEqual(r[2].project);
  expect(r[4].project).toEqual(r[2].project);
  expect(r[5].project.clips[0].durationTicks).toBe(ticks(12));
  expect(r[6].project).toEqual(r[2].project);
  expect(r[7].project).toEqual(r[5].project);
});
