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

## Rollback

Every restore creates a rollback manifest. The Rollback tab can restore overwritten files and registry settings and remove files created by that restore. Files changed again after the restore are skipped to protect newer work. Rollbacks created by older versions remain available for manual recovery.

## Build

Run:

    dotnet build -c Release
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

For cross-version migration, review the editable target path before restoring.
Application presets are generally safer to move between versions than complete binary preference files.
