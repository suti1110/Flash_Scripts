using System;

[Flags]
public enum PlayerInputType
{
    None = 0,
    Moving = 1 << 0,
    Jumping = 1 << 1,
    Attacking = 1 << 2,
    UsingSkill = 1 << 3,
    All = ~0
}
