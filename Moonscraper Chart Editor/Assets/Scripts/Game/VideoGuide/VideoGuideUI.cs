// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MoonscraperChartEditor.VideoGuide
{
    /// <summary>
    /// Small helpers that build plain uGUI hierarchies from code.
    /// The video guide ships without any prefab or scene changes, so every piece of its
    /// interface is constructed at runtime by these helpers.
    /// </summary>
    public static class VideoGuideUI
    {
        public static readonly Color kPanelBackground = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        public static readonly Color kBackdrop = new Color(0.0f, 0.0f, 0.0f, 0.55f);
        public static readonly Color kWidget = new Color(0.20f, 0.22f, 0.28f, 1.0f);
        public static readonly Color kWidgetDark = new Color(0.05f, 0.05f, 0.07f, 1.0f);
        public static readonly Color kAccent = new Color(0.29f, 0.66f, 0.40f, 1.0f);
        public static readonly Color kLabel = new Color(0.78f, 0.81f, 0.88f, 1.0f);
        public static readonly Color kText = Color.white;
        public static readonly Color kWarning = new Color(1.0f, 0.55f, 0.35f, 1.0f);

        static Font s_font;
        static bool s_fontResolved = false;

        /// <summary>
        /// The video guide is generated at runtime so it needs a font that is not referenced by
        /// any asset. Falls back to any font already loaded in the scene if the builtin one is
        /// unavailable, which keeps the panel readable on stripped down installs.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (!s_fontResolved)
                {
                    s_font = ResolveFont();
                    s_fontResolved = true;

                    if (s_font == null)
                        Debug.LogError("Video Guide could not resolve a font, its text will not be visible.");
                }

                return s_font;
            }
        }

        static Font ResolveFont()
        {
            // Arial.ttf is the builtin font name for the Unity version this project targets.
            try
            {
                Font builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (builtin != null)
                    return builtin;
            }
            catch (System.Exception)
            {
            }

            try
            {
                Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (legacy != null)
                    return legacy;
            }
            catch (System.Exception)
            {
            }

            Font[] loaded = Resources.FindObjectsOfTypeAll<Font>();
            if (loaded != null && loaded.Length > 0)
                return loaded[0];

            return null;
        }

        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder, bool withRaycaster, bool scaleWithResolution)
        {
            var types = new System.Type[] { typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler) };
            if (withRaycaster)
                types = new System.Type[] { typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster) };

            GameObject go = new GameObject(name, types);
            go.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
                go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            if (scaleWithResolution)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920.0f, 1080.0f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }
            else
            {
                // Pixel accurate sizing, the overlay is positioned in raw screen pixels.
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1.0f;
            }

            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);

            go.layer = LayerMask.NameToLayer("UI");
            return (RectTransform)go.transform;
        }

        /// <summary>Stretches a rect to fill its parent.</summary>
        public static RectTransform Stretch(RectTransform rect, float padding = 0.0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static RectTransform Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static RawImage CreateRawImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.color = color;
            return image;
        }

        public static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = alignment;
            text.color = kText;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label, int fontSize, Color? color = null)
        {
            Image image = CreateImage(name, parent, color ?? kWidget);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Label", image.transform, label, fontSize, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform, 4.0f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            return button;
        }

        public static Toggle CreateToggle(string name, Transform parent, string label, int fontSize, bool value)
        {
            Image box = CreateImage(name, parent, kWidgetDark);
            Toggle toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = CreateImage("Checkmark", box.transform, kAccent);
            Stretch((RectTransform)toggle.graphic.transform, 4.0f);
            toggle.isOn = value;

            Text text = CreateText("Label", box.transform, label, fontSize, TextAnchor.MiddleLeft);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(34.0f, 0.0f);
            textRect.offsetMax = new Vector2(-6.0f, 0.0f);
            text.color = kLabel;

            return toggle;
        }

        public static Slider CreateSlider(string name, Transform parent, float min, float max, float value)
        {
            Image background = CreateImage(name, parent, kWidgetDark);
            Slider slider = background.gameObject.AddComponent<Slider>();

            RectTransform fillArea = CreateRect("Fill Area", background.transform);
            fillArea.anchorMin = new Vector2(0.0f, 0.25f);
            fillArea.anchorMax = new Vector2(1.0f, 0.75f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;

            Image fill = CreateImage("Fill", fillArea, kAccent);
            Stretch((RectTransform)fill.transform);

            RectTransform handleArea = CreateRect("Handle Slide Area", background.transform);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8.0f, 0.0f);
            handleArea.offsetMax = new Vector2(-8.0f, 0.0f);

            Image handle = CreateImage("Handle", handleArea, kLabel);
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(18.0f, 0.0f);

            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;

            // No listeners are attached yet, so assigning value cannot notify anything.
            slider.value = value;

            return slider;
        }

        public static InputField CreateInputField(string name, Transform parent, string content, int fontSize)
        {
            Image background = CreateImage(name, parent, kWidgetDark);
            InputField input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.lineType = InputField.LineType.SingleLine;

            Text text = CreateText("Text", background.transform, content, fontSize, TextAnchor.MiddleLeft);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(8.0f, 0.0f);
            textRect.offsetMax = new Vector2(-8.0f, 0.0f);
            text.supportRichText = false;
            input.textComponent = text;

            Text placeholder = CreateText("Placeholder", background.transform, "0", fontSize, TextAnchor.MiddleLeft);
            RectTransform placeholderRect = (RectTransform)placeholder.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.pivot = new Vector2(0.5f, 0.5f);
            placeholderRect.offsetMin = new Vector2(8.0f, 0.0f);
            placeholderRect.offsetMax = new Vector2(-8.0f, 0.0f);
            placeholder.color = new Color(1.0f, 1.0f, 1.0f, 0.35f);
            input.placeholder = placeholder;

            return input;
        }

        /// <summary>
        /// Adds a vertically stacked row to a <see cref="VerticalLayoutGroup"/>. A
        /// <see cref="LayoutElement"/> is required because a bare RectTransform reports a
        /// preferred height of zero to layout groups.
        /// </summary>
        public static RectTransform CreateRow(string name, Transform parent, float height)
        {
            RectTransform row = CreateRect(name, parent);
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0.0f;
            return row;
        }

        /// <summary>
        /// Places a child across a fraction of its row's width, inset by a pixel margin so
        /// sibling widgets do not touch.
        ///
        /// The fractions become normalised anchors rather than pixel offsets, so a child always
        /// spans the same share of whatever width its row actually has. Sizing these off a fixed
        /// reference width instead made the right-hand widgets overflow the row, and the panel
        /// stayed a fixed size that could be wider than the screen.
        /// </summary>
        public static RectTransform PlaceFraction(RectTransform child, float startFraction, float endFraction, float inset = 6.0f)
        {
            child.anchorMin = new Vector2(startFraction, 0.0f);
            child.anchorMax = new Vector2(endFraction, 1.0f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.offsetMin = new Vector2(inset, 0.0f);
            child.offsetMax = new Vector2(-inset, 0.0f);
            return child;
        }

        /// <summary>Nominal panel row width, only used to convert fractions into pixel insets.</summary>
        public const float kRowWidthReference = 440.0f;

        /// <summary>Screen edge kept free so the panel is never flush against the border.</summary>
        public const float kPanelMargin = 24.0f;

        /// <summary>
        /// Pulls a corner-anchored panel fully back inside the visible canvas.
        ///
        /// Everything here works in canvas units. The canvas is scaled to a 1920x1080 reference, so
        /// screen pixels are not the same distance as an anchoredPosition offset; using pixelRect
        /// alongside anchoredPosition compares two different unit systems and lets the panel drift
        /// off screen.
        /// </summary>
        public static void ClampInsideCanvas(RectTransform target, Canvas canvas, float margin)
        {
            if (target == null || canvas == null)
                return;

            Rect area = ((RectTransform)canvas.transform).rect;
            if (area.width <= 0.0f || area.height <= 0.0f)
                return;

            Vector2 size = target.rect.size;
            if (size.x <= 0.0f || size.y <= 0.0f)
                return;

            // The pivot is the top-right corner, so the panel extends left and down from it: it
            // occupies [corner - size, corner]. Keeping the whole panel on screen therefore means
            // the corner has to sit at least one full size away from the opposite edges, not one
            // size less than them, which is what let it slide off the left and bottom.
            float left = area.xMin + margin + size.x;
            float right = area.xMax - margin;
            float bottom = area.yMin + margin + size.y;
            float top = area.yMax - margin;

            // A panel wider or taller than the canvas has no valid position; let the opposite
            // limit win rather than collapsing to a meaningless one.
            if (left > right)
                left = right;

            if (bottom > top)
                bottom = top;

            float x = Mathf.Clamp(area.xMax + target.anchoredPosition.x, left, right);
            float y = Mathf.Clamp(area.yMax + target.anchoredPosition.y, bottom, top);

            target.anchoredPosition = new Vector2(x - area.xMax, y - area.yMax);
        }
    }

    /// <summary>
    /// Makes a panel movable by dragging it. Attaches to a handle (the title bar) rather than the
    /// whole panel so the buttons underneath stay clickable. The panel is anchored to a single
    /// corner, so a pointer delta maps straight onto its anchoredPosition.
    /// </summary>
    public class VideoGuideDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _target;
        Canvas _canvas;
        Vector2 _startAnchored;
        Vector2 _startPointer;
        bool _dragging;

        /// <summary>Raised once when a drag finishes, so the caller can persist the new position.</summary>
        public event System.Action OnDragFinished;

        public void Initialise(RectTransform target)
        {
            _target = target;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_target == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _canvas = _target.GetComponentInParent<Canvas>();
            _startAnchored = _target.anchoredPosition;
            _startPointer = eventData.position;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || _target == null)
                return;

            float scale = _canvas != null && _canvas.scaleFactor > 0.0f ? _canvas.scaleFactor : 1.0f;
            Vector2 delta = (eventData.position - _startPointer) / scale;
            _target.anchoredPosition = _startAnchored + delta;
            VideoGuideUI.ClampInsideCanvas(_target, _canvas, VideoGuideUI.kPanelMargin);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;

            _dragging = false;

            if (OnDragFinished != null)
                OnDragFinished();
        }
    }

    /// <summary>
    /// Drag strip on the right edge of the panel that resizes it. The panel is anchored to a single
    /// corner, so the grabbed edge follows the pointer while the opposite edge stays put.
    /// </summary>
    public class VideoGuideResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>Narrowest the panel may be dragged.</summary>
        public const float kMinPanelWidth = 280.0f;

        RectTransform _target;
        Canvas _canvas;
        float _startWidth;
        float _startAnchoredX;
        float _startPointerX;
        bool _dragging;

        /// <summary>True while a resize drag is in progress, so callers can stop fighting it.</summary>
        public bool IsResizing { get { return _dragging; } }

        public void Initialise(RectTransform target)
        {
            _target = target;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_target == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _canvas = _target.GetComponentInParent<Canvas>();
            _startWidth = _target.sizeDelta.x;
            _startAnchoredX = _target.anchoredPosition.x;
            _startPointerX = eventData.position.x;
            _dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || _target == null)
                return;

            float scale = _canvas != null && _canvas.scaleFactor > 0.0f ? _canvas.scaleFactor : 1.0f;
            float delta = (eventData.position.x - _startPointerX) / scale;

            // Both the width and the offset are derived from the values captured at drag start.
            // Reading the live anchoredPosition here and adding the full delta again compounded the
            // movement on every frame, which flung the panel off the left of the screen.
            float width = Mathf.Max(kMinPanelWidth, _startWidth + delta);
            _target.sizeDelta = new Vector2(width, _target.sizeDelta.y);

            // The panel hangs off the right edge, so growing it has to move the right edge out too.
            _target.anchoredPosition = new Vector2(_startAnchoredX + delta, _target.anchoredPosition.y);

            // Re-flow the rows in the same frame so dragging feels immediate.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_target);
            VideoGuideUI.ClampInsideCanvas(_target, _canvas, VideoGuideUI.kPanelMargin);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;

            _dragging = false;

            if (OnResized != null && _target != null)
                OnResized(_target.sizeDelta.x);
        }

        /// <summary>Raised with the new width once a resize drag finishes.</summary>
        public event System.Action<float> OnResized;
    }
}
