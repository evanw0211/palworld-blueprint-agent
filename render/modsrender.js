// Real-mesh renderer for Simple Building Blueprints (schema-9 JSON).
//
// Two things this page does that the shipped one did not:
//
//  1. MATERIALS.  The vendored GLBs carry NO textures (`images: 0` in every file) and
//     every glTF material has `baseColorFactor: null`, so there is no colour anywhere in
//     the asset data.  What *is* present is the UE material-instance NAME on each
//     primitive (e.g. `MI_PropGlass_Roof_Glass`, `MI_PalProp_Floor_Stone`,
//     `MI_PalProp_Wall_Wood`, `MI_PalProp_JapaneseStyle_WallA`).  That name states what
//     the surface is made of, and multi-material meshes use it to separate a glass pane
//     from its frame.  So shading is chosen per PRIMITIVE from the material name, not per
//     PIECE from the construction family.  `COLOR_0` exists on every mesh but is
//     uniformly 0.0 (measured over all 17 GLBs), so it is deliberately ignored.
//     `?mat=0` restores the old per-family flat tints.
//     `?texmap=<url>` optionally supplies { materialName|BuildObjectId: imageUrl } and
//     turns on real textures, so the pak export path can be plugged in without code
//     changes once it exists.
//
//  2. FREE CAMERA.  The default framing is still an exact port of the mod's
//     BlueprintThumbnailRenderer.compute_camera_frame (FOV 40, FRAME_FILL 0.86, forward
//     ue(4.2,4.2,-3.0), 8-corner distance).  `az`/`el`/`dist`/`fit`/`top`/`ortho` move the
//     camera anywhere without touching that default, which is what makes an inspection
//     pass possible.
//
// Units: blueprint = cm, GLB = m (MODEL_UNIT_SCALE = 100).
// Axis mapping (measured previously): three = (ue.x, ue.z, -ue.y),
// three.rotation = +yaw, glb static meshes are already Y-up.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const qs = new URLSearchParams(location.search);
const BP_URL = qs.get('bp');
const IDMAP_URL = qs.get('idmap') || '/meshes/_id_to_bp.json';
const OFFSETS_URL = qs.get('offsets') || '/meshes/_actor_offsets.json';
const MESH_BASE = qs.get('meshes') || '/meshes';
const W = parseInt(qs.get('w') || '1280', 10);
const H = parseInt(qs.get('h') || '720', 10);
const CM = 0.01;                       // cm -> m

const FRAME_FILL = 0.86, FOV_H = 40.0, MIN_DIST = 1.0, MIN_DEPTH = 0.1;   // metres

// Account: pieces = placed + partial + missing + failed.
//   missing = every candidate GLB answered 404 (that id was never exported)
//   failed  = a candidate failed for another reason (parse/network/5xx)
//   partial = a multi-part piece rendered with only some of its parts (never "placed")
// `errors` entries are objects: { stage, id, url, error } (stage: texmap|texture|glb|part).
const status = { pieces: 0, placed: 0, partial: 0, missing: [], failed: 0, errors: [],
                 bbox: null, camera: null, materials: {}, textures: 0 };

function ue2three(x, y, z) { return new THREE.Vector3(x, z, -y); }
function ueQuat2three(qx, qy, qz, qw) { return new THREE.Quaternion(qx, qz, -qy, qw); }

async function json(url) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(url + ' -> ' + r.status);
  return r.json();
}

function boundsOf(objects) {
  const box = new THREE.Box3();
  let any = false;
  for (const o of objects) {
    o.updateWorldMatrix(true, true);
    const b = new THREE.Box3().setFromObject(o, true);
    if (!b.isEmpty()) { box.union(b); any = true; }
  }
  if (!any) box.set(new THREE.Vector3(-1, 0, -1), new THREE.Vector3(1, 3, 1));
  return box;
}

// ---- exact port of BlueprintThumbnailRenderer.compute_camera_frame -------
// 8-corner distance for an arbitrary camera basis and vertical tangens, so the mod
// direction and any inspection direction share one framing rule.
function frameFor(box, F, R, U, hTan, vTan) {
  const center = new THREE.Vector3(
    (box.min.x + box.max.x) / 2, (box.min.y + box.max.y) / 2, (box.min.z + box.max.z) / 2);
  let dist = MIN_DIST;
  for (const x of [box.min.x, box.max.x])
    for (const y of [box.min.y, box.max.y])
      for (const z of [box.min.z, box.max.z]) {
        const rel = new THREE.Vector3(x - center.x, y - center.y, z - center.z);
        const depth = rel.dot(F);
        dist = Math.max(dist, Math.abs(rel.dot(R)) / hTan - depth,
                        Math.abs(rel.dot(U)) / vTan - depth, MIN_DEPTH - depth);
      }
  return { F, R, U, center, dist };
}

function computeCameraFrame(min, max) {
  const F = new THREE.Vector3(4.2, -3.0, -4.2).normalize();     // ue(4.2,4.2,-3)
  const R = new THREE.Vector3(-4.2, 0.0, -4.2).normalize();     // ue(-4.2,4.2,0)
  const U = new THREE.Vector3(12.6, 35.28, -12.6).normalize();  // cross(F,R) in ue
  const hTan = Math.tan((FOV_H * 0.5) * Math.PI / 180) * FRAME_FILL;
  // the mod frames for a 320x180 target regardless of the output size -- preserved
  return frameFor(new THREE.Box3(min, max), F, R, U, hTan, hTan / (320 / 180));
}

// ---- free camera ---------------------------------------------------------
// az/el use the UE convention used everywhere else in this repo:
// az 0 = +x (east), 90 = +y (north); el = degrees above the horizon.
// The mod's own direction is az 225, el 26.8 (camera to the south-west and above).
const AZ0 = 225.0, EL0 = 26.8;
const ORTHO = qs.get('ortho') === '1';
const FIT = parseFloat(qs.get('fit') ?? '1');

function basisFor(az, el) {
  const a = az * Math.PI / 180, e = el * Math.PI / 180;
  const eyeDir = ue2three(Math.cos(e) * Math.cos(a), Math.cos(e) * Math.sin(a), Math.sin(e))
    .normalize();                                      // centre -> camera
  const F = eyeDir.clone().negate();                   // camera forward
  const up = new THREE.Vector3(0, 1, 0);
  let R = new THREE.Vector3().crossVectors(up, eyeDir);
  if (R.lengthSq() < 1e-9) R = new THREE.Vector3(1, 0, 0);   // degenerate: straight down
  R.normalize();
  const U = new THREE.Vector3().crossVectors(eyeDir, R).normalize();
  return { F, R, U, eyeDir };
}

// one camera for the whole bbox from an arbitrary direction
function viewCamera(box, az, el, aspect, distOverride) {
  const { F, R, U, eyeDir } = basisFor(az, el);
  const hTan = Math.tan((FOV_H * 0.5) * Math.PI / 180) * FRAME_FILL;
  const vTan = hTan * (1 / aspect);
  const fr = frameFor(box, F, R, U, hTan, vTan);
  const dist = distOverride != null ? distOverride : fr.dist * FIT;
  const camPos = fr.center.clone().addScaledVector(eyeDir, dist);
  let cam;
  if (ORTHO) {
    const hh = Math.max(dist * vTan, 0.001);
    cam = new THREE.OrthographicCamera(-hh * aspect, hh * aspect, hh, -hh, 0.01, 20000);
  } else {
    const vFov = 2 * Math.atan(vTan) * 180 / Math.PI;
    cam = new THREE.PerspectiveCamera(vFov, aspect, 0.05, 20000);
  }
  cam.up.copy(U);
  cam.position.copy(camPos);
  cam.lookAt(fr.center);
  return { cam, dist, center: fr.center, basis: { az, el } };
}

// ---- argument validation -------------------------------------------------
// The CLI passes every option as a query param; anything malformed is a *usage* error:
// the page marks it and exits 2 (no image) instead of rendering something arbitrary.
class UsageError extends Error {}

const NUM_PARAMS = [['az', null, null], ['el', -90, 90], ['dist', 0.001, null],
                    ['fit', 0.01, null], ['w', 1, null], ['h', 1, null]];
const BOOL_PARAMS = ['parts', 'wrot', 'mat', 'top', 'ortho'];

function firstBadParam() {
  for (const [k, min, max] of NUM_PARAMS) {
    const raw = qs.get(k);
    if (raw === null) continue;
    const n = Number(raw);
    if (raw.trim() === '' || !Number.isFinite(n)
        || (min !== null && n < min) || (max !== null && n > max)) {
      return k + '=' + raw + '（需要数字' + (min !== null ? '，下限 ' + min : '')
        + (max !== null ? '，上限 ' + max : '') + '）';
    }
  }
  for (const k of BOOL_PARAMS) {
    const v = qs.get(k);
    if (v !== null && v !== '0' && v !== '1') return k + '=' + v + '（只接受 0 或 1）';
  }
  return null;
}

// `views` is either a count (evenly spaced azimuths) or an explicit "az,el;az,el" list.
function parseViews() {
  const raw = qs.get('views');
  if (raw === null) return null;
  if (raw === '') throw new UsageError('views 不能为空');
  if (/^\d+$/.test(raw)) {
    const n = parseInt(raw, 10);
    if (n < 2 || n > 16) throw new UsageError('views N 须在 2..16（收到 ' + raw + '）');
    const el = Number(qs.get('el') ?? EL0);
    if (!Number.isFinite(el) || el < -90 || el > 90) {
      throw new UsageError('views 的 el 须在 -90..90（收到 ' + qs.get('el') + '）');
    }
    const out = [];
    for (let k = 0; k < n; k++) out.push([AZ0 + k * (360 / n), el]);
    return out;
  }
  const pairs = raw.split(';').map(s => s.split(',').map(Number));
  if (!pairs.length || pairs.some(p => p.length < 2 || !p.every(Number.isFinite))) {
    throw new UsageError('views 须为 N 或 "az,el;az,el"（收到 ' + raw + '）');
  }
  for (const pair of pairs) {
    if (pair[1] < -90 || pair[1] > 90) throw new UsageError('views 的 el 须在 -90..90（收到 ' + pair[1] + '）');
  }
  return pairs;
}

// ---- material system -----------------------------------------------------
// Colour / finish per material FAMILY x piece ROLE.  Family comes from the glTF
// material-instance name (the only real material signal in the assets); role comes
// from the construction id, because the same "JapaneseStyle" material is a dark tile
// on a roof and a pale plaster on a wall.
const P = {
  glass:        { color: 0xa8cbdb, roughness: 0.04, metalness: 0.0, opacity: 0.30 },
  glass_frame:  { color: 0x7f8489, roughness: 0.45, metalness: 0.35 },
  stone:        { color: 0xb3ad9f, roughness: 0.95, metalness: 0.0 },
  wood:         { color: 0xa17b52, roughness: 0.88, metalness: 0.0 },
  wood_roof:    { color: 0x8d6a45, roughness: 0.90, metalness: 0.0 },
  japanese_tile:{ color: 0x7e7160, roughness: 0.90, metalness: 0.0 },
  japanese_wall:{ color: 0xcdc1a9, roughness: 0.84, metalness: 0.0 },
  japanese_wood:{ color: 0x9a7a53, roughness: 0.86, metalness: 0.0 },
  metal:        { color: 0x9aa2aa, roughness: 0.35, metalness: 0.70 },
  // the 452-mesh overlay brought 575 material names; these four families cover the
  // facility props (foliage / water / ground / soft goods) that would otherwise all
  // fall through to `default` and render as one flat beige
  foliage:      { color: 0x4e7a3c, roughness: 0.92, metalness: 0.0 },
  water:        { color: 0x3d7392, roughness: 0.12, metalness: 0.0 },
  earth:        { color: 0x6d5539, roughness: 0.97, metalness: 0.0 },
  fabric:       { color: 0xbfae90, roughness: 0.95, metalness: 0.0 },
  default:      { color: 0xcfc6b4, roughness: 0.82, metalness: 0.0 },
};
// legacy per-family tints, kept behind ?mat=0 so the old image can be reproduced exactly
const LEGACY = [
  [/glass/i, 0x9fc4d8], [/roof/i, 0x8d7f6d], [/stair/i, 0xa07a4e],
  [/pillar/i, 0xb9b3a6], [/foundation/i, 0xc9c2b2], [/wall|window|door/i, 0xd8cfbb],
];

function roleOf(id) {
  const s = String(id || '').toLowerCase();
  if (s.includes('triangle')) return 'gable';
  if (s.includes('roof')) return 'roof';
  if (s.includes('foundation')) return 'foundation';
  if (s.includes('pillar')) return 'pillar';
  if (s.includes('stair')) return 'stair';
  if (/wall|window|door/.test(s)) return 'wall';
  return 'other';
}

function familyOf(name) {
  const s = String(name || '');
  if (/_Parts\b|_Parts_/i.test(s)) return 'frame';   // checked first: a glass piece's frame
  if (/glass/i.test(s)) return 'glass';
  if (/ancient/i.test(s)) return 'stone';            // ancient architecture is stonework
  if (/stone|rock/i.test(s)) return 'stone';
  if (/japanese/i.test(s)) return 'japanese';
  if (/oak|spruce|pine|bamboo|tree|bush|plant|leaf|foliage|grass|ivy|flower|moss/i.test(s)) return 'foliage';
  if (/pond|water|fishing/i.test(s)) return 'water';
  if (/farmground|soil|dirt/i.test(s)) return 'earth';
  if (/fabric|cloth|banner|flag|rug|curtain|carpet|towel/i.test(s)) return 'fabric';
  if (/kitchen|cook|workbench|tools?/i.test(s)) return 'metal';
  // facility props the 452-mesh overlay brought in: statues, plant genus names, electrical
  // gear, and pal furniture. Without these ~73 primitives fell through to `default` beige.
  if (/statue/i.test(s)) return 'stone';
  if (/geranium|hibiscus|fern|lily|orchid|cactus|palm|conifer|willow|maple|cherry|bark|leafy|petal/i.test(s)) return 'foliage';
  if (/palbox|lightpole|capacitor|energystorage|generator|silo|fridge|furnace|heater|cooler|machine|pipe/i.test(s)) return 'metal';
  if (/palbed|bedprimitive|manger|palspa|palfarm|matingstation|chest|signboard|medicinebox|bed|shelf|table|chair|desk/i.test(s)) return 'wood';
  if (/wood/i.test(s)) return 'wood';
  if (/iron|steel|metal/i.test(s)) return 'metal';
  return 'unknown';
}

function profileFor(matName, id) {
  const fam = familyOf(matName);
  const role = roleOf(id);
  if (fam === 'frame') return /glass/i.test(String(id)) ? 'glass_frame' : 'default';
  if (fam === 'glass') return 'glass';
  if (fam === 'stone') return 'stone';
  if (fam === 'metal') return 'metal';
  if (fam === 'foliage' || fam === 'water' || fam === 'earth' || fam === 'fabric') return fam;
  if (fam === 'wood') return role === 'roof' ? 'wood_roof' : 'wood';
  if (fam === 'japanese') {
    if (role === 'roof') return 'japanese_tile';
    if (role === 'pillar' || role === 'wall') return role === 'pillar' ? 'japanese_wood' : 'japanese_wall';
    return 'japanese_wall';
  }
  // no usable material name: fall back to the construction family
  if (role === 'foundation') return /stone/i.test(String(id)) ? 'stone' : 'wood';
  if (role === 'roof') return 'wood_roof';
  if (role === 'pillar') return 'stone';
  return 'default';
}

// ---- errors + textures ---------------------------------------------------
const ERROR_CAP = 80;
function collectError(stage, id, error, extra) {
  if (status.errors.length >= ERROR_CAP) {
    status.errorsTruncated = (status.errorsTruncated || 0) + 1;
    return;
  }
  status.errors.push({ stage, id: id == null ? null : String(id),
                       url: (extra && extra.url) || null,
                       error: String(error && (error.message || error)) });
}
function httpStatusOf(e) {
  if (e && e.response && typeof e.response.status === 'number') return e.response.status;
  const m = /responded with (\d{3})/.exec(String(e && e.message || e));
  return m ? parseInt(m[1], 10) : null;
}

// One decode per (url, colourspace). Without this a full-texture run decodes the same
// 2048x2048 PNG once per PIECE -- 41 walls = 41 decodes of one image.
// Textures decode asynchronously, so every user registers here; awaitTextures() must run
// before the first render, or an un-decoded texture is sampled as black.
const TEX_CACHE = new Map();
const TEX_PENDING = [];
const TEX_TIMEOUT_MS = 20000;

function cachedTexture(loader, url, srgb, slot) {
  const key = url + (srgb ? '|srgb' : '|linear');
  let entry = TEX_CACHE.get(key);
  if (!entry) {
    let settle;
    const ready = new Promise(resolve => { settle = resolve; });
    const tex = loader.load(url, () => settle({ tex }), undefined, (err) => {
      settle({ err: err instanceof Error ? err
        : new Error(String(err && err.message || err || 'texture load failed')) });
    });
    if (srgb) tex.colorSpace = THREE.SRGBColorSpace;
    tex.flipY = false;                          // glTF UV convention
    entry = { tex, ready, slots: [] };
    TEX_CACHE.set(key, entry);
    TEX_PENDING.push({ url, entry });
  }
  if (slot) entry.slots.push(slot);
  return entry.tex;
}

// Await every texture requested so far. A failed decode is recorded in status.errors and
// its materials fall back to the family colour (null map) instead of rendering black.
async function awaitTextures() {
  const pending = TEX_PENDING.splice(0);
  if (!pending.length) return;
  await Promise.all(pending.map(async ({ url, entry }) => {
    const timeout = new Promise(resolve => setTimeout(
      () => resolve({ err: new Error('texture decode timeout ' + TEX_TIMEOUT_MS + 'ms') }), TEX_TIMEOUT_MS));
    const outcome = await Promise.race([entry.ready, timeout]);
    if (outcome.err) {
      collectError('texture', null, outcome.err, { url });
      for (const slot of entry.slots) slot.fallback();
      entry.failed = true;
    }
  }));
}

function makeMaterial(prof, id, matName, texmap, loader) {
  const spec = P[prof] || P.default;
  const m = new THREE.MeshStandardMaterial({
    color: spec.color, roughness: spec.roughness, metalness: spec.metalness,
    side: prof === 'glass' ? THREE.DoubleSide : THREE.FrontSide,
  });
  if (prof === 'glass') {
    m.transparent = true;
    m.opacity = spec.opacity;
    m.depthWrite = false;
  }
  // A texmap value is either a bare albedo URL, or an object for a fuller material:
  //   { "map": url, "normalMap": url, "roughnessMap": url, "metalnessMap": url }
  const entry = texmap && (texmap[matName] || texmap[id]);
  if (entry) {
    const spec2 = typeof entry === 'string' ? { map: entry } : entry;
    const tex = (url, srgb, prop, fallback) => cachedTexture(loader, url, srgb, { prop, fallback });
    if (spec2.map) {
      m.map = tex(spec2.map, true, 'map', () => { m.map = null; m.color.set(spec.color); m.needsUpdate = true; });
      m.color.set(0xffffff);                     // texture carries colour
    }
    if (spec2.normalMap) {
      m.normalMap = tex(spec2.normalMap, false, 'normalMap', () => { m.normalMap = null; m.needsUpdate = true; });
      m.normalScale = new THREE.Vector2(1, 1);
    }
    if (spec2.roughnessMap) {
      m.roughnessMap = tex(spec2.roughnessMap, false, 'roughnessMap', () => { m.roughnessMap = null; m.needsUpdate = true; });
    }
    if (spec2.metalnessMap) {
      m.metalnessMap = tex(spec2.metalnessMap, false, 'metalnessMap', () => { m.metalnessMap = null; m.needsUpdate = true; });
    }
    status.textures++;
  }
  return m;
}

function gradientBackground() {
  const c = document.createElement('canvas');
  c.width = 16; c.height = 256;
  const g = c.getContext('2d');
  const grad = g.createLinearGradient(0, 0, 0, 256);
  grad.addColorStop(0.0, '#2f5f8e');
  grad.addColorStop(0.55, '#1b3c62');
  grad.addColorStop(1.0, '#0d1e34');
  g.fillStyle = grad; g.fillRect(0, 0, 16, 256);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function legacyTint(id) {
  const s = String(id || '');
  for (const [re, c] of LEGACY) if (re.test(s)) return c;
  return 0xcfc6b4;
}

async function main() {
  const badParam = firstBadParam();
  if (badParam) throw new UsageError(badParam);
  const views = parseViews();
  const useMaterials = qs.get('mat') !== '0';
  let texmap = null;
  if (qs.get('texmap')) {
    try { texmap = await json(qs.get('texmap')); }
    catch (e) { collectError('texmap', null, e, { url: qs.get('texmap') }); }
  }
  const [bp, idmap, offsets] = await Promise.all([json(BP_URL), json(IDMAP_URL), json(OFFSETS_URL)]);
  const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(1);
  renderer.setSize(W, H);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  document.body.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = gradientBackground();

  const key = new THREE.DirectionalLight(0xfff0dc, 2.6);
  key.position.set(-1.0, 2.2, 1.6);
  const fill = new THREE.DirectionalLight(0xadc8ff, 0.9);
  fill.position.set(1.8, -0.4, -1.4);
  scene.add(key, fill, new THREE.AmbientLight(0xffffff, 0.55));

  const loader = new GLTFLoader();
  const texLoader = new THREE.TextureLoader();
  const cache = new Map();
  let partsManifest = {};
  try {
    const r = await fetch(`${MESH_BASE}/_parts.json`);
    if (r.ok) partsManifest = await r.json();
  } catch (e) { partsManifest = {}; }
  const PARTS = new URLSearchParams(location.search).get('parts') !== '0';
  const root = new THREE.Group();
  // the mod builds every piece from anchor_world_transform * relative_transform;
  // only the rotation part matters for framing/thumbnail
  if (qs.get('wrot') === '1' && Array.isArray(bp.w) && bp.w.length === 7) {
    root.quaternion.copy(ueQuat2three(bp.w[3], bp.w[4], bp.w[5], bp.w[6]));
  }
  scene.add(root);
  const placed = [];
  const types = bp.t;

  const load = async (bpName, id) => {
    const attempts = [];
    const tryGlb = async (url, stage) => {
      try { return { gltf: await loader.loadAsync(url) }; }
      catch (e) {
        collectError(stage, id, e, { url });
        return { http: httpStatusOf(e), error: e };
      }
    };
    const entry = PARTS ? partsManifest[bpName] : null;
    let partial = null;
    if (entry && Array.isArray(entry.parts) && entry.parts.length > 1) {
      const group = new THREE.Group();
      let loaded = 0;
      for (const part of entry.parts) {
        const r = await tryGlb(`${MESH_BASE}/${bpName}/${part}.glb`, 'part');
        attempts.push(r);
        if (r.gltf) { group.add(r.gltf.scene); loaded++; }
      }
      if (loaded === entry.parts.length) return { scene: group, partial: false };
      if (loaded > 0) partial = { scene: group, loaded, total: entry.parts.length };
    }
    for (const cand of [bpName, id]) {
      if (!cand) continue;
      const r = await tryGlb(`${MESH_BASE}/${cand}.glb`, 'glb');
      attempts.push(r);
      if (r.gltf) return { scene: r.gltf.scene, partial: false };
    }
    if (partial) return { scene: partial.scene, partial: true, loaded: partial.loaded, total: partial.total };
    // only 404s (or no candidate at all) mean "this id has no GLB in meshes/"
    const allMissing = attempts.length === 0 || attempts.every(a => a.http === 404);
    return allMissing ? { missing: true } : { failed: true };
  };

  async function getModel(bpName, id) {
    const keyName = (bpName || id);
    if (cache.has(keyName)) return cache.get(keyName);
    const p = load(bpName, id);
    cache.set(keyName, p);
    return p;
  }

  for (let i = 0; i < bp.b.length; i++) {
    const rec = bp.b[i];
    const id = types[rec[0] - 1];
    const bpName = idmap[id];
    status.pieces++;
    const info = await getModel(bpName, id);
    if (!info || info.missing) { status.missing.push(id); continue; }
    if (info.failed) { status.failed++; continue; }

    const holder = new THREE.Group();
    // glTF is Y-up already: position = (ue.x, ue.z, -ue.y) * cm->m
    holder.position.copy(ue2three(rec[1], rec[2], rec[3]).multiplyScalar(CM));
    holder.quaternion.copy(ueQuat2three(rec[4], rec[5], rec[6], rec[7]));

    const inst = info.scene.clone(true);
    const off = offsets[id] || offsets[bpName];
    if (off && off.translation && off.rotation) {
      const t = ue2three(off.translation.x, off.translation.y, off.translation.z).multiplyScalar(CM);
      inst.position.copy(t);
      inst.quaternion.copy(ueQuat2three(off.rotation.x, off.rotation.y, off.rotation.z, off.rotation.w));
    }
    inst.traverse(o => {
      if (!o.isMesh) return;
      o.castShadow = false;
      if (!useMaterials) {
        o.material = new THREE.MeshStandardMaterial({ color: legacyTint(id), roughness: 0.82, metalness: 0.0 });
        status.materials.legacy = (status.materials.legacy || 0) + 1;
        return;
      }
      // per-primitive material: a glass pane and its frame live in the same mesh
      const matName = (Array.isArray(o.material) ? o.material[0] : o.material)?.name;
      const prof = profileFor(matName, id);
      status.materials[prof] = (status.materials[prof] || 0) + 1;
      o.material = makeMaterial(prof, id, matName, texmap, texLoader);
    });
    holder.add(inst);
    root.add(holder);
    placed.push(holder);
    if (info.partial) status.partial++;
    else status.placed++;
  }

  // ---- framing from real geometry bounds (mod uses mesh GetBounds())
  const box = boundsOf(placed);                     // three metres
  status.bbox = { min: box.min.toArray(), max: box.max.toArray() };

  const camOverride = views !== null || ['az', 'el', 'dist', 'fit', 'top', 'ortho']
    .some(k => qs.get(k) !== null);

  // Every texture (texmap) is decoded before the first render; see awaitTextures().
  await awaitTextures();

  // ---- inspection sheet: N directions tiled into one canvas ---------------
  if (views && views.length > 1) {
    const n = views.length;
    const cols = Math.ceil(Math.sqrt(n)), rows = Math.ceil(n / cols);
    // ceil + per-tile clipping: floor() left unpainted strips on non-divisible sizes
    const tw = Math.ceil(W / cols), th = Math.ceil(H / rows);
    renderer.setScissorTest(true);
    renderer.setClearColor(0x0b1420, 1);             // same as the page background
    renderer.setViewport(0, 0, W, H);
    renderer.setScissor(0, 0, W, H);
    renderer.clear();                                // cover the whole canvas first
    status.views = [];
    for (let k = 0; k < n; k++) {
      const [az, el] = views[k];
      const v = viewCamera(box, az, el, tw / th, null);
      const col = k % cols, row = Math.floor(k / cols);
      const x = col * tw;
      const y = Math.max(0, H - (row + 1) * th);     // three viewport origin = bottom-left
      const cw = Math.min(tw, W - x), ch = Math.min(th, H - y);
      renderer.setViewport(x, y, cw, ch);
      renderer.setScissor(x, y, cw, ch);
      renderer.render(scene, v.cam);
      const azw = ((az % 360) + 360) % 360;          // report and label in 0..360, not 225..525
      status.views.push({ az: azw, el, distance_m: v.dist });
      const lab = document.createElement('div');
      lab.textContent = 'az ' + Math.round(azw) + '\u00b0  el ' + Math.round(el) + '\u00b0';
      lab.style.cssText = 'position:fixed;left:' + (x + 10) + 'px;top:' + (row * th + 8) + 'px;'
        + 'font:600 15px/1.3 system-ui,sans-serif;color:#eaf2ff;text-shadow:0 1px 3px #000;'
        + 'pointer-events:none';
      document.body.appendChild(lab);
    }
    renderer.setScissorTest(false);
    renderer.setViewport(0, 0, W, H);
    status.camera = { mode: 'sheet', cols, rows, tile: [tw, th], views: status.views };
    window.__renderExit = 0;
    window.__renderDone = true;
    window.__renderStatus = status;
    return;
  }

  // ---- a single free-camera view -----------------------------------------
  if (views || camOverride) {
    let az = views ? views[0][0] : parseFloat(qs.get('az') ?? String(AZ0));
    let el = views ? views[0][1] : parseFloat(qs.get('el') ?? String(EL0));
    if (!views && qs.get('top') === '1') el = 89.5;
    const dv = qs.get('dist') !== null ? parseFloat(qs.get('dist')) : null;
    const v = viewCamera(box, az, el, W / H, dv);
    status.camera = { distance_m: v.dist, center: v.center.toArray(), mode: 'free', basis: v.basis };
    renderer.render(scene, v.cam);
    window.__renderExit = 0;
    window.__renderDone = true;
    window.__renderStatus = status;
    return;
  }

  // ---- default: the mod's own framing, unchanged -------------------------
  const frame = computeCameraFrame(box.min, box.max);
  status.camera = { distance_m: frame.dist, center: frame.center.toArray(), mode: 'mod', basis: null };
  const camPos = frame.center.clone().addScaledVector(frame.F, -frame.dist);
  const vFov = 2 * Math.atan(Math.tan((FOV_H / 2) * Math.PI / 180) * (H / W)) * 180 / Math.PI;
  const camera = new THREE.PerspectiveCamera(vFov, W / H, 0.05, 20000);
  camera.up.copy(frame.U);
  camera.position.copy(camPos);
  camera.lookAt(frame.center);
  scene.add(camera);

  renderer.render(scene, camera);
  window.__renderExit = 0;
  window.__renderDone = true;
  window.__renderStatus = status;
}

main().catch(e => {
  const usage = e instanceof UsageError;
  window.__renderFailed = true;
  window.__renderExit = usage ? 2 : 1;
  window.__renderStatus = { error: (usage ? 'usage: ' : '') + String(e && (usage ? e.message : (e.stack || e)) || e) };
  window.__renderDone = true;
});
