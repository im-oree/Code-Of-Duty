/**
 * A low-poly ammo crate — the worked example for the modelling pipeline.
 *
 * It exercises the parts of the toolchain that matter without being a toy:
 * segmented primitives, predicate-driven extrusion, per-part materials from
 * the project palette, and a pivot at the base so it drops onto a floor at
 * y = 0 the way a Unity prop should.
 *
 * Run it:
 *   npm run model -- --script models/ammo-crate.model.ts --preview --at -1.6,0,0
 */

import type { Modeler } from '../src/model/Modeler.ts';
import { palette } from '../src/model/Materials.ts';

export default function build(m: Modeler): void {
  const width = 0.86;
  const depth = 0.44;
  const bodyHeight = 0.34;

  // Body. Segmented across the width so the ribs below have somewhere to sit.
  m.box('Body', {
    size: { x: width, y: bodyHeight, z: depth },
    segments: { x: 6, y: 2, z: 3 },
    pivot: 'base',
  }, {
    material: { color: palette.olive, roughness: 0.85 },
  });

  // Reinforcing ribs: push out the two end bands on each long side. Chosen by
  // where the faces are, not by id -- the same predicate keeps working if the
  // segment count changes.
  const half = width / 2;
  m.extrude('Body', { x: 0, y: 0, z: 0.022 }, (c) =>
    Math.abs(c.z) > depth / 2 - 1e-4 && Math.abs(c.x) > half * 0.52);
  m.extrude('Body', { x: 0.022, y: 0, z: 0 }, (c) =>
    Math.abs(c.x) > half - 1e-4);

  // Lid, sitting on top of the body.
  m.box('Lid', {
    size: { x: width + 0.02, y: 0.075, z: depth + 0.02 },
    segments: { x: 4, y: 1, z: 2 },
    pivot: 'base',
  }, {
    position: { x: 0, y: bodyHeight, z: 0 },
    material: { color: palette.olive, roughness: 0.8 },
  });

  // Two latches on the front face.
  for (const side of [-1, 1]) {
    m.box(`Latch_${side < 0 ? 'L' : 'R'}`, {
      size: { x: 0.07, y: 0.09, z: 0.03 },
      pivot: 'center',
    }, {
      position: { x: side * 0.26, y: bodyHeight + 0.005, z: depth / 2 + 0.012 },
      material: { color: palette.steel, metallic: 0.85, roughness: 0.35 },
    });
  }

  // Rope handles at each end, as flattened cylinders lying on their side.
  for (const side of [-1, 1]) {
    m.cylinder(`Handle_${side < 0 ? 'L' : 'R'}`, {
      radius: 0.055,
      height: 0.028,
      radialSegments: 8,
      capped: true,
    }, {
      position: { x: side * (half + 0.012), y: bodyHeight * 0.62, z: 0 },
      rotation: { x: 0, y: 0, z: 90 },
      material: { color: palette.brass, metallic: 0.7, roughness: 0.45 },
    });
  }
}
