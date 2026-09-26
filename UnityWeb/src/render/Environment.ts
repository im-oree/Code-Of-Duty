/**
 * Unity RenderSettings (classID 104) -> three.js scene environment.
 * Fog, ambient lighting and the default skybox, which together account for
 * most of "does this look like the Unity frame".
 */

import * as THREE from 'three';
import { num, mapOf, type UnityMap } from '../unity/YamlParser';
import { readColor } from '../unity/Coords';

export interface EnvironmentInfo {
  fog: boolean;
  fogMode: number;      // 1 = Linear, 2 = Exponential, 3 = Exponential Squared
  ambientMode: number;  // 0 = Skybox, 1 = Trilight (gradient), 3 = Flat (colour)
  hasSkybox: boolean;
}

/** Unity's built-in default skybox, as a procedural gradient. */
function makeDefaultSky(): THREE.Texture {
  const size = 512;
  const canvas = document.createElement('canvas');
  canvas.width = 4;
  canvas.height = size;
  const ctx = canvas.getContext('2d')!;
  const grad = ctx.createLinearGradient(0, 0, 0, size);
  // Approximates Unity's default procedural sky: light blue -> horizon -> ground
  grad.addColorStop(0.00, '#3a72c4');
  grad.addColorStop(0.42, '#87a9d8');
  grad.addColorStop(0.50, '#c8cfd4');
  grad.addColorStop(0.52, '#8e8577');
  grad.addColorStop(1.00, '#4a443c');
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, 4, size);

  const tex = new THREE.CanvasTexture(canvas);
  tex.mapping = THREE.EquirectangularReflectionMapping;
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function applyRenderSettings(scene: THREE.Scene, body: UnityMap): EnvironmentInfo {
  const info: EnvironmentInfo = { fog: false, fogMode: 3, ambientMode: 3, hasSkybox: false };

  // ---- Fog ----
  info.fog = num(body.m_Fog, 0) !== 0;
  info.fogMode = num(body.m_FogMode, 3);
  if (info.fog) {
    const fc = readColor(mapOf(body.m_FogColor));
    const color = new THREE.Color().setRGB(fc.r, fc.g, fc.b, THREE.LinearSRGBColorSpace);
    if (info.fogMode === 1) {
      scene.fog = new THREE.Fog(color, num(body.m_LinearFogStart, 0), num(body.m_LinearFogEnd, 300));
    } else {
      // Unity Exp:  f = exp(-density * d)
      // Unity Exp2: f = exp(-(density * d)^2)  <- matches THREE.FogExp2
      const density = num(body.m_FogDensity, 0.01);
      scene.fog = new THREE.FogExp2(color, info.fogMode === 2 ? density * 0.72 : density);
    }
  }

  // ---- Ambient ----
  info.ambientMode = num(body.m_AmbientMode, 3);
  const sky = readColor(mapOf(body.m_AmbientSkyColor));
  const equator = readColor(mapOf(body.m_AmbientEquatorColor));
  const ground = readColor(mapOf(body.m_AmbientGroundColor));
  const intensity = num(body.m_AmbientIntensity, 1);

  if (info.ambientMode === 1) {
    // Trilight -> hemisphere light is the closest cheap match.
    const hemi = new THREE.HemisphereLight(
      new THREE.Color().setRGB(sky.r, sky.g, sky.b, THREE.LinearSRGBColorSpace),
      new THREE.Color().setRGB(ground.r, ground.g, ground.b, THREE.LinearSRGBColorSpace),
      intensity,
    );
    hemi.name = '__AmbientTrilight';
    scene.add(hemi);
    // Equator contributes a flat term so mid-height surfaces are not too dark.
    const amb = new THREE.AmbientLight(
      new THREE.Color().setRGB(equator.r, equator.g, equator.b, THREE.LinearSRGBColorSpace),
      intensity * 0.35);
    amb.name = '__AmbientEquator';
    scene.add(amb);
  } else {
    // Flat (3) and Skybox (0) both reduce to a uniform term here; for Skybox we
    // additionally set scene.environment below.
    const amb = new THREE.AmbientLight(
      new THREE.Color().setRGB(sky.r, sky.g, sky.b, THREE.LinearSRGBColorSpace), intensity);
    amb.name = '__AmbientFlat';
    scene.add(amb);
  }

  // ---- Skybox ----
  // fileID 10304 with the zero-GUID is Unity's built-in Default-Skybox.
  const skyRef = mapOf(body.m_SkyboxMaterial);
  if (skyRef && num(skyRef.fileID, 0) !== 0) {
    info.hasSkybox = true;
    const tex = makeDefaultSky();
    scene.background = tex;
    if (info.ambientMode === 0) scene.environment = tex;
  }

  return info;
}
