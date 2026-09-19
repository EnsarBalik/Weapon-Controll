# Weapon Control

Unity 6 FPS weapon showcase: camera, movement, hitscan fire, recoil, inventory, HUD, and a fully procedural magazine-swap reload. Feel targets are games like **Wardogs**, Battlefield, and Escape from Tarkov — no canned animation clips on the gun itself.

Open `Assets/Scenes/SampleScene.unity` and press Play.

<p align="center">
  <img src="docs/screenshots/general-look.png" alt="Hip-fire view with AKM, dummies, and HUD" />
</p>
<p align="center"><em>Hip fire — AKM in the Sample Scene with dummy targets and ammo HUD.</em></p>

<p align="center">
  <img src="docs/screenshots/fire.png" alt="Muzzle flash, smoke, and dynamic crosshair" />
</p>
<p align="center"><em>Firing — muzzle flash, smoke, tracer, and a spread-aware crosshair.</em></p>

<p align="center">
  <img src="docs/screenshots/inspect.png" alt="Inspect pose on the AKM" />
</p>
<p align="center"><em>Inspect — procedural pose so you can read the weapon up close.</em></p>

## What it does

- **FPS camera** with yaw/pitch split, ADS FOV, lean, and shot shake
- **CharacterController** walk / sprint / crouch / jump (scale-aware)
- **Procedural gun motion** — sway, bob, ADS, sprint pose, inspect, wall-aware retraction, idle breath
- **Hitscan fire** — semi / burst / auto, hip vs ADS spread, recoil, muzzle flash, tracers
- **Ammo** — magazine + reserve, tactical vs empty reload, dry fire
- **Procedural reload** — raise the gun, support hand pulls the mag, spare mag seats, empty reload racks the charging handle; ejected mag can drop with physics
- **Inventory** — holster / draw between AKM, M4, and PT-9M, with per-weapon hand grips
- **HUD** — ammo, fire mode, weapon name, hit marker, dynamic crosshair

Weapons are data-driven (`WeaponData` ScriptableObjects). Hands are a shared pair that blend toward each weapon’s grip targets.

## Requirements

- **Unity 6.3** (`6000.3.15f1`)
- Built-in Render Pipeline
- Input System (`com.unity.inputsystem`)

## Controls

| Action | Keyboard / Mouse | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Look | Mouse | Right stick |
| Fire | LMB | West / trigger |
| Aim | RMB | LT |
| Reload | R | X |
| Fire mode | V | D-pad up |
| Inspect | T | — |
| Sprint | Left Shift | Left stick press |
| Crouch | C | East |
| Jump | Space | South |
| Switch weapon | 1 / 2 | D-pad left / right |
| Lean | Q / E | LB / RB |

## Architecture

Everything lives under `Assets/_Project/Scripts`. One component, one job — they talk through events and small hooks instead of a god class.

```
Player
 └─ Camera (CameraController, PlayerLean)
     └─ WeaponHolder  (WeaponProceduralMotion, WeaponInventory, WeaponReloadAnimator)
         ├─ RecoilPivot
         │   ├─ Hands          (HandPoseDriver — shared trigger + support hands)
         │   └─ active weapon  (WeaponFire, WeaponAmmo, WeaponAudio, WeaponReloadRig, …)
         └─ holstered weapons
```

| Component | Role |
| --- | --- |
| `WeaponData` | Damage, RPM, spread, ammo, VFX, audio |
| `WeaponFire` | Hitscan, fire modes, muzzle flash, `IDamageable` |
| `WeaponAmmo` | Mag / reserve, tactical vs empty timing |
| `WeaponReloadAnimator` | Timed mag-out → mag-in → optional rack |
| `WeaponReloadRig` | Per-gun mag, spare mag, charging handle |
| `HandPoseDriver` | Blends shared hands to the active grip (or reload override) |
| `WeaponInventory` | Holster / draw |
| `WeaponHud` | Builds its own UI at runtime |

Reload timing is normalized to `0..1`, so a 2.1s tactical reload and a 2.8s empty reload play the same beats:

```
0.00  raise
0.18  eject old mag
0.40  mag drops (optional Rigidbody clone)
0.50  spare mag appears in the support hand
0.60  insert
0.85  seated + tap shake  (tactical ends around here)
0.90  charging handle back   (empty only)
0.96  slam forward + rack shake
1.00  gun lowers, hands return to the grip
```

## Adding a weapon

1. Create data: **Assets → Create → WeaponControl → Weapon Data** (see `AKM_Data`, `M4_Data`, `PT-9M_Data`).
2. Put the model under `WeaponHolder`. Add `WeaponReferences`, `WeaponFire`, `WeaponAmmo`, `WeaponAudio`, `WeaponHandPose`.
3. Assign muzzle / aim / grip points on `WeaponReferences`.
4. For a unique grip, add empty targets on the gun and point `WeaponHandPose` at them (the pistol does this; rifles fall back to the captured default).
5. Register the GameObject on `WeaponInventory`.
6. For the magazine animation, add `WeaponReloadRig`: in-well mag, a hidden spare mag duplicate, optional eject/fetch waypoints, optional charging handle + `chargeTravel`.

```csharp
[CreateAssetMenu(fileName = "WeaponData", menuName = "WeaponControl/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public string weaponName = "AKM";
    public float damage = 34f;
    public float fireRate = 600f;          // RPM
    public FireMode[] availableModes = { FireMode.Auto, FireMode.Semi };
    public float hipSpread = 2.5f;
    public float aimSpread = 0.35f;
    public int magazineSize = 30;
    public float tacticalReloadTime = 2.1f;
    public float emptyReloadTime = 2.8f;
    public GameObject muzzleFlashPrefab;
    public AudioClip fireClip, magOutClip, magInClip, rackClip;
}
```

Shootables only need `IDamageable`. `Health` is the included implementation (`TargetDummy` uses it):

```csharp
public struct DamageInfo
{
    public float Amount;
    public Vector3 Point, Normal, Direction;
    public GameObject Source;
}

public interface IDamageable
{
    void TakeDamage(in DamageInfo info);
}
```

`WeaponFire` stays ammo-agnostic — `WeaponAmmo` plugs in on enable:

```csharp
fire.CanFire = () => !IsReloading && MagazineCount > 0;
fire.AmmoConsumed = () => MagazineCount--;
```

## Project layout

```
Assets/_Project/
  Scripts/          camera, movement, weapons, combat, HUD
  ScriptableObjects AKM / M4 / PT-9M data
Scenes/SampleScene.unity
```

Art packs in the repo: BigRookGames M4, stylized AKM, NZ PT-9M, SimpleHands, Blink dummy targets.

## License

Project code is yours to use in this repo. Third-party meshes, textures, and audio stay under their original Asset Store / pack licenses — do not redistribute those packs on their own.
