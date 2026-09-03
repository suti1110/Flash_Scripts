using Unity.Netcode;
using UnityEngine;

public class PlayerNetworkDriver : NetworkBehaviour
{
    [SerializeField]
    private Object[] _ownerOnlyObjects;

    [SerializeField]
    private Behaviour[] _disableOnOtherPlayer;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            foreach (var obj in _ownerOnlyObjects)
            {
                Destroy(obj);
            }

            foreach (var behaviour in _disableOnOtherPlayer)
            {
                behaviour.enabled = false;
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
// PlayerNetworkDriver은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
