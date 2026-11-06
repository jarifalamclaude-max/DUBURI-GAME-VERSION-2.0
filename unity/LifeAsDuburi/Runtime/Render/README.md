# Runtime/Render — `src/render/*` and the rendering parts of `src/main.js`

Namespace `Duburi.Game` (Assembly-CSharp). URP 17 (Unity 6.3), procedural meshes, no custom
shaders: every material is a clone of a template in `Resources/Duburi/Materials` (`Mats`), post
effects come from `Resources/Duburi/PostProfile` (both made by **Life as Duburi → Set Up Project**).
`tools/check.sh Render` compiles this folder alone.

## Conventions (read first)

* **Three space under a mirrored root.** All meshes live under one `World` root with
  `localScale (1, 1, -1)` (`Space.CreateWorld("World")`) and use the JS three.js positions and
  rotations literally: `three = (east, up, south)`, water surface at Y = 0, arena floor at
  Y = -depth. NED → three: `Space.Ned(p)` (`course3d.js ned`), NED quaternion → three:
  `Space.NedQuat(q)` (`main.js nedQuat`), body FRD → three model coords: `Space.Body(b)`.
  Set child poses with `transform.localPosition / localRotation` in three space; never
  `Transform.LookAt` under the root (use `Space.LookAt`).
* **Unity world** (not mirrored): cameras, lights, particle systems, volumes.
  `Space.ToUnity(three) = (x, y, -z)`, `Space.RotToUnity(q3)` (also right for cameras),
  `Space.DirToUnity(dir)`. `ViewCamera` and `Bubbles` take three-space values and convert.
* **Layers.** 24..27 = the post volume of view 0..3 (`Post.FirstLayer + view`), 29 = bot
  thumbnails (`BotThumbs.Layer`; view cameras do not see it, the scenery sun does not light it).
* **Per-camera state.** Unity renders all cameras after your scripts, so the JS's
  "change the scene, render this viewport" becomes: call `Views.DrawView` for each view every
  frame; it records each camera's needs, and `RenderHooks` switches fog / water look / god
  rays / bubble draw order / hidden renderers when URP starts that camera.
* **Units / colours.** Metres, seconds. Colours given to `Mat3`, `Bubbles`, `Post` are linear
  (three keeps colours linear); `Colors.Rgb(hex).linear`.
* No AudioListener anywhere in this module (Sound owns the only one).

## Files

| file | JS | what |
|---|---|---|
| `Scenery.cs` | `scenery.js` | water, floor, walls / seabed, life, particles, god rays, buoys, banners; caustics as an animated sun cookie (`CausticCookie`); `WaveState`, `ClearZone` |
| `Course3D.cs` | `course3d.js` | gate, slalom, bin, board, octagon, pingers, dock |
| `Bots3D.cs`, `DuburiModel.cs` | `bots3d.js` | `Bots3D.CreateBotMesh` → `BotMesh`, `CreateItemMesh`, `DisposeObject`, `MergeStatic`, `BotState`; the DUBURI model built from the team's CAD (octagonal hull, brass hatch and porthole, red latches, bracket wings, 8 × T200, powder-coated frame, torpedo launcher) |
| `Art.cs` | `art.js` | handbook prints, logo, HUD icons, `DrawAnimal` |
| `Graphics3D.cs` | `graphics.js` | `MakeEnvironment` (reflection probe + diffuse ambient), `SetupShadows`, `CastShadows`, `Ambient`, `EnvLight`, `LiveReflection` (Ultra) |
| `GraphicsQuality.cs` | `graphics.js` QUALITY + `loadQuality/saveQuality` | levels, PlayerPrefs `life-as-duburi-quality`, URP asset settings (render scale, MSAA, shadows) |
| `Bubbles.cs` | `bubbles.js` | ring-buffer bubble pool, one world-space ParticleSystem |
| `Post.cs` | `post.js` | per-view Volume (Bloom, ACES, ColorAdjustments, Vignette) + under-water grade |
| `Views.cs` | `main.js` | `SceneExtras` (sceneExtras), `ContactShadow`, `ViewCamera`, `Views.DrawView`, `Views.PlaceItem`, `Views.HideFor` |
| `BotThumbs.cs` | `main.js` thumb / renderThumb | cached 240×150 bot pictures (contract) |
| `RenderHooks.cs` | — | per-camera switches at `RenderPipelineManager.begin/endCameraRendering` |
| `Space.cs` | — | coordinate conversions, three.js rotation helpers |
| `Node.cs`, `MeshBuilder.cs` (`Geo`, `Shape2`, `Earcut`) (+ `Geo.Prism` / `Geo.Tube` / `Geo.Faceted`), `Mats.cs` (`Mat3`, `Mats`, `Palette`, `Owned`), `Canvas2D.cs` (`Canvas2D`, `StrokeFont`) | three.js | scene graph, geometry generators, materials, 2D canvas for textures |

## Setting up (App)

```csharp
using Duburi.Core;
using CoreGame = Duburi.Core.Game;
using Env = Duburi.Core.Environment;

GraphicsQuality.Apply();                        // constructor: applyQuality() (render scale, MSAA, shadows)
post = new Post();                              // createPost: follows GraphicsQuality.Set by itself
world = Space.CreateWorld("World");             // the mirrored root, once
var logo = Art.Logo;                            // this.logoTexture

// ---- menuScene()
var sc = Scenarios.SCENARIOS["pacific"];
var arena = new Arena { length = 50, width = 25, depth = 12 };
scenery = Scenery.Create(world, sc, logo, arena, new ClearZone[0]);
extras = SceneExtras.Create(scenery, sc.look, arena);
menuCam = new ViewCamera("menu camera", 60, 0.05f, 400, 0);
foreach (var id in new[] { "duburi", "manta", "dubomini" }) {
    var m = Bots3D.CreateBotMesh(Bots.BOTS[id], Bots.BOTS[id].color, logo, world);
    Graphics3D.CastShadows(m.group);
}

// ---- start(cfg)
var c = game.course;
var clear = new List<ClearZone> { new ClearZone((float)c.dock.pos.y, (float)-c.dock.pos.x, 8), /* gate 5, slalom 5, bin 4, board 4, octagon 5 */ };
scenery = Scenery.Create(world, Scenarios.Get(cfg.scenario), logo, game.arena, clear);
course3d = Course3D.Create(c, logo, world);
Graphics3D.CastShadows(course3d.group);
extras = SceneExtras.Create(scenery, Scenarios.Get(cfg.scenario).look, game.arena);
foreach (var p in game.players) {
    var m = Bots3D.CreateBotMesh(p.design, p.color, logo, world); Graphics3D.CastShadows(m.group);
    var shadow = new ContactShadow(world, p.design);
}
views: new ViewCamera("view 1", 66, 0.03f, 300, viewIndex)   // spectator: (55, 0.05f, 300)
layoutViews: view.SetRect(x, y, w, h)                         // pixels, top-left origin like the JS rects
```

Per frame (`gameFrame`, after the physics steps):

```csharp
// syncMeshes
bm.SetPose(new Vector3((float)y, (float)-z, (float)-x), Quaternion.Slerp(Space.NedQuat(prevQ), Space.NedQuat(v.q), alpha));
bm.Update(dt, v, state);                       // b.m.update(dt, v, b.state)
shadow.Visible = !p.left; shadow.Update(x, y, z, game.course.floor);
item: go = Bots3D.CreateItemMesh(it.kind, it.owner != null ? it.owner.color : "#ffffff", world);
      Graphics3D.CastShadows(go); Views.PlaceItem(go, it);   // gone: Bots3D.DisposeObject(go)
// effects: extras.Bubbles.Emit(x, y, z, vx, vy, vz, count, size)   (three space, like the JS)
extras.Bubbles.Update(dt, (float)env.time, waveY);   // waveY = cached delegate (x, z) => (float)env.SurfaceElevation(-z, x)
course3d.Update(game.time);
scenery.Update((float)env.time, env);
// renderView: pose the camera in three space, then
view.Position = ...; view.LookAt(target);       // or view.SetPose(pos, quat); view.Fov = 66 / 78 / 72
Views.HideFor(view, onboard ? ownBotMesh : null);
Views.DrawView(view, scenery, extras, post, game.env);
```

Menu frame: `scenery.Update(t, new WaveState { A = 0.1f, K = 0.2f, W = 1.4f, T = t, D = new Vector2(0.5f, 0.86f) })`
(the JS `fakeEnv`), `bm.Update(dt, nList, BotState)` with rotor speeds `n = 0.6`,
`extras.Bubbles.Update(dt, t)` (flat surface), `Views.DrawView(menuCam, scenery, extras, post, null)`.

Dispose a scene (`disposeScene`): `BotMesh.Dispose()`, `ContactShadow.Dispose()`,
`Bots3D.DisposeObject(item)`, `course3d.Dispose()`, `extras.Dispose()`, `scenery.Dispose()`.
Cameras: `ViewCamera.Dispose()`; `Enabled = false` for views that are not drawn.

## Public API

### `Scenery` (scenery.js `createScenery`)
| member | |
|---|---|
| `static Scenery Create(Transform world, Scenario scenario, Texture2D logoTexture, Arena arena, IList<ClearZone> clearZones)` | builds under the World root; sets the ambient hemisphere, fog, the sun (a Unity-world directional light) |
| `GameObject root`, `Light Sun`, `ScenarioLook look`, `WaveState wave`, `Vector3 SunDir`, `float Caustic`, `bool IsPool` | |
| `void Update(float time, Env env)`, `Update(float time, WaveState w)` | `update(time, env)`: waves, kelp, fish, jellies, rays, snow, caustics |
| `void ApplyForCamera(Camera cam)` | `applyForCamera(camera, renderer)` (under / above: fog, background, rays, water look) |
| `float WaveY(float x, float z)`, `Color FogColor(bool under)` | surface height at three (x, z); fog colour (sRGB) |
| `void SetSunDirection(Vector3 threeDir)`, `void Dispose()` | |
| `struct WaveState {A, K, W, T, D; Eta(x, z)}`, `struct ClearZone(x, z, r)` | wave uniforms; keep-out circles (three x, z) |

### `Course3D` (course3d.js `createCourse`)
`static Course3D Create(Course course, Texture2D logoTexture, Transform parent)`, `GameObject group`,
`void Update(double time)` (pinger LEDs), `void Dispose()`, `static Texture2D TextSprite(...)`.

### `Bots3D`, `BotMesh`, `BotState` (bots3d.js)
| member | |
|---|---|
| `Bots3D.CreateBotMesh(BotDesign d, string color, Texture2D logo, Transform parent, int layer = -1)` (+ `Color` overload) → `BotMesh` | `createBotMesh` |
| `BotMesh.group`, `props`, `gripper`, `gripPoint`, `fingers`, `markers`, `torpedoes`, `Renderers` | `group`, `parts` |
| `BotMesh.SetPose(Vector3 threePos, Quaternion threeQuat)`, `Visible` | `group.position / quaternion / visible` |
| `BotMesh.Update(float dt, Vehicle veh, BotState state)`, `Update(float dt, IList<double> n, BotState state)` | `update(dt, veh, state)` |
| `BotMesh.Dispose()` | `disposeObject(group)` |
| `Bots3D.CreateItemMesh(string kind, string color, Transform parent, int layer = -1)` → `GameObject` | `createItemMesh` (shared meshes / materials) |
| `Bots3D.DisposeObject(GameObject)`, `Bots3D.ItemNode(kind, color)`, `Bots3D.MergeStatic(Node, IList<Node>)` | `disposeObject`, `mergeStatic` |
| `BotState { gripClosed, holding, markers, torpedoes }` | state |

### `Graphics3D`, `Ambient`, `EnvLight` (graphics.js)
`Graphics3D.MakeEnvironment(ScenarioLook look, float intensity, Vector3 unityCenter, float size)` → `EnvLight`
(`cube`, `go` = ReflectionProbe, `up/side/down` diffuse, `Dispose()`);
`Graphics3D.SetupShadows(Scenery, int size, Arena, bool hq = false)` → sun or null (hq = 7×7 soft shadows);
`Graphics3D.MakeLiveReflection(look, arena, pool)` → `LiveReflection` (Ultra: real-time box-projected probe; `Create`, `Tick()` once per frame, `Ready`, `Dispose()`, `go`, `probe`); `Graphics3D.CastShadows(GameObject, cast = true, receive = true)`;
`Ambient.SetHemisphere(sky, ground, intensity)`, `SetEnvironment(EnvLight)`, `Apply()`, `Compute(...)`, `Set(...)`, `Environment`.

### `GraphicsQuality` (graphics.js QUALITY; Render contract)
`Order` (`{"ultra","high","medium","low"}`), `Current`, `Spec` (`QualitySpec {label, post, shadows, pixelRatio, bubbles, cascades, msaa, supersample, ssao, reflections, hqShadows, anisotropic, hiResTextures}`; `post` may be `"ultra"`),
`QUALITY`, `Label(id)`, `Set(id)` (save + apply + `Changed`), `Next()`, `Load()`, `Save(id)`, `Apply()`,
`event Action<string> Changed`, `Asset()` (a private copy of the URP asset), `RenderScale`, `DevicePixelRatio()`, `ShadowDistance`,
`Refresh()` (window resizes; `Views.DrawView` calls it), `RenderScaleFor(spec, w, h)`, `UseRealtimeReflectionProbes()`, `MaxInternalWidth` / `MaxInternalHeight`, `Restore()`.
MSAA: 4× on Ultra, High and Low, none on Medium; Ultra supersamples up to 2× (internal ≤ 3840×2160).

### `Art` (art.js; Render contract)
`Logo`, `Image(key)` (`imageTexture`), `AnimalTexture(animal, where = "gate")`, `AnimalIcon(animal)`,
`DrawAnimal(Canvas2D g, animal, cx, cy, size, flip)`.

### `BotThumbs` (Render contract)
`static Texture Get(string botId, Color color)` — 240×150 `RenderTexture`, rendered synchronously on the
first call per bot + colour (own camera + light on layer 29, pool environment, transparent background),
re-rendered if the GPU lost it. `Clear()` releases everything. `Layer = 29`, `Width`, `Height`.
Do not call it from inside a camera render callback.

### `Bubbles` (bubbles.js `createBubbles`)
| member | JS |
|---|---|
| `new Bubbles(int max = 2000)` | `createBubbles(scene, { max })` |
| `void Emit(x, y, z, vx = 0, vy = 0, vz = 0, float count = 1, float size = 0.01f)` | `emit` (three space; fractional counts are stochastic) |
| `void EmitWorld(Vector3 unityPos, Vector3 unityVel, float count, float size)` | — (Unity world) |
| `void Update(float dt, float time = 0, Func<float, float, float> waveY = null)` | `update(dt, time, (x, z) => surfaceY)` |
| `void SetViewportHeight(float px)` | `setViewportHeight` |
| `void ApplyForCamera(Camera cam, float viewportPx = 0)` | the per-draw hooks (draw order vs the water, fog tint, size clamp, sub-pixel fade) — `Views.DrawView` calls it |
| `void Clear()`, `int Count`, `ParticleSystem Points`, `void Dispose()` | `clear`, `count`, `points`, `dispose` |

### `Post` (post.js `createPost`)
| member | |
|---|---|
| `new Post()` / `new Post(string quality, bool followQuality = false)` | `"ultra"`, `"high"`, `"low"`, `null` (= no post: ACES + exposure only) |
| `void Apply(Camera cam, int view, bool underwater, float depth, Color waterColorLinear, float turbidity)` | `post.render(...)` for one view (`Views.DrawView` calls it) |
| `void SetQuality(string)`, `string Quality`, `Settings settings {bloom, threshold, vignette, grain, absorption, dof}` (`dof`: Ultra's under-water distance-blur multiplier, 0 = off) | `setQuality`, `quality`, `settings` |
| `VolumeProfile Profile(int view)`, `static Color Absorption(depth, turb, water, k)`, `static float VignetteIntensity(v)` | |
| `void Dispose()` | `dispose` |
| `FirstLayer = 24`, `MaxViews = 4`, `ProfilePath = "Duburi/PostProfile"`, `UltraProfilePath = "Duburi/PostProfileUltra"` | |

### `Views.cs`
| member | |
|---|---|
| `SceneExtras.Create(Scenery, ScenarioLook, Arena)` → `Env`, `Sun`, `Bubbles`, `Reflection` (Ultra only), `Look`, `WaterColor` (linear), `Turbidity`; `ApplyQuality()` (also automatic on `GraphicsQuality.Changed`), `Tick()`, `Dispose()` | `sceneExtras` |
| `new ContactShadow(Transform world, BotDesign)`, `Update(x, y, z, floor)` (NED, m), `Visible`, `GameObject`, `Dispose()` | the per-bot `shadow` mesh |
| `new ViewCamera(string name, float fov, float near, float far, int view = 0)`, `camera`, `data`, `view` | `new THREE.PerspectiveCamera(fov, 1, near, far)` |
| `ViewCamera.SetRect(x, y, w, h, screenHeight = 0)`, `Rect` | `v.rect = rect; cam.aspect = w / h` (top-left origin, pixels) |
| `ViewCamera.Position`, `Quaternion` (three space), `SetPose(p, q)`, `LookAt(target[, up])`, `Fov`, `Enabled`, `Dispose()` | `cam.position`, `cam.quaternion`, `cam.lookAt`, `cam.fov` |
| `Views.DrawView(ViewCamera, Scenery, SceneExtras, Post, Env env)` (also `GraphicsQuality.Refresh()` and `extras.Tick()`) | `drawView(cam, rect, time)` |
| `Views.HideFor(ViewCamera, BotMesh own)` | `own.visible = false` around the onboard view's render |
| `Views.PlaceItem(GameObject itemMesh, Item it)` | syncMeshes' item pose |
| `Views.NedQuat(Quat)` | `nedQuat` |

### `RenderHooks`
`CamState State(Camera)`, `TryGetState`, `Forget(Camera)`, `Forget(Scenery)`,
`event Action<Camera, CamState> BeginCamera`, `CamState {under, depth, threePos, fov, pixelHeight, scenery, hide}`.

## JS call in `src/main.js` → C# call

| JS (`src/main.js`) | C# |
|---|---|
| `loadQuality()`, `QUALITY[q]` | `GraphicsQuality.Current`, `GraphicsQuality.Spec` / `QUALITY[id]` |
| `applyQuality()` (pixel ratio, post) | `GraphicsQuality.Apply()`; `post.SetQuality(GraphicsQuality.Spec.post)` (automatic with `new Post()`) |
| `saveQuality(q)` + `applyQuality()` + `setupShadows(...)` (pause menu) | `GraphicsQuality.Set(id)` (Post and SceneExtras follow `Changed`) |
| `QUALITY_ORDER[(i + 1) % 3]`, `QUALITY[q].label` | `GraphicsQuality.Next()`, `GraphicsQuality.Label(id)` |
| `new THREE.Scene()` | `Space.CreateWorld("World")` (once; everything below goes under it) |
| `createScenery(scene, sc, { logoTexture, arena, clearZones })` | `Scenery.Create(world, sc, Art.Logo, arena, clearZones)` |
| `{ x: p[1], z: -p[0], r }` | `new ClearZone((float)p.y, (float)-p.x, r)` |
| `createCourse(c, { logoTexture })` | `Course3D.Create(c, Art.Logo, world)` |
| `castShadows(obj)` | `Graphics3D.CastShadows(go)` |
| `sceneExtras(scene, look, arena)` | `extras = SceneExtras.Create(scenery, look, arena)` |
| `makeEnvironment(renderer, look)`; `scene.environment = …; environmentIntensity = 0.55` | inside `SceneExtras.Create` (`Graphics3D.MakeEnvironment` + `Ambient.SetEnvironment`) |
| `setupShadows(renderer, scene, root, q.shadows, arena)` | `Graphics3D.SetupShadows(scenery, GraphicsQuality.Spec.shadows, arena)` / `extras.ApplyQuality()` |
| `createBubbles(scene, { max: q.bubbles })` | `extras.Bubbles` (`new Bubbles(GraphicsQuality.Spec.bubbles)`) |
| `this.waterColor`, `this.turbidity` | `extras.WaterColor`, `extras.Turbidity` |
| `createPost(renderer, { quality })` | `new Post()` |
| `createBotMesh(design, color, { logoTexture })` | `Bots3D.CreateBotMesh(design, color, Art.Logo, world)` |
| `new THREE.Mesh(new THREE.CircleGeometry(max(dims) * 0.6, 24), …)` (contact shadow) | `new ContactShadow(world, design)` |
| `b.shadow.position.set(y, fl, -x); …opacity = max(0, 0.3 - hgt * 0.05)` | `shadow.Update(x, y, z, game.course.floor)` |
| `b.shadow.visible = !p.left` | `shadow.Visible = !p.left` |
| `b.m.group.visible = !p.left` | `bm.Visible = !p.left` |
| `b.m.group.position.set(y, -z, -x); quaternion = slerp(nedQuat(prevQ), nedQuat(q), alpha)` | `bm.SetPose(new Vector3(y, -z, -x), Quaternion.Slerp(Space.NedQuat(prevQ), Space.NedQuat(v.q), alpha))` |
| `b.m.update(dt, v, b.state)` | `bm.Update(dt, v, state)` |
| `b.m.update(dt, { th: b.th }, state)` (menu, `n: 0.6`) | `bm.Update(dt, nList, state)` |
| `b.m.group.position.set(…); rotation.set(rx, ry, 0)` (menu bots) | `bm.SetPose(pos, Space.Euler(rx, ry, 0))` |
| `createItemMesh(kind, color)` + `castShadows(m)` + `scene.add(m)` | `Bots3D.CreateItemMesh(kind, color, world)` + `Graphics3D.CastShadows(go)` |
| `m.position.copy(ned(...)); m.lookAt(...) / nedQuat(holder.q)` | `Views.PlaceItem(go, it)` |
| `this.scene.remove(m)` (item gone) | `Bots3D.DisposeObject(go)` |
| `ned(x, y, z)` | `Space.Ned(x, y, z)` |
| `tmpV.set(d.pos[1], -d.pos[2], -d.pos[0]).applyQuaternion(q).add(p)` | `q3 * Space.Body(d.pos) + p3` |
| `B.emit(x, y, z, vx, vy, vz, n, size)` | `extras.Bubbles.Emit(x, y, z, vx, vy, vz, n, size)` |
| `B.update(dt, env.time, (x, z) => env.surfaceElevation(-z, x))` | `extras.Bubbles.Update(dt, (float)env.time, waveY)` (cache the delegate) |
| `this.bubbles.update(dt, t)` (menu) | `extras.Bubbles.Update(dt, t)` |
| `this.course3d.update(g.time)` | `course3d.Update(game.time)` |
| `this.scenery.update(g.env.time, g.env)` | `scenery.Update((float)env.time, env)` |
| `this.scenery.update(t, this.fakeEnv)` | `scenery.Update(t, new WaveState { A = 0.1f, K = 0.2f, W = 1.4f, T = t, D = new Vector2(0.5f, 0.86f) })` |
| `renderer.shadowMap.needsUpdate = true` | — (URP renders shadows every frame) |
| `new THREE.PerspectiveCamera(66, 1, 0.03, 300)` / `(55, 1, 0.05, 300)` / `(60, 1, 0.05, 400)` | `new ViewCamera(name, 66, 0.03f, 300, view)` / `(…, 55, 0.05f, 300, 3)` / `(…, 60, 0.05f, 400, 0)` |
| `v.rect = rects[i]; v.cam.aspect = …; updateProjectionMatrix()` | `view.SetRect(r.x, r.y, r.w, r.h)` |
| `cam.position.copy(p)`, `cam.fov = 66` | `view.Position = p`, `view.Fov = 66` |
| `cam.up.set(0, 1, 0); cam.lookAt(t)` | `view.LookAt(t)` |
| `cam.quaternion.copy(grp.quaternion)`; `.multiply(axisAngle((1,0,0), -PI/2))` | `view.Quaternion = q3` / `q3 * Space.AxisAngle(Vector3.right, -Mathf.PI / 2)` |
| `this.menuCam.position.set(…); lookAt(0, -1.6, -2)` | `menuCam.Position = …; menuCam.LookAt(new Vector3(0, -1.6f, -2))` |
| `own.visible = false; drawView(…); own.visible = …` | `Views.HideFor(view, onboard ? ownBotMesh : null)` |
| `drawView(cam, rect, time)` | `Views.DrawView(view, scenery, extras, post, game?.env)` |
| `this.scenery.applyForCamera(cam, r)` | `scenery.ApplyForCamera(view.camera)` (inside `DrawView`) |
| `this.bubbles.setViewportHeight(rect.h * pixelRatio)` | `extras.Bubbles.SetViewportHeight(h)` + `ApplyForCamera` (inside `DrawView`) |
| `this.post.render(scene, cam, rect, { underwater, depth, time, waterColor, turbidity })` | `post.Apply(view.camera, view.view, under, depth, extras.WaterColor, extras.Turbidity)` (inside `DrawView`) |
| `r.render(scene, cam)` (no post) | the same `post.Apply` (quality `null`: ACES + exposure only) |
| `cam.position.y < env.surfaceElevation(-cam.z, cam.x)` (audio) | `view.Position.y < env.SurfaceElevation(-view.Position.z, view.Position.x)` |
| `this.thumb(bot, color)` (Promise of a PNG URL) | `BotThumbs.Get(bot, color)` (a `Texture`, at once) |
| `disposeObject(m.group)` | `bm.Dispose()` |
| `this.scenery.dispose()`, `this.bubbles.dispose()`, `envTex.dispose()` | `scenery.Dispose()`, `extras.Dispose()` |

## Differences from the browser (no custom shaders)

* Post: no refraction wobble, no chromatic aberration; the grain is the camera's dithering; the
  vignette is URP's (fitted to the same corner darkening). Bloom / grade map onto the URP overrides
  as described at the top of `Post.cs`.
* Scenery: the wave / sway / fish / jelly / snow / ray motions run on the CPU; the floor caustics
  are an animated cookie on the sun; the water's look from below / above is a camera-centred texture.
* Bubbles: billboards with a generated texture; the highlight takes the bubble tint; distant
  sub-pixel bubbles fade using the nearest view camera of the previous frame.
* Thumbnails: no ACES tone mapping (it would cost the transparent background in URP).
