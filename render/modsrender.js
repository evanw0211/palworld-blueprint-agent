// Real-mesh thumbnail renderer for Simple Building Blueprints (schema-9 JSON).
//
// Reproduces BlueprintThumbnailRenderer.lua:
//   * 320x180, horizontal FOV 40 deg, FRAME_FILL 0.86, MIN_CAMERA_DISTANCE 100
//   * CAMERA_FORWARD = normalize(4.2, 4.2, -3.0)
//   * bounds = union of every preview mesh AABB (here: real GLB geometry)
// Units: blueprint = cm, GLB = m (MODEL_UNIT_SCALE = 100).
// Axis mapping (measured previously): three = (ue.x, ue.z, -ue.y),
// three.rotation = +yaw, glb static meshes are already Y-up.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const qs = new URLSearchParams(location.search);
const BP_URL = qs.get('bp');
const IDMAP_URL = qs.get('idmap') || '/blueprints/meshes/_id_to_bp.json';
const OFFSETS_URL = qs.get('offsets') || '/blueprints/meshes/_actor_offsets.json';
const MESH_BASE = qs.get('meshes') || '/blueprints/meshes';
const W = parseInt(qs.get('w') || '1280', 10);
const H = parseInt(qs.get('h') || '720', 10);
const CM = 0.01;                       // cm -> m

const FRAME_FILL = 0.86, FOV_H = 40.0, MIN_DIST = 1.0, MIN_DEPTH = 0.1;   // metres: 100 cm / 10 cm

const status = { pieces: 0, placed: 0, missing: [], errors: [], bbox: null, camera: null };

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

// ---- exact port of BlueprintThumbnailRenderer.compute_camera_frame,
//      expressed in the three.js basis (ue -> three is a rigid rotation,
//      so the same 8-corner distance formula applies unchanged)
function computeCameraFrame(min, max) {
  const F = new THREE.Vector3(4.2, -3.0, -4.2).normalize();     // ue(4.2,4.2,-3)
  const R = new THREE.Vector3(-4.2, 0.0, -4.2).normalize();     // ue(-4.2,4.2,0)
  const U = new THREE.Vector3(12.6, 35.28, -12.6).normalize();  // cross(F,R) in ue
  const center = new THREE.Vector3(
    (min.x + max.x) / 2, (min.y + max.y) / 2, (min.z + max.z) / 2);
  const hTan = Math.tan((FOV_H * 0.5) * Math.PI / 180) * FRAME_FILL;
  const vTan = hTan / (320 / 180);
  let dist = MIN_DIST;
  for (const x of [min.x, max.x]) for (const y of [min.y, max.y]) for (const z of [min.z, max.z]) {
    const rel = new THREE.Vector3(x - center.x, y - center.y, z - center.z);
    const depth = rel.dot(F);
    const horizontal = Math.abs(rel.dot(R));
    const vertical = Math.abs(rel.dot(U));
    dist = Math.max(dist, horizontal / hTan - depth, vertical / vTan - depth, MIN_DEPTH - depth);
  }
  return { F, R, U, center, dist };
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

const TINTS = [
  [/glass/i, 0x9fc4d8],
  [/roof/i, 0x8d7f6d],
  [/stair/i, 0xa07a4e],
  [/pillar/i, 0xb9b3a6],
  [/foundation/i, 0xc9c2b2],
  [/wall|window|door/i, 0xd8cfbb],
];
function tintFor(id) {
  const s = String(id || "");
  for (const [re, c] of TINTS) if (re.test(s)) return c;
  return 0xcfc6b4;
}

async function main() {
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

  // Multi-part blueprints (door leaf + frame, …) are exported as
  // <BP>/<part>.glb; load every part so the piece looks like the mod's render.
  const load = async (bpName, id) => {
    const entry = PARTS ? partsManifest[bpName] : null;
    if (entry && Array.isArray(entry.parts) && entry.parts.length > 1) {
      const group = new THREE.Group();
      let any = false;
      for (const part of entry.parts) {
        try {
          const g = await loader.loadAsync(`${MESH_BASE}/${bpName}/${part}.glb`);
          group.add(g.scene);
          any = true;
        } catch (e) { /* skip missing part */ }
      }
      if (any) return { scene: group };
    }
    for (const cand of [bpName, id]) {
      if (!cand) continue;
      try { return await loader.loadAsync(`${MESH_BASE}/${cand}.glb`); } catch (e) { /* next */ }
    }
    return null;
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
    const gltfP = await getModel(bpName, id);
    let gltf = null;
    try { gltf = await gltfP; } catch (e) { gltf = null; }
    if (!gltf) { status.missing.push(id); continue; }

    const holder = new THREE.Group();
    // glTF is Y-up already: position = (ue.x, ue.z, -ue.y) * cm->m
    holder.position.copy(ue2three(rec[1], rec[2], rec[3]).multiplyScalar(CM));
    holder.quaternion.copy(ueQuat2three(rec[4], rec[5], rec[6], rec[7]));

    const inst = gltf.scene.clone(true);
    const off = offsets[id] || offsets[bpName];
    if (off && off.translation && off.rotation) {
      const t = ue2three(off.translation.x, off.translation.y, off.translation.z).multiplyScalar(CM);
      inst.position.copy(t);
      inst.quaternion.copy(ueQuat2three(off.rotation.x, off.rotation.y, off.rotation.z, off.rotation.w));
    }
    inst.traverse(o => {
      if (o.isMesh) {
        o.castShadow = false;
        // exported glb has no textures (images: 0) and COLOR_0 is dark, so use
        // a per-family tint to keep the thumbnail readable
        o.material = new THREE.MeshStandardMaterial({
          color: tintFor(id),
          roughness: 0.82,
          metalness: 0.0,
        });
      }
    });
    holder.add(inst);
    root.add(holder);
    placed.push(holder);
    status.placed++;
  }

  // ---- framing from real geometry bounds (mod uses mesh GetBounds())
  const box = boundsOf(placed);                     // three metres
  const frame = computeCameraFrame(box.min, box.max);
  status.bbox = { min: box.min.toArray(), max: box.max.toArray() };
  status.camera = { distance_m: frame.dist, center: frame.center.toArray() };

  const camPos = frame.center.clone().addScaledVector(frame.F, -frame.dist);
  const camTarget = frame.center.clone();
  const camUp = frame.U.clone();

  const vFov = 2 * Math.atan(Math.tan((FOV_H / 2) * Math.PI / 180) * (H / W)) * 180 / Math.PI;
  const camera = new THREE.PerspectiveCamera(vFov, W / H, 0.05, 20000);
  camera.up.copy(camUp);
  camera.position.copy(camPos);
  camera.lookAt(camTarget);
  scene.add(camera);

  renderer.render(scene, camera);
  window.__renderDone = true;
  window.__renderStatus = status;
}

main().catch(e => {
  window.__renderDone = true;
  window.__renderStatus = { error: String(e && e.stack || e) };
});








