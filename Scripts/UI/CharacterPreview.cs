using UnityEngine;

public class CharacterPreview : MonoBehaviour
{
    [SerializeField]
    private Renderer[] _lodRenderers;

    [SerializeField]
    private SO_PlayerMaterial _playerMaterial;

    private void Awake()
    {
        UpdateMaterial();
    }

    public void IncreaseMaterialIndex()
    {
        _playerMaterial.PlayerMaterialIndex =
            (_playerMaterial.PlayerMaterialIndex + 1) % _playerMaterial.AllSkin.Length;
        UpdateMaterial();
    }

    public void DecreaseMaterialIndex()
    {
        _playerMaterial.PlayerMaterialIndex =
            (_playerMaterial.PlayerMaterialIndex - 1 + _playerMaterial.AllSkin.Length)
            % _playerMaterial.AllSkin.Length;
        UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        foreach (var renderer in _lodRenderers)
        {
            renderer.sharedMaterial = _playerMaterial.AllSkin[_playerMaterial.PlayerMaterialIndex];
        }
    }
}
