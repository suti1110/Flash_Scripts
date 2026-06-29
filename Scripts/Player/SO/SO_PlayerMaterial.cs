using UnityEngine;

[CreateAssetMenu(fileName = "Player_Material", menuName = "Player/Material")]
public class SO_PlayerMaterial : ScriptableObject
{
    public Material[] AllSkin;
    public int PlayerMaterialIndex { get; set; }

    private void OnEnable()
    {
        PlayerMaterialIndex = 0;
    }
}
