using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 호스트가 확정한 맵을 보여주는 연출 전용 UI입니다. 이 화면은 추첨하지 않습니다.
/// 고정 개수의 카드를 재사용해 썸네일을 순환시키고 마지막에는 목표 씬에 멈춥니다.
/// </summary>
public sealed class MapRouletteView : MonoBehaviour
{
    private sealed class Card
    {
        public RectTransform Rect;
        public Image Frame;
        public Image Thumbnail;
        public CanvasGroup Group;
    }

    private readonly Card[] _cards = new Card[7];
    private RectTransform _viewport;
    private RectTransform _selectionFrame;
    private TMP_Text _title;
    private TMP_Text _result;
    private SO_MapDefinition[] _maps;
    private Sequence _animation;
    private int _lastStep;
    private float _position;
    private int _generation;
    private bool _revealed;
    private static readonly Color Gold = new Color(1f, 0.77f, 0.25f);
    private static readonly Color Navy = new Color(0.055f, 0.085f, 0.16f);

    public void Initialize(TMP_FontAsset font)
    {
        if (_viewport != null)
            return;

        var root = (RectTransform)transform;
        root.anchorMin = new Vector2(0.04f, 0.5f);
        root.anchorMax = new Vector2(0.96f, 0.5f);
        root.sizeDelta = new Vector2(0, 360);
        root.anchoredPosition = Vector2.zero;

        _title = CreateText("Mode", root, font, 32, new Vector2(0, 148));
        _result = CreateText("Map Name", root, font, 38, new Vector2(0, -145));
        _result.color = Gold;

        _viewport = CreateRect("Card Window", root, new Vector2(0, 235));
        _viewport.anchorMin = new Vector2(0, 0.5f);
        _viewport.anchorMax = new Vector2(1, 0.5f);
        _viewport.gameObject.AddComponent<RectMask2D>();
        var background = _viewport.gameObject.AddComponent<Image>();
        background.color = new Color(Navy.r, Navy.g, Navy.b, 0.9f);
        background.raycastTarget = false;

        for (int i = 0; i < _cards.Length; i++)
        {
            var rect = CreateRect("Map Card " + i, _viewport, new Vector2(300, 180));
            var frame = rect.gameObject.AddComponent<Image>();
            frame.raycastTarget = false;
            var imageRect = CreateRect("Thumbnail", rect, Vector2.zero);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.sizeDelta = new Vector2(-8, -8);
            var image = imageRect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            _cards[i] = new Card
            {
                Rect = rect, Frame = frame, Thumbnail = image,
                Group = rect.gameObject.AddComponent<CanvasGroup>()
            };
            _cards[i].Group.blocksRaycasts = false;
        }

        // 선택 위치는 고정하고 이미지만 가로로 이동합니다.
        _selectionFrame = CreateRect("Selection Frame", root, new Vector2(316, 196));
        var outline = _selectionFrame.gameObject.AddComponent<Image>();
        outline.color = new Color(0, 0, 0, 0);
        outline.raycastTarget = false;
        // 네 변만 그려 중앙의 썸네일을 가리지 않습니다.
        for (int i = 0; i < 4; i++)
        {
            var edge = CreateRect("Edge " + i, _selectionFrame, Vector2.zero);
            edge.anchorMin = i < 2 ? new Vector2(0, i) : new Vector2(i - 2, 0);
            edge.anchorMax = i < 2 ? new Vector2(1, i) : new Vector2(i - 2, 1);
            edge.sizeDelta = i < 2 ? new Vector2(0, 3) : new Vector2(3, 0);
            var line = edge.gameObject.AddComponent<Image>();
            line.color = Gold;
            line.raycastTarget = false;
        }
        CreateText("Pointer", root, font, 25, new Vector2(0, 112)).text = "▼";
    }

    public Tween Play(SO_MapDefinition[] maps, string targetScene, string mode, float duration, int spins)
    {
        Hide();
        gameObject.SetActive(true);
        _maps = maps;
        _revealed = false;
        _lastStep = -1;
        int generation = _generation;
        _title.text = mode + " · MAP SELECT";
        _result.text = "????????????";

        int targetIndex = System.Array.FindIndex(maps, map => map.SceneName == targetScene);
        if (targetIndex < 0)
        {
            // 카탈로그 불일치를 다른 맵의 당첨으로 표시하지 않습니다.
            _result.text = targetScene;
            EditorLog.LogWarning("맵 룰렛 후보에서 선택된 씬을 찾지 못했습니다: " + targetScene);
            gameObject.SetActive(false);
            return null;
        }

        float total = Mathf.Max(0, duration);
        float hold = Mathf.Min(0.65f, total * 0.2f);
        // 100회 텍스트 교체용 설정은 카드 이동에 너무 빠르므로 이동량을 제한합니다.
        int cycles = Mathf.Max(1, Mathf.Clamp(spins, 12, 28) / maps.Length);
        int finalStep = cycles * maps.Length + targetIndex;
        Render(0, false);
        var sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject).SetRecyclable(false);
        _animation = sequence;
        sequence.OnKill(() =>
        {
            if (_animation == sequence)
                _animation = null;
        });
        if (maps.Length > 1 && total > 0)
        {
            _animation.Append(DOVirtual.Float(0, finalStep, total - hold,
                value =>
                {
                    if (_generation == generation)
                        Render(value, true);
                }).SetEase(Ease.OutCubic));
        }
        _animation.AppendCallback(() =>
        {
            if (_generation != generation)
                return;
            Render(finalStep, false);
            _revealed = true;
            _result.text = maps[targetIndex].DisplayName;
            for (int i = 0; i < _cards.Length; i++)
                if (i != 3) _cards[i].Group.alpha *= 0.55f;
            PlaySound(true);
        });
        if (hold > 0)
            _animation.Append(_cards[3].Rect.DOPunchScale(Vector3.one * 0.09f, hold, 2, 0.5f));
        return sequence;
    }

    private void Render(float position, bool sound)
    {
        _position = position;
        int step = Mathf.FloorToInt(position + 0.5f);
        if (sound && step != _lastStep)
            PlaySound(false);
        _lastStep = step;

        float width = Mathf.Clamp(_viewport.rect.width / 3.4f, 160, 300);
        float spacing = width * 0.88f;
        _selectionFrame.sizeDelta = new Vector2(width * 1.08f + 12, width * 0.6f * 1.08f + 12);
        for (int i = 0; i < _cards.Length; i++)
        {
            int offset = i - 3;
            int index = ((step + offset) % _maps.Length + _maps.Length) % _maps.Length;
            float relative = step + offset - position;
            float prominence = Mathf.Clamp01(1 - Mathf.Abs(relative));
            var card = _cards[i];
            card.Rect.sizeDelta = new Vector2(width, width * 0.6f);
            card.Rect.anchoredPosition = new Vector2(relative * spacing, 0);
            card.Rect.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.08f, prominence);
            card.Frame.color = Color.Lerp(Navy, Gold, prominence);
            card.Group.alpha = Mathf.Lerp(0.4f, 1, prominence);
            if (_revealed && i != 3)
                card.Group.alpha *= 0.55f;
            card.Thumbnail.sprite = _maps[index].Thumbnail;
            card.Thumbnail.color = card.Thumbnail.sprite != null ? Color.white : Navy;
        }
        _cards[3].Rect.SetAsLastSibling();
    }

    private static void PlaySound(bool selected)
    {
        if (AudioManager.Instance == null || AudioManager.Instance.Container == null)
            return;
        AudioManager.SfxPlay(selected ? AudioManager.Instance.Container.Select : AudioManager.Instance.Container.Roulette);
    }

    public void Hide()
    {
        // Tween의 제거가 프레임 끝까지 지연되어도 이전 연출의 콜백을 무시합니다.
        _generation++;
        _animation?.Pause();
        _animation?.Kill();
        _animation = null;
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        _generation++;
        _animation?.Pause();
        _animation?.Kill();
        _animation = null;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_viewport != null && _maps != null && _maps.Length > 0)
            Render(_position, false);
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, float size, Vector2 position)
    {
        var rect = CreateRect(name, parent, new Vector2(0, 55));
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }
}
