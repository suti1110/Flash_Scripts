using UnityEngine;

[CreateAssetMenu(fileName = "SwordStats", menuName = "Player/Stat/Sword")]
public sealed class SO_SwordStats : ScriptableObject
{
    [field: SerializeField, Min(0.01f), InspectorName("기본 검 크기")]
    public float SwordSize { get; private set; } = 1f;

    // 공유 에셋에는 검 인스턴스의 자세를 저장하지 않는다. 손의 원점에 있던 메시 지점을
    // 스케일 변경 뒤에도 같은 위치에 두어 손잡이가 손에서 밀려나지 않게 한다.
    public float GetSwordSize(float multiplier) => SwordSize * Mathf.Max(0.01f, multiplier);

    public void ApplySwordSize(
        Transform sword, Vector3 originalLocalScale, Vector3 gripLocalPoint, float multiplier
    )
    {
        if (sword == null)
            return;

        sword.localScale = originalLocalScale * GetSwordSize(multiplier);
        if (sword.parent != null)
            sword.localPosition = -(sword.localRotation * Vector3.Scale(
                sword.localScale, gripLocalPoint
            ));
    }

    private void OnValidate()
    {
        if (SwordSize <= 0f)
            EditorLog.LogError("기본 검 크기는 0보다 커야 합니다.", this);
    }
}
