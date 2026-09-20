import bpy
import hashlib
import json
import math
from pathlib import Path
from mathutils import Vector


LIBRARY_ROOT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1")
OUTPUT_BLEND = LIBRARY_ROOT / "ENV_FS_StreetTheme_AssetLibrary_Master.blend"
RENDER_DIR = LIBRARY_ROOT / "Renders" / "Architecture_Master"
REPORT_DIR = LIBRARY_ROOT / "01_Architecture" / "Reports"
BRIEF_PATH = LIBRARY_ROOT / "01_Architecture" / "ENV_FS_StreetTheme_ArchitectureMaster_ProductionBrief_v01.json"
REPORT_PATH = REPORT_DIR / "ENV_FS_StreetTheme_ArchitectureMaster_Validation_v01.json"
RENDER_PATH = RENDER_DIR / "ENV_FS_ArchitectureMaster_Overview.png"

ASSETS = [
    {
        "task_id": "AR-01",
        "version": "v01",
        "label": "AR-01  BLANK WALL",
        "source": LIBRARY_ROOT / "01_Architecture" / "ENV_FS_AR-01_BlankSidingWall4m_v01.blend",
        "master_collection": "COL_FS_AR01_BlankSidingWall4m",
        "offset_x": -5.0,
        "approval_state": "APPROVED_FROZEN",
        "qa_verdict": "SHIP",
        "expected_sha256": "7F450600151AB4A31F99F22FC08365CD5891CE5FBCA3ED1871B067A46F25D8A7",
    },
    {
        "task_id": "AR-02",
        "version": "v01",
        "label": "AR-02  WINDOW WALL",
        "source": LIBRARY_ROOT / "01_Architecture" / "ENV_FS_AR-02_WindowWall4m_v01.blend",
        "master_collection": "COL_FS_AR02_WindowWall4m",
        "offset_x": 0.0,
        "approval_state": "APPROVED_FROZEN",
        "qa_verdict": "SHIP",
        "expected_sha256": "A819318926E30DD2CD7E1998A85D20ED20E1A6FE9DE3F11688D29A53E8E3369B",
    },
    {
        "task_id": "AR-03",
        "version": "v01",
        "label": "AR-03 v01  ENTRANCE WALL",
        "source": LIBRARY_ROOT / "01_Architecture" / "ENV_FS_AR-03_EntranceWall4m_v01.blend1",
        "master_collection": "COL_FS_AR03_EntranceWall4m",
        "offset_x": 5.0,
        "approval_state": "PENDING_USER_APPROVAL",
        "qa_verdict": "SHIP",
        "expected_sha256": "E7B41313B684767EFA1873ABF963ADB7BD2A83A06BBACC7A2F7B43BEC130AB4E",
    },
]


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0


def new_collection(name, parent):
    collection = bpy.data.collections.new(name)
    parent.children.link(collection)
    return collection


def import_asset(asset, architecture_collection):
    with bpy.data.libraries.load(str(asset["source"]), link=False) as (data_from, data_to):
        roots = [name for name in data_from.collections if name.startswith("COL_FS_")]
        if len(roots) != 1:
            raise RuntimeError(f"Expected one COL_FS_ root in {asset['source']}; found {roots}")
        data_to.collections = roots

    root = data_to.collections[0]
    root.name = asset["master_collection"]
    architecture_collection.children.link(root)

    asset_objects = set(root.all_objects)
    for obj in asset_objects:
        if obj.parent not in asset_objects:
            obj.location.x += asset["offset_x"]
        if obj.type == "LIGHT" or "_Reference_" in obj.name or "NeutralFloor" in obj.name:
            obj.hide_render = True

    root.instance_offset = (asset["offset_x"], 0.0, 0.0)
    root.hide_select = asset["approval_state"] == "APPROVED_FROZEN"
    root.color_tag = "COLOR_05" if root.hide_select else "COLOR_03"
    root["task_id"] = asset["task_id"]
    root["source_version"] = asset["version"]
    root["source_file"] = str(asset["source"])
    root["source_sha256"] = sha256(asset["source"])
    root["approval_state"] = asset["approval_state"]
    root["qa_verdict"] = asset["qa_verdict"]
    root["collection_instance_pivot"] = "Ground contact at local origin"
    root["master_catalogue_offset_x_m"] = asset["offset_x"]
    root["historical_source_preserved"] = True
    root.asset_mark()
    root.asset_data.description = (
        f"{asset['task_id']} {asset['version']} Street architecture module; "
        f"{asset['approval_state'].replace('_', ' ').lower()}"
    )
    return root


def consolidate_shared_materials():
    for canonical_name in ("MAT_FS_Siding_White", "MAT_FS_Trim_Cream"):
        canonical = bpy.data.materials.get(canonical_name)
        if canonical is None:
            continue
        duplicates = [
            material for material in list(bpy.data.materials)
            if material != canonical and material.name.startswith(canonical_name + ".")
        ]
        for duplicate in duplicates:
            for obj in bpy.data.objects:
                if not hasattr(obj.data, "materials"):
                    continue
                for index, material in enumerate(obj.data.materials):
                    if material == duplicate:
                        obj.data.materials[index] = canonical
            bpy.data.materials.remove(duplicate)


def make_material(name, color, roughness=0.8):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled:
        principled.inputs["Base Color"].default_value = (*color, 1.0)
        principled.inputs["Roughness"].default_value = roughness
    return material


def link_object(obj, collection):
    for existing in list(obj.users_collection):
        existing.objects.unlink(obj)
    collection.objects.link(obj)


def add_review_stage(review_collection):
    floor_material = make_material("MAT_FS_Review_Floor", (0.17, 0.20, 0.22), 0.9)
    bpy.ops.mesh.primitive_plane_add(size=30.0, location=(0.0, 1.5, -0.025))
    floor = bpy.context.object
    floor.name = "SM_FS_Review_ArchitectureFloor"
    floor.data.name = "SM_FS_Review_ArchitectureFloor_Mesh"
    floor.data.materials.append(floor_material)
    link_object(floor, review_collection)

    scale_material = make_material("MAT_FS_Review_ScaleBlue", (0.10, 0.42, 0.68), 0.65)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.22, depth=1.35, location=(-8.0, -0.5, 0.675))
    body = bpy.context.object
    body.name = "SM_FS_Review_HumanScaleBody"
    body.data.name = "SM_FS_Review_HumanScaleBody_Mesh"
    body.data.materials.append(scale_material)
    link_object(body, review_collection)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.23, location=(-8.0, -0.5, 1.575))
    head = bpy.context.object
    head.name = "SM_FS_Review_HumanScaleHead"
    head.data.name = "SM_FS_Review_HumanScaleHead_Mesh"
    head.data.materials.append(scale_material)
    link_object(head, review_collection)

    for asset in ASSETS:
        curve = bpy.data.curves.new(f"TXT_FS_{asset['task_id']}_Label_Data", type="FONT")
        curve.body = asset["label"]
        curve.align_x = "CENTER"
        curve.align_y = "CENTER"
        curve.size = 0.40
        curve.extrude = 0.008
        label = bpy.data.objects.new(f"TXT_FS_{asset['task_id']}_Label", curve)
        label.location = (asset["offset_x"], 0.25, 3.65)
        label.rotation_euler = (math.radians(90.0), 0.0, 0.0)
        review_collection.objects.link(label)


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def add_camera_and_lights(camera_collection, light_collection):
    camera_data = bpy.data.cameras.new("CAM_FS_ArchitectureMaster_Overview_Data")
    camera = bpy.data.objects.new("CAM_FS_ArchitectureMaster_Overview", camera_data)
    camera_collection.objects.link(camera)
    camera.location = (11.5, -20.0, 7.5)
    camera_data.lens = 52.0
    camera_data.sensor_width = 36.0
    look_at(camera, (0.0, 0.0, 1.55))
    bpy.context.scene.camera = camera

    sun_data = bpy.data.lights.new("LGT_FS_KeySun_Data", type="SUN")
    sun_data.energy = 2.2
    sun_data.angle = math.radians(12.0)
    sun = bpy.data.objects.new("LGT_FS_KeySun", sun_data)
    sun.rotation_euler = (math.radians(35.0), math.radians(-20.0), math.radians(-35.0))
    light_collection.objects.link(sun)

    area_data = bpy.data.lights.new("LGT_FS_SoftFill_Data", type="AREA")
    area_data.energy = 850.0
    area_data.shape = "DISK"
    area_data.size = 8.0
    area = bpy.data.objects.new("LGT_FS_SoftFill", area_data)
    area.location = (-7.0, -8.0, 8.0)
    look_at(area, (0.0, 0.0, 1.3))
    light_collection.objects.link(area)


def evaluated_triangles(collection):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in collection.all_objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    return total


def linked_data_blocks():
    linked = []
    for group_name in ("collections", "objects", "meshes", "materials", "images", "cameras", "lights", "curves", "node_groups"):
        for datablock in getattr(bpy.data, group_name):
            if datablock.library is not None:
                linked.append(f"{group_name}:{datablock.name}")
    return linked


def configure_scene(root_collection):
    scene = bpy.context.scene
    scene.name = "SCN_FS_StreetTheme_AssetLibrary_Master"
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.unit_settings.length_unit = "METERS"
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        pass
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.filepath = str(RENDER_PATH)
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.use_file_extension = True
    scene.render.engine = scene.render.engine
    scene.view_settings.look = "AgX - Medium High Contrast"

    world = bpy.data.worlds.new("WLD_FS_ArchitectureMaster_Daylight")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.10, 0.28, 0.50, 1.0)
    background.inputs["Strength"].default_value = 0.32
    scene.world = world

    root_collection["workflow"] = "Single active Street-theme master file"
    root_collection["current_scope"] = "Architecture"
    root_collection["master_schema_version"] = "v01"
    root_collection["authoring_forward_axis"] = "+Y"
    root_collection["unity_integration"] = False
    root_collection["next_gate"] = "Approve AR-03 v01 before AR-04"


def write_readme():
    text = bpy.data.texts.new("TXT_FS_ArchitectureMaster_ReadMe")
    text.write(
        "Street Theme Asset Library Master\n"
        "Current production scope: Architecture\n"
        "AR-01 v01 and AR-02 v01 are approved/frozen collections.\n"
        "AR-03 v01 passed QA and remains pending explicit user approval.\n"
        "Original atomic .blend files are preserved as historical backups.\n"
        "Use collection instance offsets to place each 4 m module at local origin.\n"
        "No Unity import, FBX export, collision, LOD, or gameplay content is included.\n"
    )


def main():
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    clear_scene()

    scene_root = bpy.context.scene.collection
    master = new_collection("COL_FS_StreetThemeAssetLibrary", scene_root)
    architecture = new_collection("COL_FS_Architecture", master)
    categories = {
        "Road": new_collection("COL_FS_Road", master),
        "Vegetation": new_collection("COL_FS_Vegetation", master),
        "StreetFurniture": new_collection("COL_FS_StreetFurniture", master),
        "Playground": new_collection("COL_FS_Playground", master),
        "Vehicles": new_collection("COL_FS_Vehicles", master),
        "Obstacles": new_collection("COL_FS_Obstacles", master),
        "Background": new_collection("COL_FS_Background", master),
    }
    for collection in categories.values():
        collection["production_state"] = "NOT_STARTED"
    cameras = new_collection("COL_FS_MasterCameras", master)
    lights = new_collection("COL_FS_MasterLights", master)
    review = new_collection("COL_FS_MasterReviewOnly", master)

    imported = [import_asset(asset, architecture) for asset in ASSETS]
    consolidate_shared_materials()
    add_review_stage(review)
    add_camera_and_lights(cameras, lights)
    configure_scene(master)
    write_readme()

    brief = {
        "deliverable": "Street Theme single-file asset-library master, architecture phase",
        "target": "Blender-only PC review source",
        "style": "Cartoon-realistic suburban architecture",
        "scale": "Metres, Z-up, +Y forward",
        "source_strategy": "Append approved/reviewed atomic collections; no external library links",
        "included_assets": [f"{asset['task_id']} {asset['version']}" for asset in ASSETS],
        "deferred": ["AR-04 onward", "road", "vegetation", "props", "vehicles", "Unity", "FBX", "LOD", "collision"],
    }
    BRIEF_PATH.write_text(json.dumps(brief, indent=2), encoding="utf-8")

    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    bpy.context.scene.render.filepath = str(RENDER_PATH)
    bpy.ops.render.render(write_still=True)

    source_results = []
    for asset, collection in zip(ASSETS, imported):
        actual_hash = sha256(asset["source"])
        source_results.append({
            "task_id": asset["task_id"],
            "version": asset["version"],
            "collection": collection.name,
            "source": str(asset["source"]),
            "source_sha256": actual_hash,
            "source_hash_matches_expected": actual_hash == asset["expected_sha256"],
            "approval_state": collection["approval_state"],
            "locked_in_master": collection.hide_select,
            "asset_browser_marked": collection.asset_data is not None,
            "objects": len(collection.all_objects),
            "mesh_objects": len([obj for obj in collection.all_objects if obj.type == "MESH"]),
            "evaluated_triangles": evaluated_triangles(collection),
            "instance_offset": list(collection.instance_offset),
        })

    checks = {
        "three_architecture_assets_present": len(imported) == 3,
        "all_sources_hash_verified": all(item["source_hash_matches_expected"] for item in source_results),
        "approved_assets_locked": all(item["locked_in_master"] for item in source_results[:2]),
        "ar03_pending_and_editable": not source_results[2]["locked_in_master"] and source_results[2]["approval_state"] == "PENDING_USER_APPROVAL",
        "all_collections_asset_marked": all(item["asset_browser_marked"] for item in source_results),
        "no_linked_external_datablocks": len(linked_data_blocks()) == 0,
        "metric_scale": bpy.context.scene.unit_settings.system == "METRIC" and bpy.context.scene.unit_settings.scale_length == 1.0,
        "render_1920x1080": bpy.context.scene.render.resolution_x == 1920 and bpy.context.scene.render.resolution_y == 1080,
        "review_render_exists": RENDER_PATH.exists(),
        "single_active_master_blend": OUTPUT_BLEND.exists(),
    }
    report = {
        "qa_verdict": "SHIP" if all(checks.values()) else "NO-SHIP",
        "approval_state": "ARCHITECTURE_MASTER_CREATED_AR03_PENDING_USER_APPROVAL",
        "master_file": str(OUTPUT_BLEND),
        "master_sha256": sha256(OUTPUT_BLEND),
        "review_render": str(RENDER_PATH),
        "collections": [collection.name for collection in master.children],
        "shared_materials": [material.name for material in bpy.data.materials if material.name.startswith("MAT_FS_")],
        "linked_external_datablocks": linked_data_blocks(),
        "source_assets": source_results,
        "checks": checks,
        "notes": [
            "The master contains local appended data and has no linked external Blender datablocks.",
            "AR-03 uses the preserved original v01 backup with 0.04 m side gaps and a 0.08 m top gap.",
            "Original atomic files remain unchanged and serve as historical backups.",
            "AR-03 v01 remains pending user approval; AR-04 has not started.",
            "Empty category collections reserve the final one-file library structure for later production waves.",
        ],
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
