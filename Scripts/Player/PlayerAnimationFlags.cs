using System;
using UnityEngine;

/// <summary>
/// State가 사용할 애니메이션 레이어를 선별하는 비트 마스크 플래그.
/// </summary>
[Flags]
public enum PlayerAnimationLayerMask
{
    None = 0,
    Base = 1 << 0,   // Layer 0 (전신/하체 기본 상태)
    Action = 1 << 1, // Layer 1 (상체 액션 오버라이드)
}

/// <summary>
/// Base Layer (Layer 0) - 플레이어의 전신/하체 기본 상태 플래그.
/// 생성자 없이 오직 유니티 인스펙터 직렬화를 통해서만 초기화된다.
/// </summary>
[Serializable]
public struct PlayerBaseAnimationFlags
{
    [SerializeField]
    private bool _isTakingDamage;

    [SerializeField]
    private bool _isTrapped;

    [SerializeField]
    private bool _isTrapJumping;

    [SerializeField]
    private bool _keepSkillPresentation;

    public bool IsTakingDamage => _isTakingDamage;
    public bool IsTrapped => _isTrapped;
    public bool IsTrapJumping => _isTrapJumping;
    public bool KeepSkillPresentation => _keepSkillPresentation;

    // 자신의 Base Layer 파라미터들을 스스로 적용하는 직속 메서드 (정보 전문가 패턴 및 기능적 응집)
    public void ApplyAnimation(Action<int, bool> setBool)
    {
        if (setBool == null)
            return;

        setBool(PlayerAnimationHash.IsTakingDamage, _isTakingDamage);
        setBool(PlayerAnimationHash.IsTrapped, _isTrapped);
        setBool(PlayerAnimationHash.IsTrapJumping, _isTrapJumping);
    }
}

/// <summary>
/// Action Layer (Layer 1) - 상체 오버라이드 액션 플래그 (공격, 투척 등).
/// 생성자 없이 오직 유니티 인스펙터 직렬화를 통해서만 초기화된다.
/// </summary>
[Serializable]
public struct PlayerActionAnimationFlags
{
    [SerializeField]
    private bool _isAttacking;

    [SerializeField]
    private bool _isThrowing;

    public bool IsAttacking => _isAttacking;
    public bool IsThrowing => _isThrowing;

    // 자신의 Action Layer 파라미터들을 스스로 적용하는 직속 메서드 (정보 전문가 패턴 및 기능적 응집)
    public void ApplyAnimation(Action<int, bool> setBool)
    {
        if (setBool == null)
            return;

        setBool(PlayerAnimationHash.IsAttacking, _isAttacking);
        setBool(PlayerAnimationHash.IsThrowing, _isThrowing);
    }
}
