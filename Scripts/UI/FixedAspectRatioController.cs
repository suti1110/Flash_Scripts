using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps the player-facing display at 16:9 and covers any unused screen area with black bars.
/// The controller creates itself at runtime, so scenes and prefabs do not require extra setup.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class FixedAspectRatioController : MonoBehaviour
{
    private const float TargetAspectRatio = 16f / 9f;
    private const int MaskSortingOrder = short.MaxValue;
    private const float CanvasScanInterval = 0.5f;

    private static FixedAspectRatioController _instance;

    private readonly Dictionary<Canvas, OverlayCanvasLayout> _overlayCanvasLayouts = new();
    private readonly List<Canvas> _staleCanvases = new();

    private Rect _viewportRect = new(0f, 0f, 1f, 1f);
    private Rect _previousViewportRect = new(0f, 0f, 1f, 1f);
    private int _screenWidth;
    private int _screenHeight;
    private Image _leftBar;
    private Image _rightBar;
    private Image _bottomBar;
    private Image _topBar;
    private Transform _screenMaskTransform;
    private Canvas _screenMaskCanvas;
    private float _nextCanvasScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateController()
    {
        if (_instance != null)
            return;

        GameObject controllerObject = new(nameof(FixedAspectRatioController));
        _instance = controllerObject.AddComponent<FixedAspectRatioController>();
        DontDestroyOnLoad(controllerObject);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        CreateScreenMask();
        RefreshViewport();
        RefreshOverlayCanvases();
    }

    private void OnEnable()
    {
        Camera.onPreCull += ApplyViewportToCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Update()
    {
        if (_screenWidth != Screen.width || _screenHeight != Screen.height)
            RefreshViewport();

        if (Time.unscaledTime >= _nextCanvasScanTime)
        {
            RefreshOverlayCanvases();
            _nextCanvasScanTime = Time.unscaledTime + CanvasScanInterval;
        }
    }

    private void OnDisable()
    {
        Camera.onPreCull -= ApplyViewportToCamera;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance != this)
            return;

        RestoreOverlayCanvases();
        _instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        RefreshOverlayCanvases();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera targetCamera)
    {
        ApplyViewportToCamera(targetCamera);
    }

    private void ApplyViewportToCamera(Camera targetCamera)
    {
        if (
            targetCamera == null
            || targetCamera.targetTexture != null
            || targetCamera.targetDisplay != 0
        )
            return;

        Rect cameraRect = targetCamera.rect;
        if (
            !IsFullScreenRect(cameraRect)
            && !RectsApproximatelyEqual(cameraRect, _previousViewportRect)
            && !RectsApproximatelyEqual(cameraRect, _viewportRect)
        )
            return;

        targetCamera.rect = _viewportRect;
    }

    private void RefreshViewport()
    {
        _screenWidth = Screen.width;
        _screenHeight = Screen.height;
        _previousViewportRect = _viewportRect;
        _viewportRect = CalculateViewportRect(_screenWidth, _screenHeight);
        UpdateScreenMask(_viewportRect);
        UpdateOverlayCanvasLayouts();
    }

    internal static Rect CalculateViewportRect(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return new Rect(0f, 0f, 1f, 1f);

        float screenAspectRatio = (float)screenWidth / screenHeight;

        if (screenAspectRatio > TargetAspectRatio)
        {
            float viewportWidth = TargetAspectRatio / screenAspectRatio;
            return new Rect((1f - viewportWidth) * 0.5f, 0f, viewportWidth, 1f);
        }

        float viewportHeight = screenAspectRatio / TargetAspectRatio;
        return new Rect(0f, (1f - viewportHeight) * 0.5f, 1f, viewportHeight);
    }

    private void CreateScreenMask()
    {
        GameObject maskObject = new(
            "16:9 Screen Mask",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster)
        );
        maskObject.transform.SetParent(transform, false);
        _screenMaskTransform = maskObject.transform;

        _screenMaskCanvas = maskObject.GetComponent<Canvas>();
        _screenMaskCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _screenMaskCanvas.overrideSorting = true;
        _screenMaskCanvas.sortingOrder = MaskSortingOrder;

        _leftBar = CreateBar("Left Black Bar");
        _rightBar = CreateBar("Right Black Bar");
        _bottomBar = CreateBar("Bottom Black Bar");
        _topBar = CreateBar("Top Black Bar");
    }

    private Image CreateBar(string barName)
    {
        GameObject barObject = new(barName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barObject.transform.SetParent(_screenMaskTransform, false);

        Image bar = barObject.GetComponent<Image>();
        bar.color = Color.black;
        bar.raycastTarget = true;
        return bar;
    }

    private void UpdateScreenMask(Rect viewport)
    {
        SetBarRect(_leftBar, new Vector2(0f, 0f), new Vector2(viewport.xMin, 1f));
        SetBarRect(_rightBar, new Vector2(viewport.xMax, 0f), new Vector2(1f, 1f));
        SetBarRect(_bottomBar, new Vector2(0f, 0f), new Vector2(1f, viewport.yMin));
        SetBarRect(_topBar, new Vector2(0f, viewport.yMax), new Vector2(1f, 1f));
    }

    private static void SetBarRect(Image bar, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rectTransform = bar.rectTransform;
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        bar.enabled = anchorMax.x > anchorMin.x && anchorMax.y > anchorMin.y;
    }

    private static bool IsFullScreenRect(Rect rect)
    {
        return Mathf.Approximately(rect.x, 0f)
            && Mathf.Approximately(rect.y, 0f)
            && Mathf.Approximately(rect.width, 1f)
            && Mathf.Approximately(rect.height, 1f);
    }

    private static bool RectsApproximatelyEqual(Rect first, Rect second)
    {
        return Mathf.Approximately(first.x, second.x)
            && Mathf.Approximately(first.y, second.y)
            && Mathf.Approximately(first.width, second.width)
            && Mathf.Approximately(first.height, second.height);
    }

    private void RefreshOverlayCanvases()
    {
        RemoveStaleCanvasLayouts();

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);

        foreach (Canvas canvas in canvases)
        {
            if (!CanControlCanvas(canvas))
                continue;

            if (!_overlayCanvasLayouts.TryGetValue(canvas, out OverlayCanvasLayout layout))
            {
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (!UsesTargetReferenceResolution(scaler))
                    continue;

                layout = new OverlayCanvasLayout(canvas, scaler);
                _overlayCanvasLayouts.Add(canvas, layout);
            }

            UpdateOverlayCanvasLayout(layout, false);
        }
    }

    private bool CanControlCanvas(Canvas canvas)
    {
        return canvas != null
            && canvas != _screenMaskCanvas
            && canvas.isRootCanvas
            && canvas.renderMode == RenderMode.ScreenSpaceOverlay
            && canvas.targetDisplay == 0;
    }

    private static bool UsesTargetReferenceResolution(CanvasScaler scaler)
    {
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return false;

        Vector2 referenceResolution = scaler.referenceResolution;
        if (referenceResolution.x <= 0f || referenceResolution.y <= 0f)
            return false;

        return Mathf.Approximately(referenceResolution.x / referenceResolution.y, TargetAspectRatio);
    }

    private void UpdateOverlayCanvasLayouts()
    {
        foreach (OverlayCanvasLayout layout in _overlayCanvasLayouts.Values)
        {
            if (layout.Canvas != null)
                UpdateOverlayCanvasLayout(layout);
        }
    }

    private void UpdateOverlayCanvasLayout(OverlayCanvasLayout layout, bool remapExistingChildren = true)
    {
        Vector2 referenceResolution = layout.ReferenceResolution;
        float viewportPixelWidth = _screenWidth * _viewportRect.width;
        float viewportPixelHeight = _screenHeight * _viewportRect.height;
        float scaleFactor = Mathf.Min(
            viewportPixelWidth / referenceResolution.x,
            viewportPixelHeight / referenceResolution.y
        );

        layout.Scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        layout.Scaler.scaleFactor = Mathf.Max(0.01f, scaleFactor);

        Transform canvasTransform = layout.Canvas.transform;
        for (int childIndex = 0; childIndex < canvasTransform.childCount; childIndex++)
        {
            if (canvasTransform.GetChild(childIndex) is not RectTransform child)
                continue;

            bool isNewChild = !layout.OriginalAnchors.TryGetValue(
                child,
                out AnchorLayout originalAnchors
            );
            if (isNewChild)
            {
                originalAnchors = new AnchorLayout(child.anchorMin, child.anchorMax);
                layout.OriginalAnchors.Add(child, originalAnchors);
            }

            if (!isNewChild && !remapExistingChildren)
                continue;

            child.anchorMin = RemapAnchor(originalAnchors.Min, _viewportRect);
            child.anchorMax = RemapAnchor(originalAnchors.Max, _viewportRect);
        }
    }

    private static Vector2 RemapAnchor(Vector2 originalAnchor, Rect viewport)
    {
        return new Vector2(
            viewport.xMin + originalAnchor.x * viewport.width,
            viewport.yMin + originalAnchor.y * viewport.height
        );
    }

    private void RemoveStaleCanvasLayouts()
    {
        _staleCanvases.Clear();

        foreach (KeyValuePair<Canvas, OverlayCanvasLayout> entry in _overlayCanvasLayouts)
        {
            if (
                entry.Key == null
                || entry.Value.Scaler == null
                || !CanControlCanvas(entry.Key)
            )
            {
                entry.Value.Restore();
                _staleCanvases.Add(entry.Key);
            }
        }

        foreach (Canvas staleCanvas in _staleCanvases)
            _overlayCanvasLayouts.Remove(staleCanvas);
    }

    private void RestoreOverlayCanvases()
    {
        foreach (OverlayCanvasLayout layout in _overlayCanvasLayouts.Values)
            layout.Restore();

        _overlayCanvasLayouts.Clear();
    }

    private readonly struct AnchorLayout
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;

        public AnchorLayout(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }
    }

    private sealed class OverlayCanvasLayout
    {
        public readonly Canvas Canvas;
        public readonly CanvasScaler Scaler;
        public readonly Vector2 ReferenceResolution;
        public readonly Dictionary<RectTransform, AnchorLayout> OriginalAnchors = new();

        private readonly CanvasScaler.ScaleMode _originalScaleMode;
        private readonly float _originalScaleFactor;

        public OverlayCanvasLayout(Canvas canvas, CanvasScaler scaler)
        {
            Canvas = canvas;
            Scaler = scaler;
            ReferenceResolution = scaler.referenceResolution;
            _originalScaleMode = scaler.uiScaleMode;
            _originalScaleFactor = scaler.scaleFactor;
        }

        public void Restore()
        {
            if (Scaler != null)
            {
                Scaler.uiScaleMode = _originalScaleMode;
                Scaler.scaleFactor = _originalScaleFactor;
            }

            foreach (KeyValuePair<RectTransform, AnchorLayout> entry in OriginalAnchors)
            {
                if (entry.Key == null)
                    continue;

                entry.Key.anchorMin = entry.Value.Min;
                entry.Key.anchorMax = entry.Value.Max;
            }
        }
    }
}
