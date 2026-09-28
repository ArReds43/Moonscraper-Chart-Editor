// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System;
using UnityEngine;
using UnityEngine.UI;

namespace MoonscraperChartEditor.VideoGuide
{
    /// <summary>
    /// Control panel for the video guide. Built entirely from code so no prefab or scene
    /// changes are needed.
    ///
    /// Unlike the other editor menus this one deliberately does not stop the chart preview:
    /// the whole point of the sound/video offset fields is to align the video against the song
    /// audio, which means playback has to keep running while they are being adjusted.
    /// </summary>
    public class VideoGuidePanel : DisplayMenu
    {
        const int kFontSize = 16;
        const int kSmallFontSize = 14;
        const float kLabelWidth = 0.42f;

        /// <summary>Width of the right-edge strip that resizes the panel.</summary>
        const float kGripWidth = 8.0f;
        const float kNudgeSmall = 0.01f;
        const float kNudgeLarge = 0.1f;

        VideoGuideController _controller;

        Button _loadButton;
        Button _clearButton;
        Button _closeButton;
        Button _nudgeBackSmall;
        Button _nudgeForwardSmall;
        Button _nudgeBackLarge;
        Button _nudgeForwardLarge;

        Text _fileLabel;
        Text _statusLabel;
        Text _soundOffsetLabel;
        Text _videoOffsetLabel;

        InputField _soundOffsetField;
        InputField _videoOffsetField;

        /// <summary>Root of the panel, so the drag handle can move it and the position can be saved.</summary>
        RectTransform _panelRect;

        /// <summary>Owning canvas, used to keep the panel within the visible screen area.</summary>
        Canvas _canvas;

        /// <summary>Right-edge resize strip, so the width clamp can stand down while it is dragged.</summary>
        VideoGuideResizeHandle _resizeHandle;
        Toggle _visibleToggle;
        Slider _widthSlider;
        Slider _posXSlider;
        Slider _posYSlider;
        Slider _opacitySlider;
        Text _widthValue;
        Text _posXValue;
        Text _posYValue;
        Text _opacityValue;

        bool _built = false;

        /// <summary>
        /// Unity 2018.4 has no SetValueWithoutNotify/SetTextWithoutNotify/SetIsOnWithoutNotify
        /// (those arrived in 2019.1), so refreshing the widgets writes through the normal
        /// properties. This flag stops that from bouncing back through the change callbacks.
        /// </summary>
        bool _suppressCallbacks = false;

        protected override void Awake()
        {
            base.Awake();

            if (!editor)
            {
                Debug.LogError("Video Guide panel: no ChartEditor available, the panel cannot open.");
                enabled = false;
                return;
            }

            _controller = VideoGuideController.Instance;
            if (_controller == null)
            {
                Debug.LogError("Video Guide panel could not find the video guide controller. " +
                    "The panel was built before the feature finished setting up.");
                enabled = false;
                return;
            }

            BuildUI();
            _built = true;
        }

        #region UI construction

        void BuildUI()
        {
            Canvas canvas = VideoGuideUI.CreateCanvas("VideoGuidePanelCanvas", transform, VideoGuideController.kPanelSortingOrder, true, true);

            RectTransform panel = VideoGuideUI.CreateRect("Panel", canvas.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1.0f, 1.0f);
            _panelRect = panel;
            _canvas = canvas;

            VideoGuidePreferences prefs = VideoGuideStore.Preferences;
            panel.anchoredPosition = new Vector2(prefs.panelPosX, prefs.panelPosY);
            panel.sizeDelta = new Vector2(prefs.panelWidth, 0.0f);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = VideoGuideUI.kPanelBackground;
            background.raycastTarget = true;

            ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6.0f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperLeft;

            // DisplayMenu fields, assigned before the object is activated.
            mouseArea = panel;
            defaultSelectable = null;

            BuildHeader(layout.transform);
            BuildResizeGrip(panel);
            BuildFileRow(layout.transform);
            BuildStatusRow(layout.transform);
            BuildOffsetRows(layout.transform);
            BuildToggleRow(layout.transform);
            BuildSliderRows(layout.transform);
            BuildCloseRow(layout.transform);

            defaultSelectable = _loadButton;

            RefreshValues();
        }

        void BuildHeader(Transform parent)
        {
            RectTransform row = VideoGuideUI.CreateRow("Header", parent, 26.0f);

            // The header doubles as the drag handle, so the panel can be moved out of the way.
            Image handle = row.gameObject.AddComponent<Image>();
            handle.color = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            handle.raycastTarget = true;

            VideoGuideDragHandle drag = row.gameObject.AddComponent<VideoGuideDragHandle>();
            drag.Initialise(_panelRect);
            drag.OnDragFinished += OnPanelDragFinished;

            Text title = VideoGuideUI.CreateText("Title", row, "VIDEO GUIDE", 20, TextAnchor.MiddleLeft);
            VideoGuideUI.PlaceFraction((RectTransform)title.transform, 0.0f, 0.7f, 0.0f);
            title.fontStyle = FontStyle.Bold;

            Text hint = VideoGuideUI.CreateText("Hint", row, "drag to move", kSmallFontSize, TextAnchor.MiddleRight);
            VideoGuideUI.PlaceFraction((RectTransform)hint.transform, 0.7f, 1.0f, 0.0f);
            hint.color = VideoGuideUI.kLabel;
        }

        void OnPanelDragFinished()
        {
            VideoGuidePreferences prefs = VideoGuideStore.Preferences;
            prefs.panelPosX = _panelRect.anchoredPosition.x;
            prefs.panelPosY = _panelRect.anchoredPosition.y;
            VideoGuideStore.Save();
        }

        /// <summary>
        /// Thin strip on the panel's right edge for resizing it. It is a child of the panel but opts
        /// out of the layout group, which would otherwise treat it as another row and stack it.
        /// </summary>
        void BuildResizeGrip(RectTransform panel)
        {
            GameObject grip = new GameObject("ResizeGrip", typeof(RectTransform), typeof(Image));
            grip.layer = panel.gameObject.layer;
            grip.transform.SetParent(panel, false);

            RectTransform gripRect = (RectTransform)grip.transform;
            gripRect.anchorMin = new Vector2(1.0f, 0.0f);
            gripRect.anchorMax = new Vector2(1.0f, 1.0f);
            gripRect.pivot = new Vector2(1.0f, 0.5f);
            gripRect.anchoredPosition = Vector2.zero;
            gripRect.sizeDelta = new Vector2(kGripWidth, 0.0f);

            LayoutElement element = grip.AddComponent<LayoutElement>();
            element.ignoreLayout = true;

            Image image = grip.GetComponent<Image>();
            image.color = VideoGuideUI.kAccent;
            image.raycastTarget = true;

            VideoGuideResizeHandle resize = grip.AddComponent<VideoGuideResizeHandle>();
            resize.Initialise(panel);
            resize.OnResized += OnPanelResized;
            _resizeHandle = resize;
        }

        void BuildFileRow(Transform parent)
        {
            RectTransform row = VideoGuideUI.CreateRow("FileRow", parent, 30.0f);

            _loadButton = VideoGuideUI.CreateButton("Load", row, "Load video...", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_loadButton.transform, 0.0f, 0.5f);
            _loadButton.onClick.AddListener(OnLoadClicked);

            _clearButton = VideoGuideUI.CreateButton("Clear", row, "Clear", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_clearButton.transform, 0.5f, 1.0f);
            _clearButton.onClick.AddListener(OnClearClicked);
        }

        void BuildStatusRow(Transform parent)
        {
            RectTransform row = VideoGuideUI.CreateRow("StatusRow", parent, 40.0f);

            _fileLabel = VideoGuideUI.CreateText("File", row, "No video loaded", kSmallFontSize, TextAnchor.UpperLeft);
            VideoGuideUI.PlaceFraction((RectTransform)_fileLabel.transform, 0.0f, 1.0f, 0.0f);
            _fileLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _fileLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _fileLabel.color = VideoGuideUI.kLabel;

            _statusLabel = VideoGuideUI.CreateText("Status", row, string.Empty, kSmallFontSize, TextAnchor.LowerLeft);
            VideoGuideUI.PlaceFraction((RectTransform)_statusLabel.transform, 0.0f, 1.0f, 0.0f);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _statusLabel.color = VideoGuideUI.kAccent;
        }

        void BuildOffsetRows(Transform parent)
        {
            // Sound offset: the song time the video's 0:00 is anchored to.
            RectTransform soundRow = VideoGuideUI.CreateRow("SoundOffsetRow", parent, 28.0f);

            _soundOffsetLabel = VideoGuideUI.CreateText("Label", soundRow, "Sound offset (s)", kFontSize, TextAnchor.MiddleLeft);
            VideoGuideUI.PlaceFraction((RectTransform)_soundOffsetLabel.transform, 0.0f, kLabelWidth, 0.0f);
            _soundOffsetLabel.color = VideoGuideUI.kLabel;

            _soundOffsetField = VideoGuideUI.CreateInputField("Field", soundRow, "0", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_soundOffsetField.transform, kLabelWidth, 1.0f);
            _soundOffsetField.onValidateInput = LocalesManager.ValidateDecimalInput;
            _soundOffsetField.onEndEdit.AddListener(OnSoundOffsetEdited);

            // Video offset: fine trim on top of the anchor point. The nudge buttons get a row of
            // their own; squeezed into the remaining 40% of the offset row their labels wrapped and
            // were truncated away, so the buttons read as blank.
            RectTransform videoRow = VideoGuideUI.CreateRow("VideoOffsetRow", parent, 28.0f);

            _videoOffsetLabel = VideoGuideUI.CreateText("Label", videoRow, "Video offset (s)", kFontSize, TextAnchor.MiddleLeft);
            VideoGuideUI.PlaceFraction((RectTransform)_videoOffsetLabel.transform, 0.0f, kLabelWidth, 0.0f);
            _videoOffsetLabel.color = VideoGuideUI.kLabel;

            _videoOffsetField = VideoGuideUI.CreateInputField("Field", videoRow, "0", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_videoOffsetField.transform, kLabelWidth, 1.0f, 0.0f);
            _videoOffsetField.onValidateInput = LocalesManager.ValidateDecimalInput;
            _videoOffsetField.onEndEdit.AddListener(OnVideoOffsetEdited);

            RectTransform nudgeRow = VideoGuideUI.CreateRow("VideoNudgeRow", parent, 26.0f);

            _nudgeBackLarge = VideoGuideUI.CreateButton("BackLarge", nudgeRow, "-0.1", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_nudgeBackLarge.transform, 0.0f, 0.25f);
            _nudgeBackLarge.onClick.AddListener(() => NudgeVideoOffset(-kNudgeLarge));

            _nudgeBackSmall = VideoGuideUI.CreateButton("BackSmall", nudgeRow, "-0.01", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_nudgeBackSmall.transform, 0.25f, 0.50f);
            _nudgeBackSmall.onClick.AddListener(() => NudgeVideoOffset(-kNudgeSmall));

            _nudgeForwardSmall = VideoGuideUI.CreateButton("ForwardSmall", nudgeRow, "+0.01", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_nudgeForwardSmall.transform, 0.50f, 0.75f);
            _nudgeForwardSmall.onClick.AddListener(() => NudgeVideoOffset(kNudgeSmall));

            _nudgeForwardLarge = VideoGuideUI.CreateButton("ForwardLarge", nudgeRow, "+0.1", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_nudgeForwardLarge.transform, 0.75f, 1.0f);
            _nudgeForwardLarge.onClick.AddListener(() => NudgeVideoOffset(kNudgeLarge));
        }

        void BuildToggleRow(Transform parent)
        {
            RectTransform row = VideoGuideUI.CreateRow("ToggleRow", parent, 26.0f);

            _visibleToggle = VideoGuideUI.CreateToggle("ShowOverlay", row, "Show video over the highway", kFontSize, true);
            VideoGuideUI.PlaceFraction((RectTransform)_visibleToggle.transform, 0.0f, 1.0f, 0.0f);
            _visibleToggle.onValueChanged.AddListener(OnVisibleToggled);
        }

        void BuildSliderRows(Transform parent)
        {
            _widthSlider = BuildSliderRow(parent, "Width", VideoGuideLayout.kMinWidthFraction, VideoGuideLayout.kMaxWidthFraction, out _widthValue);
            _posXSlider = BuildSliderRow(parent, "Pos X", -VideoGuideLayout.kPosRange, VideoGuideLayout.kPosRange, out _posXValue);
            _posYSlider = BuildSliderRow(parent, "Pos Y", -VideoGuideLayout.kPosRange, VideoGuideLayout.kPosRange, out _posYValue);
            _opacitySlider = BuildSliderRow(parent, "Opacity", 0.0f, 1.0f, out _opacityValue);
        }

        Slider BuildSliderRow(Transform parent, string label, float min, float max, out Text valueLabel)
        {
            RectTransform row = VideoGuideUI.CreateRow(label + "Row", parent, 24.0f);

            Text labelText = VideoGuideUI.CreateText("Label", row, label, kSmallFontSize, TextAnchor.MiddleLeft);
            VideoGuideUI.PlaceFraction((RectTransform)labelText.transform, 0.0f, 0.24f, 0.0f);
            labelText.color = VideoGuideUI.kLabel;

            Slider slider = VideoGuideUI.CreateSlider("Slider", row, min, max, min);
            VideoGuideUI.PlaceFraction((RectTransform)slider.transform, 0.24f, 0.86f);
            slider.onValueChanged.AddListener(value => OnLayoutSliderChanged());

            valueLabel = VideoGuideUI.CreateText("Value", row, string.Empty, kSmallFontSize, TextAnchor.MiddleRight);
            VideoGuideUI.PlaceFraction((RectTransform)valueLabel.transform, 0.86f, 1.0f, 0.0f);
            valueLabel.color = VideoGuideUI.kLabel;

            return slider;
        }

        void BuildCloseRow(Transform parent)
        {
            RectTransform row = VideoGuideUI.CreateRow("CloseRow", parent, 30.0f);

            _closeButton = VideoGuideUI.CreateButton("Close", row, "Close", kFontSize);
            VideoGuideUI.PlaceFraction((RectTransform)_closeButton.transform, 0.0f, 1.0f);
            _closeButton.onClick.AddListener(Disable);
        }

        #endregion

        #region Callbacks

        void OnLoadClicked()
        {
            string defExt = string.Empty;
            for (int i = 0; i < VideoGuideController.ValidVideoExtensions.Length; ++i)
            {
                if (!string.IsNullOrEmpty(defExt))
                    defExt += ",";

                defExt += VideoGuideController.ValidVideoExtensions[i];
            }

            try
            {
                string filepath;
                if (FileExplorer.OpenFilePanel(new ExtensionFilter("Video files", VideoGuideController.ValidVideoExtensions), defExt, out filepath))
                {
                    _controller.SetVideo(filepath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(Logger.LogException(e, "Could not open a video file."));
            }

            RefreshValues();
        }

        void OnClearClicked()
        {
            _controller.ClearVideo();
            RefreshValues();
        }

        void OnSoundOffsetEdited(string value)
        {
            _controller.SoundOffset = ParseOffset(value);
            _controller.SaveSettings();
            RefreshValues();
        }

        void OnVideoOffsetEdited(string value)
        {
            _controller.VideoOffset = ParseOffset(value);
            _controller.SaveSettings();
            RefreshValues();
        }

        void NudgeVideoOffset(float delta)
        {
            _controller.VideoOffset = Mathf.Round((_controller.VideoOffset + delta) * 1000.0f) / 1000.0f;
            _controller.SaveSettings();
            RefreshValues();
        }

        void OnVisibleToggled(bool value)
        {
            if (_suppressCallbacks)
                return;

            VideoGuideStore.Preferences.overlayVisible = value;
            VideoGuideStore.Save();
        }

        void OnLayoutSliderChanged()
        {
            if (!_built || _suppressCallbacks)
                return;

            VideoGuidePreferences prefs = VideoGuideStore.Preferences;
            prefs.overlayWidth = _widthSlider.value;
            prefs.overlayPosX = _posXSlider.value;
            prefs.overlayPosY = _posYSlider.value;
            prefs.overlayOpacity = _opacitySlider.value;

            UpdateSliderValueLabels();
            VideoGuideStore.Save();
        }

        static float ParseOffset(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0.0f;

            float parsed;
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out parsed))
                return parsed;

            // Fall back to the invariant culture, the chart format always writes en-US.
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return 0.0f;
        }

        #endregion

        #region Values

        void RefreshValues()
        {
            if (!_built)
                return;

            VideoGuidePreferences prefs = VideoGuideStore.Preferences;

            _suppressCallbacks = true;
            try
            {
                // InputField.text does not raise onEndEdit when set from code, but the sliders and
                // the toggle do raise onValueChanged, hence the flag above.
                _soundOffsetField.text = _controller.SoundOffset.ToString("0.###");
                _videoOffsetField.text = _controller.VideoOffset.ToString("0.###");

                _visibleToggle.isOn = prefs.overlayVisible;
                _widthSlider.value = prefs.overlayWidth;
                _posXSlider.value = prefs.overlayPosX;
                _posYSlider.value = prefs.overlayPosY;
                _opacitySlider.value = prefs.overlayOpacity;
            }
            finally
            {
                _suppressCallbacks = false;
            }

            UpdateSliderValueLabels();
            UpdateFileLabel();
        }

        void UpdateSliderValueLabels()
        {
            _widthValue.text = Mathf.RoundToInt(_widthSlider.value * 100.0f) + "%";
            _posXValue.text = _posXSlider.value.ToString("+0.00;-0.00");
            _posYValue.text = _posYSlider.value.ToString("+0.00;-0.00");
            _opacityValue.text = Mathf.RoundToInt(_opacitySlider.value * 100.0f) + "%";
        }

        void UpdateFileLabel()
        {
            if (_controller.HasVideo)
                _fileLabel.text = _controller.VideoFileName;
            else
                _fileLabel.text = "No video loaded for this chart";
        }

        void UpdateStatusLabel()
        {
            if (!_built)
                return;

            if (!_controller.HasVideo)
            {
                _statusLabel.color = VideoGuideUI.kLabel;
                _statusLabel.text = "Pick a video, then set the sound offset to the song time its 0:00 lines up with.";
                return;
            }

            if (_controller.State == VideoGuideState.Error)
            {
                _statusLabel.color = VideoGuideUI.kWarning;
                _statusLabel.text = _controller.ErrorMessage;
                return;
            }

            _statusLabel.color = VideoGuideUI.kAccent;
            _statusLabel.text = string.Format(
                "{0}   song {1}   video {2}   drift {3}s",
                StateLabel(_controller.State),
                VideoGuideController.FormatTime(_controller.CurrentSongTime()),
                VideoGuideController.FormatTime(_controller.CurrentVideoTime),
                _controller.CurrentDrift.ToString("+0.000;-0.000"));
        }

        static string StateLabel(VideoGuideState state)
        {
            switch (state)
            {
                case VideoGuideState.Preparing: return "loading";
                case VideoGuideState.Ready: return "ready";
                case VideoGuideState.WaitingForStart: return "waiting for start";
                case VideoGuideState.Playing: return "playing";
                case VideoGuideState.Paused: return "paused";
                case VideoGuideState.Finished: return "video ended";
                case VideoGuideState.Error: return "error";
                default: return "no video";
            }
        }

        #endregion

        /// <summary>
        /// Closing is intentionally limited to the Close button and the menu close input, and the
        /// highway is not frozen: playback has to keep running so the offsets can be aligned.
        /// </summary>
        protected override void Update()
        {
            if (!editor || !_built)
                return;

            ApplyPanelWidth();
            UpdateStatusLabel();

            if (MSChartEditorInput.GetInputDown(MSChartEditorInputActions.CloseMenu) && !editor.uiServices.popupBlockerEnabled)
                Disable();
        }

        /// <summary>
        /// Keeps the panel inside the screen. The canvas is scaled to a 1920x1080 reference, so its
        /// usable width in canvas units changes with the game window; a fixed pixel width would hang
        /// off the edge and clip the right-hand widgets.
        ///
        /// This also doubles as the recovery path: any position that would leave the panel
        /// unreachable is pulled back every frame, so reopening it always brings it back.
        /// </summary>
        void ApplyPanelWidth()
        {
            if (_panelRect == null || _canvas == null)
                return;

            VideoGuideUI.ClampInsideCanvas(_panelRect, _canvas, VideoGuideUI.kPanelMargin);

            // While a resize drag is running the handle owns the width; clamping it here as well
            // would snap it back to the stored value on the very next frame.
            if (_resizeHandle != null && _resizeHandle.IsResizing)
                return;

            float available = ((RectTransform)_canvas.transform).rect.width - 2.0f * VideoGuideUI.kPanelMargin;
            if (available < VideoGuideResizeHandle.kMinPanelWidth)
                return;

            VideoGuidePreferences prefs = VideoGuideStore.Preferences;
            float width = Mathf.Clamp(prefs.panelWidth, VideoGuideResizeHandle.kMinPanelWidth, available);

            if (!Mathf.Approximately(_panelRect.sizeDelta.x, width))
                _panelRect.sizeDelta = new Vector2(width, _panelRect.sizeDelta.y);
        }

        void OnPanelResized(float width)
        {
            VideoGuideStore.Preferences.panelWidth = width;
            VideoGuideStore.Save();
        }

        protected override void OnEnable()
        {
            if (!_built)
                return;

            RefreshValues();
        }

        protected override void OnDisable()
        {
            // Deliberately not calling base.OnDisable(): this panel never changes the editor
            // state, so it must not drag the editor out of playback.
            UITabbing.defaultSelectable = null;

            // Awake bails out when there is no controller to talk to, so this can run unset.
            if (_controller != null)
                _controller.SaveSettings();
        }

        public void Toggle()
        {
            if (gameObject.activeSelf)
                Disable();
            else
                gameObject.SetActive(true);
        }
    }
}
