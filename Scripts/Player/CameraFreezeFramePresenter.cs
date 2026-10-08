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

    [SerializeField] private Canvas _canvas;
    [SerializeField] private RawImage _rawImage;
    private RenderTexture _capturedTexture;
    private bool _isFrozen;

    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// 지정된 카메라의 현재 프레임을 캡처하여 배경 RawImage에 표시하고 프리즈 상태로 진입합니다.
    /// </summary>
    /// <param name="targetCamera">캡처할 대상 메인 카메라</param>
    /// <returns>캡처 및 표시 성공 여부</returns>
    public bool CaptureAndFreeze(Camera targetCamera)
    {
        if (targetCamera == null)
            return false;

        if (_canvas == null || _rawImage == null) return false;

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
        ReleaseTexture();
    }

}
