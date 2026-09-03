using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class InitAudioUI : MonoBehaviour
{
    [SerializeField]
    private AudioMixer _mixer;

    [SerializeField]
    private OnlyOneUnityString _name;

    private Slider _slider;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        if (_name != null && _mixer.GetFloat(_name, out float value))
        {
            _slider.SetValueWithoutNotify(ConvertToLinear(value));
        }
    }

    // 오디오 믹서의 데시벨(dB) 값을 선형적인 0~1 값으로 변환하는 역함수
    private float ConvertToLinear(float decibel)
    {
        // dB = 20 * Log10(V) 의 역산인 V = 10 ^ (dB / 20) 공식을 적용합니다.
        float linearVolume = Mathf.Pow(10f, decibel / 20f);

        // 0dB 이상이 들어와 1.0을 초과하거나, 매우 작은 dB가 들어와 음수가 되는 것을 방지하기 위해 0~1 사이로 클램핑합니다.
        // 원본 함수에서 0.0001f를 최소값으로 잡았으므로, 하한선을 0f 대신 0.0001f로 맞추셔도 무방합니다.
        return Mathf.Clamp(linearVolume, 0f, 1f);
    }
}
