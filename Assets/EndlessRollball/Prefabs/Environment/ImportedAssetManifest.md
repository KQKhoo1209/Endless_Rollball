# Imported Environment Asset Manifest

These assets are currently restricted to the Futuristic Street review workflow. Their original store URLs, authors and licenses were not included with the downloaded folders and must be recorded before a distributable build.

| Asset | Unity source | Review use | Source / author / license | Import notes |
|---|---|---|---|---|
| Low-poly city street pack | `Street/source/city.fbx` | Distant and roadside building scenery | Unverified; supply the original Sketchfab or Asset Store listing and license | 167 renderable model objects. Unity reports self-intersecting polygons in several meshes and inconsistent `_LOD1` naming. It is visual-only and route-crossing renderers are filtered by the preview builder. |
| Small trees | `Trees/small-trees/source/Small Trees.fbx` | Three repeated roadside tree clusters | Unverified; supply the original listing and license | Three tree variants, separated into trunk and leaf meshes. The preview builder creates URP material copies and enables alpha clipping for foliage. |
| Palm trees | `Trees/palm-trees/source/PalmTrees.fbx` | Two restrained roadside accent clusters; Water Theme Park use is conditional on licence verification | Unverified; supply the original listing and license | Four palm variants, separated into bark and leaf meshes. The preview builder creates URP material copies and enables alpha clipping for foliage. |
| Futuristic Street skybox | `Art/Environment/FuturisticStreet/Skybox/ENV_FS_Skybox_BlueHourCity.jpg` | Far blue-hour skyline | Poly Haven, Modern Buildings Night, Greg Zaal, CC0 | Project colour treatment; original downloaded JPG retained beside the review texture. |
| Water Theme Park skybox | `Art/Environment/WaterThemePark/Skybox/WP_Skybox_Kloppenheim05_4K.jpg` | Far tropical late-morning sky | Poly Haven, Kloppenheim 05 Pure Sky, Greg Zaal and Jarod Guest, CC0 | 4096 x 2048 source panorama; Unity import is capped at 2048 on all configured targets. |
| Ancient Dungeon panorama | `Art/Environment/AncientDungeon/Skybox/T_ENV_AD_SmallCave_8K.jpg` | Far cave closure behind local shell | Poly Haven, Small Cave, Andreas Mischok, CC0 | Lower capture fill is concealed by the dungeon shell and track presentation. |

The imported FBX files are never used for track collision, route geometry or gameplay behaviour. `TRK_Gap` and the Unity-authored track colliders remain authoritative.
