using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 기존 레이스 씬의 참조를 유지하면서 영역 이탈 대미지와 개인 시작점 복귀를 담당한다.
// 대미지와 위치 변경은 서버만 확정하며, 클라이언트는 기존 네트워크 동기화 결과를 따른다.
public sealed class OutOfBoundsVolume : MonoBehaviour
{
    [SerializeField, Min(1)]
    private int _damage = 1;

    // 플레이어에 여러 Collider가 있어도 같은 이탈을 한 번만 처리하도록 겹친 수를 추적한다.
    private readonly Dictionary<PlayerDamage, int> _overlapCounts = new();

    private void OnTriggerEnter(Collider other)
    {
        if (!HasAuthority())
            return;

        PlayerDamage playerDamage = other.GetComponentInParent<PlayerDamage>();
        if (playerDamage == null)
            return;

        if (_overlapCounts.TryGetValue(playerDamage, out int overlapCount))
        {
            _overlapCounts[playerDamage] = overlapCount + 1;
            return;
        }

        _overlapCounts.Add(playerDamage, 1);
        HandlePlayerExitedBounds(playerDamage);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!HasAuthority())
            return;

        PlayerDamage playerDamage = other.GetComponentInParent<PlayerDamage>();
        if (playerDamage == null || !_overlapCounts.TryGetValue(playerDamage, out int overlapCount))
            return;

        if (overlapCount <= 1)
            _overlapCounts.Remove(playerDamage);
        else
            _overlapCounts[playerDamage] = overlapCount - 1;
    }

    private void OnDisable()
    {
        _overlapCounts.Clear();
    }

    private void HandlePlayerExitedBounds(PlayerDamage playerDamage)
    {
        if (!playerDamage.IsAlive)
            return;

        // 치명 피해는 PlayerDeath가 재스폰 또는 탈락을 이미 처리하므로 여기서 다시 이동시키지 않는다.
        // 영역 이탈도 대미지·넉백·경직 경로를 사용하지만 넉백과 경직 수치는 의도적으로 0이다.
        bool wasLethal = playerDamage.ApplyDamageOnServer(_damage, Vector3.zero, 0f);
        if (wasLethal)
            return;

        if (!playerDamage.TryGetComponent(out PlayerSpawnHandler spawnHandler))
        {
            EditorLog.LogError("영역 이탈 플레이어에 PlayerSpawnHandler가 없습니다.", playerDamage);
            return;
        }

        if (!spawnHandler.TryRespawnAtSpawnPoint())
            EditorLog.LogError(
                "영역 이탈 플레이어의 최초 스폰 지점을 찾을 수 없습니다.",
                playerDamage
            );
    }

    private static bool HasAuthority()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private void OnValidate()
    {
        if (_damage <= 0)
            EditorLog.LogError("영역 이탈 대미지는 1 이상이어야 합니다.", this);

        Collider[] colliders = GetComponents<Collider>();
        if (colliders.Length == 0)
        {
            EditorLog.LogError("RaceFloor에 Collider가 없습니다.", this);
            return;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i].isTrigger)
                return;
        }

        EditorLog.LogError(
            "RaceFloor의 Collider 중 하나는 Is Trigger가 활성화되어야 합니다.",
            this
        );
    }
}
