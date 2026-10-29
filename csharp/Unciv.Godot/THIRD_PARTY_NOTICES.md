# Unciv (Godot client): licence and third-party notices

Shipped next to every exported build (copied by `tools/package.sh`).

## Unciv

The Unciv engine and client are licensed under the Mozilla Public License 2.0; the full text is in `LICENSE` in the same folder
(the repository's `LICENSE`). Source: https://github.com/yairm210/Unciv

The ruleset data in `data/jsons` (units, buildings, techs, translations and so on) is Unciv's own data, distributed under the same licence.
Unciv's atlases, sounds and images are **not** bundled: the client draws generated placeholder art. No Firaxis / Civ 5 asset is included.

## Godot Engine

Built with and running on the Godot Engine 4.7.2 (.NET), copyright Juan Linietsky, Ariel Manzur and Godot Engine contributors, MIT licence
(https://godotengine.org/license). The engine bundles further third-party components (for example the Open Sans font under the SIL Open Font
License 1.1, FreeType, zlib, mbedTLS, and others); their notices are listed in the Godot licence documentation at
https://docs.godotengine.org/en/stable/about/complying_with_licenses.html and in the `Godot > About > Third-party licenses` list of the editor.

## Noto fonts

The fallback fonts behind the client's default font and the type-scale faces (Noto Serif for screen titles and banners; Noto Sans at several weights) (Latin/Cyrillic/Greek, Arabic, Hebrew, Thai, Devanagari, Bengali and a subset of Noto Sans CJK
for Chinese, Japanese and Korean) are Google Noto fonts, copyright The Noto Project Authors (https://github.com/notofonts) and Adobe (CJK), under
the SIL Open Font License 1.1. The licence text is `Assets/fonts/OFL.txt` in the project and is copied with the exported fonts.

## .NET runtime and NuGet packages

Runs on the .NET runtime (MIT licence, Microsoft and contributors). The client assemblies use only the packages restored from
`Directory.Packages.props` (see that file for names and versions); each carries its own licence in its NuGet package.

Asset provenance for the whole client is tracked in `csharp/migration/ASSETS.md`.
