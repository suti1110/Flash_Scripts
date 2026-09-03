using UnityEngine;

public class SwordPreview : MonoBehaviour
{
    [SerializeField]
    private Renderer _swordRenderer;

    [SerializeField]
    private MeshFilter _swordMeshFilter;

    [SerializeField]
    private SO_Sword _sword;

    private void Awake()
    {
        PlayerLoadoutState.Instance.InitializeSword(
            _sword.DefaultSwordIndex,
            _sword.AllSwords.Length
        );
        UpdateSword();
    }

    public void IncreaseIndex()
    {
        PlayerLoadoutState.Instance.ChangeSwordIndex(1, _sword.AllSwords.Length);
        UpdateSword();
    }

    public void DecreaseIndex()
    {
        PlayerLoadoutState.Instance.ChangeSwordIndex(-1, _sword.AllSwords.Length);
        UpdateSword();
    }

    private void UpdateSword()
    {
        int swordIndex = PlayerLoadoutState.Instance.SwordIndex;
        _swordRenderer.sharedMaterial = _sword.AllSwords[swordIndex].Material;
        _swordMeshFilter.mesh = _sword.AllSwords[swordIndex].Mesh;
    }
}
// SwordPreview은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
