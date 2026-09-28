// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MoonscraperEngine;
using MoonscraperEngine.Audio;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MoonscraperChartEditor.VideoGuide
{
    public enum VideoGuideState
    {
        NoVideo,
        Preparing,
        Ready,
        /// <summary>Playback started but the song has not reached the video's start point yet.</summary>
        WaitingForStart,
        Playing,
        Paused,
        /// <summary>The song ran past the end of the video.</summary>
        Finished,
        Error
    }

    /// <summary>
    /// Plays a reference video above the highway while charting, locked to the song's BASS audio
    /// stream. The audio file is the master clock: the highway scroll and this video both derive
    /// their position from <see cref="AudioStream.CurrentPositionSeconds"/>, so pausing and
    /// resuming the chart preview also pauses and resumes the video in step.
    ///
    /// Decoding is done by Unity's own <see cref="VideoPlayer"/> with every audio track disabled,
    /// so the guide never produces sound of its own.
    ///
    /// Note: this targets the Unity 2018.4 VideoPlayer API, which differs from later versions
    /// (EnableAudioTrack, length, loopPointReached and C# style events rather than UnityEvents).
    /// </summary>
    public class VideoGuideController : MonoBehaviour
    {
        public static VideoGuideController Instance { get; private set; }

        /// <summary>Video formats offered by the file dialog.</summary>
        public static readonly string[] ValidVideoExtensions =
        {
            "mp4", "m4v", "webm", "mov", "avi", "mkv", "wmv", "ogv", "flv", "mpg", "mpeg"
        };

        /// <summary>Drift threshold (seconds) beyond which a hard seek is required (e.g. song scrub or timeline jump).</summary>
        const double kHardSeekThreshold = 0.5;

        /// <summary>Drift tolerance (seconds) below which playback speed remains untouched at 1.0x.</summary>
        const double kSoftSyncDeadzone = 0.03;

        /// <summary>Maximum time (seconds) to wait for a seekCompleted event before releasing the seek lock.</summary>
        const float kSeekTimeout = 1.5f;

        // The guide only needs to be legible, not pristine.
        const int kMaxRenderWidth = 1280;
        const int kMaxRenderHeight = 720;

        const int kOverlaySortingOrder = 100;

        /// <summary>Sort order of the control panel, drawn above the video.</summary>
        public const int kPanelSortingOrder = 200;

        ChartEditor _editor;
        VideoPlayer _videoPlayer;
        RenderTexture _renderTexture;
        RawImage _frameImage;
        Image _backdrop;
        Text _hudText;
        RectTransform _overlayRoot;

        VideoGuideSongSettings _settings;
        VideoGuideState _state = VideoGuideState.NoVideo;
        string _videoPath = string.Empty;

        /// <summary>
        /// The path actually handed to the VideoPlayer. Differs from <see cref="_videoPath"/> when
        /// the user's file sits somewhere the Windows video backend cannot open (see ResolvePlayPath).
        /// </summary>
        string _playPath = string.Empty;
        string _errorMessage = string.Empty;
        int _prepareAttempt = 0;

        /// <summary>True while an asynchronous seek is in flight in the VideoPlayer backend.</summary>
        bool _isSeeking = false;

        /// <summary>Real time when the current seek was dispatched.</summary>
        float _seekIssuedAt = 0.0f;

        /// <summary>True if a newer seek request arrived while a seek was already in progress.</summary>
        bool _hasPendingSeek = false;
        double _pendingSeekTarget = 0.0;

        public VideoGuideState State { get { return _state; } }
        public string ErrorMessage { get { return _errorMessage; } }
        public bool HasVideo { get { return !string.IsNullOrEmpty(_videoPath); } }
        public string VideoFileName { get { return HasVideo ? Path.GetFileName(_videoPath) : string.Empty; } }

        /// <summary>Song time (seconds) that the video's own 0:00 is anchored to.</summary>
        public float SoundOffset
        {
            get { return _settings != null ? _settings.soundOffset : 0.0f; }
            set
            {
                if (_settings == null)
                    return;

                _settings.soundOffset = value;
            }
        }

        /// <summary>Extra trim applied on top of <see cref="SoundOffset"/>, in seconds.</summary>
        public float VideoOffset
        {
            get { return _settings != null ? _settings.videoOffset : 0.0f; }
            set
            {
                if (_settings == null)
                    return;

                _settings.videoOffset = value;
            }
        }

        /// <summary>Whether the status/debug line over the video is shown. Explicit-forced on even
        /// when off, so an error message always surfaces.</summary>
        public bool ShowDebugInfo
        {
            get { return VideoGuideStore.Preferences.showDebugInfo; }
            set
            {
                VideoGuideStore.Preferences.showDebugInfo = value;
                VideoGuideStore.Save();

                // Refresh the hud the frame the preference changes so the line goes away or comes.
                // back as soon as the user releases the toggle.
                UpdateHud();
            }
        }

        /// <summary>Position the video is showing right now, in seconds.</summary>
        public float CurrentVideoTime
        {
            get
            {
                if (_videoPlayer != null && _videoPlayer.isPrepared)
                    return (float)_videoPlayer.time;

                return 0.0f;
            }
        }

        public float VideoLength
        {
            get
            {
                if (_videoPlayer != null && _videoPlayer.isPrepared)
                    return (float)_videoPlayer.length;

                return 0.0f;
            }
        }

        /// <summary>
        /// Width over height of the loaded video, falling back to 16:9 until the file has been
        /// prepared. Used to size the overlay box so the frames are not stretched.
        /// </summary>
        public float DisplayAspect
        {
            get
            {
                if (_videoPlayer != null && _videoPlayer.isPrepared && _videoPlayer.height > 0u)
                    return (float)_videoPlayer.width / _videoPlayer.height;

                return 16.0f / 9.0f;
            }
        }

        /// <summary>How far the video is from where the song says it should be, in seconds.</summary>
        public float CurrentDrift
        {
            get
            {
                if (_videoPlayer == null || !_videoPlayer.isPrepared)
                    return 0.0f;

                return (float)(TargetVideoTime() - _videoPlayer.time);
            }
        }

        #region Setup

        public void Initialise(ChartEditor editor)
        {
            _editor = editor;
            Instance = this;

            BuildOverlay();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.source = VideoSource.Url;

            // The guide is a silent reference. VideoAudioOutputMode.None is the actual guarantee
            // that nothing is ever routed to a speaker; per-track disabling on prepare is only
            // belt-and-braces for files that carry audio.
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _renderTexture;

            _videoPlayer.prepareCompleted += OnPrepareCompleted;
            _videoPlayer.errorReceived += OnErrorReceived;
            _videoPlayer.loopPointReached += OnVideoEnded;
            _videoPlayer.seekCompleted += OnSeekCompleted;

            BindToCurrentSong();
        }

        void BuildOverlay()
        {
            // A dedicated canvas with no raycaster, so the video is drawn on top of the highway
            // without swallowing any clicks meant for the notes underneath it.
            Canvas canvas = VideoGuideUI.CreateCanvas("VideoGuideOverlay", transform, kOverlaySortingOrder, false, false);

            _overlayRoot = VideoGuideUI.CreateRect("Overlay", canvas.transform);
            VideoGuideUI.Stretch(_overlayRoot);

            _renderTexture = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
            _renderTexture.name = "VideoGuideTexture";
            _renderTexture.autoGenerateMips = false;
            _renderTexture.useMipMap = false;
            _renderTexture.filterMode = FilterMode.Bilinear;
            _renderTexture.Create();

            _backdrop = VideoGuideUI.CreateImage("Backdrop", _overlayRoot, VideoGuideUI.kBackdrop);
            _backdrop.raycastTarget = false;
            VideoGuideUI.Anchor((RectTransform)_backdrop.transform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(64.0f, 36.0f));

            _frameImage = VideoGuideUI.CreateRawImage("Video", _overlayRoot, Color.white);
            _frameImage.texture = _renderTexture;
            _frameImage.raycastTarget = false;
            VideoGuideUI.Anchor((RectTransform)_frameImage.transform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(64.0f, 36.0f));

            _hudText = VideoGuideUI.CreateText("Hud", _overlayRoot, string.Empty, 15, TextAnchor.LowerLeft);
            _hudText.color = VideoGuideUI.kLabel;
            _hudText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        #endregion

        #region Song binding

        /// <summary>Points the guide at the chart that is currently open, restoring its saved data.</summary>
        public void BindToCurrentSong()
        {
            string chartPath = _editor != null ? _editor.lastLoadedFilePath : string.Empty;
            _settings = VideoGuideStore.GetSongSettings(chartPath);

            string wanted = _settings.videoPath;
            if (string.IsNullOrEmpty(wanted) || !File.Exists(wanted))
            {
                if (!string.IsNullOrEmpty(wanted))
                {
                    Debug.LogWarning("Video Guide: the video saved for this chart no longer exists, clearing it. " + wanted);
                    _settings.videoPath = string.Empty;
                }

                wanted = string.Empty;
            }

            if (wanted == _videoPath && _state != VideoGuideState.Error)
                return;

            UnloadVideo();
            SetVideo(wanted);
        }

        public void SaveSettings()
        {
            VideoGuideStore.Save();
        }

        #endregion

        #region Video loading

        public void SetVideo(string path)
        {
            if (_settings == null)
                return;

            if (string.IsNullOrEmpty(path))
            {
                _settings.videoPath = string.Empty;
                UnloadVideo();
                SaveSettings();
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception e)
            {
                SetError("Invalid video path: " + path + " (" + e.Message + ")");
                return;
            }

            if (!File.Exists(fullPath))
            {
                SetError("Video file not found: " + fullPath);
                return;
            }

            _settings.videoPath = fullPath;
            _videoPath = fullPath;
            _playPath = ResolvePlayPath(fullPath);
            _errorMessage = string.Empty;
            _prepareAttempt = 0;
            _state = VideoGuideState.Preparing;

            _videoPlayer.Stop();
            _videoPlayer.url = BuildUrl(_playPath, 0);
            _videoPlayer.Prepare();

            ApplyLayout();
            SaveSettings();
        }

        public void ClearVideo()
        {
            if (_settings != null)
                _settings.videoPath = string.Empty;

            UnloadVideo();
            SaveSettings();
        }

        void UnloadVideo()
        {
            _videoPath = string.Empty;
            _playPath = string.Empty;
            _errorMessage = string.Empty;
            _state = VideoGuideState.NoVideo;

            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.url = string.Empty;
            }

            SetOverlayActive(false);
            UpdateHud();
        }

        void SetError(string message)
        {
            _errorMessage = message;
            _state = VideoGuideState.Error;
            Debug.LogError("Video Guide: " + message);
            SetOverlayActive(false);
            UpdateHud();
        }

        /// <summary>
        /// Unity percent-escapes the url it is given (spaces become %20, commas become %2C) and the
        /// Windows video backend then opens that escaped string literally, so a file whose name
        /// contains anything outside a plain alphanumeric set fails with 0x80070002 (file not found)
        /// even though the file is right there. Copying to a sanitised name sidesteps it.
        /// </summary>
        static bool NeedsSanitisedCopy(string fullPath)
        {
            foreach (char c in fullPath)
            {
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.' || c == '/' || c == '\\' || c == ':';

                if (!safe)
                    return true;
            }

            return false;
        }

        static string StableHash(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder sb = new StringBuilder(16);
                for (int i = 0; i < 8; ++i)
                    sb.Append(bytes[i].ToString("x2"));

                return sb.ToString();
            }
        }

        /// <summary>
        /// Returns a path the video backend can definitely open, copying to the temp folder first if
        /// the original name would be mangled by url escaping. Falls back to the original path if the
        /// copy cannot be made, so a read-only or full temp folder degrades instead of breaking.
        /// </summary>
        string ResolvePlayPath(string fullPath)
        {
            if (!NeedsSanitisedCopy(fullPath))
                return fullPath;

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "MoonscraperVideoGuide");
                Directory.CreateDirectory(dir);

                string ext = Path.GetExtension(fullPath);
                string target = Path.Combine(dir, StableHash(fullPath) + ext);

                DateTime sourceStamp = File.GetLastWriteTimeUtc(fullPath);
                bool cached = File.Exists(target) && File.GetLastWriteTimeUtc(target) >= sourceStamp;
                if (!cached)
                {
                    File.Copy(fullPath, target, true);
                    Debug.Log("Video Guide: copied the video to a temporary name so it can be opened: " + target);
                }

                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Video Guide: could not stage a temporary copy (" + e.Message + "), using the original path.");
                return fullPath;
            }
        }

        /// <summary>
        /// Unity wants a URL rather than a raw path. The first attempt uses a proper file URI;
        /// the fallbacks cover the rare path that Uri cannot represent.
        /// </summary>
        string BuildUrl(string fullPath, int attempt)
        {
            if (attempt == 0)
            {
                try
                {
                    return new Uri(fullPath).AbsoluteUri;
                }
                catch (Exception)
                {
                }
            }

            if (attempt == 1)
                return fullPath.Replace('\\', '/');

            return "file:///" + fullPath.Replace('\\', '/');
        }

        void OnPrepareCompleted(VideoPlayer source)
        {
            // Belt-and-braces: switch off any audio track the file happens to carry. The track
            // count is not knowable in advance for URLs, so the loop simply runs zero times for a
            // file with no audio. Silence itself is guaranteed by audioOutputMode = None.
            for (ushort track = 0; track < source.controlledAudioTrackCount; ++track)
            {
                source.EnableAudioTrack(track, false);
            }

            ApplyPlaybackSpeed();

            SizeRenderTexture();

            _state = VideoGuideState.Ready;
            _isSeeking = false;
            _hasPendingSeek = false;

            SetOverlayActive(true);
            UpdateHud();
        }

        void OnSeekCompleted(VideoPlayer source)
        {
            _isSeeking = false;

            if (_hasPendingSeek)
            {
                _hasPendingSeek = false;
                StartSeek(_pendingSeekTarget);
                return;
            }

            if (IsChartPlaying())
            {
                double target = TargetVideoTime();
                double drift = target - _videoPlayer.time;

                if (Math.Abs(drift) > kHardSeekThreshold)
                {
                    StartSeek(target);
                    return;
                }

                ApplyPlaybackSpeed(drift);
                _videoPlayer.Play();
                _state = VideoGuideState.Playing;
            }
            else
            {
                if (_state == VideoGuideState.Playing)
                    _state = VideoGuideState.Paused;
            }
        }

        void OnErrorReceived(VideoPlayer source, string message)
        {
            // A path Uri could not express is worth one more try before giving up.
            if (_prepareAttempt < 2 && !string.IsNullOrEmpty(_playPath))
            {
                ++_prepareAttempt;
                Debug.LogWarning("Video Guide: " + message + ", retrying with an alternative path form.");
                _state = VideoGuideState.Preparing;
                source.url = BuildUrl(_playPath, _prepareAttempt);
                source.Prepare();
                return;
            }

            SetError(message);
        }

        void OnVideoEnded(VideoPlayer source)
        {
            if (_state == VideoGuideState.Playing)
                _state = VideoGuideState.Finished;
        }

        /// <summary>
        /// The song audio is time stretched by the game speed. Adjusts video playback rate to match
        /// chart speed and smoothly steer out small timing drifts without hard seeks.
        /// </summary>
        void ApplyPlaybackSpeed(double drift = 0.0)
        {
            if (_videoPlayer == null || !_videoPlayer.canSetPlaybackSpeed)
                return;

            float baseSpeed = Globals.gameSettings.gameSpeed;
            if (baseSpeed <= 0.0f)
                return;

            if (Math.Abs(drift) > kSoftSyncDeadzone)
            {
                // Drift is positive if video is lagging behind song (need to speed up slightly to catch up).
                // Drift is negative if video is ahead of song (need to slow down slightly).
                float delta = Mathf.Clamp((float)drift * 0.4f, -0.15f * baseSpeed, 0.15f * baseSpeed);
                _videoPlayer.playbackSpeed = Mathf.Max(0.1f, baseSpeed + delta);
            }
            else
            {
                _videoPlayer.playbackSpeed = baseSpeed;
            }
        }

        void SizeRenderTexture()
        {
            uint videoWidth = _videoPlayer.width;
            uint videoHeight = _videoPlayer.height;

            int width = videoWidth > 0u ? (int)videoWidth : 640;
            int height = videoHeight > 0u ? (int)videoHeight : 360;

            // Scale down proportionally so the texture keeps the video's aspect ratio.
            float scale = 1.0f;
            if (width > kMaxRenderWidth)
                scale = kMaxRenderWidth / (float)width;

            if (height * scale > kMaxRenderHeight)
                scale = kMaxRenderHeight / (float)height;

            width = Mathf.Max(16, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(16, Mathf.RoundToInt(height * scale));

            if (_renderTexture.width == width && _renderTexture.height == height)
                return;

            _renderTexture.Release();
            _renderTexture.width = width;
            _renderTexture.height = height;
            _renderTexture.autoGenerateMips = false;
            _renderTexture.useMipMap = false;
            _renderTexture.filterMode = FilterMode.Bilinear;
            _renderTexture.Create();
        }

        #endregion

        #region Sync

        /// <summary>
        /// Song time, in seconds, taken from the song audio stream. This is the exact clock
        /// <see cref="MovementController"/> uses to keep the highway in step, which is what makes
        /// the video and the chart agree.
        /// </summary>
        public float CurrentSongTime()
        {
            if (_editor == null || _editor.currentSongAudio == null)
                return 0.0f;

            AudioStream mainAudio = _editor.currentSongAudio.mainSongAudio;
            if (AudioManager.StreamIsValid(mainAudio) && mainAudio.IsPlaying())
                return mainAudio.CurrentPositionSeconds - _editor.services.totalSongAudioOffset;

            return _editor.currentVisibleTime;
        }

        bool IsChartPlaying()
        {
            if (_editor == null || _editor.currentSongAudio == null)
                return false;

            if (_editor.currentState != ChartEditor.State.Playing)
                return false;

            AudioStream mainAudio = _editor.currentSongAudio.mainSongAudio;
            return AudioManager.StreamIsValid(mainAudio) && mainAudio.IsPlaying();
        }

        /// <summary>The video position the song is currently asking for. May be negative.</summary>
        double TargetVideoTime()
        {
            return (double)(CurrentSongTime() - SoundOffset) + VideoOffset;
        }

        void Tick()
        {
            if (_overlayRoot == null)
                return;

            ApplyLayout();
            UpdateHud();

            if (!HasVideo || _state == VideoGuideState.Error)
                return;

            if (_state == VideoGuideState.Preparing || !_videoPlayer.isPrepared)
                return;

            // Safety timeout: if VideoPlayer failed to fire seekCompleted within kSeekTimeout
            if (_isSeeking && (Time.realtimeSinceStartup - _seekIssuedAt) >= kSeekTimeout)
            {
                _isSeeking = false;
                if (_hasPendingSeek)
                {
                    _hasPendingSeek = false;
                    StartSeek(_pendingSeekTarget);
                    return;
                }
            }

            double target = TargetVideoTime();
            double length = _videoPlayer.length;

            if (!IsChartPlaying())
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Pause();

                if (_state == VideoGuideState.Playing)
                    _state = VideoGuideState.Paused;

                // While paused, update frame to match song position if user scrubbed or moved timeline
                if (!_isSeeking && target >= 0.0 && (length <= 0.0 || target <= length))
                {
                    double pauseDrift = target - _videoPlayer.time;
                    if (Math.Abs(pauseDrift) > 0.06)
                    {
                        StartSeek(target);
                    }
                }

                return;
            }

            // The song has not reached the point the video starts at yet.
            if (target < 0.0)
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Pause();

                _state = VideoGuideState.WaitingForStart;
                SetOverlayActive(false);
                return;
            }

            SetOverlayActive(true);

            // The song ran past the end of the video.
            if (length > 0.0 && target > length)
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Pause();

                _state = VideoGuideState.Finished;
                return;
            }

            // If a seek is currently resolving, wait for it to land unless the target changed significantly
            if (_isSeeking)
            {
                double scrubDiff = target - _pendingSeekTarget;
                if (Math.Abs(scrubDiff) > kHardSeekThreshold)
                {
                    _pendingSeekTarget = target;
                    _hasPendingSeek = true;
                }
                return;
            }

            bool needsStart = !_videoPlayer.isPlaying;
            double drift = target - _videoPlayer.time;

            if (needsStart)
            {
                if (Math.Abs(drift) > 0.08)
                {
                    StartSeek(target);
                }
                else
                {
                    ApplyPlaybackSpeed(drift);
                    _videoPlayer.Play();
                    _state = VideoGuideState.Playing;
                }
            }
            else if (Math.Abs(drift) > kHardSeekThreshold)
            {
                // Significant drift / jump during playback -> hard seek
                StartSeek(target);
            }
            else
            {
                // Minor drift during normal playback -> steer speed smoothly without stuttering
                ApplyPlaybackSpeed(drift);
            }
        }

        void StartSeek(double time)
        {
            if (_videoPlayer == null || !_videoPlayer.isPrepared)
                return;

            double clamped = time < 0.0 ? 0.0 : time;
            double length = _videoPlayer.length;
            if (length > 0.0 && clamped > length)
                clamped = length;

            if (_isSeeking)
            {
                // Queue newest requested position instead of spamming seeks to VideoPlayer
                _pendingSeekTarget = clamped;
                _hasPendingSeek = true;
                return;
            }

            // Pausing around the seek keeps the backend from trying to decode intermediate frames
            // while flushing its pipeline, avoiding decoder starvation and frame drops.
            if (_videoPlayer.isPlaying)
                _videoPlayer.Pause();

            _isSeeking = true;
            _seekIssuedAt = Time.realtimeSinceStartup;
            _videoPlayer.time = clamped;
        }

        #endregion

        #region Overlay layout and hud

        int _lastScreenWidth = -1;
        int _lastScreenHeight = -1;
        float _lastOverlayWidth = -1f;
        float _lastOverlayPosX = -999f;
        float _lastOverlayPosY = -999f;
        float _lastOverlayOpacity = -1f;
        float _lastAspect = -1f;
        float _lastHudUpdate = 0.0f;

        void SetOverlayActive(bool active)
        {
            bool visible = active && VideoGuideStore.Preferences.overlayVisible;

            if (_overlayRoot != null && _overlayRoot.gameObject.activeSelf != visible)
                _overlayRoot.gameObject.SetActive(visible);
        }

        void ApplyLayout()
        {
            if (_overlayRoot == null)
                return;

            VideoGuidePreferences prefs = VideoGuideStore.Preferences;

            int screenW = Screen.width;
            int screenH = Screen.height;
            float aspect = DisplayAspect;

            if (screenW == _lastScreenWidth &&
                screenH == _lastScreenHeight &&
                Mathf.Approximately(prefs.overlayWidth, _lastOverlayWidth) &&
                Mathf.Approximately(prefs.overlayPosX, _lastOverlayPosX) &&
                Mathf.Approximately(prefs.overlayPosY, _lastOverlayPosY) &&
                Mathf.Approximately(prefs.overlayOpacity, _lastOverlayOpacity) &&
                Mathf.Approximately(aspect, _lastAspect))
            {
                return;
            }

            _lastScreenWidth = screenW;
            _lastScreenHeight = screenH;
            _lastOverlayWidth = prefs.overlayWidth;
            _lastOverlayPosX = prefs.overlayPosX;
            _lastOverlayPosY = prefs.overlayPosY;
            _lastOverlayOpacity = prefs.overlayOpacity;
            _lastAspect = aspect;

            float width = Mathf.Max(64.0f, screenW * prefs.overlayWidth);
            float height = width / aspect;

            if (height > screenH * 0.95f)
            {
                height = screenH * 0.95f;
                width = height * aspect;
            }

            Vector2 position = new Vector2(prefs.overlayPosX * screenW, prefs.overlayPosY * screenH);
            Vector2 centre = new Vector2(0.5f, 0.5f);

            RectTransform frameRect = (RectTransform)_frameImage.transform;
            VideoGuideUI.Anchor(frameRect, centre, centre, position, new Vector2(width, height));

            RectTransform backdropRect = (RectTransform)_backdrop.transform;
            VideoGuideUI.Anchor(backdropRect, centre, centre, position, new Vector2(width + 10.0f, height + 10.0f));

            Color frameColor = Color.white;
            frameColor.a = prefs.overlayOpacity;
            _frameImage.color = frameColor;

            RectTransform hudRect = (RectTransform)_hudText.transform;
            VideoGuideUI.Anchor(hudRect, centre, new Vector2(0.5f, 0.0f), position + new Vector2(0.0f, height * 0.5f + 8.0f), new Vector2(Mathf.Max(200.0f, width), 60.0f));
        }

        void UpdateHud()
        {
            if (_hudText == null)
                return;

            if (_state == VideoGuideState.Error)
            {
                // Errors always surface even with the debug line off.
                SetHud("VIDEO GUIDE: " + _errorMessage, VideoGuideUI.kWarning);
                return;
            }

            if (!VideoGuideStore.Preferences.showDebugInfo)
            {
                SetHud(string.Empty, VideoGuideUI.kLabel);
                return;
            }

            if (!HasVideo)
            {
                SetHud(string.Empty, VideoGuideUI.kLabel);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - _lastHudUpdate < 0.05f)
                return;
            _lastHudUpdate = now;

            float drift = CurrentDrift;

            SetHud(
                string.Format(
                    "{0}   song {1}   video {2}   drift {3}s   video starts at song {4}s",
                    VideoFileName,
                    FormatTime(CurrentSongTime()),
                    FormatTime(CurrentVideoTime),
                    (drift >= 0.0f ? "+" : string.Empty) + drift.ToString("0.000"),
                    SoundOffset.ToString("0.000")),
                VideoGuideUI.kLabel);
        }

        /// <summary>
        /// Assigns to the HUD only when the text or colour actually changed. The status line is
        /// rebuilt every frame, and pushing an identical string into a Text component still dirties
        /// the mesh and the canvas.
        /// </summary>
        void SetHud(string text, Color color)
        {
            if (_hudText.text != text)
                _hudText.text = text;

            if (_hudText.color != color)
                _hudText.color = color;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds < 0.0f)
                seconds = 0.0f;

            int total = Mathf.FloorToInt(seconds);
            return string.Format("{0:00}:{1:00}.{2:00}", total / 60, total % 60, Mathf.FloorToInt((seconds - total) * 100.0f));
        }

        #endregion

        void Update()
        {
            Tick();
        }

        void OnDestroy()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnPrepareCompleted;
                _videoPlayer.errorReceived -= OnErrorReceived;
                _videoPlayer.loopPointReached -= OnVideoEnded;
                _videoPlayer.seekCompleted -= OnSeekCompleted;
                _videoPlayer.Stop();
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            if (Instance == this)
                Instance = null;
        }
    }
}
