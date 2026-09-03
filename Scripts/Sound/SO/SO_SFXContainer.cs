using UnityEngine;

[CreateAssetMenu(fileName = "SFXContainer", menuName = "Sound/SFXContainer")]
public class SO_SFXContainer : ScriptableObject
{
    [field: SerializeField]
    public AudioClip MouseClick { get; private set; }

    [field: SerializeField]
    public AudioClip Attack { get; private set; }

    [field: SerializeField]
    public AudioClip Hit { get; private set; }

    [field: SerializeField]
    public AudioClip Roulette { get; private set; }

    [field: SerializeField]
    public AudioClip Select { get; private set; }

    [Header("Player")]
    [field: SerializeField]
    public AudioClip Jump { get; private set; }

    [field: SerializeField]
    public AudioClip Land { get; private set; }

    [field: SerializeField]
    public AudioClip Death { get; private set; }

    [field: SerializeField]
    public AudioClip Respawn { get; private set; }

    [Header("Match")]
    [field: SerializeField]
    public AudioClip GameStart { get; private set; }

    [field: SerializeField]
    public AudioClip Victory { get; private set; }

    [field: SerializeField]
    public AudioClip Defeat { get; private set; }

    [field: SerializeField]
    public AudioClip Draw { get; private set; }

    [Header("UI")]
    [field: SerializeField]
    public AudioClip Warning { get; private set; }

    [field: SerializeField]
    public AudioClip Notification { get; private set; }

    [field: SerializeField]
    public AudioClip ChatReceive { get; private set; }
}
// SO_SFXContainer은 인스펙터에서 조정하는 게임 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
