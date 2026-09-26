/**
 * Editor icons, as inline SVG.
 *
 * Unity keeps its icon set inside the editor binary, so it cannot be loaded
 * here. Emoji were the obvious substitute and the wrong one: the headless
 * Chromium used for captures has no emoji font, so every icon came out as a
 * missing-glyph box. Inline SVG renders identically everywhere, which matters
 * because the screenshot has to be trustworthy evidence of what the user sees.
 *
 * The shapes are deliberately crude. Their job is to make a hierarchy row
 * identifiable by silhouette and colour before you read the label.
 */

const svg = (body: string, size = 12): string =>
  `<svg viewBox="0 0 16 16" width="${size}" height="${size}" fill="none" ` +
  `xmlns="http://www.w3.org/2000/svg">${body}</svg>`;

const C = {
  grey: '#a8a8a8',
  dim: '#7a7a7a',
  blue: '#5a9fd4',
  cyan: '#4fc3c7',
  green: '#7bbf5a',
  yellow: '#e0c05a',
  orange: '#e08a3c',
  purple: '#a87fd0',
  red: '#d4705a',
  white: '#d8d8d8',
};

const ICONS: Record<string, string> = {
  camera: svg(`<path d="M2 5h7v6H2z" fill="${C.grey}"/><path d="M9 7l4-2v6l-4-2z" fill="${C.dim}"/>`),
  light: svg(`<circle cx="8" cy="7" r="3.4" fill="${C.yellow}"/>` +
             `<path d="M6.5 11h3v1.6h-3z" fill="${C.dim}"/>`),
  canvas: svg(`<rect x="2" y="3" width="12" height="10" rx="1" stroke="${C.blue}" ` +
              `stroke-width="1.4"/>`),
  audio: svg(`<path d="M3 6h2.5L8 3.5v9L5.5 10H3z" fill="${C.grey}"/>` +
             `<path d="M10 6q1.8 2 0 4" stroke="${C.grey}" stroke-width="1.2"/>`),
  particle: svg(`<circle cx="5" cy="5" r="1.6" fill="${C.cyan}"/>` +
                `<circle cx="11" cy="7" r="1.2" fill="${C.cyan}" opacity=".7"/>` +
                `<circle cx="7" cy="11" r="1" fill="${C.cyan}" opacity=".5"/>`),
  skinned: svg(`<circle cx="8" cy="4" r="2.2" fill="${C.cyan}"/>` +
               `<path d="M4.5 14c0-2.6 1.6-4.4 3.5-4.4s3.5 1.8 3.5 4.4z" fill="${C.cyan}"/>`),
  animator: svg(`<rect x="2" y="3" width="12" height="10" rx="1.5" fill="${C.purple}"/>` +
                `<path d="M6.5 6l4 2-4 2z" fill="#1d1d1d"/>`),
  text: svg(`<path d="M3 4h10v1.9H9.1V13H6.9V5.9H3z" fill="${C.blue}"/>`),
  image: svg(`<rect x="2" y="3" width="12" height="10" rx="1" fill="${C.green}"/>` +
             `<circle cx="5.6" cy="6.2" r="1.1" fill="#1d1d1d" opacity=".55"/>` +
             `<path d="M2 11l3.4-3 3 2.6L11 8l3 3v2H2z" fill="#1d1d1d" opacity=".4"/>`),
  button: svg(`<rect x="1.5" y="5" width="13" height="6" rx="3" fill="${C.green}" ` +
              `opacity=".85"/><circle cx="11" cy="8" r="1.4" fill="#1d1d1d" opacity=".5"/>`),
  slider: svg(`<rect x="2" y="7.2" width="12" height="1.6" rx=".8" fill="${C.dim}"/>` +
              `<circle cx="10" cy="8" r="2.4" fill="${C.green}"/>`),
  input: svg(`<rect x="2" y="4.5" width="12" height="7" rx="1" stroke="${C.green}" ` +
             `stroke-width="1.3"/><path d="M4.6 6.4v3.2" stroke="${C.green}" stroke-width="1.2"/>`),
  scroll: svg(`<rect x="2.5" y="2.5" width="11" height="11" rx="1" stroke="${C.green}" ` +
              `stroke-width="1.3"/><path d="M11 5v6" stroke="${C.green}" stroke-width="1.6"/>`),
  event: svg(`<path d="M9 2L4 9h3l-1 5 5-7H8z" fill="${C.yellow}"/>`),
  mesh: svg(`<path d="M8 2l5.5 3v6L8 14l-5.5-3V5z" fill="${C.grey}" opacity=".9"/>` +
            `<path d="M8 2v12M2.5 5L8 8l5.5-3" stroke="#1d1d1d" stroke-width=".9" opacity=".5"/>`),
  terrain: svg(`<path d="M1.5 13l4-7 3 4 2-3 4 6z" fill="${C.green}"/>`),
  nav: svg(`<path d="M8 2l5 3v6l-5 3-5-3V5z" stroke="${C.cyan}" stroke-width="1.3"/>`),
  physics: svg(`<circle cx="8" cy="8" r="5" stroke="${C.orange}" stroke-width="1.4"/>` +
               `<circle cx="8" cy="8" r="1.6" fill="${C.orange}"/>`),
  collider: svg(`<rect x="2.8" y="2.8" width="10.4" height="10.4" stroke="${C.green}" ` +
                `stroke-width="1.3" stroke-dasharray="2.4 1.6"/>`),
  rect: svg(`<rect x="2" y="4" width="12" height="8" stroke="${C.blue}" stroke-width="1.3"/>` +
            `<circle cx="8" cy="8" r="1" fill="${C.blue}"/>`),
  transform: svg(`<path d="M8 2v12M2 8h12" stroke="${C.dim}" stroke-width="1.3"/>` +
                 `<circle cx="8" cy="8" r="2" fill="${C.dim}"/>`),
  script: svg(`<rect x="2.5" y="2" width="11" height="12" rx="1" fill="${C.blue}" ` +
              `opacity=".85"/><path d="M5 5.5h6M5 8h6M5 10.5h4" stroke="#1d1d1d" ` +
              `stroke-width="1.1" opacity=".6"/>`),
  go: svg(`<rect x="2.5" y="2.5" width="11" height="11" rx="1.5" stroke="${C.grey}" ` +
          `stroke-width="1.3" opacity=".8"/>`),
  prefab: svg(`<path d="M8 2l5.5 3v6L8 14l-5.5-3V5z" fill="${C.blue}" opacity=".9"/>`),

  // Project browser
  folder: svg(`<path d="M1.5 4h5l1.2 1.6h6.8V13H1.5z" fill="${C.blue}" opacity=".85"/>`),
  scene: svg(`<path d="M2 11.5l4-6 3 4 2.2-2.8L14 11.5z" fill="${C.green}"/>` +
             `<rect x="1.5" y="2.5" width="13" height="11" rx="1" stroke="${C.dim}" ` +
             `stroke-width="1.1"/>`),
  material: svg(`<circle cx="8" cy="8" r="5.4" fill="${C.orange}"/>` +
                `<path d="M4 10.4A5.4 5.4 0 0 1 10.4 4" stroke="#fff" stroke-width="1.2" ` +
                `opacity=".45"/>`),
  model: svg(`<path d="M8 2l5.5 3v6L8 14l-5.5-3V5z" fill="${C.cyan}" opacity=".85"/>`),
  texture: svg(`<rect x="1.8" y="2.8" width="12.4" height="10.4" rx="1" fill="${C.purple}" ` +
               `opacity=".8"/><circle cx="5.4" cy="6" r="1.2" fill="#1d1d1d" opacity=".5"/>`),
  shader: svg(`<path d="M8 1.6l6 3.4v6l-6 3.4-6-3.4v-6z" fill="${C.purple}"/>` +
              `<path d="M8 1.6v12.8" stroke="#1d1d1d" stroke-width="1" opacity=".5"/>`),
  font: svg(`<path d="M3 13L7 3h2l4 10h-2.1l-.9-2.4H6l-.9 2.4z" fill="${C.white}"/>`),
  audioclip: svg(`<path d="M11.5 2.5v7.2a2.2 2.2 0 1 1-1.4-2V5L6 6v5.7a2.2 2.2 0 1 1-1.4-2V4z" ` +
                 `fill="${C.orange}"/>`),
  controller: svg(`<circle cx="4.5" cy="8" r="2.4" fill="${C.purple}"/>` +
                  `<circle cx="11.5" cy="8" r="2.4" fill="${C.purple}"/>` +
                  `<path d="M6.9 8h2.2" stroke="${C.purple}" stroke-width="1.3"/>`),
  asset: svg(`<rect x="2.5" y="2" width="11" height="12" rx="1" fill="${C.dim}"/>` +
             `<path d="M5 6h6M5 8.5h6M5 11h4" stroke="#1d1d1d" stroke-width="1" opacity=".6"/>`),
  file: svg(`<path d="M3.5 1.8h6L12.5 5v9.2h-9z" fill="${C.dim}" opacity=".8"/>` +
            `<path d="M9.5 1.8V5h3" fill="#1d1d1d" opacity=".4"/>`),
};

/** Fall back to a plain GameObject box rather than throwing on a new type. */
function icon(key: string, size = 12): string {
  const found = ICONS[key] ?? ICONS.go;
  return size === 12 ? found : found.replace('width="12" height="12"', `width="${size}" height="${size}"`);
}

/** Directly addressable icon, for callers that know the kind they want. */
export function namedIcon(key: string, size = 12): string {
  return icon(key, size);
}

/** Icon for a GameObject, chosen from the components it carries. */
export function iconForComponents(componentNames: string[]): string {
  const has = (n: string) => componentNames.some((c) => c === n || c.endsWith(`.${n}`));

  if (has('Camera')) return icon('camera');
  if (has('Light')) return icon('light');
  if (has('Canvas')) return icon('canvas');
  if (has('AudioSource') || has('AudioListener')) return icon('audio');
  if (has('ParticleSystem')) return icon('particle');
  if (has('SkinnedMeshRenderer')) return icon('skinned');
  if (has('Animator') || has('Animation')) return icon('animator');
  if (has('TextMeshProUGUI') || has('TextMeshPro') || has('Text')) return icon('text');
  if (has('Image') || has('RawImage')) return icon('image');
  if (has('Button')) return icon('button');
  if (has('Slider')) return icon('slider');
  if (has('TMP_InputField') || has('InputField')) return icon('input');
  if (has('ScrollRect')) return icon('scroll');
  if (has('EventSystem')) return icon('event');
  if (has('MeshRenderer') || has('MeshFilter')) return icon('mesh');
  if (has('Terrain')) return icon('terrain');
  if (has('NavMeshAgent') || has('NavMeshSurface')) return icon('nav');
  if (has('Rigidbody') || has('Rigidbody2D')) return icon('physics');
  if (componentNames.some((c) => c.includes('Collider'))) return icon('collider');
  if (has('RectTransform')) return icon('rect');
  return icon('go');
}

/** Icon for a component header row in the Inspector. */
export function iconForComponent(name: string): string {
  const short = name.includes('.') ? name.slice(name.lastIndexOf('.') + 1) : name;
  if (short === 'Transform') return icon('transform');
  if (short === 'RectTransform') return icon('rect');
  if (short === 'MonoBehaviour') return icon('script');
  const known = iconForComponents([short]);
  // An unrecognised type is almost always one of the project's own scripts.
  return known === ICONS.go ? icon('script') : known;
}

/** Icon for a project asset, chosen from its extension. */
export function iconForFile(name: string, isDir: boolean, size = 12): string {
  if (isDir) return icon('folder', size);
  const ext = name.slice(name.lastIndexOf('.') + 1).toLowerCase();
  const pick = (k: string) => icon(k, size);
  switch (ext) {
    case 'unity': return pick('scene');
    case 'prefab': return pick('prefab');
    case 'mat': return pick('material');
    case 'cs': return pick('script');
    case 'fbx': case 'glb': case 'gltf': case 'obj': case 'blend': return pick('model');
    case 'png': case 'jpg': case 'jpeg': case 'tga': case 'psd': case 'exr': case 'hdr':
    case 'webp': case 'bmp': case 'tif': case 'tiff': return pick('texture');
    case 'shader': case 'shadergraph': case 'hlsl': case 'cginc': case 'compute':
      return pick('shader');
    case 'ttf': case 'otf': return pick('font');
    case 'wav': case 'mp3': case 'ogg': case 'aiff': return pick('audioclip');
    case 'controller': case 'overridecontroller': return pick('controller');
    case 'anim': return pick('animator');
    case 'asset': return pick('asset');
    default: return pick('file');
  }
}
