# Graphics Settings Migrator

Windows GUI utility for backing up and moving settings between versions or PCs.

Supported discovery rules:

- Adobe Photoshop, Illustrator, After Effects, Media Encoder and Camera Raw
- Adobe Substance 3D Painter, Designer, Modeler and Sampler
- Maxon ZBrush
- 3DCoat
- Plasticity

## Safety model

- Backup packages are ordinary portable folders with manifest.json and payload.
- Every payload file has a SHA-256 checksum.
- Restore merges files and never deletes extra target files.
- Existing destination files are copied to
  Documents\GraphicsSettingsMigrator Rollbacks before overwrite.
- Running graphics applications block restore.
- Paths use portable profile tokens, so a package can be moved to another Windows user or PC.

## Build

Run:

    dotnet build -c Release
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

For cross-version migration, review the editable target path before restoring.
Application presets are generally safer to move between versions than complete binary preference files.
