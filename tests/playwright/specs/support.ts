import { test as base, expect } from '@playwright/test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
export const exec = promisify(execFile);
export type Command = Record<string, string | number>;
export interface Clip { id: string; mediaId: string; track: number; startTicks: number; durationTicks: number; sourceStartTicks: number; parentId: string | null; orphanNotice: boolean }
export interface Snapshot {
  error: { type: string; code: string | null; message: string } | null;
  value: unknown; canUndo: boolean; canRedo: boolean; displayDuration: string;
  project: { formatVersion: number; clips: Clip[]; media: { id: string; durationTicks: number | null; sourcePath: string }[]; markers: unknown[]; timelineVersion: number };
}
export const ticks = (seconds: number) => Math.round(seconds * 10_000_000);
export const test = base.extend<{ folder: string }>({
  folder: async ({}, use) => {
    const folder = await mkdtemp(path.join(tmpdir(), 'editor-playwright-'));
    try { await use(folder); } finally { await rm(folder, { recursive: true, force: true }); }
  },
});
export { expect };
// Spawn without a shell. Each invocation owns its state and environment.
export async function run(commands: Command[], env: NodeJS.ProcessEnv = {}): Promise<Snapshot[]> {
  const dll = path.resolve(__dirname, '../../Editor.Integration.Driver/bin/Release/net10.0/Editor.Integration.Driver.dll');
  return new Promise((resolve, reject) => {
    const child = execFile('dotnet', [dll], { env: { ...process.env, ...env }, timeout: 45_000, maxBuffer: 8 * 1024 * 1024 }, (error, stdout, stderr) => {
      if (error) { reject(new Error(`Driver failed: ${error.message}\n${stderr}\n${stdout}`)); return; }
      try { resolve(JSON.parse(stdout)); } catch (e) { reject(e); }
    });
    child.stdin!.end(JSON.stringify(commands));
  });
}
export function allOk(results: Snapshot[]) { for (const result of results) expect(result.error).toBeNull(); }
// Small, valid PCM WAV: no network, codec download or user media.
export async function wave(folder: string, name = 'source.wav', seconds = 12) {
  const data = Buffer.alloc(44 + seconds * 8000 * 2);
  data.write('RIFF'); data.writeUInt32LE(data.length - 8, 4); data.write('WAVEfmt ', 8);
  data.writeUInt32LE(16, 16); data.writeUInt16LE(1, 20); data.writeUInt16LE(1, 22);
  data.writeUInt32LE(8000, 24); data.writeUInt32LE(16000, 28); data.writeUInt16LE(2, 32);
  data.writeUInt16LE(16, 34); data.write('data', 36); data.writeUInt32LE(data.length - 44, 40);
  const file = path.join(folder, name); await writeFile(file, data); return file;
}
export async function media(folder: string, extension: string, video = false) {
  const file = path.join(folder, `fixture.${extension}`);
  await exec('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', video ? 'color=c=black:s=32x32:r=10' : 'sine=frequency=440:sample_rate=44100', '-t', '12', '-y', file], { timeout: 20_000 });
  return file;
}
