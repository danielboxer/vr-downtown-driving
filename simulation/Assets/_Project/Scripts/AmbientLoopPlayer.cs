using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AmbientLoopPlayer : MonoBehaviour
{
    [Header("Playback")]
    [Tooltip("Clip played as 2D ambient audio.")]
    [SerializeField] private AudioClip ambienceClip;

    [Tooltip("Start playback automatically when the scene begins.")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("Keep replaying the clip after it reaches the end.")]
    [SerializeField] private bool loop = true;

    [Tooltip("Delay before playback begins, in seconds.")]
    [Min(0f)]
    [SerializeField] private float startDelay = 0f;

    [Tooltip("Fade in from silence over this many seconds.")]
    [Min(0f)]
    [SerializeField] private float fadeInDuration = 0f;

    [Tooltip("Playback volume for the ambient track.")]
    [Min(0f)]
    [SerializeField] private float volume = 0.2f;

    private AudioSource _audioSource;
    private Coroutine _playRoutine;

    private void Awake()
    {
        EnsureAudioSource();
        ApplySettings();
    }

    private void Start()
    {
        if (playOnStart)
            PlayAmbient();
    }

    private void OnDisable()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        if (_audioSource != null && _audioSource.isPlaying)
            _audioSource.Stop();
    }

    private void OnValidate()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();

        ApplySettings();
    }

    public void PlayAmbient()
    {
        if (!isActiveAndEnabled)
            return;

        EnsureAudioSource();
        ApplySettings();

        if (_playRoutine != null)
            StopCoroutine(_playRoutine);

        _playRoutine = StartCoroutine(PlayAmbientRoutine());
    }

    public void StopAmbient()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        if (_audioSource != null)
            _audioSource.Stop();
    }

    private IEnumerator PlayAmbientRoutine()
    {
        if (ambienceClip == null)
        {
            _playRoutine = null;
            yield break;
        }

        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        _audioSource.clip = ambienceClip;
        _audioSource.loop = loop;

        if (fadeInDuration > 0f)
        {
            _audioSource.volume = 0f;
            _audioSource.Play();

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                _audioSource.volume = Mathf.Lerp(0f, volume, Mathf.Clamp01(elapsed / fadeInDuration));
                yield return null;
            }
        }
        else
        {
            _audioSource.volume = volume;
            _audioSource.Play();
        }

        _audioSource.volume = volume;
        _playRoutine = null;
    }

    private void EnsureAudioSource()
    {
        if (_audioSource != null)
            return;

        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void ApplySettings()
    {
        if (_audioSource == null)
            return;

        _audioSource.playOnAwake = false;
        _audioSource.loop = loop;
        _audioSource.clip = ambienceClip;
        _audioSource.spatialBlend = 0f;
        _audioSource.volume = volume;
    }
}