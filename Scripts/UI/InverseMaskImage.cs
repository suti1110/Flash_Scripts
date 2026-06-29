using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class InverseMaskImage : Image
{
    private Material materialCache;

    public override Material materialForRendering
    {
        get
        {
            if (materialCache == null)
            {
                materialCache = new Material(base.materialForRendering);

                materialCache.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
            }

            return materialCache;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // 메모리 누수 방지
        if (materialCache != null)
        {
            // 1. 실제 게임 플레이 중일 때는 안전한 Destroy 사용
            if (Application.isPlaying)
            {
                Destroy(materialCache);
            }
            // 2. 에디터 모드(플레이 대기/정지 상태)일 때는 DestroyImmediate 사용
            else
            {
                DestroyImmediate(materialCache);
            }
        }
    }
}
