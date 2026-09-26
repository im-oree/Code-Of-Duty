/**
 * Browser-side access to the Unity project's files.
 *
 * The vite plugin (`vite.config.ts`) exposes three endpoints:
 *   /api/index                -> [{ path, guid }]  built by walking Assets/**\/*.meta
 *   /api/list?ext=.unity      -> file listing for the scene picker
 *   /api/file?path=Assets/... -> raw bytes
 *
 * Everything is cached in-memory for the lifetime of the page, because a scene
 * resolves the same material/prefab GUIDs many times over.
 */

import { parseUnityYaml, type UnityFile } from './YamlParser';

export class AssetDatabase {
  private guidToPath = new Map<string, string>();
  private pathToGuid = new Map<string, string>();
  private textCache = new Map<string, string>();
  private yamlCache = new Map<string, UnityFile>();
  private bufferCache = new Map<string, ArrayBuffer>();
  private indexLoaded = false;

  async loadIndex(): Promise<void> {
    if (this.indexLoaded) return;
    const res = await fetch('/api/index');
    if (!res.ok) throw new Error(`asset index failed: ${res.status}`);
    const data = (await res.json()) as { entries: { path: string; guid: string }[] };
    for (const e of data.entries) {
      this.guidToPath.set(e.guid, e.path);
      this.pathToGuid.set(e.path, e.guid);
    }
    this.indexLoaded = true;
  }

  get indexSize(): number { return this.guidToPath.size; }

  pathForGuid(guid: string | undefined): string | null {
    if (!guid) return null;
    return this.guidToPath.get(guid) ?? null;
  }

  guidForPath(path: string): string | null {
    return this.pathToGuid.get(path) ?? null;
  }

  async listFiles(ext: string): Promise<string[]> {
    const res = await fetch(`/api/list?ext=${encodeURIComponent(ext)}`);
    if (!res.ok) return [];
    const data = (await res.json()) as { files: string[] };
    return data.files;
  }

  async readText(path: string): Promise<string> {
    const hit = this.textCache.get(path);
    if (hit !== undefined) return hit;
    const res = await fetch(`/api/file?path=${encodeURIComponent(path)}`);
    if (!res.ok) throw new Error(`read failed (${res.status}): ${path}`);
    const text = await res.text();
    this.textCache.set(path, text);
    return text;
  }

  async readBuffer(path: string): Promise<ArrayBuffer> {
    const hit = this.bufferCache.get(path);
    if (hit !== undefined) return hit;
    const res = await fetch(`/api/file?path=${encodeURIComponent(path)}`);
    if (!res.ok) throw new Error(`read failed (${res.status}): ${path}`);
    const buf = await res.arrayBuffer();
    this.bufferCache.set(path, buf);
    return buf;
  }

  /** Parse (and cache) a Unity YAML asset by project-relative path. */
  async readYaml(path: string): Promise<UnityFile> {
    const hit = this.yamlCache.get(path);
    if (hit !== undefined) return hit;
    const text = await this.readText(path);
    const file = parseUnityYaml(text);
    this.yamlCache.set(path, file);
    return file;
  }

  /** Parse (and cache) a Unity YAML asset by GUID. Returns null if unknown. */
  async readYamlByGuid(guid: string | undefined): Promise<{ file: UnityFile; path: string } | null> {
    const path = this.pathForGuid(guid);
    if (!path) return null;
    try {
      return { file: await this.readYaml(path), path };
    } catch {
      return null;
    }
  }

  /** True when the asset is a Unity YAML text asset rather than a binary import. */
  static isYamlAsset(path: string): boolean {
    return /\.(unity|prefab|asset|mat|physicMaterial|controller|anim|renderTexture|lighting)$/i.test(path);
  }
}

export const assets = new AssetDatabase();
