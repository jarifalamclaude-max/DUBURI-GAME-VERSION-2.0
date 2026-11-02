// (The JS mutates vectors in place: every array stored per step is copied.)
// Runs the JavaScript original (../../../src) and writes golden JSON for the C#
// Core tests (tools/CoreTests) to tools/golden/out/ (gitignored).
//   node tools/golden/make-golden.mjs
// The C# tests rebuild the same structures from Duburi.Core and compare them
// value by value. Everything here is deterministic (seeded RNGs; Math.random is
// replaced by a seeded generator, it only names items).

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const src = join(here, '../../../src');
const out = join(here, 'out');
const imp = (p) => import(join(src, p));

const M = await imp('core/math.js');
const { RNG, GaussMarkov } = await imp('core/rng.js');
const W = await imp('hydro/water.js');
const { Environment, floorProfile } = await imp('physics/environment.js');
const TH = await imp('physics/thruster.js');
const AL = await imp('alloc/allocation.js');
const FR = await imp('alloc/frames.js');
const { BOTS, BOT_ORDER } = await imp('sim/bots.js');
const { Vehicle, DT } = await imp('sim/vehicle.js');
const { buildCourse, boardLocal } = await imp('sim/course.js');
const { SCENARIOS, SCENARIO_ORDER, environmentConfig } = await imp('sim/scenarios.js');
const { botStats } = await imp('sim/stats.js');
const { Game } = await imp('sim/game.js');
const { Autopilot } = await imp('sim/autopilot.js');
const { defaultSetup, GAME_NAME } = await imp('ui/menu.js').catch(() => ({})); // menu.js touches the DOM only when used

{ // seeded Math.random (item ids)
  const r = new RNG(12345);
  Math.random = () => r.next();
}

mkdirSync(out, { recursive: true });
const files = [];
const save = (name, data) => {
  writeFileSync(join(out, name + '.json'), JSON.stringify(data));
  files.push(name);
};
const arr = (a) => Array.from(a);

// ------------------------------------------------------------------ math
{
  const rng = new RNG(42);
  const mat = (r, c, rank = 0) => {
    let A = Array.from({ length: r }, () => Array.from({ length: c }, () => rng.uniform(-2, 2)));
    if (rank) { // low rank: A = X Y
      const X = Array.from({ length: r }, () => Array.from({ length: rank }, () => rng.uniform(-1, 1)));
      const Y = Array.from({ length: rank }, () => Array.from({ length: c }, () => rng.uniform(-1, 1)));
      A = M.matMul(X, Y);
    }
    return A;
  };
  const mats = [mat(3, 3), mat(6, 8), mat(8, 6), mat(6, 5), mat(6, 6, 4), mat(5, 9, 3), [[1, 0], [0, 0]], [[0, 0, 0]]];
  const linalg = mats.map((A) => { const s = M.svd(A); return { A, S: s.S, U: s.U, V: s.V, P: M.pinv(A), rank: M.rankOf(A) }; });
  const solveA = mat(5, 5), solveB = [1, -2, 3, 0.5, 4];
  const quats = [];
  for (let i = 0; i < 40; i++) {
    const e = [rng.uniform(-3.2, 3.2), rng.uniform(-1.6, 1.6), rng.uniform(-3.2, 3.2)];
    const q = M.qFromEuler(...e);
    const v = [rng.uniform(-2, 2), rng.uniform(-2, 2), rng.uniform(-2, 2)];
    const q2 = M.qnormalize([rng.gauss(), rng.gauss(), rng.gauss(), rng.gauss()]);
    quats.push({ e, q, euler: M.qToEuler(q), aa: M.qToAxisAngle(q2), q2, fromAA: M.qFromAxisAngleVec(v), v, rot: M.qrotate(q, v), rotInv: M.qrotateInv(q, v), mul: M.qmul(q, q2), mat: M.qToMat(q2), wrap: M.wrapPI(e[0] * 7), wrap2: M.wrap2PI(-e[2] * 5), hyp: Math.hypot(...v), norm: M.vnorm(v), cross: M.vcross(v, e) });
  }
  const r2 = new RNG(7), gm = new GaussMarkov(new RNG(9), 0.3, 2);
  const seq = Array.from({ length: 50 }, () => [r2.next(), r2.gauss(), gm.step(1 / 120)]);
  const m3 = mat(3, 3).flat();
  save('math', { linalg, solve: { A: solveA, b: solveB, x: M.solve(solveA, solveB) }, m3: { m: m3, inv: M.m3inv(m3), mul: M.m3mul(m3, M.m3inv(m3)), t: M.m3transpose(m3) }, quats, rng: { seq, seed0: new RNG(0).next(), seedNeg: new RNG(-5.7).next() } });
}

// ------------------------------------------------------------------ water
{
  const Ts = [-2, -1.5, 0, 4, 10, 20, 22, 26, 35], Ss = [0, 10, 34, 34.5, 40];
  const dens = [], visc = [];
  for (const T of Ts) for (const S of Ss) {
    dens.push([T, S, W.waterDensity(T, S), W.eos80Density0(S, W.toT68(T)), W.eos80SecantBulkModulus(S, T, 50), W.eos80Density(S, T, 100)]);
    visc.push([T, S, W.waterViscosity(T, S), W.kinematicViscosity(T, S)]);
  }
  const am = W.ellipsoidAddedMass(0.3, 0.2, 0.1, 1025);
  const drag = W.dragFromProjections({ area: [0.1, 0.2, 0.3], k3: { roll: 0.01, pitch: 0.02, yaw: 0.03 } }, 1020);
  save('water', { dens, visc, air: [0, 100, 1500, 4000].map((h) => [h, W.airPressureAtAltitude(h)]), am, drag, sphere: W.ellipsoidAddedMass(0.2, 0.2, 0.2).values });
}

// ------------------------------------------------------------------ thruster
{
  const curves = {};
  for (const [k, p] of Object.entries(TH.THRUSTER_PRESETS)) curves[k] = TH.parametricCurve(p.curve);
  curves.default = TH.parametricCurve();
  curves.odd = TH.parametricCurve({ fwdMax: 33.3, revMax: 21.7, deadband: 0, expo: 1.7, pwmMin: 1150, pwmMax: 1850 });
  curves.wide = TH.parametricCurve({ deadband: 500 });
  const pwms = []; for (let p = 1000; p <= 2000; p += 7.3) pwms.push(p);
  const thrust = {};
  for (const k of Object.keys(curves)) { const c = new TH.ThrustCurve(curves[k]); thrust[k] = { pwm: arr(c.pwm), T: arr(c.T), fwdMax: c.fwdMax, revMax: c.revMax, at: pwms.map((p) => c.thrust(p)) }; }
  const variants = {
    t200: { preset: 't200-16v' },
    t500: { preset: 't500-20v', hand: -1, deduction: 0.1 },
    small: { preset: 'generic-small', propDiameter: 0.06 },
    rev: { preset: 't200-16v', polarity: -1 },
    revTable: { preset: 't200-12v', polarity: -1, tableOnReversedUnit: true, deductionRev: 0.2 },
    off: { preset: 't200-16v', enabled: false },
    weak: { preset: 't200-16v', health: 0.5, tauUp: 0, reverseDwell: 0 },
    noElec: { preset: 't200-16v', elec: null, inflowModel: false, J0: 0.8 },
    custom: { table: curves.odd, voltage: 14, escMinV: 9, idleCurrent: 0.2 },
  };
  const traces = {};
  const Ttab = (v) => (v.table ? v.table : TH.parametricCurve(TH.THRUSTER_PRESETS[v.preset].curve));
  for (const [name, v] of Object.entries(variants)) {
    const st = new TH.ThrusterState({ ...v, table: Ttab(v) });
    const rows = [];
    for (let i = 0; i < 400; i++) {
      const t = i * DT;
      const pwm = i < 60 ? 1900 : i < 120 ? 1100 : i < 200 ? 1500 + 380 * Math.sin(t * 9) : i < 260 ? 1520 : i < 330 ? 1300 : 1500 + 400 * Math.cos(t * 4);
      const vb = i < 300 ? st.Vnom : st.Vnom * (1 - 0.6 * ((i - 300) / 100));
      st.update(pwm, DT, vb);
      const eff = st.effective(Math.sin(t * 3) * 1.5, -0.05 + i * 0.001);
      rows.push([st.n, st.T, st.Tcmd, st.current, st.dwell, eff, st.vent, st.reactionTorque()]);
    }
    traces[name] = { Vnom: st.Vnom, torqueRatio: st.torqueRatio, reverseDwell: st.reverseDwell, J0Rev: st.J0Rev, escMinV: st.escMinV, idleA: st.idleA, maxCurrent: [6, 12, 16, 22].map((V) => st.maxCurrent(V)), vscale: [5, 10, 12, 16, 21].map((V) => [st.voltageScale(V, 1), st.voltageScale(V, -1)]), rows };
    st.T = 12.5; traces[name].setT = [st.n, st.T];
  }
  const text = '# pwm, kgf\n1100, -2.9\r\n1300;-1.2\n 1500 0\n1700\t1.5,\n\n1900, 3.7, x\nfoo\n';
  save('thruster', { curves, thrust, pwms, traces, parsed: TH.parseCurveText(text, 'kgf'), parsedN: TH.parseCurveText(text) });
}

// ------------------------------------------------------------------ environment
{
  const cfgs = {
    pool: environmentConfig('pool'),
    pacific: environmentConfig('pacific', 20),
    arctic: environmentConfig('arctic'),
    sloped: { temperature: 12, salinity: 30, densityOverride: 0, airPressure: 990, current: [0.1, -0.05, 0.01], turbulence: { sigma: 0.08, tau: 0.5 }, waves: { amplitude: 0.3, period: 3.5, direction: 200 }, pool: { length: 30, width: 14, depth: 6, floor: 'sloped', shallowDepth: 1.5, shallowEnd: 'north', slopeStart: 12, slopeEnd: 13 } },
    override: { densityOverride: 1100, gravity: 9.7, waves: { amplitude: 0.05, period: 0 }, pool: { floor: 'sloped', shallowDepth: 2, slopeStart: 'x', slopeEnd: 4 } },
  };
  const envs = {};
  for (const [name, cfg] of Object.entries(cfgs)) {
    const e = new Environment(cfg, new RNG(77));
    const f = e.floor;
    const xs = [-14, -6.5, -1, 0, 0.5, 3, 7.25, 13];
    const rec = {
      rho: e.rho, nu: e.nu, g: e.g, pAtm: e.pAtm, waveOmega: e.waveOmega, waveK: e.waveK, waveDir: e.waveDir, T68: e.T68, salinityPSU: e.salinityPSU,
      floor: { L: f.L, W: f.W, sloped: f.sloped, minDepth: f.minDepth, maxDepth: f.maxDepth, a: f.a, b: f.b, north: f.north, breaks: f.breaks, depth: xs.map((x) => f.depthAt(x)), slope: xs.map((x) => f.slopeAt(x)) },
      pTable: [0, 1, 7, 100, 255, 511, 512].map((i) => e.pTable[i]), pDz: e.pDz,
      gauge: [-1, 0, 0.3, 1, 2.5, 4, 7.7, 12, 30].map((z) => [e.pressureGauge(z), e.densityAtDepth(z)]),
      steps: [],
    };
    for (let i = 0; i < 240; i++) {
      e.step(DT);
      if (i % 8 === 7) {
        const x = -10 + i * 0.07, y = 3 - i * 0.02, z = 0.2 + i * 0.01;
        rec.steps.push({ t: e.time, gust: e.gust.slice(), gustAccel: e.gustAccel.slice(), eta: e.surfaceElevation(x, y), vel: e.fluidVelocity(x, y, z), acc: e.fluidAccel(x, y, z), p: e.pressureAt(x, y, z), pa: e.absolutePressureAt(x, y, z * 3), wp: e.waveProfile(x, z) });
      }
    }
    envs[name] = rec;
  }
  save('env', envs);
}

// ------------------------------------------------------------------ allocation + frames
const dumpAnalysis = (r) => ({ N: r.N, controllable: r.controllable, status: r.status, factors: r.factors, authority: r.authority, coupling: r.coupling, singular: r.singular, rank: r.rank, warnings: r.warnings, conditioning: r.conditioning ?? 0, candidates: r.candidates ?? [], lever: r.lever ?? 0, Fref: r.Fref ?? 0, summary: r.summary ?? null });
{
  const bots = {};
  for (const id of BOT_ORDER) {
    const d = BOTS[id];
    const env = new Environment(environmentConfig('pool'), new RNG(1));
    const v = new Vehicle(d, env);
    const defs = d.thrusters.map((t, i) => ({ pos: t.pos, axis: t.axis, fwdMax: v.th[i].curve.fwdMax, revMax: v.th[i].curve.revMax }));
    const bb = AL.buildB(defs);
    bots[id] = {
      thrusters: d.thrusters.map((t) => ({ name: t.name, pos: t.pos, axis: t.axis, preset: t.preset, hand: t.hand })),
      B: bb.B, nom: bb.nom, P: v.P, wmax: v.wmax, hands: v.th.map((t) => t.def.hand), volume: v.volume, Mt: v.Mt, It: v.It, voltage: v.voltage,
      analysis: dumpAnalysis(AL.analyzeLayout(defs, [0.01, 0, 0.02])),
      limited: dumpAnalysis(AL.analyzeLayout(defs, [0, 0, 0], { threshold: 0.2, enabledAxes: { roll: false, pitch: false } })),
    };
  }
  const frames = {};
  for (let fc = 0; fc <= 8; fc++) {
    const L = FR.frameLayout(fc);
    const tq = AL.tableQuality(L, FR.frameTable(fc), [0, 0, 0], L.map((_, i) => (i % 3 ? 1 : -1)));
    frames[fc] = { layout: L, table: FR.frameTable(fc), order: FR.frameTestOrder(fc), analysis: dumpAnalysis(AL.analyzeLayout(L)), W: tq.W, rows: tq.rows, ew: AL.effectiveWrench(L, FR.frameTable(fc)) };
  }
  save('alloc', { bots, frames, empty: dumpAnalysis(AL.analyzeLayout([])) });
}

// ------------------------------------------------------------------ vehicle step traces
{
  const traces = {};
  for (const sc of ['pool', 'pacific']) for (const id of BOT_ORDER) {
    const env = new Environment(environmentConfig(sc), new RNG(5));
    const v = new Vehicle(BOTS[id], env);
    v.reset([2, -1, 0.3], 0.4);
    v.depthTarget = 0.8;
    const rows = [];
    for (let i = 0; i < 900; i++) {
      const I = v.input;
      I.surge = I.sway = I.heave = I.yaw = I.roll = I.pitch = 0;
      if (i >= 150 && i < 300) { I.surge = 1; I.yaw = 0.5; }
      if (i >= 300 && i < 400) I.roll = 1;
      if (i >= 400 && i < 480) { I.pitch = -1; I.sway = 0.7; }
      if (i >= 480 && i < 560) I.heave = 1; // up
      if (i === 560) v.depthTarget = 0; // surface on depth hold (floats at the top)
      if (i === 800) v.depthHold = false;
      if (i >= 805 && i < 840) I.heave = -0.5; // down again
      env.step(DT);
      v.step(DT, !(i >= 860 && i < 875));
      if (i % 4 === 3) rows.push({ i, p: v.p.slice(), v: v.v.slice(), w: v.w.slice(), q: v.q.slice(), tq: v.targetQ.slice(), hdg: v.hdg, dt: v.depthTarget, dI: v.depthI, acro: v.acro, u: v.u.slice(), depth: v.depth, speed: v.speed(), diag: { ...v.diag, flow: v.diag.flow.slice() }, n: v.th.map((t) => t.n), Te: v.th.map((t) => t.Teff), cur: v.th.map((t) => t.current) });
    }
    traces[sc + '-' + id] = rows;
  }
  save('vehicle', traces);
}

// ------------------------------------------------------------------ course
const dumpCourse = (c) => ({
  floor: c.floor, random: c.random, dock: c.dock, gate: c.gate, paths: c.paths, slalom: c.slalom, bin: c.bin, board: c.board, octagon: c.octagon,
  obstacles: c.obstacles.map((o) => (o.type === 'board' ? { type: o.type, tag: o.tag } : o)), pingers: c.pingers, tasks: c.tasks,
});
{
  const courses = [];
  for (const seed of [1, 2, 3, 42, 1234, 99991]) for (const depth of [7, 12, 20]) {
    const r = new RNG(seed);
    const c = buildCourse(() => r.next(), depth);
    const probe = [[6.9, -4.1, c.board.depth + 0.1], [7.5, -3.6, 4], [0, 0, 0]].map((p) => boardLocal(c.board, p));
    courses.push({ seed, depth, course: dumpCourse(c), probe });
  }
  save('course', courses);
}

// ------------------------------------------------------------------ stats
{
  const st = {};
  for (const id of BOT_ORDER) st[id] = botStats(BOTS[id]);
  const sc = {};
  for (const id of SCENARIO_ORDER) sc[id] = { cfg: environmentConfig(id), cfg20: environmentConfig(id, 20), look: SCENARIOS[id].look };
  save('stats', { stats: st, scenarios: sc, setup: defaultSetup ? defaultSetup() : null, name: GAME_NAME ?? null });
}

// ------------------------------------------------------------------ whole matches (autopilot)
const playerState = (g, p) => ({
  p: p.veh.p.slice(), q: p.veh.q.slice(), v: p.veh.v.slice(), w: p.veh.w.slice(), dt: p.veh.depthTarget, hold: p.veh.depthHold, state: p.run.state, total: g.total(p), best: p.best, left: !!p.left,
  markers: p.run.markers, torpedoes: p.run.torpedoes, chosen: p.run.chosen, scores: { ...p.run.scores }, task: g.currentTask(p),
});
const runMatch = (name, opts, seconds, botsList) => {
  const g = new Game({ ...opts, players: botsList.map((bot, i) => ({ bot, name: i === 1 ? '' : `P${i + 1}` })) });
  const aps = g.players.map((_, i) => new Autopilot(g, i));
  const states = [];
  const n = Math.round(seconds / DT);
  for (let k = 1; k <= n && !g.over; k++) {
    aps.forEach((a) => a.update(DT));
    g.step(DT);
    if (k % 120 === 0) states.push({ t: g.time, over: g.over, idle: g.idle ?? 0, k: aps.map((a) => a.k), players: g.players.map((p) => playerState(g, p)), items: g.items.map((it) => ({ kind: it.kind, state: it.state, pos: it.pos.slice(), on: it.on ?? null, owner: it.owner ? it.owner.index : -1 })) });
  }
  return {
    name, seconds, opts: { ...opts, players: botsList }, time: g.time, over: g.over, steps: states,
    events: g.events.map((e) => ({ player: e.player, text: e.text, pts: e.pts, kind: e.kind, t: e.t })),
    final: g.players.map((p) => ({ name: p.name, color: p.color, bot: p.bot, best: p.best, run: { state: p.run.state, reason: p.run.reason, scores: p.run.scores, total: g.total(p), chosen: p.run.chosen, gateSide: p.run.gateSide, finished: p.run.finished }, runs: p.runs })),
  };
};
{
  const matches = [];
  const four = [['duburi', 'manta', 'kraken', 'barracuda'], ['dubomini', 'duburi', 'barracuda', 'manta']];
  for (const sc of SCENARIO_ORDER) for (const [si, seed] of [3, 11].entries()) {
    matches.push(runMatch(`${sc}-1p-${seed}`, { scenario: sc, seed, coinFlip: true }, 120, [BOT_ORDER[(si + SCENARIO_ORDER.indexOf(sc)) % 5]]));
    matches.push(runMatch(`${sc}-4p-${seed}`, { scenario: sc, seed, coinFlip: si === 0, depth: si ? 9 : 0, runMinutes: 1.5 }, 120, four[si]));
  }
  save('matches', matches);
  // long runs: the end-to-end cases of tests/game.test.mjs, to the end
  save('matches-long', [
    runMatch('pool-solo-duburi-3', { scenario: 'pool', seed: 3, coinFlip: true }, 600, ['duburi']),
    runMatch('pool-4p-5', { scenario: 'pool', seed: 5, coinFlip: true }, 900, ['duburi', 'manta', 'kraken', 'barracuda']),
    runMatch('pool-solo-dubomini-4', { scenario: 'pool', seed: 4, coinFlip: false }, 800, ['dubomini']),
  ]);
}

writeFileSync(join(out, 'manifest.json'), JSON.stringify({ files, created: new Date().toISOString() }));
console.log('golden: wrote', files.join(', '));
