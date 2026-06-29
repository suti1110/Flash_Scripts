using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(fileName = "TakingDamage_Stat", menuName = "Player/Stat/TakingDamage")]
public class SO_TakingDamage : ScriptableObject
{
    [field: SerializeField]
    public int MaxHp { get; private set; } = 3;
}
