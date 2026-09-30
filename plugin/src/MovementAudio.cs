using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NotGodlike.TitanfallMovement
{
    internal sealed class MovementAudio : MonoBehaviour
    {
        private readonly List<AudioClip> _jumpClips = new List<AudioClip>();
        private readonly List<AudioClip> _doubleJumpClips = new List<AudioClip>();
        private readonly List<AudioClip> _wallRunClips = new List<AudioClip>();
        private readonly List<AudioClip> _slideClips = new List<AudioClip>();

        private TitanfallMovementPlugin _plugin;
        private AudioSource _jumpSource;
        private AudioSource _doubleJumpSource;
        private AudioSource _wallRunSource;
        private AudioSource _slideSource;
        private int _lastJumpIndex = -1;
        private int _lastDoubleJumpIndex = -1;
        private int _lastWallRunIndex = -1;
        private int _lastSlideIndex = -1;
        private bool _slideFading;
        private float _slideFadeStartVolume;
        private float _slideFadeElapsed;
        private bool _doubleJumpFading;
        private float _doubleJumpFadeStartVolume;
        private float _doubleJumpFadeElapsed;

        private const float SlideFadeDuration = 0.24f;
        private const float DoubleJumpFadeDuration = 0.2f;

        internal void Initialize(TitanfallMovementPlugin plugin)
        {
            _plugin = plugin;
            _jumpSource = CreateNonSpatialSource(false);
            _doubleJumpSource = CreateNonSpatialSource(false);

            _wallRunSource = CreateNonSpatialSource(true);

            _slideSource = CreateNonSpatialSource(true);

            StartCoroutine(LoadAllClips());
        }

        internal void PlayJump()
        {
            PlayRandom(_jumpSource, _jumpClips, ref _lastJumpIndex);
        }

        internal void PlayDoubleJump()
        {
            StopDoubleJumpImmediate();
            PlayRandom(_doubleJumpSource, _doubleJumpClips, ref _lastDoubleJumpIndex);
        }

        internal void FadeOutDoubleJump()
        {
            if (_doubleJumpSource == null || !_doubleJumpSource.isPlaying || _doubleJumpFading)
            {
                return;
            }

            _doubleJumpFading = true;
            _doubleJumpFadeStartVolume = _doubleJumpSource.volume;
            _doubleJumpFadeElapsed = 0f;
        }

        internal void StartWallRun()
        {
            if (_wallRunClips.Count == 0 || _wallRunSource.isPlaying)
            {
                return;
            }
            int clipIndex = PickRandomIndex(_wallRunClips.Count, ref _lastWallRunIndex);
            _wallRunSource.clip = _wallRunClips[clipIndex];
            _wallRunSource.volume = Mathf.Clamp01(_plugin.SoundVolume.Value);
            _wallRunSource.Play();
        }

        internal void StopWallRun()
        {
            if (_wallRunSource != null && _wallRunSource.isPlaying)
            {
                _wallRunSource.Stop();
            }
        }

        internal void StartSlide()
        {
            if (_slideClips.Count == 0)
            {
                return;
            }

            StopSlideImmediate();
            int clipIndex = PickRandomIndex(_slideClips.Count, ref _lastSlideIndex);
            _slideSource.clip = _slideClips[clipIndex];
            _slideSource.volume = Mathf.Clamp01(_plugin.SoundVolume.Value);
            _slideSource.Play();
        }

        internal void StopSlide()
        {
            if (_slideSource == null || !_slideSource.isPlaying || _slideFading)
            {
                return;
            }

            _slideFading = true;
            _slideFadeStartVolume = _slideSource.volume;
            _slideFadeElapsed = 0f;
        }

        internal void StopAllMovementSounds()
        {
            if (_jumpSource != null)
            {
                _jumpSource.Stop();
            }
            StopDoubleJumpImmediate();
            if (_wallRunSource != null)
            {
                _wallRunSource.Stop();
                _wallRunSource.clip = null;
            }
            StopSlideImmediate();
        }

        private void Update()
        {
            if (_slideFading && _slideSource != null)
            {
                _slideFadeElapsed += Time.deltaTime;
                float slideFade = 1f - Mathf.Clamp01(_slideFadeElapsed / SlideFadeDuration);
                _slideSource.volume = _slideFadeStartVolume * slideFade;
                if (slideFade <= 0f)
                {
                    StopSlideImmediate();
                }
            }

            if (_doubleJumpFading && _doubleJumpSource != null)
            {
                _doubleJumpFadeElapsed += Time.deltaTime;
                float doubleJumpFade = 1f - Mathf.Clamp01(_doubleJumpFadeElapsed / DoubleJumpFadeDuration);
                _doubleJumpSource.volume = _doubleJumpFadeStartVolume * doubleJumpFade;
                if (doubleJumpFade <= 0f)
                {
                    StopDoubleJumpImmediate();
                }
            }
        }

        private void StopSlideImmediate()
        {
            _slideFading = false;
            _slideFadeElapsed = 0f;
            if (_slideSource != null)
            {
                _slideSource.Stop();
                _slideSource.clip = null;
            }
        }

        private void StopDoubleJumpImmediate()
        {
            _doubleJumpFading = false;
            _doubleJumpFadeElapsed = 0f;
            if (_doubleJumpSource != null)
            {
                _doubleJumpSource.Stop();
                _doubleJumpSource.clip = null;
            }
        }

        private AudioSource CreateNonSpatialSource(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private void PlayRandom(AudioSource source, List<AudioClip> clips, ref int lastIndex)
        {
            if (source == null || clips.Count == 0)
            {
                return;
            }
            int clipIndex = PickRandomIndex(clips.Count, ref lastIndex);
            source.clip = clips[clipIndex];
            source.volume = Mathf.Clamp01(_plugin.SoundVolume.Value);
            source.Play();
        }

        private static int PickRandomIndex(int count, ref int lastIndex)
        {
            if (count <= 1)
            {
                lastIndex = 0;
                return 0;
            }

            if (lastIndex < 0 || lastIndex >= count)
            {
                lastIndex = UnityEngine.Random.Range(0, count);
                return lastIndex;
            }

            int candidate = UnityEngine.Random.Range(0, count - 1);
            if (candidate >= lastIndex)
            {
                candidate++;
            }
            lastIndex = candidate;
            return candidate;
        }

        private IEnumerator LoadAllClips()
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(TitanfallMovementPlugin).Assembly.Location);
            string soundDirectory = Path.Combine(pluginDirectory, "jump sounds");
            if (!Directory.Exists(soundDirectory))
            {
                soundDirectory = pluginDirectory;
            }

            string[] jumpFiles = { "jump1.wav", "jump2.wav", "jump3.wav" };
            string[] doubleJumpFiles = { "doublejump1.wav", "doublejump2.wav", "doublejump3.wav" };
            string[] wallRunFiles = { "wallrun1.wav", "wallrun2.wav", "wallrun3.wav" };
            string[] slideFiles = { "slide.wav", "slide2.wav", "slide3.wav" };

            yield return StartCoroutine(LoadGroup(soundDirectory, jumpFiles, AudioType.WAV, _jumpClips));
            yield return StartCoroutine(LoadGroup(soundDirectory, doubleJumpFiles, AudioType.WAV, _doubleJumpClips));
            yield return StartCoroutine(LoadGroup(soundDirectory, wallRunFiles, AudioType.WAV, _wallRunClips));
            yield return StartCoroutine(LoadGroup(soundDirectory, slideFiles, AudioType.WAV, _slideClips));

            TitanfallMovementPlugin.Logger.LogInfo(string.Format(
                "Movement audio loaded: {0} jump, {1} double-jump, {2} wall-run, {3} slide clips.",
                _jumpClips.Count,
                _doubleJumpClips.Count,
                _wallRunClips.Count,
                _slideClips.Count));
        }

        private IEnumerator LoadGroup(string directory, string[] fileNames, AudioType audioType, List<AudioClip> destination)
        {
            for (int i = 0; i < fileNames.Length; i++)
            {
                string path = Path.Combine(directory, fileNames[i]);
                if (!File.Exists(path))
                {
                    TitanfallMovementPlugin.Logger.LogWarning("Missing movement sound: " + path);
                    continue;
                }

                string uri = new Uri(path).AbsoluteUri;
                using (WWW request = new WWW(uri))
                {
                    yield return request;
                    if (!string.IsNullOrEmpty(request.error))
                    {
                        TitanfallMovementPlugin.Logger.LogWarning("Could not load " + fileNames[i] + ": " + request.error);
                        continue;
                    }

                    AudioClip clip = request.GetAudioClip(false, false, audioType);
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(fileNames[i]);
                        destination.Add(clip);
                    }
                }
            }
        }
    }
}
