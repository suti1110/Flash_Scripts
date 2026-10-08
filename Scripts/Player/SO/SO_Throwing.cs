using UnityEngine;

// SO_Throwing은 투척 액션의 동작 시간, 충격량과 발사 각도를 에셋으로 제공한다.
[CreateAssetMenu(fileName = "Throwing_Stat", menuName = "Player/Stat/Throwing")]
public sealed class SO_Throwing : ScriptableObject
{
    [SerializeField, Min(0.05f)]
    private float _actionDuration = 0.35f;

    [SerializeField, Range(0f, 1f)]
    [Tooltip("우클릭을 누르는 동안 이 지점 직전까지 애니메이션을 재생하고 대기합니다. 버튼을 놓으면 이 지점에서 즉시 투척합니다.")]
    private float _executeTime = 0.5f;

    [SerializeField, Min(0f)]
    [Tooltip("투척 충격량입니다. 실제 초기 속도는 이 값을 물체 질량으로 나눈 값입니다.")]
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
            EditorLog.LogError("투척 충격량은 0 이상이어야 합니다.", this);

        if (_throwAngle < 0f || _throwAngle > 90f)
            EditorLog.LogError("투척 각도는 0도와 90도 사이여야 합니다.", this);
    }
}
