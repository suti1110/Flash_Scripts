using UnityEngine;

/// <summary>
/// 씬이 바뀌어도 이 오브젝트와 자식 오브젝트를 유지합니다.
/// 이 컴포넌트가 붙은 오브젝트는 게임 전체에서 하나만 유지합니다.
/// Hierarchy의 최상위 오브젝트에 붙여 사용하세요.
/// </summary>
[DisallowMultipleComponent]
public class DontDestroyOnLoadObject : MonoBehaviour
{
    private static DontDestroyOnLoadObject _instance;

    private void Awake()
    {
        // 씬을 다시 로드해 새 오브젝트가 생기면, 기존 오브젝트를 유지합니다.
        if (_instance != null && _instance != this)
        {
            // Destroy는 프레임 끝에 처리되므로 Volume 효과는 즉시 비활성화합니다.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        _instance = this;
        // DontDestroyOnLoad는 부모가 없는 최상위 오브젝트에 적용해야 합니다.
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // 중복 오브젝트가 제거될 때 기존 인스턴스의 참조를 지우지 않습니다.
        if (_instance == this)
            _instance = null;
    }
}
