using Unity.Netcode;
using UnityEngine;

public static class GameObjectExtensions
{
    /// <summary>
    /// 자식까지 레이어를 같이 바꿔주는 게임 오브젝트 확장 메서드
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="newLayer"></param>
    public static void SetLayerRecursively(this GameObject obj, int newLayer)
    {
        if (obj == null)
            return;

        Transform[] allChildren = obj.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            child.gameObject.layer = newLayer;
        }
    }
}

/// <summary>
/// Owner와 Others의 레이어를 구분하는 클래스
/// </summary>
public class PlayerNetworkLayer : NetworkBehaviour
{
    [SerializeField]
    private GameObject _target;

    [SerializeField, SingleLayer]
    private int _owner;

    [SerializeField, SingleLayer]
    private int _others;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            _target.SetLayerRecursively(_owner);
        else
            _target.SetLayerRecursively(_others);
    }
}
