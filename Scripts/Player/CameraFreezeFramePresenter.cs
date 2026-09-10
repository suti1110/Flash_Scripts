using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 공격 적중 등의 히트스톱(HitStop) 발생 시 카메라 3D 화면을 단일 렌더 요청(SingleCameraRequest)으로 캡처하여
/// UI 뒤쪽 배경 레이어(RawImage)에 띄우고, 카메라 비활성화 시에도 UI가 정상 렌더링되도록 돕는 프리젠터입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraFreezeFramePresenter : MonoBehaviour
{
    [SerializeField, InspectorName("프리즈 프레임 캔버스 정렬 순서")]
    private int _canvasSortingOrder = -100;

    private Canvas _canvas;
    private RawImage _rawImage;
    private RenderTexture _capturedTexture;
    private bool _isFrozen;

    public bool IsFrozen => _isFrozen;

    private void Awake()
    {
        EnsureUIInitialized();
    }

    /// <summary>
    /// 지정된 카메라의 현재 프레임을 캡처하여 배경 RawImage에 표시하고 프리즈 상태로 진입합니다.
    /// </summary>
    /// <param name="targetCamera">캡처할 대상 메인 카메라</param>
    /// <returns>캡처 및 표시 성공 여부</returns>
    public bool CaptureAndFreeze(Camera targetCamera)
    {
        if (targetCamera == null)
            return false;

        EnsureUIInitialized();

        // 기존에 캡처 중이던 텍스처가 있다면 먼저 안전하게 반환합니다.
        ReleaseTexture();

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        RenderTextureFormat format = targetCamera.allowHDR
            ? RenderTextureFormat.DefaultHDR
            : RenderTextureFormat.ARGB32;

        RenderTextureDescriptor descriptor = new(width, height, format, 24)
        {
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
            msaaSamples = 1,
        };

        _capturedTexture = RenderTexture.GetTemporary(descriptor);
        _capturedTexture.name = "FreezeFrame_RenderTexture";
        _capturedTexture.filterMode = FilterMode.Bilinear;
        _capturedTexture.wrapMode = TextureWrapMode.Clamp;

        // URP의 SingleCameraRequest를 통해 단일 카메라 렌더링만 텍스처로 요청합니다.
        UniversalRenderPipeline.SingleCameraRequest request = new()
        {
            destination = _capturedTexture,
        };

        if (RenderPipeline.SupportsRenderRequest(targetCamera, request))
        {
            RenderPipeline.SubmitRenderRequest(targetCamera, request);
            _rawImage.texture = _capturedTexture;
            _canvas.gameObject.SetActive(true);
            _isFrozen = true;
            return true;
        }

        EditorLog.LogError(
            "UniversalRenderPipeline.SingleCameraRequest 렌더 요청이 지원되지 않습니다.",
            this
        );
        ReleaseTexture();
        return false;
    }

    /// <summary>
    /// 프리즈 프레임 표시를 종료하고 임시 RenderTexture를 풀에 반환합니다.
    /// </summary>
    public void EndFreeze()
    {
        if (!_isFrozen)
            return;

        if (_canvas != null)
            _canvas.gameObject.SetActive(false);

        if (_rawImage != null)
            _rawImage.texture = null;

        ReleaseTexture();
        _isFrozen = false;
    }

    private void EnsureUIInitialized()
    {
        if (_canvas != null)
            return;

        // 씬/프리팹을 오염시키지 않도록 런타임에 전용 오버레이 캔버스를 동적 생성합니다.
        GameObject canvasObject = new("HitStop_FreezeFrameCanvas");
        canvasObject.transform.SetParent(transform, false);

        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        // 다른 인게임 UI(기본 sortingOrder 0 이상)보다 뒤에 위치하도록 낮은 값을 지정합니다.
        _canvas.sortingOrder = _canvasSortingOrder;

        GameObject imageObject = new(
            "FreezeFrame_RawImage",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        imageObject.transform.SetParent(canvasObject.transform, false);

        _rawImage = imageObject.GetComponent<RawImage>();
        _rawImage.raycastTarget = false; // 마우스 클릭 등 UI 레이캐스트 차단 방지

        RectTransform rect = _rawImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        canvasObject.SetActive(false);
    }

    private void ReleaseTexture()
    {
        if (_capturedTexture != null)
        {
            RenderTexture.ReleaseTemporary(_capturedTexture);
            _capturedTexture = null;
        }
    }

    private void OnDisable()
    {
        EndFreeze();
    }

    private void OnDestroy()
    {
        EndFreeze();
        if (_canvas != null && _canvas.gameObject != null)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
    }

    private void OnValidate()
    {
        if (_canvasSortingOrder >= 0)
        {
            EditorLog.LogError(
                "FreezeFrame 캔버스의 SortingOrder는 일반 UI 뒤에 배치되도록 0 미만이어야 합니다.",
                this
            );
        }
    }
}
