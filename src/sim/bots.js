// The vehicles players can pick. Each is a physical description in the body
// FRD frame (x forward, y right, z down, origin at the CG): mass, inertia,
// added mass, linear + quadratic damping (Fossen form) and a thruster layout
// that runs through the simulator's own thruster model (T200 / T500 /
// generic, src/physics/thruster.js) and allocation (src/alloc/allocation.js).
// DUBURI uses the BlueROV2 Heavy geometry (ArduSub FRAME_CONFIG 2) and the
// hydrodynamic coefficients identified for it by Wu (2018); the others are
// scaled from it by size and shape.

import { heavyLayout, vectoredLayout } from '../alloc/frames.js';

const S = Math.SQRT1_2;
const scale = (L, k) => L.map((t) => ({ ...t, pos: t.pos.map((v) => v * k) }));

export const BOTS = {
  duburi: {
    id: 'duburi',
    name: 'Duburi',
    kind: 'Open frame · 8 × T200 vectored 6-DOF',
    blurb: 'Team DUBURI\'s workhorse. Full 6-DOF control, good at everything. Rolls and flips cleanly for gate style.',
    color: '#ffb547',
    mass: 11.5, Bnet: 2.0, dims: [0.46, 0.58, 0.26], zB: 0.02,
    inertia: [0.26, 0.23, 0.37],
    addedMass: [6.36, 7.12, 18.68, 0.189, 0.135, 0.222],
    dampLin: [13.7, 8, 33, 0.4, 0.8, 0.5],
    dampQuad: [141, 217, 190, 1.19, 0.47, 1.5],
    thrusters: heavyLayout().map((t, i) => ({ ...t, preset: 't200-16v', hand: i % 2 ? -1 : 1 })),
    spheres: [[0.11, 0, 0, 0.21], [-0.11, 0, 0, 0.21]],
    gripper: [0.27, 0, 0.14], marker: [0, 0, 0.15], torpedo: [0.25, 0, 0.05],
  },
  barracuda: {
    id: 'barracuda',
    name: 'Barracuda',
    kind: 'Torpedo AUV · 5 thrusters',
    blurb: 'Long and slim: fastest in a straight line, but slow to turn and it cannot roll. Pitch flips only.',
    color: '#4fd1c5',
    mass: 16, Bnet: 1.5, dims: [1.1, 0.17, 0.17], zB: 0.03,
    inertia: [0.06, 1.65, 1.65],
    addedMass: [1.0, 24, 24, 0.01, 1.7, 1.7],
    dampLin: [3, 25, 25, 0.3, 4, 4],
    dampQuad: [10, 160, 160, 0.1, 8, 8],
    thrusters: [
      { name: 'Main', pos: [-0.56, 0, 0], axis: [1, 0, 0], preset: 't200-16v', hand: 1 },
      { name: 'Vert fwd', pos: [0.36, 0, 0], axis: [0, 0, 1], preset: 't200-16v', hand: 1 },
      { name: 'Vert aft', pos: [-0.34, 0, 0], axis: [0, 0, 1], preset: 't200-16v', hand: -1 },
      { name: 'Lat fwd', pos: [0.32, 0, 0], axis: [0, 1, 0], preset: 't200-16v', hand: 1 },
      { name: 'Lat aft', pos: [-0.3, 0, 0], axis: [0, 1, 0], preset: 't200-16v', hand: -1 },
    ],
    spheres: [[0.4, 0, 0, 0.1], [0.13, 0, 0, 0.1], [-0.14, 0, 0, 0.1], [-0.42, 0, 0, 0.1]],
    gripper: [0.52, 0, 0.1], marker: [0, 0, 0.1], torpedo: [0.5, 0, -0.06],
  },
  kraken: {
    id: 'kraken',
    name: 'Kraken',
    kind: 'Box frame · 6 × T500 vectored',
    blurb: 'Heavy and very stable — shrugs off current. Huge thrust, but it is big and has no pitch control.',
    color: '#f56565',
    mass: 30, Bnet: 3.0, dims: [0.76, 0.62, 0.46], zB: 0.06,
    inertia: [0.9, 1.0, 1.3],
    addedMass: [14, 18, 40, 0.6, 0.6, 0.7],
    dampLin: [25, 30, 60, 2, 2, 1.5],
    dampQuad: [260, 320, 350, 4, 4, 4],
    // horizontal thrusters raised to the CG plane so full surge does not pitch it over
    thrusters: scale(vectoredLayout(), 1.7).map((t, i) => ({ ...t, pos: [t.pos[0], t.pos[1], i < 4 ? 0 : t.pos[2]], preset: 't500-20v', hand: i % 2 ? -1 : 1 })),
    spheres: [[0.17, 0, 0, 0.32], [-0.17, 0, 0, 0.32]],
    gripper: [0.42, 0, 0.24], marker: [0, 0, 0.25], torpedo: [0.4, 0.2, 0.08],
  },
  manta: {
    id: 'manta',
    name: 'Manta',
    kind: 'Flat wing · 8 × T200, low stability',
    blurb: 'Wide and flat with small righting moment: the best stunt bot. Rolls fast, a bit twitchy in waves.',
    color: '#9f7aea',
    mass: 13, Bnet: 1.5, dims: [0.62, 0.84, 0.18], zB: 0.012,
    inertia: [0.42, 0.3, 0.65],
    addedMass: [5, 6, 30, 0.4, 0.25, 0.35],
    dampLin: [12, 14, 45, 0.6, 0.6, 0.6],
    dampQuad: [110, 140, 320, 2.2, 1.2, 1.4],
    thrusters: [
      { name: 'H front-right', pos: [0.2, 0.26, 0.02], axis: [-S, S, 0] },
      { name: 'H front-left', pos: [0.2, -0.26, 0.02], axis: [-S, -S, 0] },
      { name: 'H rear-right', pos: [-0.2, 0.26, 0.02], axis: [S, S, 0] },
      { name: 'H rear-left', pos: [-0.2, -0.26, 0.02], axis: [S, -S, 0] },
      { name: 'V front-right', pos: [0.16, 0.36, -0.02], axis: [0, 0, 1] },
      { name: 'V front-left', pos: [0.16, -0.36, -0.02], axis: [0, 0, 1] },
      { name: 'V rear-right', pos: [-0.16, 0.36, -0.02], axis: [0, 0, 1] },
      { name: 'V rear-left', pos: [-0.16, -0.36, -0.02], axis: [0, 0, 1] },
    ].map((t, i) => ({ ...t, preset: 't200-16v', hand: i % 2 ? -1 : 1 })),
    spheres: [[0.12, 0.22, 0, 0.14], [0.12, -0.22, 0, 0.14], [-0.12, 0.22, 0, 0.14], [-0.12, -0.22, 0, 0.14]],
    gripper: [0.32, 0, 0.1], marker: [0, 0, 0.1], torpedo: [0.3, 0.1, 0.02],
  },
  dubomini: {
    id: 'dubomini',
    name: 'Dubo Mini',
    kind: 'Mini AUV · 5 small thrusters',
    blurb: 'Team DUBURI\'s little one: two drive thrusters, a lateral tunnel and two vertical tunnels. Light, quick to strafe, does pitch flips — but the current pushes it around.',
    color: '#68d391',
    mass: 4.2, Bnet: 0.6, dims: [0.34, 0.26, 0.16], zB: 0.014,
    inertia: [0.03, 0.04, 0.05],
    addedMass: [1.4, 2.2, 3.5, 0.02, 0.025, 0.02],
    dampLin: [5, 7, 10, 0.1, 0.1, 0.08],
    dampQuad: [30, 45, 55, 0.2, 0.2, 0.15],
    // surge + yaw (two drive thrusters), sway (tunnel), heave + pitch (two vertical tunnels); roll is passive
    thrusters: [
      { name: 'Fwd right', pos: [-0.12, 0.1, 0], axis: [1, 0, 0], hand: 1 },
      { name: 'Fwd left', pos: [-0.12, -0.1, 0], axis: [1, 0, 0], hand: -1 },
      { name: 'Lateral', pos: [0.02, 0, 0], axis: [0, 1, 0], hand: 1 },
      { name: 'Vert front', pos: [0.12, 0, -0.01], axis: [0, 0, 1], hand: 1 },
      { name: 'Vert rear', pos: [-0.08, 0, -0.01], axis: [0, 0, 1], hand: -1 },
    ].map((t) => ({ ...t, preset: 'generic-small' })),
    spheres: [[0, 0, 0, 0.17]],
    gripper: [0.19, 0, 0.08], marker: [0, 0, 0.08], torpedo: [0.17, 0, -0.04],
  },
};

export const BOT_ORDER = ['duburi', 'dubomini', 'barracuda', 'kraken', 'manta'];
export const PLAYER_COLORS = ['#ffb547', '#4fd1c5', '#f56565', '#9f7aea', '#68d391', '#63b3ed', '#f6e05e', '#ed64a6'];
