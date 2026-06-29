using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class EnergyIndicator : MonoBehaviour
{
    [SerializeField]
    private EnergyTracker _energyTracker;

    private TMP_Text _energyText;

    private void Awake()
    {
        _energyTracker.OnEnergyChanged += OnEnergyChanged;
        _energyText = GetComponent<TMP_Text>();
    }

    private void OnEnergyChanged(float energy)
    {
        _energyText.SetText("{0:0}", energy);
    }

    private void OnValidate()
    {
        if (_energyTracker == null)
            EditorLog.LogError("EnergyTracker가 할당되지 않았습니다!!!", this);
    }
}
