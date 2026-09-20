import bpy
import json
from pathlib import Path


ROOT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1")
MASTER_PATH = ROOT / "ENV_FS_StreetTheme_AssetLibrary_Master.blend"
REPORT_PATH = ROOT / "01_Architecture" / "Reports" / "ENV_FS_StreetTheme_ArchitectureMaster_Validation_v01.json"
RENDER_PATH = ROOT / "Renders" / "Architecture_Master" / "ENV_FS_ArchitectureMaster_Overview.png"


def linked_data_blocks():
    linked = []
    for group_name in ("collections", "objects", "meshes", "materials", "images", "cameras", "lights", "curves", "node_groups"):
        for datablock in getattr(bpy.data, group_name):
            if datablock.library is not None:
                linked.append(f"{group_name}:{datablock.name}")
    return linked


def missing_images():
    missing = []
    for image in bpy.data.images:
        if image.source != "FILE" or image.packed_file is not None:
            continue
        resolved = Path(bpy.path.abspath(image.filepath))
        if not resolved.exists():
            missing.append(str(resolved))
    return missing


def main():
    master = bpy.data.collections.get("COL_FS_StreetThemeAssetLibrary")
    expected_assets = {
        "COL_FS_AR01_BlankSidingWall4m": ("AR-01", "v01", "APPROVED_FROZEN", True),
        "COL_FS_AR02_WindowWall4m": ("AR-02", "v01", "APPROVED_FROZEN", True),
        "COL_FS_AR03_EntranceWall4m": ("AR-03", "v01", "PENDING_USER_APPROVAL", False),
    }
    expected_categories = {
        "COL_FS_Architecture", "COL_FS_Road", "COL_FS_Vegetation", "COL_FS_StreetFurniture",
        "COL_FS_Playground", "COL_FS_Vehicles", "COL_FS_Obstacles", "COL_FS_Background",
        "COL_FS_MasterCameras", "COL_FS_MasterLights", "COL_FS_MasterReviewOnly",
    }

    asset_checks = {}
    for name, (task_id, version, state, locked) in expected_assets.items():
        collection = bpy.data.collections.get(name)
        asset_checks[name] = {
            "exists": collection is not None,
            "task_id": collection.get("task_id") == task_id if collection else False,
            "source_version": collection.get("source_version") == version if collection else False,
            "approval_state": collection.get("approval_state") == state if collection else False,
            "selection_lock": collection.hide_select == locked if collection else False,
            "asset_browser_marked": collection.asset_data is not None if collection else False,
            "instance_offset": list(collection.instance_offset) if collection else None,
        }

    linked = linked_data_blocks()
    missing = missing_images()
    render_image = bpy.data.images.load(str(RENDER_PATH), check_existing=False) if RENDER_PATH.exists() else None
    render_size = list(render_image.size) if render_image else [0, 0]
    if render_image:
        bpy.data.images.remove(render_image)

    checks = {
        "master_reopens_at_expected_path": Path(bpy.data.filepath) == MASTER_PATH,
        "master_root_exists": master is not None,
        "category_structure_complete": master is not None and expected_categories.issubset({child.name for child in master.children}),
        "asset_metadata_complete": all(all(values.values()) for values in asset_checks.values()),
        "no_linked_external_datablocks": not linked,
        "no_missing_file_images": not missing,
        "metric_scale": bpy.context.scene.unit_settings.system == "METRIC" and bpy.context.scene.unit_settings.scale_length == 1.0,
        "authoring_axis_plus_y": master is not None and master.get("authoring_forward_axis") == "+Y",
        "unity_integration_disabled": master is not None and master.get("unity_integration") is False,
        "render_is_1920x1080": render_size == [1920, 1080],
    }

    report = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
    report["reopen_qa_verdict"] = "SHIP" if all(checks.values()) else "NO-SHIP"
    report["reopen_checks"] = checks
    report["reopen_asset_checks"] = asset_checks
    report["reopen_linked_external_datablocks"] = linked
    report["reopen_missing_images"] = missing
    report["reopen_render_size"] = render_size
    REPORT_PATH.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"reopen_qa_verdict": report["reopen_qa_verdict"], "checks": checks}, indent=2))


if __name__ == "__main__":
    main()
