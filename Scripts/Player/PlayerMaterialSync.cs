using Unity.Netcode;
using UnityEngine;

public class PlayerMaterialSync : NetworkBehaviour
{
    [Header("Renderer And Material")]
    [SerializeField]
    private Renderer[] _renderers;

    [SerializeField]
    private SO_PlayerMaterial _playerMaterial; // 모든 스킨과 현재 플레이어의 스킨 정보가 저장된 SO

    private readonly NetworkVariable<int> _skinIndex = new();

    public override void OnNetworkSpawn()
    {
        UpdateSkin(_skinIndex.Value);

        _skinIndex.OnValueChanged += OnSkinChanged;

        if (IsOwner)
            SetSkinIndexServerRpc(_playerMaterial.PlayerMaterialIndex);
    }

    public override void OnNetworkDespawn()
    {
        _skinIndex.OnValueChanged -= OnSkinChanged;
    }

    [ServerRpc]
    private void SetSkinIndexServerRpc(int index)
    {
        _skinIndex.Value = index;
    }

    private void OnSkinChanged(int preValue, int newValue)
    {
        UpdateSkin(newValue);
    }

    private void UpdateSkin(int index)
    {
        if (index < 0 || index >= _playerMaterial.AllSkin.Length)
            return;

        foreach (Renderer renderer in _renderers)
        {
            renderer.sharedMaterial = _playerMaterial.AllSkin[index];
        }
    }

    private void OnValidate()
    {
        if (_renderers.Length == 0)
        {
            EditorLog.LogError("Rederer[]가 입력되지 않았습니다!!!", this);
        }

        if (!_playerMaterial)
        {
            EditorLog.LogError("SO_PlayerMaterial이 입력되지 않았습니다!!!", this);
        }
    }
}
