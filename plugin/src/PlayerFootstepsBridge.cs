using System;
using System.Reflection;
using BepInEx.Bootstrap;
using FistVR;

namespace NotGodlike.TitanfallMovement
{
    internal sealed class PlayerFootstepsBridge
    {
        private const string PluginGuid = "h3vr.kodeman.playerfootsteps";

        private readonly object[] _playArguments = new object[2];
        private object _instance;
        private MethodInfo _playFootstepMethod;
        private FieldInfo _initializedField;
        private bool _resolutionAttempted;
        private bool _disabledAfterFailure;

        internal bool PlayWallRunStep(BulletImpactSoundType surface, bool useGenericFallback)
        {
            if (_disabledAfterFailure || (!Resolve() && _instance == null))
            {
                return false;
            }

            if (_initializedField != null && !(bool)_initializedField.GetValue(_instance))
            {
                return false;
            }

            try
            {
                _playArguments[0] = surface;
                _playArguments[1] = useGenericFallback;
                _playFootstepMethod.Invoke(_instance, _playArguments);
                return true;
            }
            catch (Exception exception)
            {
                _disabledAfterFailure = true;
                TitanfallMovementPlugin.Logger.LogWarning(
                    "Player Footsteps wall-run integration disabled after an invocation failure: " + exception);
                return false;
            }
        }

        private bool Resolve()
        {
            if (_instance != null && _playFootstepMethod != null)
            {
                return true;
            }
            if (_resolutionAttempted)
            {
                return false;
            }
            _resolutionAttempted = true;

            BepInEx.PluginInfo pluginInfo;
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out pluginInfo) || pluginInfo.Instance == null)
            {
                TitanfallMovementPlugin.Logger.LogInfo(
                    "Player Footsteps is not installed; wall-run footstep integration will remain inactive.");
                return false;
            }

            Type pluginType = pluginInfo.Instance.GetType();
            _playFootstepMethod = pluginType.GetMethod(
                "PlayFootstepSound",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(BulletImpactSoundType), typeof(bool) },
                null);
            _initializedField = pluginType.GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_playFootstepMethod == null)
            {
                TitanfallMovementPlugin.Logger.LogWarning(
                    "Player Footsteps was found, but its compatible PlayFootstepSound method was not available.");
                return false;
            }

            _instance = pluginInfo.Instance;
            TitanfallMovementPlugin.Logger.LogInfo(
                "Player Footsteps wall-run integration enabled; installed sound-pack replacements will be used.");
            return true;
        }
    }
}
