// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using UnityEngine;

namespace MoonscraperChartEditor.VideoGuide
{
    /// <summary>
    /// Entry point for the video guide. Creates everything the feature needs at runtime so no
    /// prefab or scene has to be edited, waits for the editor to finish booting, and wires the
    /// panel to the F12 shortcut.
    /// </summary>
    public class VideoGuideBootstrap : MonoBehaviour
    {
        static VideoGuideBootstrap s_instance;

        ChartEditor _editor;
        VideoGuideController _controller;
        VideoGuidePanel _panel;
        GameObject _panelObject;
        bool _setupDone = false;
        bool _warnedMissingEditor = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInstance()
        {
            if (s_instance != null)
                return;

            GameObject go = new GameObject("VideoGuide");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<VideoGuideBootstrap>();
        }

        void Update()
        {
            if (!_setupDone)
            {
                TrySetup();
                return;
            }

            if (MSChartEditorInput.GetInputDown(MSChartEditorInputActions.ToggleVideoGuide))
            {
                if (_panel == null)
                {
                    Debug.LogError("Video Guide: F12 was pressed but the panel was never built.");
                    return;
                }

                _panel.Toggle();
            }
        }

        void TrySetup()
        {
            // The editor is not in the first scene of the build, so wait for it to appear instead
            // of reading ChartEditor.Instance, which would try to generate a duplicate editor.
            // FindObjectOfType has no side effects. ChartEditor.InstanceExists is deliberately not
            // used: that flag is only set once something reads ChartEditor.Instance, which nothing
            // does this early, so waiting on it can never succeed.
            _editor = FindObjectOfType<ChartEditor>();
            if (_editor == null)
            {
                if (!_warnedMissingEditor)
                {
                    _warnedMissingEditor = true;
                    Debug.LogWarning("Video Guide: waiting for the ChartEditor to appear...");
                }

                return;
            }

            try
            {
                _controller = gameObject.AddComponent<VideoGuideController>();
                _controller.Initialise(_editor);

                _editor.events.songLoadedEvent.Register(OnSongLoaded);

                BuildPanel();
                _setupDone = true;

                Debug.Log("Video Guide: ready. Press F12 to open the video guide panel.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("Video Guide: setup failed. " + e);
                _setupDone = true;
            }
        }

        void BuildPanel()
        {
            // Built inactive: the panel's Awake needs the controller, and it must not run its
            // OnEnable refresh before the widgets exist.
            _panelObject = new GameObject("VideoGuidePanel");
            _panelObject.transform.SetParent(transform, false);
            _panelObject.SetActive(false);

            _panel = _panelObject.AddComponent<VideoGuidePanel>();
        }

        void OnSongLoaded()
        {
            // Each chart remembers its own reference video and offsets.
            _controller.BindToCurrentSong();
        }

        void OnApplicationQuit()
        {
            VideoGuideStore.Save();
        }

        void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }
    }
}
