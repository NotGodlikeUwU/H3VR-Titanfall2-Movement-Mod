# Titanfall 2 Movement for H3VR

A BepInEx plugin that adds Titanfall 2-inspired momentum movement to H3VR while retaining H3VR's native smooth-locomotion collision and held-object handling.

[VIDEO DEMONSTRATION](https://www.youtube.com/watch?v=LsGEUONcGFU)

## Features

- Right-stick click is jump; left-stick click toggles sprint; action-stick down is crouch/slide.
- Slide mechanic. Slide's momentum boosts if you combine it with wall running and jumping.
- Wall-Running mechanic.
- 25-degree wall-run camera roll
- Air Strafing
- Sounds for jumping, wall running, and sliding.
- BepInEx configuration for every important speed, force, duration, comfort, input, and audio value.

## Required H3VR setup

Use **Twin Stick** locomotion for the intended control layout. The plugin also supports Single Two Axis and Armswinger, but Twin Stick keeps movement and action input separate.

In Twin Stick mode the plugin automatically uses the non-movement/turn stick for actions:

- Right-stick click: jump, double jump, or wall jump.
- Left-stick click: toggle walking/sprinting.
- Stick down: crouch toggle; at sufficient speed, start a slide.

The crouch/slide hand can be forced with `CrouchActionHand = Left` or `CrouchActionHand = Right` in `BepInEx/config/com.notgodlike.h3vr.titanfallmovement.cfg`.

When Kodeman's Player Footsteps is installed, `PlayerFootstepsWallRun = true` enables wall-run steps and `WallRunFootstepDistance` controls their spacing. The integration is optional and Titanfall Movement continues to load without Player Footsteps.

Wall-run head tilt can be disabled independently with `Head Tilt Enabled = false` in the `[WallRunning]` config section. `Head Tilt Angle` and `Head Tilt Transition Speed` control its strength and transition rate. `RunSpeedDistance` and `MaximumDistance` tune the wall-run distance curve.

## Credits

Sounds ripped from Call of Duty Modern Warfare 2019 and Titanfall 2. Made by NotGodlike with heavy use of ChatGPT-6.
