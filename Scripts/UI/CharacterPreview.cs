using UnityEngine;

public class CharacterPreview : MonoBehaviour
{
    [SerializeField]
    private Renderer[] _lodRenderers;

    [SerializeField]
    private SO_PlayerMaterial _playerMaterial;

    private void Awake()
    {
        PlayerLoadoutState.Instance.InitializeSkin(
            _playerMaterial.DefaultSkinIndex,
            _playerMaterial.AllSkin.Length
        );
        UpdateMaterial();
    }

    public void IncreaseMaterialIndex()
    {
        PlayerLoadoutState.Instance.ChangeSkinIndex(1, _playerMaterial.AllSkin.Length);
        UpdateMaterial();
    }

    public void DecreaseMaterialIndex()
    {
        PlayerLoadoutState.Instance.ChangeSkinIndex(-1, _playerMaterial.AllSkin.Length);
        UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        foreach (var renderer in _lodRenderers)
        {
            renderer.sharedMaterial = _playerMaterial.AllSkin[
                PlayerLoadoutState.Instance.SkinIndex
            ];
        }
    }
}
// CharacterPreview은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
