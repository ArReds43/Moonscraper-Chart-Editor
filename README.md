> **⚠️ NOTE: THIS IS NOT THE APPLICATION PROGRAM, THESE ARE THE SOURCE FILES. ⚠️**
>
> If you are looking to download Moonscraper Chart Editor please see the
> [releases page](https://github.com/FireFox2000000/Moonscraper-Chart-Editor/releases) or visit the [official website](https://moonscrapercharteditor.com/).
>
> The releases page and moonscrapercharteditor.com are the only official sources for distribution. Any alternative sources should NOT be trusted. 

## About
Visit the [About page](https://firefox2000000.github.io/Moonscraper-Chart-Editor/) for more information and download links.

*Note that as Moonscraper Chart Editor 2 is currently in development this repository will no longer receive any major updates.

## Compiling from source 
Follow the instructions below for your desired platform to build and run from source.

### All Platforms
1. Download and install Unity 2018.4.23f1
2. Run Unity and open the project folder with it
3. Use the menu option Build Processes > Build Full Releases
  - Note that 7zip and Inno Setup are required to be installed to build distributables and installers respectively. 

### Runtime dependencies (Windows)
Required runtime dependencies are included with the build.

### Runtime dependencies (Linux)
The application requires the following dependencies to be installed:
- `ffmpeg sdl2 libx11-6 libgtk-3-0`
- `libbass` (included with the build)

A [`PKGBUILD` file for Arch Linux](aur/PKGBUILD) is included in the repository.

Other distribution packagers can use the `PKGBUILD` file for reference.

## Video Guide

The editor can overlay a silent video (e.g. a drum cam) on the playfield, kept in sync with the
song playback, to use as a positional reference while charting.

### How to use
1. Open the chart you want to attach a video to. The pairing is per-song, so load the chart
   *before* picking a video.
2. Focus the Game view and press the **Toggle Video Guide** key (`F8` by default, see below).
3. In the panel that appears, use **Load video...** to pick a video file.
4. Press Play on the chart. The video follows the cursor and is hidden whenever the song has not
   reached the video's start point yet.

### Controls
- **Video offset (s)** field and the `-0.1` / `-0.01` / `+0.01` / `+0.1` buttons nudge the video
  relative to the song, with the same precision field in **Sound offset (s)** available on top of it.
- **Show video over the highway** toggles the overlay without closing the panel.
- The video can be positioned with the mouse: drag it to move it, and use the green strip on its
  right edge to resize it. It is kept inside the screen at all times.
- The **Clear** button detaches the video, and **Close** hides the panel.
- A status line shows the playing file and the current song/video times plus live drift in ms.

### Configuration
- Offsets, the per-song video path and the overlay/panel layout are saved to `videoguide.json` in
  the per-user settings folder, keyed by chart so each song keeps its own video and alignment.
- The **Toggle Video Guide** key is a normal editor input (see `DefaultControls.json`) and can be
  remapped through the in-editor key binding screen; the runtime reads the user's saved controls.
- Video files whose path contains characters the Windows video backend cannot open (spaces, commas,
  etc.) are played from a temporary sanitised copy automatically.

## License
- See [attribution.txt](https://github.com/FireFox2000000/Moonscraper-Chart-Editor/blob/master/Moonscraper%20Chart%20Editor/Assets/Documentation/attribution.txt) for third party libraries and resources included in this repository.
- See [LICENSE](LICENSE).
- The BASS audio library (a dependency of this application) is a commercial product. While it is free for non-commercial use, please ensure to obtain a valid licence if you plan on distributing any application using it commercially.
- The "Moonscraper" and "Moonscraper Chart Editor" branding and namesake are not covered by the licensing. Copyright to the application and it's branding is automatically granted to only Alexander "FireFox" Ong as per Australian copyright law. Although the source is free to use within the guidance of the linked license, do not use the Moonscraper Chart Editor namesake or branding imagery (such as the logo), attempt to act as the copyright holder, or violate copyright in any way without obtaining explicit permission from myself. 
