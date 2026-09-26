/**
 * Unity classID -> type name.
 *
 * Only the ids this viewer needs are listed; everything else renders as an
 * inert data component so the inspector can still show it. Counts in the
 * comments are from Assets/Scenes/StartMenu.unity (1,644 documents) and drove
 * the implementation priority.
 */

export const CLASS_NAMES: Record<number, string> = {
  1: 'GameObject',              // 484 in StartMenu
  2: 'Component',
  4: 'Transform',               // 180
  8: 'Behaviour',
  20: 'Camera',                 // 1
  21: 'Material',               // 9
  23: 'MeshRenderer',           // 36
  25: 'Renderer',
  28: 'Texture2D',
  29: 'OcclusionCullingSettings',
  33: 'MeshFilter',             // 36
  43: 'Mesh',
  48: 'Shader',
  54: 'Rigidbody',
  56: 'Collider',
  64: 'MeshCollider',
  65: 'BoxCollider',
  81: 'AudioListener',
  82: 'AudioSource',
  95: 'Animator',               // 1
  102: 'TextMesh',
  104: 'RenderSettings',        // 1
  108: 'Light',                 // 3
  111: 'Animation',
  114: 'MonoBehaviour',         // 322 — all uGUI components live here
  115: 'MonoScript',
  120: 'LineRenderer',
  135: 'SphereCollider',
  136: 'CapsuleCollider',
  137: 'SkinnedMeshRenderer',   // 2
  143: 'CharacterController',
  157: 'LightmapSettings',
  196: 'NavMeshSettings',
  198: 'ParticleSystem',        // 2
  199: 'ParticleSystemRenderer',// 2
  212: 'SpriteRenderer',
  213: 'Sprite',
  222: 'CanvasRenderer',        // 252
  223: 'Canvas',                // 1
  224: 'RectTransform',         // 304
  225: 'CanvasGroup',
  320: 'PlayableDirector',
  328: 'VideoPlayer',
  1001: 'PrefabInstance',
  1660057539: 'SceneRoots',     // 1 — root ordering
};

export function classNameOf(classId: number): string {
  return CLASS_NAMES[classId] ?? `Class_${classId}`;
}

/** Components that place an object in the hierarchy. */
export const TRANSFORM_CLASS_IDS = new Set([4, 224]);

/** Class ids we deliberately skip (no visual or structural effect here). */
export const IGNORED_CLASS_IDS = new Set([29, 157, 196, 115, 48]);

/**
 * uGUI MonoBehaviour scripts are identified by the GUID of their MonoScript.
 * These are the stable, well-known GUIDs shipped inside Unity's UI package.
 */
export const UGUI_SCRIPT_GUIDS: Record<string, string> = {
  'fe87c0e1cc204ed48ad3b37840f39efc': 'Image',
  '5f7201a12d95ffc409449d95f23cf332': 'Text',
  'f4688fdb7df04437aeb418b961361dc5': 'TextMeshProUGUI',
  '4e29b1a8efbd4b44bb3f3716e73f07ff': 'Button',
  '1344c3c82d62a2a41a3576d8abb8e3ea': 'RawImage',
  '2a4db7a114972834c8e4117be1d82ba3': 'Slider',
  '9085046f02f69544eb97fd06b6048fe2': 'Toggle',
  '30649d3a9faa99c48a7b1166b86bf2a0': 'Mask',
  '3245ec927659c4140ac4f8d17403cc18': 'CanvasScaler',
  'dc42784cf147c0c48a680349fa168899': 'LayoutElement',
  '59f8146938fff824cb5fd77236b75775': 'HorizontalLayoutGroup',
  '30649d3a9faa99c48a7b1166b86bf2a1': 'VerticalLayoutGroup',
  '8a8695521f0d02e499659fee002a26c2': 'GridLayoutGroup',
  '0cd44c1031e13a943bb63640046fad76': 'ContentSizeFitter',
  '306cc8c2b49d7114eaa3623786fc2126': 'ScrollRect',
  'dd0603e3b8d21a04eb5e8aa6d0effd3f': 'AspectRatioFitter',
  '4f231c4fb786f3946a6b90b886c48677': 'GraphicRaycaster',
  'f8c4a8a1b9f9f2c4a8e9b8c7d6e5f4a3': 'Outline',
};
