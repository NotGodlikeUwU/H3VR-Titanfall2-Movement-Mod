using System;
using FistVR;
using UnityEngine;

namespace NotGodlike.TitanfallMovement
{
    internal sealed class TitanfallMovementController : IDisposable
    {
        private readonly TitanfallMovementPlugin _plugin;
        private readonly MovementAudio _audio;
        private readonly PlayerFootstepsBridge _playerFootsteps;
        private readonly FVRMovementManager.TransformVelocity _velocityFilter;

        private FVRMovementManager _manager;
        private Vector3 _horizontalMomentum;
        private float _verticalVelocity;
        private bool _lastGrounded = true;
        private bool _jumpQueued;
        private bool _crouchQueued;
        private bool _crouched;
        private bool _sliding;
        private float _slideTime;
        private float _slideDuration;
        private Vector3 _slideDirection;
        private float _slideCooldownRemaining;
        private float _lastSlideEndTime = -10f;
        private int _airJumpsRemaining = 1;
        private bool _jumpedSinceGrounded;
        private bool _sprinting;
        private Vector3 _rawMoveDirection;
        private float _rawMoveAmount;
        private bool _landingSlideHeld;

        private bool _wallRunning;
        private Vector3 _wallNormal;
        private Vector3 _wallDirection;
        private Collider _currentWallCollider;
        private Collider _blockedWallCollider;
        private float _wallRunCameraSide;
        private float _wallRunTime;
        private float _wallRunDuration;
        private float _wallRunDistance;
        private float _wallRunDistanceLimit;
        private float _wallRunCooldown;
        private float _wallRunFootstepDistance;

        private bool _crouchPressedLastFrame;
        private float _crouchOffset;
        private float _appliedCrouchOffset;
        private float _lastVelocityTickTime = -10f;

        internal TitanfallMovementController(
            TitanfallMovementPlugin plugin,
            MovementAudio audio,
            PlayerFootstepsBridge playerFootsteps)
        {
            _plugin = plugin;
            _audio = audio;
            _playerFootsteps = playerFootsteps;
            _velocityFilter = ModifyVelocity;
        }

        internal bool IsBoundTo(FVRMovementManager manager)
        {
            return manager != null && manager == _manager && IsSupportedMode(manager);
        }

        internal bool IsWallRunning
        {
            get { return _wallRunning && _plugin.Enabled.Value; }
        }

        internal float WallRunCameraSide
        {
            get
            {
                if (!_wallRunning || _manager == null || _manager.Head == null)
                {
                    return 0f;
                }

                // Derive left/right from the current view yaw, not from the view
                // direction at wall-run entry. Roll around forward does not change
                // this projected forward vector, so turning past the wall correctly
                // reverses the lean without feeding camera roll back into the test.
                Vector3 viewForward = _manager.Head.forward;
                viewForward.y = 0f;
                if (viewForward.sqrMagnitude < 0.001f)
                {
                    return _wallRunCameraSide;
                }

                Vector3 viewRight = Vector3.Cross(Vector3.up, viewForward.normalized);
                Vector3 towardWall = -_wallNormal;
                towardWall.y = 0f;
                float side = Vector3.Dot(towardWall.normalized, viewRight);
                if (side > 0.08f)
                {
                    return -1f;
                }
                if (side < -0.08f)
                {
                    return 1f;
                }
                return _wallRunCameraSide;
            }
        }

        internal void Update()
        {
            BindCurrentManager();

            bool active = _plugin.Enabled.Value && _manager != null && IsSupportedMode(_manager);
            if (!active)
            {
                StopTransientState();
                UpdateCrouchOffset(false);
                return;
            }

            FVRViveHand actionHand = FindActionHand(_manager);
            FVRViveHand rightHand = FindRightHand(_manager);
            FVRViveHand leftHand = FindLeftHand(_manager);
            FVRViveHand movementHand = FindMovementHand(_manager);
            bool jumpDown = ReadStickClickDown(rightHand);
            bool sprintToggleDown = ReadStickClickDown(leftHand);
            bool crouchPressed = ReadSouthPressed(actionHand);
            bool landingSlidePressed = ReadSouthPressed(rightHand);

            ReadRawMovement(movementHand, out _rawMoveDirection, out _rawMoveAmount);
            _landingSlideHeld = !_lastGrounded && landingSlidePressed;

            if (jumpDown)
            {
                _jumpQueued = true;
            }

            if (sprintToggleDown)
            {
                _sprinting = !_sprinting;
            }

            if (crouchPressed && !_crouchPressedLastFrame)
            {
                _crouchQueued = true;
            }

            _crouchPressedLastFrame = crouchPressed;
            UpdateCrouchOffset(_crouched || _sliding);
        }

        internal void BeforeMovementTick(FVRMovementManager manager)
        {
            if (manager != _manager || _appliedCrouchOffset <= 0f)
            {
                return;
            }

            manager.transform.position += Vector3.up * _appliedCrouchOffset;
            _appliedCrouchOffset = 0f;
        }

        internal void AfterMovementTick(FVRMovementManager manager)
        {
            if (manager != _manager || !_plugin.Enabled.Value || !IsSupportedMode(manager) || _crouchOffset <= 0f)
            {
                return;
            }

            manager.transform.position -= Vector3.up * _crouchOffset;
            _appliedCrouchOffset = _crouchOffset;
            if (manager.Body != null)
            {
                manager.Body.UpdatePlayerBodyPositions();
            }
        }

        public void Dispose()
        {
            UnbindManager();
            _audio.StopAllMovementSounds();
        }

        private void BindCurrentManager()
        {
            if (ManagerSingleton<GM>.Instance == null)
            {
                return;
            }

            FVRMovementManager current = GM.CurrentMovementManager;
            if (current == _manager)
            {
                return;
            }

            UnbindManager();
            _manager = current;
            if (_manager != null)
            {
                _manager.RegisterSmoothLocoRestrictor(_velocityFilter);
                ResetState();
                TitanfallMovementPlugin.Logger.LogInfo("Attached to movement manager in mode " + _manager.Mode + ".");
            }
        }

        private void UnbindManager()
        {
            RestoreCrouchOffset();
            if (_manager != null)
            {
                _manager.DeregisterSmoothLocoRestrictor(_velocityFilter);
            }

            _manager = null;
            ResetState();
        }

        private void ModifyVelocity(ref Vector3 velocity, ref bool isGrounded, ref bool didChange)
        {
            if (!_plugin.Enabled.Value || _manager == null || !IsSupportedMode(_manager))
            {
                return;
            }

            float now = Time.time;
            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            Vector3 vanillaHorizontal = new Vector3(velocity.x, 0f, velocity.z);

            if (now - _lastVelocityTickTime > 0.25f)
            {
                _horizontalMomentum = vanillaHorizontal;
                _verticalVelocity = velocity.y;
            }
            _lastVelocityTickTime = now;

            if (_wallRunCooldown > 0f)
            {
                _wallRunCooldown -= deltaTime;
            }
            if (_slideCooldownRemaining > 0f)
            {
                _slideCooldownRemaining -= deltaTime;
            }

            bool justLanded = isGrounded && !_lastGrounded;
            if (isGrounded)
            {
                if (justLanded)
                {
                    _airJumpsRemaining = 1;
                    _jumpedSinceGrounded = false;
                    StopWallRun();
                    _blockedWallCollider = null;
                    _audio.FadeOutDoubleJump();
                }
                _verticalVelocity = 0f;
            }
            else if (_crouched || _sliding)
            {
                _crouched = false;
                StopSlide(true);
            }

            if (justLanded && _landingSlideHeld && TryStartBufferedLandingSlide())
            {
                _crouchQueued = false;
                _landingSlideHeld = false;
            }

            ProcessCrouchOrSlide(isGrounded);
            ProcessJump(ref isGrounded);

            float inputAmount = Mathf.Clamp01(vanillaHorizontal.magnitude / 1.5f);
            Vector3 inputDirection = vanillaHorizontal.sqrMagnitude > 0.0001f ? vanillaHorizontal.normalized : Vector3.zero;
            if (!isGrounded)
            {
                inputAmount = _rawMoveAmount;
                inputDirection = _rawMoveDirection;
            }

            if (!isGrounded && _jumpedSinceGrounded && !_wallRunning && _wallRunCooldown <= 0f)
            {
                TryStartWallRun(inputDirection);
            }

            if (_wallRunning)
            {
                UpdateWallRun(deltaTime, inputDirection, ref isGrounded);
            }
            else if (_sliding && isGrounded)
            {
                UpdateSlide(deltaTime);
            }
            else
            {
                UpdateRunning(deltaTime, inputDirection, inputAmount, isGrounded);
            }

            if (!isGrounded && !_wallRunning)
            {
                _verticalVelocity -= Mathf.Max(0.01f, _plugin.Gravity.Value) * deltaTime;
            }
            else if (isGrounded)
            {
                _verticalVelocity = Mathf.Max(0f, _verticalVelocity);
            }

            _horizontalMomentum = Vector3.ClampMagnitude(_horizontalMomentum, Mathf.Max(1f, _plugin.MaxMomentumSpeed.Value));
            velocity = _horizontalMomentum + Vector3.up * _verticalVelocity;
            didChange = true;
            _lastGrounded = isGrounded;
        }

        private void ProcessCrouchOrSlide(bool isGrounded)
        {
            if (!_crouchQueued)
            {
                return;
            }
            _crouchQueued = false;

            // Airborne down input is deliberately discarded. It must not lower the
            // virtual body, start a slide on landing, or affect fall speed.
            if (!isGrounded)
            {
                _crouched = false;
                return;
            }

            if (_sliding)
            {
                StopSlide(false);
                _crouched = false;
                return;
            }

            if (_crouched)
            {
                _crouched = false;
                return;
            }

            float speed = _horizontalMomentum.magnitude;
            bool enoughSpeed = speed >= _plugin.SlideMinimumSpeed.Value;
            bool directionAllowed = IsSlideDirectionAllowed();
            if (enoughSpeed && directionAllowed && _slideCooldownRemaining <= 0f)
            {
                StartSlide(speed);
            }
            else if (enoughSpeed && directionAllowed)
            {
                // During cooldown, ignore slide attempts instead of toggling crouch.
                return;
            }
            else
            {
                _crouched = true;
            }
        }

        private void ProcessJump(ref bool isGrounded)
        {
            if (!_jumpQueued)
            {
                return;
            }
            _jumpQueued = false;

            if (_wallRunning)
            {
                float carriedSpeed = Mathf.Max(_horizontalMomentum.magnitude, _plugin.WallRunMinimumSpeed.Value);
                Vector3 jumpDirection = HorizontalHeadForward();
                float intoWall = Vector3.Dot(jumpDirection, -_wallNormal);
                if (intoWall > 0f)
                {
                    jumpDirection = (jumpDirection + _wallNormal * (intoWall + 0.15f)).normalized;
                }
                float wallJumpSpeed = Mathf.Min(
                    _plugin.MaxMomentumSpeed.Value,
                    carriedSpeed + _plugin.WallJumpPushSpeed.Value * 0.5f);
                _horizontalMomentum = jumpDirection * wallJumpSpeed;
                _verticalVelocity = _plugin.WallJumpUpSpeed.Value;
                _manager.DelayGround(0.25f);
                isGrounded = false;
                _jumpedSinceGrounded = true;
                _crouched = false;
                StopSlide(true);
                StopWallRun();
                _wallRunCooldown = 0.22f;
                _audio.PlayDoubleJump();
                return;
            }

            if (isGrounded)
            {
                _verticalVelocity = _plugin.JumpSpeed.Value;
                _manager.DelayGround(0.25f);
                isGrounded = false;
                _jumpedSinceGrounded = true;
                _crouched = false;
                StopSlide(true);
                _audio.PlayJump();
                return;
            }

            if (_airJumpsRemaining > 0)
            {
                _airJumpsRemaining--;
                _verticalVelocity = _plugin.DoubleJumpSpeed.Value;
                Vector3 boostDirection = _rawMoveDirection.sqrMagnitude > 0.001f
                    ? _rawMoveDirection.normalized
                    : (_horizontalMomentum.sqrMagnitude > 0.001f
                        ? _horizontalMomentum.normalized
                        : HorizontalHeadForward());
                float boostedSpeed = Mathf.Min(
                    _plugin.MaxMomentumSpeed.Value,
                    _horizontalMomentum.magnitude + Mathf.Max(0f, _plugin.DoubleJumpMomentumBoost.Value));
                _horizontalMomentum = boostDirection * boostedSpeed;
                _manager.DelayGround(0.2f);
                _jumpedSinceGrounded = true;
                _crouched = false;
                StopSlide(true);
                _audio.PlayDoubleJump();
            }
        }

        private bool TryStartBufferedLandingSlide()
        {
            if (_sliding || _slideCooldownRemaining > 0f || !IsSlideDirectionAllowed())
            {
                return false;
            }

            float entrySpeed = Mathf.Max(_horizontalMomentum.magnitude, _plugin.SlideMinimumSpeed.Value);
            StartSlide(entrySpeed);
            return true;
        }

        private void UpdateRunning(float deltaTime, Vector3 inputDirection, float inputAmount, bool grounded)
        {
            float currentSpeed = _horizontalMomentum.magnitude;
            if (inputAmount <= 0.01f)
            {
                if (grounded)
                {
                    _horizontalMomentum = Vector3.MoveTowards(
                        _horizontalMomentum,
                        Vector3.zero,
                        _plugin.GroundFriction.Value * deltaTime);
                }
                return;
            }

            float locomotionSpeed = _sprinting ? _plugin.RunSpeed.Value : _plugin.WalkSpeed.Value;
            float targetSpeed = locomotionSpeed * inputAmount;
            if (_crouched)
            {
                targetSpeed *= Mathf.Clamp01(_plugin.CrouchSpeedMultiplier.Value);
            }

            if (grounded)
            {
                if (_sprinting && !_crouched && inputAmount >= 0.75f)
                {
                    float accumulatedSpeed = currentSpeed + _plugin.SprintMomentumAcceleration.Value * inputAmount * deltaTime;
                    targetSpeed = Mathf.Max(targetSpeed, Mathf.Min(_plugin.MaxMomentumSpeed.Value, accumulatedSpeed));
                }

                float speedRate = currentSpeed > targetSpeed ? _plugin.OverspeedDrag.Value : _plugin.GroundAcceleration.Value;
                float newSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedRate * deltaTime);
                Vector3 target = inputDirection * newSpeed;
                float alignment = currentSpeed > 0.01f ? Vector3.Dot(_horizontalMomentum / currentSpeed, inputDirection) : 1f;
                float steeringRate = alignment < 0.7f
                    ? Mathf.Max(_plugin.GroundFriction.Value, _plugin.GroundAcceleration.Value)
                    : Mathf.Max(_plugin.GroundAcceleration.Value, newSpeed * 4f);
                _horizontalMomentum = Vector3.MoveTowards(_horizontalMomentum, target, steeringRate * deltaTime);
            }
            else
            {
                float preservedSpeed = Mathf.Max(currentSpeed, targetSpeed);
                float newSpeed = Mathf.MoveTowards(currentSpeed, preservedSpeed, _plugin.AirAcceleration.Value * deltaTime);
                // Titanfall-style air strafing: input has full authority over the
                // horizontal direction while preserving accumulated momentum.
                _horizontalMomentum = inputDirection * newSpeed;
            }
        }

        private void StartSlide(float entrySpeed)
        {
            float speedFactor = Mathf.InverseLerp(_plugin.SlideMinimumSpeed.Value, _plugin.MaxMomentumSpeed.Value, entrySpeed);
            // Give ordinary sprint-speed slides a useful duration without
            // materially extending already fast momentum-chain slides.
            float durationFactor = Mathf.Sqrt(speedFactor);
            bool repeatedQuickly = Time.time - _lastSlideEndTime <= _plugin.SlideRepeatPenaltyWindow.Value;
            float boostMultiplier = repeatedQuickly ? _plugin.SlideRepeatSpeedMultiplier.Value : 1f;
            float boostedSpeed = Mathf.Min(
                _plugin.MaxMomentumSpeed.Value,
                entrySpeed + _plugin.SlideBoost.Value * speedFactor * boostMultiplier);
            _slideDirection = _horizontalMomentum.sqrMagnitude > 0.001f
                ? _horizontalMomentum.normalized
                : HorizontalHeadForward();
            _horizontalMomentum = _slideDirection * boostedSpeed;
            _slideDuration = Mathf.Lerp(_plugin.SlideMinimumDuration.Value, _plugin.SlideMaximumDuration.Value, durationFactor);
            _slideTime = 0f;
            _sliding = true;
            _crouched = true;
            _audio.StartSlide();
        }

        private bool IsSlideDirectionAllowed()
        {
            if (_horizontalMomentum.sqrMagnitude < 0.001f)
            {
                return true;
            }

            // Forward, diagonal and exactly lateral slides are supported. Keep
            // the rear hemisphere excluded so crouching while retreating cannot
            // unexpectedly launch a backwards slide.
            return Vector3.Dot(_horizontalMomentum.normalized, HorizontalHeadForward()) >= -0.05f;
        }

        private void UpdateSlide(float deltaTime)
        {
            _slideTime += deltaTime;
            float speed = Mathf.MoveTowards(_horizontalMomentum.magnitude, 0f, _plugin.SlideFriction.Value * deltaTime);
            _horizontalMomentum = _slideDirection * speed;

            if (_slideTime >= _slideDuration || speed < 0.8f)
            {
                StopSlide(false);
            }
        }

        private void StopSlide(bool standUp)
        {
            bool wasSliding = _sliding;
            _sliding = false;
            _slideTime = 0f;
            _slideDuration = 0f;
            if (wasSliding)
            {
                _lastSlideEndTime = Time.time;
                _slideCooldownRemaining = Mathf.Max(_slideCooldownRemaining, _plugin.SlideCooldown.Value);
                _audio.StopSlide();
            }
            if (standUp)
            {
                _crouched = false;
            }
        }

        private void TryStartWallRun(Vector3 inputDirection)
        {
            float speed = _horizontalMomentum.magnitude;
            if (speed < _plugin.WallRunMinimumSpeed.Value || inputDirection.sqrMagnitude < 0.01f)
            {
                return;
            }

            RaycastHit hit;
            if (!FindRunnableWall(out hit))
            {
                return;
            }
            if (_blockedWallCollider != null && hit.collider == _blockedWallCollider)
            {
                return;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, hit.normal).normalized;
            Vector3 travel = _horizontalMomentum.sqrMagnitude > 0.001f ? _horizontalMomentum.normalized : inputDirection;
            if (Vector3.Dot(tangent, travel) < 0f)
            {
                tangent = -tangent;
            }

            float speedFactor = Mathf.InverseLerp(_plugin.WallRunMinimumSpeed.Value, _plugin.MaxMomentumSpeed.Value, speed);
            _wallNormal = hit.normal;
            _wallDirection = tangent;
            _currentWallCollider = hit.collider;
            // Seed the side used only inside the small front/back dead zone. The
            // public camera-side value otherwise follows the player's live view yaw.
            Vector3 headRight = _manager.Head.right;
            headRight.y = 0f;
            headRight = headRight.sqrMagnitude > 0.001f ? headRight.normalized : Vector3.right;
            float hitSide = Vector3.Dot(hit.point - _manager.Head.position, headRight);
            if (Mathf.Abs(hitSide) < 0.01f)
            {
                hitSide = -Vector3.Dot(hit.normal, headRight);
            }
            _wallRunCameraSide = hitSide < 0f ? 1f : -1f;
            _wallRunDuration = Mathf.Lerp(_plugin.WallRunMinimumDuration.Value, _plugin.WallRunMaximumDuration.Value, speedFactor);
            float sprintSpeed = Mathf.Max(_plugin.WallRunMinimumSpeed.Value + 0.01f, _plugin.RunSpeed.Value);
            if (speed <= sprintSpeed)
            {
                float sprintFactor = Mathf.InverseLerp(_plugin.WallRunMinimumSpeed.Value, sprintSpeed, speed);
                _wallRunDistanceLimit = Mathf.Lerp(
                    _plugin.WallRunRunSpeedDistance.Value * 0.7f,
                    _plugin.WallRunRunSpeedDistance.Value,
                    sprintFactor);
            }
            else
            {
                float momentumFactor = Mathf.InverseLerp(sprintSpeed, _plugin.MaxMomentumSpeed.Value, speed);
                _wallRunDistanceLimit = Mathf.Lerp(
                    _plugin.WallRunRunSpeedDistance.Value,
                    _plugin.WallRunMaximumDistance.Value,
                    momentumFactor);
            }
            _wallRunTime = 0f;
            _wallRunDistance = 0f;
            _wallRunFootstepDistance = 0f;
            _wallRunning = true;
            _sliding = false;
            _crouched = false;
            _airJumpsRemaining = 1;
            _verticalVelocity = Mathf.Max(_verticalVelocity, _plugin.WallRunVerticalSpeed.Value);
            _audio.FadeOutDoubleJump();
            _audio.StartWallRun();
            PlayWallRunFootstep(hit);
        }

        private void UpdateWallRun(float deltaTime, Vector3 inputDirection, ref bool isGrounded)
        {
            _wallRunTime += deltaTime;
            RaycastHit hit;
            bool hasWall = FindCurrentWall(out hit);
            bool movingForward = inputDirection.sqrMagnitude > 0.01f &&
                                 Vector3.Dot(inputDirection.normalized, _wallDirection) > 0.2f;
            bool pathBlocked = IsWallRunPathBlocked();
            if (!hasWall || !movingForward || pathBlocked ||
                _wallRunDistance >= _wallRunDistanceLimit || _wallRunTime >= _wallRunDuration)
            {
                StopWallRun();
                _wallRunCooldown = 0.15f;
                return;
            }

            _wallNormal = hit.normal;
            _currentWallCollider = hit.collider;

            float entryFactor = Mathf.InverseLerp(_plugin.WallRunMinimumDuration.Value, _plugin.WallRunMaximumDuration.Value, _wallRunDuration);
            float targetSpeed = Mathf.Lerp(_plugin.WallRunMinimumSpeed.Value, _plugin.MaxMomentumSpeed.Value, entryFactor);
            float speed = Mathf.Max(_horizontalMomentum.magnitude, targetSpeed);
            speed = Mathf.Min(_plugin.MaxMomentumSpeed.Value, speed + _plugin.WallRunAcceleration.Value * deltaTime);
            _wallRunDistance += speed * deltaTime;
            // Never push the player into the wall collider. H3VR resolves that inward
            // component by cancelling movement, which made wall runs feel glued in place.
            Vector3 alongWall = Vector3.ProjectOnPlane(_wallDirection, _wallNormal);
            alongWall.y = 0f;
            _horizontalMomentum = alongWall.sqrMagnitude > 0.001f
                ? alongWall.normalized * speed
                : _wallDirection * speed;
            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, _plugin.WallRunVerticalSpeed.Value, _plugin.Gravity.Value * 0.8f * deltaTime);
            UpdateWallRunFootsteps(hit, speed, deltaTime);
            isGrounded = false;
        }

        private void UpdateWallRunFootsteps(RaycastHit wallHit, float speed, float deltaTime)
        {
            if (!_plugin.PlayerFootstepsWallRun.Value || _playerFootsteps == null)
            {
                return;
            }

            _wallRunFootstepDistance += Mathf.Max(0f, speed) * deltaTime;
            float spacing = Mathf.Max(0.25f, _plugin.WallRunFootstepDistance.Value);
            if (_wallRunFootstepDistance < spacing)
            {
                return;
            }

            _wallRunFootstepDistance %= spacing;
            PlayWallRunFootstep(wallHit);
        }

        private void PlayWallRunFootstep(RaycastHit wallHit)
        {
            if (!_plugin.PlayerFootstepsWallRun.Value || _playerFootsteps == null)
            {
                return;
            }

            // Match Player Footsteps' own surface lookup exactly. The boolean
            // selects its generic fallback path when the collider has no PMat.
            BulletImpactSoundType surface = (BulletImpactSoundType)5;
            bool useGenericFallback = true;
            Collider collider = wallHit.collider;
            if (collider != null)
            {
                PMat physicalMaterial = collider.GetComponent<PMat>();
                if (physicalMaterial != null && physicalMaterial.MatDef != null)
                {
                    surface = physicalMaterial.MatDef.BulletImpactSound;
                    useGenericFallback = false;
                }
            }

            _playerFootsteps.PlayWallRunStep(surface, useGenericFallback);
        }

        private bool FindCurrentWall(out RaycastHit hit)
        {
            hit = new RaycastHit();
            if (_manager == null || _manager.Head == null)
            {
                return false;
            }

            Vector3 towardWall = -_wallNormal;
            towardWall.y = 0f;
            if (towardWall.sqrMagnitude < 0.001f)
            {
                return false;
            }

            float distance = _plugin.WallRunProbeDistance.Value + 0.15f;
            if (!Physics.Raycast(_manager.Head.position, towardWall.normalized, out hit, distance, _manager.LM_TeleCast) ||
                !IsRunnableWall(hit.normal))
            {
                return false;
            }

            Vector3 continuedDirection = Vector3.ProjectOnPlane(_wallDirection, hit.normal);
            continuedDirection.y = 0f;
            return continuedDirection.sqrMagnitude > 0.001f &&
                   Vector3.Dot(continuedDirection.normalized, _wallDirection) > 0.35f;
        }

        private bool IsWallRunPathBlocked()
        {
            if (_manager == null || _manager.Head == null)
            {
                return true;
            }

            RaycastHit hit;
            return Physics.Raycast(
                _manager.Head.position,
                _wallDirection,
                out hit,
                0.55f,
                _manager.LM_TeleCast);
        }

        private bool FindRunnableWall(out RaycastHit bestHit)
        {
            bestHit = new RaycastHit();
            if (_manager == null || _manager.Head == null)
            {
                return false;
            }

            Vector3 origin = _manager.Head.position;
            Vector3 right = _manager.Head.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.001f)
            {
                return false;
            }
            right.Normalize();

            RaycastHit rightHit;
            RaycastHit leftHit;
            bool foundRight = Physics.Raycast(origin, right, out rightHit, _plugin.WallRunProbeDistance.Value, _manager.LM_TeleCast) &&
                              IsRunnableWall(rightHit.normal) && rightHit.collider != _blockedWallCollider;
            bool foundLeft = Physics.Raycast(origin, -right, out leftHit, _plugin.WallRunProbeDistance.Value, _manager.LM_TeleCast) &&
                             IsRunnableWall(leftHit.normal) && leftHit.collider != _blockedWallCollider;

            if (foundRight && foundLeft)
            {
                bestHit = rightHit.distance <= leftHit.distance ? rightHit : leftHit;
                return true;
            }
            if (foundRight)
            {
                bestHit = rightHit;
                return true;
            }
            if (foundLeft)
            {
                bestHit = leftHit;
                return true;
            }
            return false;
        }

        private static bool IsRunnableWall(Vector3 normal)
        {
            return Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) < 0.3f;
        }

        private void StopWallRun()
        {
            if (_wallRunning && _currentWallCollider != null)
            {
                // The short cooldown expires while the player is still beside
                // the wall. Lock this collider until landing so falling cannot
                // immediately start another run; another wall remains valid.
                _blockedWallCollider = _currentWallCollider;
            }
            _wallRunning = false;
            _currentWallCollider = null;
            _wallRunCameraSide = 0f;
            _wallRunTime = 0f;
            _wallRunDuration = 0f;
            _wallRunDistance = 0f;
            _wallRunDistanceLimit = 0f;
            _wallRunFootstepDistance = 0f;
            // Always stop the loop, even if movement state and audio state became
            // desynchronised during a scene or locomotion transition.
            _audio.StopWallRun();
        }

        private void UpdateCrouchOffset(bool lowered)
        {
            float target = lowered ? Mathf.Max(0f, _plugin.CrouchHeight.Value) : 0f;
            _crouchOffset = Mathf.MoveTowards(_crouchOffset, target, Mathf.Max(0.01f, _plugin.CrouchTransitionSpeed.Value) * Time.deltaTime);
        }

        private void RestoreCrouchOffset()
        {
            if (_manager != null && _appliedCrouchOffset > 0f)
            {
                _manager.transform.position += Vector3.up * _appliedCrouchOffset;
                if (_manager.Body != null)
                {
                    _manager.Body.UpdatePlayerBodyPositions();
                }
            }
            _appliedCrouchOffset = 0f;
            _crouchOffset = 0f;
        }

        private void StopTransientState()
        {
            _jumpQueued = false;
            _crouchQueued = false;
            _crouched = false;
            StopSlide(true);
            StopWallRun();
            _audio.StopAllMovementSounds();
        }

        private void ResetState()
        {
            _horizontalMomentum = Vector3.zero;
            _verticalVelocity = 0f;
            _lastGrounded = true;
            _sprinting = false;
            _rawMoveDirection = Vector3.zero;
            _rawMoveAmount = 0f;
            _landingSlideHeld = false;
            _airJumpsRemaining = 1;
            _jumpedSinceGrounded = false;
            _wallRunCooldown = 0f;
            _crouchPressedLastFrame = false;
            _lastVelocityTickTime = -10f;
            StopTransientState();
            _currentWallCollider = null;
            _blockedWallCollider = null;
            _slideCooldownRemaining = 0f;
            _lastSlideEndTime = -10f;
        }

        private Vector3 HorizontalHeadForward()
        {
            if (_manager == null || _manager.Head == null)
            {
                return Vector3.forward;
            }
            Vector3 forward = _manager.Head.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        private static bool IsSupportedMode(FVRMovementManager manager)
        {
            return manager.Mode == FVRMovementManager.MovementMode.TwinStick ||
                   manager.Mode == FVRMovementManager.MovementMode.SingleTwoAxis ||
                   manager.Mode == FVRMovementManager.MovementMode.Armswinger;
        }

        private FVRViveHand FindActionHand(FVRMovementManager manager)
        {
            if (manager.Hands == null || manager.Hands.Length == 0)
            {
                return null;
            }

            string preference = (_plugin.ActionHand.Value ?? "TurnHand").Trim();
            bool wantRight;
            if (string.Equals(preference, "Left", StringComparison.OrdinalIgnoreCase))
            {
                wantRight = false;
            }
            else if (string.Equals(preference, "Right", StringComparison.OrdinalIgnoreCase))
            {
                wantRight = true;
            }
            else
            {
                wantRight = true;
                if (manager.Mode == FVRMovementManager.MovementMode.TwinStick)
                {
                    wantRight = GM.Options.MovementOptions.TwinStickLeftRightState != MovementOptions.TwinStickLeftRightSetup.RightStickMove;
                }
            }

            for (int i = 0; i < manager.Hands.Length; i++)
            {
                FVRViveHand hand = manager.Hands[i];
                if (hand != null && hand.IsThisTheRightHand == wantRight)
                {
                    return hand;
                }
            }
            return manager.Hands[0];
        }

        private static FVRViveHand FindRightHand(FVRMovementManager manager)
        {
            if (manager == null || manager.Hands == null)
            {
                return null;
            }
            for (int i = 0; i < manager.Hands.Length; i++)
            {
                FVRViveHand hand = manager.Hands[i];
                if (hand != null && hand.IsThisTheRightHand)
                {
                    return hand;
                }
            }
            return null;
        }

        private static FVRViveHand FindLeftHand(FVRMovementManager manager)
        {
            if (manager == null || manager.Hands == null)
            {
                return null;
            }
            for (int i = 0; i < manager.Hands.Length; i++)
            {
                FVRViveHand hand = manager.Hands[i];
                if (hand != null && !hand.IsThisTheRightHand)
                {
                    return hand;
                }
            }
            return null;
        }

        private static FVRViveHand FindMovementHand(FVRMovementManager manager)
        {
            if (manager == null || manager.Hands == null)
            {
                return null;
            }

            bool wantRight = manager.Mode == FVRMovementManager.MovementMode.TwinStick &&
                             GM.Options.MovementOptions.TwinStickLeftRightState == MovementOptions.TwinStickLeftRightSetup.RightStickMove;
            for (int i = 0; i < manager.Hands.Length; i++)
            {
                FVRViveHand hand = manager.Hands[i];
                if (hand != null && hand.IsThisTheRightHand == wantRight)
                {
                    return hand;
                }
            }
            return manager.Hands.Length > 0 ? manager.Hands[0] : null;
        }

        private void ReadRawMovement(FVRViveHand hand, out Vector3 direction, out float amount)
        {
            direction = Vector3.zero;
            amount = 0f;
            if (hand == null || _manager == null || _manager.Head == null)
            {
                return;
            }

            Vector2 axes = hand.CMode == ControlMode.Vive || hand.CMode == ControlMode.Oculus
                ? hand.Input.TouchpadAxes
                : hand.Input.Secondary2AxisInputAxes;
            float magnitude = Mathf.Clamp01(axes.magnitude);
            const float deadzone = 0.12f;
            if (magnitude <= deadzone)
            {
                return;
            }

            amount = Mathf.InverseLerp(deadzone, 1f, magnitude);
            Vector3 forward = HorizontalHeadForward();
            Vector3 right = _manager.Head.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
            Vector3 requested = right * axes.x + forward * axes.y;
            if (requested.sqrMagnitude > 0.001f)
            {
                direction = requested.normalized;
            }
        }

        private static bool ReadStickClickDown(FVRViveHand hand)
        {
            if (hand == null)
            {
                return false;
            }
            if (hand.CMode == ControlMode.Vive || hand.CMode == ControlMode.Oculus)
            {
                return hand.Input.TouchpadDown;
            }
            return hand.Input.Secondary2AxisInputDown;
        }

        private static bool ReadSouthPressed(FVRViveHand hand)
        {
            if (hand == null)
            {
                return false;
            }
            if (hand.CMode == ControlMode.Vive || hand.CMode == ControlMode.Oculus)
            {
                return hand.Input.TouchpadSouthPressed;
            }
            return hand.Input.Secondary2AxisSouthPressed;
        }
    }
}
