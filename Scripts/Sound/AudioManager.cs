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
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public static void BgmPlay(AudioClip clip)
    {
        _instance._bgm.clip = clip;
        _instance._bgm.Play();
    }

    public static void SfxPlay(AudioClip clip)
    {
        _instance._sfx.PlayOneShot(clip);
    }
}
