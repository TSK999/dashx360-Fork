# DashX360, an Xbox 360 Metro Dashboard for Windows

The first fanmade recreation of the Xbox 360 metro dashboard experience for Windows with tile navigation, controller support, Guide overlays, local profile data, custom themes, boot media, and dashboard audio cues.

If you like my work, feel free to donate to my ko-fi! however money will never be needed to use this! https://Ko-fi.com/zivvoz

Original app credit: ZivvoZ
https://youtube.com/@zivvoz

## Version 1.2.2 New Features

* Added Spotify media controls.
* Added a YouTube app.
* Added widescreen support.
* Added dashboard volume control under Audio Settings.
* Added more visual-only loading menus (optional)
* Support for adding shortcuts and URLS to tiles and shortcuts in My Games/My Apps
* Overhauled dashboard sounds, and menu animations.

## Bug Fixes and Improvements

* Tweaked dashboard tab positioning and alignment.
* Refined the Music Player, Apps, Profile, and Add Friend menus.
* Improved navigation in the Friends and Themes menus.
* The on-screen **(B) Back** button can now be clicked with a mouse.
* Fixed achievement information not displaying when opening a game.
* Fixed broken keyboard navigation in the Friends and Themes menus.
* Fixed Steam friends showing **Send Friend Request** instead of **Remove Friend**.
* Additional bug fixes, stability improvements, and quality-of-life changes.

## Features

- Xbox 360-inspired dashboard tabs for games, apps, music, video, social, Bing, and settings
- Controller-first navigation with keyboard and mouse support
- Xbox Guide overlay with Friends, Party, Profile, media controls, achievements, and search screens
- Local profile and friend data with cached gamer pictures
- Boot video, dashboard audio cues, and Metro-style tile presentation
- Custom theme support
- Steam library scanning with Steam-provided cover art
- Import/export support for user data transfer and version updates

## How to Use
1. Launch the application.
2. Connect your controller.
3. In Steam, turn off Enable Guide Button Chords for controllers.
4. Use the Back + Start buttons together (or Win + Left Shift + Left Ctrl) to open the Guide.
5. Navigate with the controller just like the original Xbox 360 dashboard.


### Requirements

- Untick `Enable Guide Button Chords for controllers` to use the guide if using steam
- Windows 10 or Windows 11

### Working on the Project

DashX360 is open for people who want to help improve it. If you use this project, modify it, or build on top of it, please credit the original project and creator:

Original project by zivvoz / DashX360

Do not reupload or redistribute modified versions in a way that makes it look like you created the original project from scratch.

## Controls

- `A` / `Enter`: select
- `B` / `Escape`: back
- `X`: context actions where available
- `Y`: secondary actions where available
- Win + Ctrl + Shift / Back + Start : open the Xbox Guide overlay

## Legal / Disclaimer

This is an unofficial, non-commercial fan project. Xbox, Xbox 360, Xbox LIVE, Microsoft, and related names, logos, and imagery are property of Microsoft. This project is not affiliated with, endorsed by, or sponsored by Microsoft.


## Building and data storage

See [the stability repair notes](docs/stability-repairs.md) for build commands, regression checks, backup compatibility, data migration and desktop acceptance checks. The default writable data location is `%LOCALAPPDATA%\DashX360\UserData`; use `--portable` to keep data beside the application.
