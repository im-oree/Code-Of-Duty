/**
 * The modelling pipeline.
 *
 * These cases are the ones that actually bit. The first crate built by this
 * toolchain rendered as an open shell with holes where faces should have been,
 * and nothing in the pipeline complained: the geometry was valid, the export
 * succeeded, the round-trip check passed. Two defects hid behind that silence.
 *
 *   1. Face winding was reversed on some faces and not others, because each
 *      primitive had hand-tuned its vertex order by trial against a mirrored
 *      coordinate system. A back-facing polygon is not an error, it is just
 *      invisible, so only a render showed it.
 *   2. The vendored kernel over-allocates its buffers to twice the size the
 *      mesh needs, as editing headroom, and zero-fills the slack. That padding
 *      was being exported: phantom vertices at the origin and degenerate
 *      triangles in a shipped asset.
 *
 * Both are geometric invariants, so they are asserted as invariants here
 * rather than left to the eye on the next screenshot.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { Modeler } from '../src/model/Modeler.ts';

interface SurfaceAudit {
  vertices: number;
  triangles: number;
  degenerate: number;
  /** Directed edges lacking exactly one opposing twin: mixed or open winding. */
  inconsistentEdges: number;
  /** Positive when the surface is wound outward, negative when inside-out. */
  signedVolume: number;
}

/**
 * Check that a closed surface is consistently wound outward.
 *
 * The obvious test -- "does each triangle's normal point away from the centre"
 * -- only holds for convex shapes, and quietly reports correct geometry as
 * broken the moment a model grows a rib, a recess or any other concave detail.
 * Two properties are exact for any closed manifold, convex or not:
 *
 *   - every directed edge a->b appears exactly once, paired with exactly one
 *     b->a in the neighbouring triangle, which fails the instant two adjacent
 *     faces disagree about which way is out;
 *   - the signed volume of the surface is positive when it is wound outward
 *     and negative when it is inside-out, which catches a mesh that is
 *     perfectly consistent but globally reversed.
 *
 * Edges are keyed by position rather than index because flat shading
 * duplicates a vertex per face, so index-adjacency does not exist here.
 */
function auditSurface(root: THREE.Object3D): SurfaceAudit {
  const result: SurfaceAudit = {
    vertices: 0, triangles: 0, degenerate: 0, inconsistentEdges: 0, signedVolume: 0,
  };
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  const key = (v: THREE.Vector3): string =>
    `${Math.round(v.x * 1e5)},${Math.round(v.y * 1e5)},${Math.round(v.z * 1e5)}`;

  root.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (!mesh.isMesh) return;
    const geometry = mesh.geometry;
    const position = geometry.getAttribute('position');
    const index = geometry.getIndex();
    assert.ok(index, `mesh ${mesh.name} should be indexed`);
    result.vertices += position.count;

    const directed = new Map<string, number>();
    const count = (from: THREE.Vector3, to: THREE.Vector3): void => {
      const k = `${key(from)}>${key(to)}`;
      directed.set(k, (directed.get(k) ?? 0) + 1);
    };

    for (let i = 0; i < index!.count; i += 3) {
      a.fromBufferAttribute(position, index!.getX(i));
      b.fromBufferAttribute(position, index!.getX(i + 1));
      c.fromBufferAttribute(position, index!.getX(i + 2));
      result.triangles++;
      if (b.clone().sub(a).cross(c.clone().sub(a)).lengthSq() < 1e-16) {
        result.degenerate++;
        continue;
      }
      result.signedVolume += a.dot(b.clone().cross(c)) / 6;
      count(a, b); count(b, c); count(c, a);
    }

    for (const [edge, times] of directed) {
      const [from, to] = edge.split('>');
      if (times !== 1 || (directed.get(`${to}>${from}`) ?? 0) !== 1) result.inconsistentEdges++;
    }
  });
  return result;
}

test('every primitive is built entirely outward-facing', () => {
  const shapes: Array<[string, (m: Modeler) => unknown]> = [
    ['box', (m) => m.box('P', { size: { x: 1, y: 1, z: 1 } })],
    ['segmented box', (m) => m.box('P', {
      size: { x: 0.9, y: 0.4, z: 0.5 }, segments: { x: 6, y: 2, z: 3 },
    })],
    ['base-pivoted box', (m) => m.box('P', { size: { x: 1, y: 2, z: 1 }, pivot: 'base' })],
    ['cylinder', (m) => m.cylinder('P', { radius: 0.3, height: 1, radialSegments: 8 })],
    ['tapered cylinder', (m) => m.cylinder('P', {
      radius: 0.3, radiusTop: 0.05, height: 1, radialSegments: 10, heightSegments: 3,
    })],
    ['sphere', (m) => m.sphere('P', { radius: 0.5, segments: 10, rings: 6 })],
  ];

  for (const [name, make] of shapes) {
    const modeller = new Modeler('orientation');
    make(modeller);
    const audit = auditSurface(modeller.build());
    assert.equal(audit.inconsistentEdges, 0,
      `${name}: ${audit.inconsistentEdges} edges are wound inconsistently`);
    assert.ok(audit.signedVolume > 0,
      `${name}: surface is inside-out (signed volume ${audit.signedVolume})`);
    assert.equal(audit.degenerate, 0, `${name}: ${audit.degenerate} degenerate triangles`);
    assert.ok(audit.triangles > 0, `${name}: produced no geometry`);
  }
});

test('exported geometry carries no allocator padding', () => {
  const modeller = new Modeler('compaction');
  modeller.box('Cube', { size: { x: 1, y: 1, z: 1 } });
  const audit = auditSurface(modeller.build());

  // A cube is six quads. Flat shading duplicates each corner per face, so
  // 6 * 4 vertices and 6 * 2 triangles -- and nothing beyond that.
  assert.equal(audit.vertices, 24, 'cube should export exactly 24 vertices');
  assert.equal(audit.triangles, 12, 'cube should export exactly 12 triangles');
  assert.equal(audit.degenerate, 0);
});

test('a plane faces up, and is the only open surface that needs saying so', () => {
  const modeller = new Modeler('plane');
  modeller.plane('Ground', { size: { x: 2, z: 2 } });
  const root = modeller.build();

  // An open surface has no inside, so the closed-shape audit does not apply:
  // check the normal directly instead.
  let checked = 0;
  root.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (!mesh.isMesh) return;
    const normals = mesh.geometry.getAttribute('normal');
    for (let i = 0; i < normals.count; i++) {
      assert.ok(normals.getY(i) > 0.99, 'plane normals should point along +Y');
      checked++;
    }
  });
  assert.ok(checked > 0, 'plane produced no normals');
});

test('a non-finite dimension is rejected rather than silently producing NaN geometry', () => {
  const modeller = new Modeler('guard');
  // The plane API takes {x, z}; passing {x, y} used to yield NaN vertices and
  // an unrenderable, un-diagnosable mesh.
  assert.throws(
    () => modeller.plane('Bad', { size: { x: 1, z: undefined as unknown as number } }),
    /finite/,
    'a missing dimension should fail loudly',
  );
});

test('extrusion adds geometry and leaves the surface outward-facing', () => {
  const modeller = new Modeler('extrude');
  const part = modeller.box('Body', {
    size: { x: 1, y: 0.4, z: 0.6 }, segments: { x: 4, y: 1, z: 1 },
  });
  const before = part.mesh.faceIds.length;

  // Push out the two end bands on the +Z side, the same predicate-driven
  // operation the crate uses for its ribs.
  modeller.extrude('Body', { x: 0, y: 0, z: 0.05 }, (c) => c.z > 0.29 && Math.abs(c.x) > 0.24);

  // Duplicated cap faces replace the originals, and every boundary edge of the
  // selection gains a side wall joining the raised cap back to the shell. Two
  // separate quads with four boundary edges each: 18 - 2 + 2 + 8.
  assert.equal(part.mesh.faceIds.length, 26,
    'extrusion should replace the selected faces and build side walls');
  assert.ok(part.mesh.faceIds.length > before, 'extrusion should add faces');

  const audit = auditSurface(modeller.build());
  assert.equal(audit.inconsistentEdges, 0,
    `${audit.inconsistentEdges} edges are wound inconsistently after extrusion`);
  assert.ok(audit.signedVolume > 0, 'extruded surface should stay outward-facing');
  assert.equal(audit.degenerate, 0);
});

test('an asymmetric, rotated, scaled model round-trips through Unity export', async () => {
  const modeller = new Modeler('round-trip');

  // Asymmetry is the point: a symmetric shape survives a sign error unchanged,
  // so it cannot detect the one bug this check exists to catch.
  modeller.box('Chassis', {
    size: { x: 0.8, y: 0.2, z: 0.5 }, segments: { x: 3, y: 1, z: 2 }, pivot: 'base',
  }, { position: { x: 0.31, y: 0.07, z: -0.42 } });

  modeller.cylinder('Barrel', {
    radius: 0.06, radiusTop: 0.03, height: 0.7, radialSegments: 7,
  }, {
    position: { x: -0.17, y: 0.55, z: 0.28 },
    rotation: { x: 18, y: -37, z: 6 },
    scale: { x: 1, y: 1.4, z: 0.8 },
  });

  modeller.extrude('Chassis', { x: 0, y: 0.09, z: 0 }, (c) => c.x > 0.13);

  const { report } = await modeller.toGlbVerified();
  assert.ok(report.ok, report.detail);
  // Rotation and scale used to disable this check entirely; make sure it is
  // actually looking at both parts rather than quietly skipping them.
  assert.ok(report.checked >= 20, `only ${report.checked} vertices were verified`);
  assert.ok(report.maxError < 1e-4, `round-trip error ${report.maxError} m`);
});

test('Unity coordinates survive the handedness flip on the way in and out', () => {
  const modeller = new Modeler('coords');
  const part = modeller.box('Marker', { size: { x: 2, y: 2, z: 2 } });

  // +Z is forward in Unity. Author a vertex there and read it back.
  const id = part.mesh.addVertex({ x: 0.25, y: -0.5, z: 0.75 });
  const back = part.mesh.vertexPosition(id);
  assert.ok(Math.abs(back.x - 0.25) < 1e-9);
  assert.ok(Math.abs(back.y - -0.5) < 1e-9);
  assert.ok(Math.abs(back.z - 0.75) < 1e-9, 'forward axis should not flip sign on read-back');

  part.mesh.setVertexPosition(id, { x: -1, y: 0.5, z: -0.25 });
  const moved = part.mesh.vertexPosition(id);
  assert.ok(Math.abs(moved.z - -0.25) < 1e-9, 'writing a Unity position should be symmetric with reading it');
});
