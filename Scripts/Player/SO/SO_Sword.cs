using UnityEngine;

[CreateAssetMenu(fileName = "Player_Sword", menuName = "Player/Sword")]
public class SO_Sword : ScriptableObject
{
    [System.Serializable]
    public struct MeshMaterialPair
    {
        public Mesh Mesh;
        public Material Material;
    }

    public MeshMaterialPair[] AllSwords;

    public int SwordIndex;

    private void OnEnable()
    {
        SwordIndex = 0;
    }
}
