# Supported application catalog

The scanner only shows settings that exist on the current PC. Restore also
uses installed-version rules to map a backup to a newer version.

## Adobe and texturing

- Adobe Photoshop
- Adobe Illustrator
- Adobe After Effects
- Adobe Media Encoder
- Adobe Camera Raw
- Adobe shared CEP/UXP extensions, color settings, and shared plug-ins
- Adobe Lightroom Classic
- Adobe Substance 3D Painter
- Adobe Substance 3D Designer
- Adobe Substance 3D Modeler
- Adobe Substance 3D Sampler
- 3DCoat

## 3D, CAD, rendering, and game tools

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
- Unreal Engine global editor configuration
- Unity file-based editor preferences and layouts
- Godot

## 2D, painting, and design

- Affinity Photo 2
- Affinity Designer 2
- Affinity Publisher 2
- Krita, including Microsoft Store installs
- GIMP
- Inkscape
- CorelDRAW
- Corel Painter
- Clip Studio Paint
- paint.net
- Aseprite
- PureRef
- Capture One

## Scope notes

- The scanner includes custom scripts, plug-ins, extensions, presets, brushes,
  materials, packages, and libraries when applications store them outside the
  normal user-settings profile.
- Installation-level content is included for Photoshop, Illustrator, and After
  Effects. Restoring it under Program Files or Common Files may require running
  the utility as administrator.
- Native binary plug-ins can be tied to a specific application version. Review
  the editable restore target and the Preview before copying them to a new version.
- Project files, renders, autosaves, caches, crash dumps, and licensing data
  are intentionally excluded where their locations are known.
- Unreal Engine project-specific settings remain inside each project's
  Saved\Config directory and are not scanned globally.
- Some programs must be launched once before their user profile directory
  exists.
- Cross-version migration of presets and text configuration is generally
  safer than copying opaque binary preference files.
