using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [SerializeField]
    private AudioSource _bgm;

    [SerializeField]
    private AudioSource _sfx;
    public SO_SFXContainer Container;

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

        manager._bgm.clip = clip;
        manager._bgm.Play();
    }

    public static void BgmStop()
    {
        AudioManager manager = Instance;
        if (manager == null || manager._bgm == null)
            return;

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
}
// AudioManager은 오디오 설정 또는 재생 요청을 한곳에서 관리한다.
// 호출 측이 오디오 소스의 생성과 수명주기를 직접 다루지 않도록 재생 책임을 캡슐화한다.
