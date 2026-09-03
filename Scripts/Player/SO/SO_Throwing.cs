using UnityEngine;

// SO_Throwing은 투척 액션의 동작 시간, 실행 시점, 초기 속도와 발사 각도를 에셋으로 제공한다.
// 실행 시점은 정규화된 값으로 저장하여 애니메이션 길이가 바뀌어도 같은 투척 흐름을 재사용한다.
[CreateAssetMenu(fileName = "Throwing_Stat", menuName = "Player/Stat/Throwing")]
public sealed class SO_Throwing : ScriptableObject
{
    [SerializeField, Min(0.05f)]
    private float _actionDuration = 0.35f;

    [SerializeField, Range(0f, 1f)]
    private float _executeTime = 0.5f;

    [SerializeField, Min(0f)]
    private float _throwSpeed = 30f;

    [SerializeField, Range(0f, 90f)]
    private float _throwAngle;

    public float ActionDuration => _actionDuration;
    public float ExecuteTime => _executeTime;
    public float ThrowSpeed => _throwSpeed;
    public float ThrowAngle => _throwAngle;

    private void OnValidate()
    {
        if (_actionDuration < 0.05f)
            EditorLog.LogError("투척 액션 지속시간은 0.05초 이상이어야 합니다.", this);

        if (_executeTime < 0f || _executeTime > 1f)
            EditorLog.LogError("투척 발동 지점은 0과 1 사이여야 합니다.", this);

        if (_throwSpeed < 0f)
            EditorLog.LogError("투척 속도는 0 이상이어야 합니다.", this);

        if (_throwAngle < 0f || _throwAngle > 90f)
            EditorLog.LogError("투척 각도는 0도와 90도 사이여야 합니다.", this);
    }
}
