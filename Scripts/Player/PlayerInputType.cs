using System;

[Flags]
public enum PlayerInputType
{
    None = 0,
    Moving = 1 << 0,
    Jumping = 1 << 1,
    Attacking = 1 << 2,
    UsingSkill = 1 << 3,
    Interacting = 1 << 4,
    All = ~0,
}
// PlayerInputType은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
