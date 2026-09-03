using UnityEngine;

[CreateAssetMenu(fileName = "PlayerThrowingState", menuName = "Flash/Player/States/Throwing")]
public sealed class PlayerThrowingState : PlayerState
{
    // 서버 승인 RPC보다 공격 State 진입이 먼저 끝난 경우에도 늦게 도착한 투척이 공격을 덮어쓰지 않게 한다.
    public override bool CanEnterFrom(PlayerState previousState)
    {
        return previousState is not PlayerAttackingState;
    }
}

// PlayerThrowingState는 PlayerAttackingState처럼 투척 중 입력 및 애니메이션 표현 정책만 정의한다.
// 액션 시간과 실제 투척 실행은 PlayerMapInteraction이 소유하여 State 에셋에 런타임 진행 상태를 두지 않는다.
