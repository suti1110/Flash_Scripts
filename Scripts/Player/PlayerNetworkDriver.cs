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
