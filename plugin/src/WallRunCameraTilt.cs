using FistVR;
using System.Collections.Generic;
using UnityEngine;

namespace NotGodlike.TitanfallMovement
{
    internal sealed class WallRunCameraTilt : MonoBehaviour
    {
        private TitanfallMovementPlugin _plugin;
        private TitanfallMovementController _controller;

        private Transform _trackingRoot;
        private Transform _rollPivot;
        private float _currentRoll;
        private float _targetRoll;
        private bool _rigRollApplied;

        private Camera _appliedSpectatorCamera;
        private Vector3 _spectatorBasePosition;
        private Quaternion _spectatorBaseRotation;
        private bool _spectatorRollApplied;
        private bool _loggedRig;
        private bool _loggedSpectatorCamera;
        private bool _loggedMeasuredRoll;
        private readonly List<ExternalPose> _externalPoses = new List<ExternalPose>();

        private const float MaximumTiltAngle = 25f;
        // SteamVR presents tracking-space parent roll more strongly in the HMD
        // than the scene-camera transform reports. A half-angle tracking-space
        // rotation produces the requested 25-degree perceived world tilt.
        private const float HmdTrackingRollScale = 0.5f;

        private sealed class ExternalPose
        {
            internal Transform Transform;
            internal Vector3 BasePosition;
            internal Quaternion BaseRotation;
            internal Vector3 AppliedPosition;
            internal Quaternion AppliedRotation;
        }

        internal static WallRunCameraTilt Instance { get; private set; }

        internal void Initialize(TitanfallMovementPlugin plugin, TitanfallMovementController controller)
        {
            _plugin = plugin;
            _controller = controller;
            Instance = this;
        }

        private void OnEnable()
        {
            Camera.onPreCull += ApplySpectatorRoll;
            Camera.onPreRender += ApplySpectatorRoll;
            Camera.onPostRender += RestoreSpectatorRoll;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= ApplySpectatorRoll;
            Camera.onPreRender -= ApplySpectatorRoll;
            Camera.onPostRender -= RestoreSpectatorRoll;
            CleanupRigPivot();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            bool canTilt = _plugin != null &&
                           _controller != null &&
                           _plugin.Enabled.Value &&
                           _plugin.WallRunHeadTilt.Value &&
                           _controller.IsWallRunning;

            float configuredTarget = canTilt
                ? _controller.WallRunCameraSide * Mathf.Clamp(Mathf.Abs(_plugin.WallRunCameraTiltAngle.Value), 0f, MaximumTiltAngle)
                : 0f;
            _targetRoll = configuredTarget * HmdTrackingRollScale;
            float speed = Mathf.Max(1f, _plugin.WallRunCameraTiltSpeed.Value);
            _currentRoll = Mathf.MoveTowards(_currentRoll, _targetRoll, speed * HmdTrackingRollScale * Time.deltaTime);
        }

        private void LateUpdate()
        {
            RestoreExternalCompensation();
            RestoreRigRoll();

            if (Mathf.Abs(_currentRoll) < 0.01f || !EnsureRigPivot())
            {
                return;
            }

            AdoptNewTrackingChildren();

            Camera eyeCamera = GM.CurrentPlayerBody != null ? GM.CurrentPlayerBody.EyeCam : null;
            if (eyeCamera == null)
            {
                return;
            }

            // [CameraRig]Fixed remains H3VR's authoritative locomotion root. Only
            // its tracking branches live below this identity pivot, so H3VR can
            // keep writing world movement to the root without baking our roll into
            // its local rotation during collisions, wall jumps, or corner stops.
            Vector3 eyePosition = eyeCamera.transform.position;
            Vector3 viewForward = eyeCamera.transform.forward;
            Vector3 baselineUp = eyeCamera.transform.up;

            // Build the roll from the authoritative root every frame. Multiplying
            // a previously modified pivot can accumulate an old roll and make a
            // configured 25 degrees look like the former 45-degree setting.
            _rollPivot.rotation = Quaternion.AngleAxis(_currentRoll, viewForward) * _trackingRoot.rotation;
            _rollPivot.position += eyePosition - eyeCamera.transform.position;
            _rigRollApplied = true;

            ApplyQuickbeltCompensation(eyePosition, viewForward);

            if (!_loggedMeasuredRoll && Mathf.Abs(_currentRoll - _targetRoll) < 0.05f)
            {
                Vector3 before = Vector3.ProjectOnPlane(baselineUp, viewForward);
                Vector3 after = Vector3.ProjectOnPlane(eyeCamera.transform.up, viewForward);
                float measured = _currentRoll;
                if (before.sqrMagnitude > 0.001f && after.sqrMagnitude > 0.001f)
                {
                    measured = Vector3.Angle(before, after) *
                               Mathf.Sign(Vector3.Dot(viewForward, Vector3.Cross(before, after)));
                }
                TitanfallMovementPlugin.Logger.LogInfo(
                    "Wall-run configured visual roll=" + (_targetRoll / HmdTrackingRollScale).ToString("F1") +
                    " degrees, tracking-space delta=" + measured.ToString("F1") + " degrees.");
                _loggedMeasuredRoll = true;
            }

            if (!_loggedRig)
            {
                TitanfallMovementPlugin.Logger.LogInfo(
                    "Wall-run physical tracking roll attached inside player root " + _trackingRoot.name + ".");
                _loggedRig = true;
            }
        }

        private bool EnsureRigPivot()
        {
            if (ManagerSingleton<GM>.Instance == null || GM.CurrentPlayerRoot == null)
            {
                return false;
            }

            Transform root = GM.CurrentPlayerRoot;
            if (_rollPivot != null && _trackingRoot == root && _rollPivot.parent == root)
            {
                return true;
            }

            CleanupRigPivot();
            _trackingRoot = root;

            GameObject pivotObject = new GameObject("Titanfall Player Tracking Roll Pivot");
            _rollPivot = pivotObject.transform;
            _rollPivot.SetParent(root, false);
            _rollPivot.localPosition = Vector3.zero;
            _rollPivot.localRotation = Quaternion.identity;
            _rollPivot.localScale = Vector3.one;
            AdoptNewTrackingChildren();
            return true;
        }

        private void AdoptNewTrackingChildren()
        {
            if (_trackingRoot == null || _rollPivot == null)
            {
                return;
            }

            // Iterate backwards because SetParent changes the root child list.
            // The pivot is identity while this runs, so every branch retains its
            // existing local tracking pose and physical alignment.
            for (int i = _trackingRoot.childCount - 1; i >= 0; --i)
            {
                Transform child = _trackingRoot.GetChild(i);
                if (child != _rollPivot)
                {
                    child.SetParent(_rollPivot, false);
                }
            }
        }

        private void RestoreRigRoll()
        {
            if (_rigRollApplied && _rollPivot != null)
            {
                _rollPivot.localPosition = Vector3.zero;
                _rollPivot.localRotation = Quaternion.identity;
            }
            _rigRollApplied = false;
        }

        private void ApplyQuickbeltCompensation(Vector3 eyePosition, Vector3 viewForward)
        {
            FVRPlayerBody body = GM.CurrentPlayerBody;
            if (body == null || body.QuickbeltSlots == null)
            {
                return;
            }

            Quaternion roll = Quaternion.AngleAxis(_currentRoll, viewForward);
            for (int i = 0; i < body.QuickbeltSlots.Count; ++i)
            {
                FVRQuickBeltSlot slot = body.QuickbeltSlots[i];
                if (slot == null)
                {
                    continue;
                }

                CompensateExternalTransform(slot.transform, eyePosition, roll);

                FVRPhysicalObject contained = slot.CurObject;
                if (contained != null && !contained.IsHeld)
                {
                    CompensateExternalTransform(contained.transform, eyePosition, roll);
                }
            }
        }

        private void CompensateExternalTransform(Transform target, Vector3 eyePosition, Quaternion roll)
        {
            if (target == null || target == _rollPivot || target.IsChildOf(_rollPivot))
            {
                return;
            }

            for (int i = 0; i < _externalPoses.Count; ++i)
            {
                if (_externalPoses[i].Transform == target)
                {
                    return;
                }
            }

            ExternalPose pose = new ExternalPose();
            pose.Transform = target;
            pose.BasePosition = target.position;
            pose.BaseRotation = target.rotation;
            pose.AppliedPosition = eyePosition + roll * (pose.BasePosition - eyePosition);
            pose.AppliedRotation = roll * pose.BaseRotation;
            target.position = pose.AppliedPosition;
            target.rotation = pose.AppliedRotation;
            _externalPoses.Add(pose);
        }

        private void RestoreExternalCompensation()
        {
            for (int i = 0; i < _externalPoses.Count; ++i)
            {
                ExternalPose pose = _externalPoses[i];
                if (pose.Transform == null)
                {
                    continue;
                }

                // Only undo our exact pose. If H3VR has already authored a new
                // pose this frame, preserving it avoids snapping or drift.
                if ((pose.Transform.position - pose.AppliedPosition).sqrMagnitude < 0.000001f &&
                    Quaternion.Angle(pose.Transform.rotation, pose.AppliedRotation) < 0.05f)
                {
                    pose.Transform.position = pose.BasePosition;
                    pose.Transform.rotation = pose.BaseRotation;
                }
            }
            _externalPoses.Clear();
        }

        private void CleanupRigPivot()
        {
            RestoreSpectatorRollInternal();
            RestoreExternalCompensation();
            RestoreRigRoll();

            if (_trackingRoot != null && _rollPivot != null && _rollPivot.parent == _trackingRoot)
            {
                while (_rollPivot.childCount > 0)
                {
                    _rollPivot.GetChild(0).SetParent(_trackingRoot, false);
                }
            }
            if (_rollPivot != null)
            {
                Destroy(_rollPivot.gameObject);
            }

            _trackingRoot = null;
            _rollPivot = null;
            _loggedMeasuredRoll = false;
        }

        internal bool TryGetPhysicalCompensation(out Vector3 eyePosition, out Quaternion roll)
        {
            eyePosition = Vector3.zero;
            roll = Quaternion.identity;
            if (_plugin == null || !_plugin.Enabled.Value || !_plugin.WallRunHeadTilt.Value ||
                Mathf.Abs(_currentRoll) < 0.01f || GM.CurrentPlayerBody == null ||
                GM.CurrentPlayerBody.EyeCam == null)
            {
                return false;
            }

            Camera eye = GM.CurrentPlayerBody.EyeCam;
            eyePosition = eye.transform.position;
            roll = Quaternion.AngleAxis(_currentRoll, eye.transform.forward);
            return true;
        }

        internal static void CompensatePoint(ref Vector3 point)
        {
            Vector3 eyePosition;
            Quaternion roll;
            if (Instance != null && Instance.TryGetPhysicalCompensation(out eyePosition, out roll))
            {
                point = eyePosition + roll * (point - eyePosition);
            }
        }

        internal static void CompensateRotation(ref Quaternion rotation)
        {
            Vector3 eyePosition;
            Quaternion roll;
            if (Instance != null && Instance.TryGetPhysicalCompensation(out eyePosition, out roll))
            {
                rotation = roll * rotation;
            }
        }

        private void ApplySpectatorRoll(Camera camera)
        {
            if (!IsHdSpectatorCamera(camera) || Mathf.Abs(_currentRoll) < 0.01f)
            {
                return;
            }

            RestoreSpectatorRollInternal();
            _appliedSpectatorCamera = camera;
            _spectatorBasePosition = camera.transform.position;
            _spectatorBaseRotation = camera.transform.rotation;

            // SpectatorCamera is outside the physical VR player root. Rebuild a
            // level baseline around its current forward vector and apply the same
            // signed roll so H3VR's optional roll-removal cannot erase the effect.
            Vector3 forward = camera.transform.forward;
            Vector3 levelUp = Vector3.ProjectOnPlane(Vector3.up, forward);
            Quaternion levelRotation = levelUp.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward, levelUp.normalized)
                : camera.transform.rotation;
            float spectatorRoll = _currentRoll / HmdTrackingRollScale;
            camera.transform.rotation = Quaternion.AngleAxis(spectatorRoll, forward) * levelRotation;
            _spectatorRollApplied = true;

            if (!_loggedSpectatorCamera)
            {
                TitanfallMovementPlugin.Logger.LogInfo(
                    "Wall-run roll attached to H3VR HD spectator camera " + camera.name + ".");
                _loggedSpectatorCamera = true;
            }
        }

        private void RestoreSpectatorRoll(Camera camera)
        {
            if (camera == _appliedSpectatorCamera)
            {
                RestoreSpectatorRollInternal();
            }
        }

        private void RestoreSpectatorRollInternal()
        {
            if (_spectatorRollApplied && _appliedSpectatorCamera != null)
            {
                _appliedSpectatorCamera.transform.position = _spectatorBasePosition;
                _appliedSpectatorCamera.transform.rotation = _spectatorBaseRotation;
            }
            _appliedSpectatorCamera = null;
            _spectatorRollApplied = false;
        }

        private static bool IsHdSpectatorCamera(Camera camera)
        {
            return camera != null &&
                   ManagerSingleton<GM>.Instance != null &&
                   GM.CurrentSceneSettings != null &&
                   camera == GM.CurrentSceneSettings.SpectatorCamera;
        }
    }
}
