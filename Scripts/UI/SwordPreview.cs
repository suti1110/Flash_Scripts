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
        UpdateSword();
    }

    public void IncreaseIndex()
    {
        _sword.SwordIndex = (_sword.SwordIndex + 1) % _sword.AllSwords.Length;
        UpdateSword();
    }

    public void DecreaseIndex()
    {
        _sword.SwordIndex =
            (_sword.SwordIndex - 1 + _sword.AllSwords.Length) % _sword.AllSwords.Length;
        UpdateSword();
    }

    private void UpdateSword()
    {
        _swordRenderer.sharedMaterial = _sword.AllSwords[_sword.SwordIndex].Material;
        _swordMeshFilter.mesh = _sword.AllSwords[_sword.SwordIndex].Mesh;
    }
}
