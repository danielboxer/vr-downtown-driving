using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace UnityStandardAssets.Vehicles.Car
{
    [RequireComponent(typeof(CarController))]
    public class CarAudio : MonoBehaviour
    {
        // ────────────────────────  NEW  ────────────────────────
        [Header("Volume Sliders (0 = mute, 2 = max)")]
        [Range(0f, 2f)] public float engineVolume = 0.05f;
        [Range(0f, 2f)] public float turnSignalVolume = 1f;
        // ───────────────────────────────────────────────────────

        public enum EngineAudioOptions { Simple, FourChannel }
        public EngineAudioOptions engineSoundStyle = EngineAudioOptions.FourChannel;

        public AudioClip lowAccelClip;
        public AudioClip lowDecelClip;
        public AudioClip highAccelClip;
        public AudioClip highDecelClip;

        public float pitchMultiplier = 1f;
        public float lowPitchMin = 1f;
        public float lowPitchMax = 6f;
        public float highPitchMultiplier = 0.25f;
        public float maxRolloffDistance = 500;
        public float dopplerLevel = 1;
        public bool useDoppler = true;

        [Header("Horn Sound")]
        [Tooltip("Pool of short horn clips; a random one plays each press.")]
        public List<AudioClip> hornClips = new List<AudioClip>();
        [Range(0f, 2f)] public float hornVolume = 1f;

        [Header("Gear Change Sound")]
        [Tooltip("Clip played when the driver toggles between drive and reverse gear.")]
        public AudioClip gearChangeClip;
        [Range(0f, 2f)] public float gearChangeVolume = 1f;

        [Header("Turn Signal Sounds")]
        public AudioClip turnSignalToggleSound;
        public AudioClip turnSignalLoopSound;
        [Tooltip("Extra seconds to wait after the toggle click before the loop starts")]
        public float turnSignalLoopDelay = 0.1f;

        private AudioSource m_LowAccel, m_LowDecel, m_HighAccel, m_HighDecel;
        private AudioSource m_HornSource;
        private AudioSource m_GearChangeSource;
        private AudioSource m_TurnSignalToggleSource;
        private AudioSource m_TurnSignalLoopSource;
        private Coroutine m_LoopStartCoroutine;
        private bool m_StartedSound;
        private CarController m_CarController;

        /* ───────────────────────── helper funcs (unchanged) ───────────────────────── */

        private void StartSound()
        {
            m_CarController = GetComponent<CarController>();
            m_HighAccel = SetUpEngineAudioSource(highAccelClip);

            if (engineSoundStyle == EngineAudioOptions.FourChannel)
            {
                m_LowAccel = SetUpEngineAudioSource(lowAccelClip);
                m_LowDecel = SetUpEngineAudioSource(lowDecelClip);
                m_HighDecel = SetUpEngineAudioSource(highDecelClip);
            }
            m_StartedSound = true;
        }

        private void StopSound()
        {
            foreach (var src in GetComponents<AudioSource>()) Destroy(src);
            m_StartedSound = false;
        }

        /* ─────────────────────────── main update ─────────────────────────── */

        private Camera _cachedCam;

        private void Update()
        {
            if (_cachedCam == null) _cachedCam = Camera.main;
            if (_cachedCam == null) return;
            float camDistSqr = (_cachedCam.transform.position - transform.position).sqrMagnitude;
            float maxDistSqr = maxRolloffDistance * maxRolloffDistance;

            if (m_StartedSound && camDistSqr > maxDistSqr) StopSound();
            if (!m_StartedSound && camDistSqr < maxDistSqr) StartSound();

            if (!m_StartedSound) return;

            float pitch = Mathf.Min(lowPitchMax, ULerp(lowPitchMin, lowPitchMax, m_CarController.Revs));

            if (engineSoundStyle == EngineAudioOptions.Simple)
            {
                m_HighAccel.pitch = pitch * pitchMultiplier * highPitchMultiplier;
                m_HighAccel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                m_HighAccel.volume = 1f * engineVolume;
            }
            else
            {
                // four-channel calculations (unchanged) …
                float accFade = Mathf.Abs(m_CarController.AccelInput);
                float decFade = 1 - accFade;
                float highFade = Mathf.InverseLerp(0.2f, 0.8f, m_CarController.Revs);
                float lowFade = 1 - highFade;

                highFade = 1 - (1 - highFade) * (1 - highFade);
                lowFade = 1 - (1 - lowFade) * (1 - lowFade);
                accFade = 1 - (1 - accFade) * (1 - accFade);
                decFade = 1 - (1 - decFade) * (1 - decFade);

                m_LowAccel.pitch = pitch * pitchMultiplier;
                m_LowDecel.pitch = pitch * pitchMultiplier;
                m_HighAccel.pitch = pitch * highPitchMultiplier * pitchMultiplier;
                m_HighDecel.pitch = pitch * highPitchMultiplier * pitchMultiplier;

                m_LowAccel.volume = lowFade * accFade * engineVolume;
                m_LowDecel.volume = lowFade * decFade * engineVolume;
                m_HighAccel.volume = highFade * accFade * engineVolume;
                m_HighDecel.volume = highFade * decFade * engineVolume;

                float dop = useDoppler ? dopplerLevel : 0;
                m_LowAccel.dopplerLevel = dop;
                m_LowDecel.dopplerLevel = dop;
                m_HighAccel.dopplerLevel = dop;
                m_HighDecel.dopplerLevel = dop;
            }
        }

        /* ───────────────────── setup helper (unchanged) ───────────────────── */

        private AudioSource SetUpEngineAudioSource(AudioClip clip)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = 0;             // will be set in Update()
            src.loop = true;
            src.time = Random.Range(0f, clip.length);
            src.Play();
            src.minDistance = 5;
            src.maxDistance = maxRolloffDistance;
            src.dopplerLevel = 0;
            return src;
        }

        private static float ULerp(float from, float to, float value) =>
            (1f - value) * from + value * to;

        private void Start()
        {
            // Dedicated one-shot source for the player horn
            m_HornSource = gameObject.AddComponent<AudioSource>();
            m_HornSource.playOnAwake = false;
            m_HornSource.loop = false;
            m_HornSource.spatialBlend = 1f;

            // Dedicated one-shot source for gear change
            m_GearChangeSource = gameObject.AddComponent<AudioSource>();
            m_GearChangeSource.playOnAwake = false;
            m_GearChangeSource.loop = false;
            m_GearChangeSource.spatialBlend = 1f;

            // Dedicated one-shot source for the toggle click (activate/deactivate)
            m_TurnSignalToggleSource = gameObject.AddComponent<AudioSource>();
            m_TurnSignalToggleSource.playOnAwake = false;
            m_TurnSignalToggleSource.loop = false;
            m_TurnSignalToggleSource.spatialBlend = 1f;

            // Dedicated looping source for the blinker tick while active
            m_TurnSignalLoopSource = gameObject.AddComponent<AudioSource>();
            m_TurnSignalLoopSource.playOnAwake = false;
            m_TurnSignalLoopSource.loop = true;
            m_TurnSignalLoopSource.spatialBlend = 1f;
            m_TurnSignalLoopSource.clip = turnSignalLoopSound;
        }

        public void PlayHorn()
        {
            if (hornClips == null || hornClips.Count == 0) return;
            AudioClip clip = hornClips[Random.Range(0, hornClips.Count)];
            if (clip != null)
                m_HornSource.PlayOneShot(clip, hornVolume);
        }

        public void PlayGearChange()
        {
            if (gearChangeClip != null)
                m_GearChangeSource.PlayOneShot(gearChangeClip, gearChangeVolume);
        }

        private void PlayTurnSignalToggle()
        {
            if (turnSignalToggleSound != null)
            {
                m_TurnSignalToggleSource.PlayOneShot(turnSignalToggleSound, turnSignalVolume);
            }
        }

        public void PlayTurnSignalOnSound()
        {
            // Stop any running loop so switching signals resets it cleanly
            if (m_TurnSignalLoopSource != null && m_TurnSignalLoopSource.isPlaying)
                m_TurnSignalLoopSource.Stop();

            PlayTurnSignalToggle();
            // Delay loop start until the toggle click finishes (+ configurable extra buffer)
            if (m_LoopStartCoroutine != null) StopCoroutine(m_LoopStartCoroutine);
            float delay = (turnSignalToggleSound != null ? turnSignalToggleSound.length : 0f) + turnSignalLoopDelay;
            m_LoopStartCoroutine = StartCoroutine(StartLoopAfterDelay(delay));
        }

        private IEnumerator StartLoopAfterDelay(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            PlayTurnSignalLoop();
        }

        public void PlayTurnSignalLoop()
        {
            if (m_TurnSignalLoopSource != null && turnSignalLoopSound != null && !m_TurnSignalLoopSource.isPlaying)
            {
                m_TurnSignalLoopSource.volume = turnSignalVolume;
                m_TurnSignalLoopSource.Play();
            }
        }

        public void StopTurnSignalLoop()
        {
            // Cancel any pending loop-start coroutine so it doesn't restart after deactivation
            if (m_LoopStartCoroutine != null)
            {
                StopCoroutine(m_LoopStartCoroutine);
                m_LoopStartCoroutine = null;
            }

            if (m_TurnSignalLoopSource != null && m_TurnSignalLoopSource.isPlaying)
                m_TurnSignalLoopSource.Stop();

            PlayTurnSignalToggle();
        }
    }
}
