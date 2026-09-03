using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "Sound_Controller", menuName = "Sound/Controller")]
public class SO_SoundController : ScriptableObject
{
    [SerializeField]
    private AudioMixer _mixer;

    [Header("노출 파라미터")]
    [SerializeField]
    private OnlyOneUnityString _masterParameter;

    [SerializeField]
    private OnlyOneUnityString _bgmParameter;

    [SerializeField]
    private OnlyOneUnityString _sfxParameter;

    [SerializeField]
    private float _minVolume;

    [SerializeField]
    private float _maxVolume;

    public void SetMasterVolume(float volume)
    {
        SetVolume(_masterParameter, volume);
    }

    public void SetBGMVolume(float volume)
    {
        SetVolume(_bgmParameter, volume);
    }

    public void SetSFXVolume(float volume)
    {
        SetVolume(_sfxParameter, volume);
    }

    private void SetVolume(OnlyOneUnityString parameter, float volume)
    {
        if (_mixer == null || parameter == null)
            return;

        _mixer.SetFloat(parameter, ConvertToDecibel(volume));
    }

    // 선형적인 0~1 값을 오디오 믹서용 데시벨(dB)로 변환하는 핵심 함수
    private float ConvertToDecibel(float linearVolume)
    {
        // 0이 들어오면 Log10 연산 시 -Infinity가 되어 믹서가 고장나므로, 아주 작은 값으로 클램핑합니다.
        float clampedVolume = Mathf.Clamp(linearVolume, 0.0001f, 1f);

        // 1.0일 때 0dB(최대 볼륨), 0.0001일 때 -80dB(완전 음소거)로 변환되는 공식입니다.
        return Mathf.Log10(clampedVolume) * 20f;
    }
}
