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
// EnergyIndicator은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
