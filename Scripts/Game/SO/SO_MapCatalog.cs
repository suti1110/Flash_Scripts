using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MapCatalog", menuName = "ScriptableObjects/게임/맵 카탈로그")]
public sealed class SO_MapCatalog : ScriptableObject
{
    [SerializeField, HideInInspector]
    private List<SO_MapDefinition> _maps = new();

    public IReadOnlyList<SO_MapDefinition> Maps => _maps;

    public void CollectPracticeModes(List<SO_GameModeDefinition> results)
    {
        results.Clear();

        for (int i = 0; i < _maps.Count; i++)
        {
            SO_MapDefinition map = _maps[i];
            if (
                map == null
                || !map.AvailableInPractice
                || map.Mode == null
                || results.Contains(map.Mode)
            )
                continue;

            results.Add(map.Mode);
        }
    }

    public void CollectPracticeMaps(SO_GameModeDefinition mode, List<SO_MapDefinition> results)
    {
        results.Clear();

        for (int i = 0; i < _maps.Count; i++)
        {
            SO_MapDefinition map = _maps[i];
            if (map != null && map.AvailableInPractice && map.Mode == mode)
                results.Add(map);
        }
    }

    public void CollectOnlineModes(int playerCount, List<SO_GameModeDefinition> results)
    {
        results.Clear();

        for (int i = 0; i < _maps.Count; i++)
        {
            SO_MapDefinition map = _maps[i];
            if (
                !IsAvailableOnline(map, playerCount)
                || map.Mode == null
                || results.Contains(map.Mode)
            )
                continue;

            results.Add(map.Mode);
        }
    }

    public void CollectOnlineMaps(
        SO_GameModeDefinition mode,
        int playerCount,
        List<SO_MapDefinition> results
    )
    {
        results.Clear();

        for (int i = 0; i < _maps.Count; i++)
        {
            SO_MapDefinition map = _maps[i];
            if (IsAvailableOnline(map, playerCount) && map.Mode == mode)
                results.Add(map);
        }
    }

    public void CollectOnlineMaps(string modeId, int playerCount, List<SO_MapDefinition> results)
    {
        results.Clear();

        for (int i = 0; i < _maps.Count; i++)
        {
            SO_MapDefinition map = _maps[i];
            if (
                IsAvailableOnline(map, playerCount)
                && map.Mode != null
                && string.Equals(map.Mode.Id, modeId, StringComparison.Ordinal)
            )
            {
                results.Add(map);
            }
        }
    }

    private static bool IsAvailableOnline(SO_MapDefinition map, int playerCount)
    {
        return map != null
            && map.AvailableInOnline
            && map.HasScene
            && map.SupportsPlayerCount(playerCount);
    }

#if UNITY_EDITOR
    public bool EditorSetMaps(IReadOnlyList<SO_MapDefinition> maps)
    {
        if (_maps.Count == maps.Count)
        {
            bool isSame = true;
            for (int i = 0; i < _maps.Count; i++)
            {
                if (_maps[i] == maps[i])
                    continue;

                isSame = false;
                break;
            }

            if (isSame)
                return false;
        }

        _maps.Clear();
        for (int i = 0; i < maps.Count; i++)
            _maps.Add(maps[i]);

        return true;
    }
#endif
}
// SO_MapCatalog은 인스펙터에서 조정하는 게임 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
