# Graphics Settings Migrator

Windows GUI utility for backing up and moving settings between versions or PCs.

## Supported applications (44)

The scanner only shows settings that exist on the current PC. Restore can map a
backup to another installed version or to an editable custom target path.

### Adobe and texturing

- Adobe Photoshop
- Adobe Illustrator
- Adobe After Effects
- Adobe Media Encoder
- Adobe Camera Raw
- Adobe Lightroom Classic
- Adobe Substance 3D Painter
- Adobe Substance 3D Designer
- Adobe Substance 3D Modeler
- Adobe Substance 3D Sampler
- 3DCoat

### 3D, CAD, rendering, and game tools

- Blender
- Autodesk Maya
- Autodesk 3ds Max
- Cinema 4D
- Houdini
- Maxon ZBrush
- Plasticity
- Rhino
- Grasshopper
- SketchUp
- Nuke
- Mari
- Modo
- Marmoset Toolbag
- Marvelous Designer
- CLO
- KeyShot
- Unreal Engine
- Unity
- Godot

### 2D, painting, and design

- Affinity Photo 2
- Affinity Designer 2
- Affinity Publisher 2
- Krita
- GIMP
- Inkscape
- CorelDRAW
- Corel Painter
- Clip Studio Paint
- paint.net
- Aseprite
- PureRef
- Capture One

See [SUPPORTED_APPS.md](SUPPORTED_APPS.md) for scope notes and application-specific details.

## Safety model

- Backup packages are ordinary portable folders with manifest.json and payload.
- Every payload file has a SHA-256 checksum.
- Restore merges files and never deletes extra target files.
- Existing destination files are copied to
  Documents\GraphicsSettingsMigrator Rollbacks before overwrite.
- Running graphics applications block restore.
- Paths use portable profile tokens, so a package can be moved to another Windows user or PC.
- Custom scripts, plug-ins, extensions, presets, brushes, materials, packages,
  and libraries stored outside the usual preferences folder are included where
  their locations are known.

## Plug-in discovery

In addition to plug-ins already contained in normal application profiles, the
scanner checks install-level, shared, and environment-configured locations for
ZBrush, Autodesk/Maya/3ds Max, Cinema 4D, Houdini, Nuke/OpenFX, Blender, Mari,
paint.net, Capture One, and Unreal Engine. Adobe installation plug-ins and
CEP/UXP extensions remain supported. Native binaries are shown separately
because they may require administrator rights and may not work across versions.

Project plug-ins belonging to Unity, Godot, or Unreal projects remain inside
those projects and are not discovered by a global disk search.

## Selection and cache handling

- Use Ctrl or Shift to highlight multiple rows, then click any highlighted
  checkbox or press Space to toggle all of their checkboxes.
- **Auto-select folders up to** is saved between runs and defaults to 500 MB.
  Larger folders remain visible but unchecked; set it to `0` for no size limit.
  **Select / clear all** also respects this limit.
- **Select / clear all** selects normal settings but deliberately skips every
  cache-containing set. Cache rows can only be enabled manually.
- Choosing a backup folder with **Backup...** on the Restore tab loads its
  manifest immediately. **Load backup** remains available for a pasted or
  manually edited path.

## Removing old settings

Highlight one or more rows on the Backup tab and choose **Remove selected...**.
Removal is blocked while a supported graphics application is running and always
requires an explicit Yes/No confirmation (No is the default). Before anything
is removed, the selected data is backed up to
`Documents\GraphicsSettingsMigrator Removed Settings`. Only files whose SHA-256
still matches that recovery backup are deleted. Excluded projects, scenes, and
caches remain untouched unless their own row is explicitly highlighted.

## Updates

Version 1.3.0 and later can update themselves from the repository's latest
GitHub Release. Use **Check for updates** on the Backup tab. The updater:

- downloads only `GraphicsSettingsMigrator-win-x64.zip` from this repository;
- verifies the exact asset size and GitHub-provided SHA-256 digest;
- closes the running application before replacing files;
- copies release files into the current portable folder without deleting other files;
- restarts the updated application and removes its temporary download.

Updating an installation in a protected folder may show a Windows UAC prompt.

## Rollback

Every restore creates a rollback manifest. The Rollback tab can restore overwritten files and registry settings and remove files created by that restore. Files changed again after the restore are skipped to protect newer work. Rollbacks created by older versions remain available for manual recovery.

## Build

Run:

    dotnet build -c Release
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

For cross-version migration, review the editable target path before restoring.
Application presets are generally safer to move between versions than complete binary preference files.
