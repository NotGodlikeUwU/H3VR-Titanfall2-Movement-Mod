using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace NotGodlike.TitanfallMovement
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("h3vr.exe")]
    [BepInDependency("h3vr.kodeman.playerfootsteps", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class TitanfallMovementPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.notgodlike.h3vr.titanfallmovement";
        public const string PluginName = "Titanfall Movement";
        public const string PluginVersion = "1.8.7";

        internal static TitanfallMovementPlugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }

        internal ConfigEntry<bool> Enabled { get; private set; }
        internal ConfigEntry<float> WalkSpeed { get; private set; }
        internal ConfigEntry<float> RunSpeed { get; private set; }
        internal ConfigEntry<float> GroundAcceleration { get; private set; }
        internal ConfigEntry<float> GroundFriction { get; private set; }
        internal ConfigEntry<float> OverspeedDrag { get; private set; }
        internal ConfigEntry<float> SprintMomentumAcceleration { get; private set; }
        internal ConfigEntry<float> AirAcceleration { get; private set; }
        internal ConfigEntry<float> MaxMomentumSpeed { get; private set; }
        internal ConfigEntry<float> JumpSpeed { get; private set; }
        internal ConfigEntry<float> DoubleJumpSpeed { get; private set; }
        internal ConfigEntry<float> DoubleJumpMomentumBoost { get; private set; }
        internal ConfigEntry<float> Gravity { get; private set; }
        internal ConfigEntry<float> SlideMinimumSpeed { get; private set; }
        internal ConfigEntry<float> SlideBoost { get; private set; }
        internal ConfigEntry<float> SlideFriction { get; private set; }
        internal ConfigEntry<float> SlideMinimumDuration { get; private set; }
        internal ConfigEntry<float> SlideMaximumDuration { get; private set; }
        internal ConfigEntry<float> SlideCooldown { get; private set; }
        internal ConfigEntry<float> SlideRepeatPenaltyWindow { get; private set; }
        internal ConfigEntry<float> SlideRepeatSpeedMultiplier { get; private set; }
        internal ConfigEntry<float> CrouchSpeedMultiplier { get; private set; }
        internal ConfigEntry<float> CrouchHeight { get; private set; }
        internal ConfigEntry<float> CrouchTransitionSpeed { get; private set; }
        internal ConfigEntry<float> WallRunProbeDistance { get; private set; }
        internal ConfigEntry<float> WallRunMinimumSpeed { get; private set; }
        internal ConfigEntry<float> WallRunMinimumDuration { get; private set; }
        internal ConfigEntry<float> WallRunMaximumDuration { get; private set; }
        internal ConfigEntry<float> WallRunRunSpeedDistance { get; private set; }
        internal ConfigEntry<float> WallRunMaximumDistance { get; private set; }
        internal ConfigEntry<float> WallRunVerticalSpeed { get; private set; }
        internal ConfigEntry<float> WallRunAcceleration { get; private set; }
        internal ConfigEntry<float> WallJumpUpSpeed { get; private set; }
        internal ConfigEntry<float> WallJumpPushSpeed { get; private set; }
        internal ConfigEntry<string> ActionHand { get; private set; }
        internal ConfigEntry<float> SoundVolume { get; private set; }
        internal ConfigEntry<bool> WallRunHeadTilt { get; private set; }
        internal ConfigEntry<float> WallRunCameraTiltAngle { get; private set; }
        internal ConfigEntry<float> WallRunCameraTiltSpeed { get; private set; }
        internal ConfigEntry<bool> PlayerFootstepsWallRun { get; private set; }
        internal ConfigEntry<float> WallRunFootstepDistance { get; private set; }

        private Harmony _harmony;
        private TitanfallMovementController _controller;
        private MovementAudio _audio;
        private PlayerFootstepsBridge _playerFootsteps;
        private WallRunCameraTilt _cameraTilt;
        private bool _markedRunningModded;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            BindConfiguration();

            _audio = gameObject.AddComponent<MovementAudio>();
            _audio.Initialize(this);
            _playerFootsteps = new PlayerFootstepsBridge();
            _controller = new TitanfallMovementController(this, _audio, _playerFootsteps);
            _cameraTilt = gameObject.AddComponent<WallRunCameraTilt>();
            _cameraTilt.Initialize(this, _controller);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(TitanfallMovementPlugin).Assembly);

            Logger.LogMessage(PluginName + " " + PluginVersion + " initialized; waiting for the H3VR movement manager.");
        }

        private void Update()
        {
            if (ManagerSingleton<GM>.Instance == null)
            {
                return;
            }

            if (!_markedRunningModded)
            {
                GM.SetRunningModded();
                _markedRunningModded = true;
            }

            if (_controller != null)
            {
                _controller.Update();
            }
        }

        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.Dispose();
            }

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }

            Instance = null;
        }

        private void BindConfiguration()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Enable Titanfall movement while using an H3VR smooth locomotion mode.");
            ActionHand = Config.Bind("Input", "CrouchActionHand", "TurnHand", "Stick used for crouch/slide: TurnHand, Left, or Right. Jump is the right-stick click; left-stick click toggles sprint.");

            WalkSpeed = Config.Bind("Running", "WalkSpeed", 3.2f, "Maximum walking speed before sprint is toggled.");
            RunSpeed = Config.Bind("Running", "RunSpeed", 5.2f, "Maximum sprint speed after clicking the left stick.");
            GroundAcceleration = Config.Bind("Running", "GroundAcceleration", 22f, "Ground acceleration in metres per second squared.");
            GroundFriction = Config.Bind("Running", "GroundFriction", 32f, "Braking and sharp-turn response when the movement stick is released or reversed.");
            OverspeedDrag = Config.Bind("Running", "OverspeedDrag", 1.6f, "How quickly accumulated speed decays when walking instead of sprinting.");
            SprintMomentumAcceleration = Config.Bind("Running", "SprintMomentumAcceleration", 0.35f, "Speed gained per second while sprinting at strong stick input, up to MaxMomentumSpeed.");
            AirAcceleration = Config.Bind("Running", "AirAcceleration", 5f, "Air-control acceleration.");
            MaxMomentumSpeed = Config.Bind("Running", "MaxMomentumSpeed", 12f, "Safety cap for horizontal momentum.");

            JumpSpeed = Config.Bind("Jumping", "JumpSpeed", 5.35f, "Vertical speed of the first jump.");
            DoubleJumpSpeed = Config.Bind("Jumping", "DoubleJumpSpeed", 5.0f, "Vertical speed of the double jump.");
            DoubleJumpMomentumBoost = Config.Bind("Jumping", "DoubleJumpMomentumBoost", 1.4f, "Horizontal speed added by a double jump and carried into the next slide.");
            Gravity = Config.Bind("Jumping", "Gravity", 12.5f, "Custom downward acceleration while airborne.");

            SlideMinimumSpeed = Config.Bind("Sliding", "MinimumSpeed", 3.0f, "Minimum horizontal speed required to start a slide.");
            SlideBoost = Config.Bind("Sliding", "Boost", 1.8f, "Maximum speed added when a fast slide starts.");
            SlideFriction = Config.Bind("Sliding", "Friction", 2.4f, "Slide deceleration.");
            SlideMinimumDuration = Config.Bind("Sliding", "MinimumDuration", 0.65f, "Slide duration at minimum entry speed. Mid-speed slides use a generous nonlinear duration curve.");
            SlideMaximumDuration = Config.Bind("Sliding", "MaximumDuration", 1.55f, "Slide duration at maximum momentum.");
            SlideCooldown = Config.Bind("Sliding", "Cooldown", 0.65f, "Delay after a slide ends before another slide can start.");
            SlideRepeatPenaltyWindow = Config.Bind("Sliding", "RepeatPenaltyWindow", 1.5f, "Time after a slide ends during which the next slide receives less boost.");
            SlideRepeatSpeedMultiplier = Config.Bind("Sliding", "RepeatSpeedMultiplier", 0.55f, "Multiplier applied only to the next slide's added speed when used inside RepeatPenaltyWindow. Duration is never reduced.");

            CrouchSpeedMultiplier = Config.Bind("Crouching", "SpeedMultiplier", 0.48f, "Maximum running speed while crouched.");
            CrouchHeight = Config.Bind("Crouching", "VirtualHeight", 0.52f, "Virtual camera/body lowering in metres. Set to zero to disable forced lowering.");
            CrouchTransitionSpeed = Config.Bind("Crouching", "TransitionSpeed", 3.5f, "Crouch lowering/raising speed in metres per second.");

            WallRunProbeDistance = Config.Bind("WallRunning", "ProbeDistance", 0.72f, "Maximum distance from the head to a runnable wall.");
            WallRunMinimumSpeed = Config.Bind("WallRunning", "MinimumSpeed", 3.5f, "Minimum horizontal speed required to attach to a wall.");
            WallRunMinimumDuration = Config.Bind("WallRunning", "MinimumDuration", 0.65f, "Wall-run duration at minimum entry speed.");
            WallRunMaximumDuration = Config.Bind("WallRunning", "MaximumDuration", 2.4f, "Wall-run duration at maximum momentum.");
            WallRunRunSpeedDistance = Config.Bind("WallRunning", "RunSpeedDistance", 4.75f, "Wall-run distance in metres at ordinary sprint speed.");
            WallRunMaximumDistance = Config.Bind("WallRunning", "MaximumDistance", 18f, "Wall-run distance in metres at maximum accumulated momentum.");
            WallRunVerticalSpeed = Config.Bind("WallRunning", "VerticalSpeed", -0.25f, "Vertical speed maintained during a wall run.");
            WallRunAcceleration = Config.Bind("WallRunning", "Acceleration", 0.9f, "Horizontal speed gained per second during wall running, up to MaxMomentumSpeed.");
            WallJumpUpSpeed = Config.Bind("WallRunning", "JumpUpSpeed", 5.1f, "Vertical wall-jump speed.");
            WallJumpPushSpeed = Config.Bind("WallRunning", "JumpPushSpeed", 4.0f, "Horizontal speed away from the wall during a wall jump.");
            WallRunHeadTilt = Config.Bind("WallRunning", "Head Tilt Enabled", true, "Tilt the player tracking space and HD spectator camera toward the wall during wall running.");
            WallRunCameraTiltAngle = Config.Bind("WallRunning", "Head Tilt Angle", 25f, "Wall-run head tilt in degrees (clamped to 25 degrees for comfort and to prevent accidental double roll).");
            WallRunCameraTiltSpeed = Config.Bind("WallRunning", "Head Tilt Transition Speed", 180f, "Head tilt transition speed in degrees per second.");

            PlayerFootstepsWallRun = Config.Bind("Compatibility", "PlayerFootstepsWallRun", true, "Use the installed Player Footsteps mod and its sound-pack addons for wall-run steps.");
            WallRunFootstepDistance = Config.Bind("Compatibility", "WallRunFootstepDistance", 0.9f, "Distance travelled along a wall between Player Footsteps sounds, in metres.");

            SoundVolume = Config.Bind("Audio", "Volume", 0.85f, "Volume for jump, double-jump, looping wall-run and looping slide sounds.");

            // Migrate the original slippery defaults while preserving any custom values.
            if (Mathf.Abs(GroundAcceleration.Value - 16f) < 0.001f)
            {
                GroundAcceleration.Value = 22f;
            }
            if (Mathf.Abs(GroundFriction.Value - 9f) < 0.001f)
            {
                GroundFriction.Value = 32f;
            }
            if (Mathf.Abs(RunSpeed.Value - 6.5f) < 0.001f)
            {
                RunSpeed.Value = 5.2f;
            }
            if (Mathf.Abs(WallRunCameraTiltAngle.Value - 45f) < 0.001f)
            {
                WallRunCameraTiltAngle.Value = 25f;
            }
            if (Mathf.Abs(SlideMinimumDuration.Value - 0.45f) < 0.001f)
            {
                SlideMinimumDuration.Value = 0.65f;
            }

            // Migrate the removed option out of existing BepInEx files. Binding
            // once consumes an orphaned legacy value; removing and saving then
            // prevents it from remaining visible in Configuration Manager.
            ConfigEntry<bool> legacyOmnimovementSlide = Config.Bind(
                "Sliding",
                "Omnimovement Slide",
                false,
                "Removed in 1.8.6; slides now always support forward and lateral movement.");
            Config.Remove(legacyOmnimovementSlide.Definition);
            Config.Save();
        }

        internal bool BlockVanillaJump(FVRMovementManager manager)
        {
            return Enabled.Value && _controller != null && _controller.IsBoundTo(manager);
        }

        internal void BeforeMovementTick(FVRMovementManager manager)
        {
            if (_controller != null)
            {
                _controller.BeforeMovementTick(manager);
            }
        }

        internal void AfterMovementTick(FVRMovementManager manager)
        {
            if (_controller != null)
            {
                _controller.AfterMovementTick(manager);
            }
        }
    }


    // These two getters are the final targets consumed by H3VR's physical held-
    // object solver. Patching them also covers the filtered pose fields that
    // bypass HandInput's public position/rotation getters.
    [HarmonyPatch(typeof(FVRInteractiveObject), "get_m_handPos")]
    internal static class HeldObjectPositionTiltPatch
    {
        private static void Postfix(ref Vector3 __result)
        {
            WallRunCameraTilt.CompensatePoint(ref __result);
        }
    }

    [HarmonyPatch(typeof(FVRInteractiveObject), "get_m_handRot")]
    internal static class HeldObjectRotationTiltPatch
    {
        private static void Postfix(ref Quaternion __result)
        {
            WallRunCameraTilt.CompensateRotation(ref __result);
        }
    }

    // Quickbelt contents use the slot PoseOverride directly and therefore never
    // pass through the hand target above. Compensate the actual physics target,
    // not only the slot's rendered hierarchy.
    [HarmonyPatch(typeof(FVRPhysicalObject), "GetPosTarget")]
    internal static class QuickbeltObjectPositionTiltPatch
    {
        private static void Postfix(FVRPhysicalObject __instance, ref Vector3 __result)
        {
            if (!__instance.IsHeld && __instance.QuickbeltSlot != null)
            {
                WallRunCameraTilt.CompensatePoint(ref __result);
            }
        }
    }

    [HarmonyPatch(typeof(FVRPhysicalObject), "GetRotTarget")]
    internal static class QuickbeltObjectRotationTiltPatch
    {
        private static void Postfix(FVRPhysicalObject __instance, ref Quaternion __result)
        {
            if (!__instance.IsHeld && __instance.QuickbeltSlot != null)
            {
                WallRunCameraTilt.CompensateRotation(ref __result);
            }
        }
    }

    [HarmonyPatch(typeof(FVRMovementManager), "Jump")]
    internal static class VanillaJumpPatch
    {
        private static bool Prefix(FVRMovementManager __instance)
        {
            TitanfallMovementPlugin plugin = TitanfallMovementPlugin.Instance;
            return plugin == null || !plugin.BlockVanillaJump(__instance);
        }
    }

    [HarmonyPatch(typeof(FVRMovementManager), "FU")]
    internal static class MovementTickPatch
    {
        private static void Prefix(FVRMovementManager __instance)
        {
            TitanfallMovementPlugin plugin = TitanfallMovementPlugin.Instance;
            if (plugin != null)
            {
                plugin.BeforeMovementTick(__instance);
            }
        }

        private static void Postfix(FVRMovementManager __instance)
        {
            TitanfallMovementPlugin plugin = TitanfallMovementPlugin.Instance;
            if (plugin != null)
            {
                plugin.AfterMovementTick(__instance);
            }
        }
    }
}
