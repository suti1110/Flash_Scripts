using Unity.Netcode;
using UnityEngine;

public class PlayerSwordSync : NetworkBehaviour
{
    [Header("Renderer And MeshFilter")]
    [SerializeField]
    private Renderer _swordRenderer;

    [SerializeField]
    private MeshFilter _swordMeshFilter;

    [SerializeField]
    private SO_Sword _sword;

    private readonly NetworkVariable<int> _swordIndex = new();

    public override void OnNetworkSpawn()
    {
        UpdateSword(_swordIndex.Value);

        _swordIndex.OnValueChanged += OnSkinChanged;

        if (IsOwner)
            SetSkinIndexServerRpc(_sword.SwordIndex);
    }

    public override void OnNetworkDespawn()
    {
        _swordIndex.OnValueChanged -= OnSkinChanged;
    }

    [ServerRpc]
    private void SetSkinIndexServerRpc(int index)
    {
        _swordIndex.Value = index;
    }

    private void OnSkinChanged(int preValue, int newValue)
    {
        UpdateSword(newValue);
    }

    private void UpdateSword(int index)
    {
        if (index < 0 || index >= _sword.AllSwords.Length)
            return;

        _swordRenderer.sharedMaterial = _sword.AllSwords[index].Material;
        _swordMeshFilter.mesh = _sword.AllSwords[index].Mesh;
    }

    private void OnValidate()
    {
        if (!_swordRenderer)
        {
            EditorLog.LogError("Rederer가 입력되지 않았습니다!!!", this);
        }

        if (!_swordMeshFilter)
        {
            EditorLog.LogError("MeshFilter가 입력되지 않았습니다!!!", this);
        }

        if (!_sword)
        {
            EditorLog.LogError("SO_Sword가 입력되지 않았습니다!!!", this);
        }
    }
}
