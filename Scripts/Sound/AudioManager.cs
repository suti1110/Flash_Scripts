using DG.Tweening;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [SerializeField]
    private AudioSource _bgm;

    [SerializeField]
    private AudioSource _sfx;
    public SO_SFXContainer Container;

    private Tween _bgmDuckTween;
    private float _bgmNormalVolume = 1f;
    private bool _sharedSfxPausedForHitStop;
    private bool _sharedSfxWasPlaying;

    private static AudioManager _instance;
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<AudioManager>();

                if (_instance == null)
                {
                    EditorLog.LogError("AudioManager가 없습니다!!!");
                }
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null || _instance == this)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (_bgm != null)
                _bgmNormalVolume = _bgm.volume;

            if (_sfx != null)
                _sfx.ignoreListenerPause = true;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public static void BgmPlay(AudioClip clip)
    {
        AudioManager manager = Instance;
        if (manager == null || manager._bgm == null || clip == null)
            return;

        manager.CancelBgmDuck(true);
        manager._bgm.clip = clip;
        manager._bgm.Play();
    }

    public static void BgmStop()
    {
        AudioManager manager = Instance;
        if (manager == null || manager._bgm == null)
            return;

        manager.CancelBgmDuck(true);
        manager._bgm.Stop();
        manager._bgm.clip = null;
    }

    public static void SfxPlay(AudioClip clip)
    {
        AudioManager manager = Instance;
        if (manager == null || manager._sfx == null || clip == null)
            return;

        manager._sfx.PlayOneShot(clip);
    }

    public static void SfxPlayAtPoint(
        AudioClip clip,
        Vector3 position,
        float volume = 1f,
        float minDistance = 1f,
        float maxDistance = 30f
    )
    {
        AudioManager manager = Instance;
        if (manager == null || clip == null)
            return;

        GameObject audioObject = new($"SFX_{clip.name}");
        audioObject.transform.position = position;

        AudioSource source = audioObject.AddComponent<AudioSource>();
        manager.ConfigureSpatialSource(source, minDistance, maxDistance);
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.Play();
        Destroy(audioObject, clip.length + 0.1f);
    }

    public static void ConfigureSpatialSfxSource(
        AudioSource source,
        float minDistance = 1f,
        float maxDistance = 30f
    )
    {
        AudioManager manager = Instance;
        if (manager == null || source == null)
            return;

        manager.ConfigureSpatialSource(source, minDistance, maxDistance);
    }

    public static void ConfigureListenerSfxSource(AudioSource source)
    {
        AudioManager manager = Instance;
        if (manager == null || source == null)
            return;

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;

        if (manager._sfx != null)
            source.outputAudioMixerGroup = manager._sfx.outputAudioMixerGroup;
    }

    public static void PauseSharedSfxForHitStop()
    {
        AudioManager manager = Instance;
        if (manager == null || manager._sfx == null || manager._sharedSfxPausedForHitStop)
            return;

        manager._sharedSfxWasPlaying = manager._sfx.isPlaying;
        manager._sharedSfxPausedForHitStop = true;
        manager._sfx.Pause();
    }

    public static void ResumeSharedSfxAfterHitStop()
    {
        AudioManager manager = Instance;
        if (manager == null || manager._sfx == null || !manager._sharedSfxPausedForHitStop)
            return;

        if (manager._sharedSfxWasPlaying)
            manager._sfx.UnPause();

        manager._sharedSfxWasPlaying = false;
        manager._sharedSfxPausedForHitStop = false;
    }

    public static void PlayListenerSfxWithBgmDuck(
        AudioClip clip,
        AudioSource listenerSource,
        float sfxVolume,
        float duckVolumeRatio,
        float duckDuration,
        float recoveryDuration
    )
    {
        AudioManager manager = Instance;
        if (manager == null || clip == null || listenerSource == null)
            return;

        ConfigureListenerSfxSource(listenerSource);
        listenerSource.Stop();
        listenerSource.clip = clip;
        listenerSource.volume = Mathf.Clamp01(sfxVolume);
        listenerSource.Play();

        manager.StartBgmDuck(
            clip.length / Mathf.Max(0.01f, Mathf.Abs(listenerSource.pitch)),
            duckVolumeRatio,
            duckDuration,
            recoveryDuration
        );
    }

    private void ConfigureSpatialSource(AudioSource source, float minDistance, float maxDistance)
    {
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance, maxDistance);

        if (_sfx != null)
            source.outputAudioMixerGroup = _sfx.outputAudioMixerGroup;
    }

    private void StartBgmDuck(
        float sfxDuration,
        float duckVolumeRatio,
        float duckDuration,
        float recoveryDuration
    )
    {
        if (_bgm == null)
            return;

        _bgmDuckTween?.Kill();

        float fadeDownDuration = Mathf.Max(0.01f, duckDuration);
        float holdDuration = Mathf.Max(0f, sfxDuration - fadeDownDuration);
        float targetVolume = _bgmNormalVolume * Mathf.Clamp01(duckVolumeRatio);

        Sequence sequence = DOTween.Sequence();
        sequence.SetTarget(this);
        sequence.SetUpdate(true);
        sequence.Append(
            DOTween
                .To(() => _bgm.volume, value => _bgm.volume = value, targetVolume, fadeDownDuration)
                .SetEase(Ease.OutQuad)
        );
        sequence.AppendInterval(holdDuration);
        sequence.Append(
            DOTween
                .To(
                    () => _bgm.volume,
                    value => _bgm.volume = value,
                    _bgmNormalVolume,
                    Mathf.Max(0.01f, recoveryDuration)
                )
                .SetEase(Ease.InOutSine)
        );
        _bgmDuckTween = sequence.OnComplete(() => _bgmDuckTween = null);
    }

    private void CancelBgmDuck(bool restoreVolume)
    {
        _bgmDuckTween?.Kill();
        _bgmDuckTween = null;

        if (restoreVolume && _bgm != null)
            _bgm.volume = _bgmNormalVolume;
    }

    private void OnDestroy()
    {
        if (_instance != this)
            return;

        CancelBgmDuck(true);
        _sharedSfxPausedForHitStop = false;
        _sharedSfxWasPlaying = false;
        _instance = null;
    }
}
// AudioManager은 오디오 설정 또는 재생 요청을 한곳에서 관리한다.
// 호출 측이 오디오 소스의 생성과 수명주기를 직접 다루지 않도록 재생 책임을 캡슐화한다.
