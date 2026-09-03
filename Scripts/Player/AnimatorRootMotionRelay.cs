using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class AnimatorRootMotionRelay : MonoBehaviour
{
    private Animator _animator;
    private PlayerSkillMotion _receiver;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void Bind(PlayerSkillMotion receiver)
    {
        _receiver = receiver;
    }

    public void Unbind(PlayerSkillMotion receiver)
    {
        if (_receiver == receiver)
            _receiver = null;
    }

    private void OnAnimatorMove()
    {
        if (_animator == null)
            return;

        if (_receiver != null)
        {
            _receiver.TryConsumeRootMotionDelta(_animator.deltaPosition, _animator.deltaRotation);
        }
    }
}
// AnimatorRootMotionRelay은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
