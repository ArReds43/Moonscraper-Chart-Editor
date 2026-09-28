// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoonscraperChartEditor.VideoGuide
{
    /// <summary>
    /// Per song video guide data. This is deliberately kept out of the .chart/.msce file
    /// because the video file itself is a machine local asset, and a chart file with an
    /// absolute path baked in would be useless (and noisy) to anyone it gets shared with.
    /// </summary>
    [Serializable]
    public class VideoGuideSongSettings
    {
        /// <summary>Normalised full path of the chart file these settings belong to.</summary>
        public string chartKey = string.Empty;

        /// <summary>Absolute path of the reference video, or empty when none is set.</summary>
        public string videoPath = string.Empty;

        /// <summary>
        /// The point in the song (in seconds) that the video's own 0:00 lines up with.
        /// With 0 the video simply plays in step with the song audio from the start.
        /// </summary>
        public float soundOffset = 0.0f;

        /// <summary>
        /// Extra trim applied on top of <see cref="soundOffset"/>. Positive values delay the
        /// video, negative values pull it earlier. Useful for nudging a video into frame
        /// accurate alignment without moving the song anchor point.
        /// </summary>
        public float videoOffset = 0.0f;

        public VideoGuideSongSettings()
        {
        }

        public VideoGuideSongSettings(string key)
        {
            chartKey = key;
        }
    }

    /// <summary>Overlay layout preferences, shared by every song.</summary>
    [Serializable]
    public class VideoGuidePreferences
    {
        public bool overlayVisible = true;
        public float overlayWidth = 0.55f;
        public float overlayPosX = 0.0f;
        public float overlayPosY = 0.0f;
        public float overlayOpacity = 1.0f;

        /// <summary>Panel position, as an offset from the canvas top-right corner.</summary>
        public float panelPosX = -20.0f;
        public float panelPosY = -20.0f;
        public float panelWidth = VideoGuideUI.kRowWidthReference;
    }

    [Serializable]
    public class VideoGuideSaveData
    {
        public List<VideoGuideSongSettings> songs = new List<VideoGuideSongSettings>();
        public VideoGuidePreferences preferences = new VideoGuidePreferences();
    }

    /// <summary>
    /// Loads/saves the video guide data as JSON inside the Moonscraper user data folder,
    /// alongside config.ini and controls.json.
    /// </summary>
    public static class VideoGuideStore
    {
        const string kFileName = "videoguide.json";

        /// <summary>Key used for a song that has never been saved to disk.</summary>
        public const string UNSAVED_SONG_KEY = "<unsaved song>";

        static VideoGuideSaveData s_data = null;

        public static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, kFileName); }
        }

        public static VideoGuideSaveData Data
        {
            get
            {
                if (s_data == null)
                    Load();

                return s_data;
            }
        }

        public static VideoGuidePreferences Preferences
        {
            get { return Data.preferences; }
        }

        /// <summary>
        /// Builds the lookup key for a chart. Case and slash direction are normalised so the
        /// same song always resolves to the same entry.
        /// </summary>
        public static string KeyForChart(string chartFilePath)
        {
            if (string.IsNullOrEmpty(chartFilePath))
                return UNSAVED_SONG_KEY;

            try
            {
                return Path.GetFullPath(chartFilePath).Replace('\\', '/').ToLowerInvariant();
            }
            catch (Exception)
            {
                // Malformed paths (or paths on a disconnected drive) fall back to a literal key.
                return chartFilePath.Replace('\\', '/').ToLowerInvariant();
            }
        }

        public static VideoGuideSongSettings GetSongSettings(string chartFilePath)
        {
            string key = KeyForChart(chartFilePath);
            List<VideoGuideSongSettings> songs = Data.songs;

            for (int i = 0; i < songs.Count; ++i)
            {
                if (songs[i] != null && songs[i].chartKey == key)
                    return songs[i];
            }

            VideoGuideSongSettings created = new VideoGuideSongSettings(key);
            songs.Add(created);
            return created;
        }

        public static void Load()
        {
            s_data = new VideoGuideSaveData();

            string path = FilePath;
            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                    return;

                VideoGuideSaveData loaded = JsonUtility.FromJson<VideoGuideSaveData>(json);
                if (loaded == null)
                    return;

                if (loaded.songs == null)
                    loaded.songs = new List<VideoGuideSongSettings>();

                // Drop empty/corrupt entries so a bad hand edit cannot break every lookup.
                loaded.songs.RemoveAll(song => song == null || string.IsNullOrEmpty(song.chartKey));

                if (loaded.preferences != null)
                    loaded.preferences = Sanitise(loaded.preferences);
                else
                    loaded.preferences = new VideoGuidePreferences();

                s_data = loaded;
            }
            catch (Exception e)
            {
                Logger.LogException(e, "Failed to read the video guide config, starting from defaults.");
                s_data = new VideoGuideSaveData();
            }
        }

        public static void Save()
        {
            if (s_data == null)
                return;

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(s_data, true), System.Text.Encoding.UTF8);
            }
            catch (Exception e)
            {
                Logger.LogException(e, "Failed to write the video guide config.");
            }
        }

        static VideoGuidePreferences Sanitise(VideoGuidePreferences prefs)
        {
            prefs.overlayWidth = Mathf.Clamp(prefs.overlayWidth, VideoGuideLayout.kMinWidthFraction, VideoGuideLayout.kMaxWidthFraction);
            prefs.overlayPosX = Mathf.Clamp(prefs.overlayPosX, -0.5f, 0.5f);
            prefs.overlayPosY = Mathf.Clamp(prefs.overlayPosY, -0.5f, 0.5f);
            prefs.overlayOpacity = Mathf.Clamp01(prefs.overlayOpacity);
        // The panel is anchored to the top-right corner, so negative values pull it inwards.
        prefs.panelPosX = Mathf.Clamp(prefs.panelPosX, -4000.0f, 4000.0f);
        prefs.panelPosY = Mathf.Clamp(prefs.panelPosY, -4000.0f, 4000.0f);
        prefs.panelWidth = Mathf.Max(VideoGuideResizeHandle.kMinPanelWidth, prefs.panelWidth);
            return prefs;
        }
    }

    /// <summary>Shared limits for the overlay layout, used by both the panel and the store.</summary>
    public static class VideoGuideLayout
    {
        public const float kMinWidthFraction = 0.15f;
        public const float kMaxWidthFraction = 0.98f;
        public const float kPosRange = 0.5f;
    }
}
