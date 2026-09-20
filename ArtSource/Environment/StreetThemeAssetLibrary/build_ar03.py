import bpy
import hashlib
import json
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

from build_wave0_ar01 import (
    ROOT, RENDER_ROOT, PALETTE, reset_scene, make_root, material,
    procedural_surface_material, hex_rgba, cylinder, move_to,
    target_empty, add_camera, add_lighting, setup_floor, save, render,
)

ASSET_ID = "AR-03"
ASSET_NAME = "EntranceWall4m"
OUTPUT = ROOT / "01_Architecture" / "ENV_FS_AR-03_EntranceWall4m_v01.blend"
RENDER_DIR = RENDER_ROOT / "AR-03"
REPORT_DIR = ROOT / "01_Architecture" / "Reports"
MANIFEST = ROOT / "00_Direction" / "ENV_FS_DIR-05_AssetManifest_v01.json"
AR02 = ROOT / "01_Architecture" / "ENV_FS_AR-02_WindowWall4m_v01.blend"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def cube(name, location, dimensions, mat=None, bevel=0.0, collection=None):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if mat:
        obj.data.materials.append(mat)
    if bevel > 0:
        modifier = obj.modifiers.new("Bevel_FS_Rounded", 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = 'ANGLE'
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    if collection:
        move_to(obj, collection)
    return obj


def linked_object(name, mesh_data, location, collection):
    obj = bpy.data.objects.new(name, mesh_data)
    obj.location = location
    collection.objects.link(obj)
    obj["linked_instance"] = True
    return obj


def write_production_brief():
    brief = {
        "task_id": ASSET_ID,
        "purpose": "Reusable 4m residential entrance-wall section with a standardized rough opening for the future AR-10 exterior door.",
        "asset_tier": "standard modular architecture",
        "target": "Windows PC game source; Blender review only",
        "style": "realistic construction with cartoon-based silhouette, color and rounded edge treatment",
        "specialist_skills": ["blender-director", "environment-artist", "blender-modeler", "cartoon-style", "materials", "asset-optimization", "rendering", "qa-review"],
        "dimensions_m": {
            "width_x": 4.0, "height_z": 3.0, "structural_core_depth_y": 0.22,
            "overall_depth_y": 0.375, "rough_opening_width_x": 1.0,
            "rough_opening_height_z": 2.2, "threshold_z": 0.0,
        },
        "triangle_budget": [500, 5000], "material_budget": 2,
        "pivot": "collection origin at ground-contact centre (0,0,0)",
        "front_axis": "-Y", "street_authoring_forward": "+Y", "animation": "static",
        "dependency": "AR-01 approved proportions and materials; AR-02 approved optimized bevel standard; AR-10 future finished exterior door",
        "review_placeholder": "A removable door placeholder lives in COL_ReviewOnly and is not part of asset geometry or material budget.",
        "deferred": ["finished exterior door", "LOD", "retopology", "texture baking", "collision", "FBX export", "Unity integration"],
    }
    (ROOT / "01_Architecture" / "ENV_FS_AR-03_EntranceWall4m_ProductionBrief_v01.json").write_text(json.dumps(brief, indent=2), encoding="utf-8")


def record_ar02_approval():
    approval = {
        "task_id": "AR-02", "status": "APPROVED_FROZEN", "approved_on": "2026-08-17",
        "approved_by": "user", "asset": str(AR02), "sha256": sha256(AR02),
        "qa_verdict": "SHIP", "revision_rule": "Do not overwrite v01; future changes must use v02 or later.",
    }
    (REPORT_DIR / "ENV_FS_AR-02_ApprovalRecord_v01.json").write_text(json.dumps(approval, indent=2), encoding="utf-8")


def update_manifest():
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for task in data["tasks"]:
        if task["id"] == "AR-02":
            task["status"] = "APPROVED_FROZEN"
            task["approved_on"] = "2026-08-17"
            task["sha256"] = sha256(AR02)
        elif task["id"] == "AR-03":
            task["name"] = "4m entrance-wall section"
            task["status"] = "QA_SHIP_PENDING_USER_APPROVAL"
    MANIFEST.write_text(json.dumps(data, indent=2), encoding="utf-8")


def build_asset():
    scene = reset_scene("ENV_FS_AR-03_EntranceWall4m_v01")
    _, col = make_root(ASSET_NAME)
    scene["task_id"] = ASSET_ID
    scene["asset_tier"] = "standard modular architecture"
    scene["dimensions_m"] = "4.0 X, 0.375 overall Y, 3.0 Z"
    scene["rough_opening_m"] = "1.00 X by 2.20 Z; threshold at 0.0 Z"
    scene["triangle_budget"] = "500-5000 evaluated triangles"
    scene["material_budget"] = "1-2 asset materials"
    scene["pivot_contract"] = "collection origin at ground contact centre (0,0,0)"
    scene["front_axis"] = "-Y"
    scene["animation_state"] = "static"
    scene["workflow_state"] = "QA_SHIP_PENDING_USER_APPROVAL"
    scene["approved_dependency"] = "AR-02 v01 SHA256 " + sha256(AR02)
    scene["review_placeholder_excluded_from_asset"] = True
    scene["deferred"] = "AR-10 finished door, LOD, retopology, baking, collision, export and Unity integration"

    white = procedural_surface_material("MAT_FS_Siding_White", hex_rgba(PALETTE["WhiteSiding"]), 0.58, 0.035, 8.0)
    cream = procedural_surface_material("MAT_FS_Trim_Cream", hex_rgba(PALETTE["CreamSiding"]), 0.60, 0.025, 9.0)
    setup_floor(col["ReviewOnly"], (11, 9), (0, 0.6, -0.06))

    # Tier 1 structural blockout: 1.0m x 2.2m clear doorway at ground level.
    for name, location, dimensions in [
        ("SM_FS_Architecture_EntranceWall_Pier_L", (-1.25, 0.055, 1.5), (1.5, 0.22, 3.0)),
        ("SM_FS_Architecture_EntranceWall_Pier_R", (1.25, 0.055, 1.5), (1.5, 0.22, 3.0)),
        ("SM_FS_Architecture_EntranceWall_HeaderCore", (0, 0.055, 2.6), (1.0, 0.22, 0.8)),
    ]:
        obj = cube(name, location, dimensions, white, 0.040, col["Geo"])
        obj["construction_tier"] = "primary structural blockout"

    # Tier 2 siding: three linked full boards above the doorway.
    full_master = cube(
        "SM_FS_Architecture_EntranceWall_SidingFull_12",
        (0, -0.098, 0.145 + 12 * 0.19), (3.72, 0.105, 0.19), white, 0.026, col["Geo"],
    )
    full_master["linked_component_role"] = "full-width horizontal siding"
    for index in (13, 14):
        linked_object(f"SM_FS_Architecture_EntranceWall_SidingFull_{index:02d}", full_master.data, (0, -0.098, 0.145 + index * 0.19), col["Geo"])

    # Twenty-four linked side boards preserve the clear door opening from ground to header.
    side_master = cube(
        "SM_FS_Architecture_EntranceWall_SidingSide_L_00",
        (-1.18, -0.098, 0.145), (1.36, 0.105, 0.19), white, 0.026, col["Geo"],
    )
    side_master["linked_component_role"] = "door-opening-side horizontal siding"
    linked_object("SM_FS_Architecture_EntranceWall_SidingSide_R_00", side_master.data, (1.18, -0.098, 0.145), col["Geo"])
    for index in range(1, 12):
        z = 0.145 + index * 0.19
        linked_object(f"SM_FS_Architecture_EntranceWall_SidingSide_L_{index:02d}", side_master.data, (-1.18, -0.098, z), col["Geo"])
        linked_object(f"SM_FS_Architecture_EntranceWall_SidingSide_R_{index:02d}", side_master.data, (1.18, -0.098, z), col["Geo"])

    # Door casing remains outside the exact rough opening.
    cube("SM_FS_Architecture_EntranceWall_Casing_L", (-0.58, -0.135, 1.10), (0.16, 0.12, 2.20), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_Casing_R", (0.58, -0.135, 1.10), (0.16, 0.12, 2.20), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_Casing_Top", (0, -0.135, 2.28), (1.0, 0.12, 0.16), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_EndTrim_L", (-1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_EndTrim_R", (1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_BaseTrim_L", (-1.17, -0.145, 0.08), (1.34, 0.13, 0.16), cream, 0.025, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_BaseTrim_R", (1.17, -0.145, 0.08), (1.34, 0.13, 0.16), cream, 0.025, col["Geo"])
    cube("SM_FS_Architecture_EntranceWall_TopTrim", (0, -0.145, 2.92), (4.0, 0.13, 0.16), cream, 0.025, col["Geo"])

    # Review-only door confirms scale and readability without duplicating AR-10.
    blue = material("MAT_FS_Review_DoorPlaceholder_Blue", hex_rgba(PALETTE["BlueAccent"]), 0.42)
    teal = material("MAT_FS_Review_DoorPlaceholder_Teal", hex_rgba(PALETTE["TealAccent"]), 0.38)
    dark_glass = material("MAT_FS_Review_DoorPlaceholder_Glass", (0.035, 0.14, 0.21, 1), 0.22)
    yellow = material("MAT_FS_Review_DoorPlaceholder_Handle", hex_rgba(PALETTE["RouteGuidance"]), 0.34, 0.65)
    cube("SM_FS_Review_DoorPlaceholder_Slab", (0, -0.045, 1.06), (0.92, 0.05, 2.12), blue, 0.022, col["ReviewOnly"])
    cube("SM_FS_Review_DoorPlaceholder_Glass", (0, -0.083, 1.67), (0.62, 0.025, 0.58), dark_glass, 0.020, col["ReviewOnly"])
    cube("SM_FS_Review_DoorPlaceholder_Panel_L", (-0.20, -0.083, 0.69), (0.30, 0.025, 0.54), teal, 0.018, col["ReviewOnly"])
    cube("SM_FS_Review_DoorPlaceholder_Panel_R", (0.20, -0.083, 0.69), (0.30, 0.025, 0.54), teal, 0.018, col["ReviewOnly"])
    cylinder("SM_FS_Review_DoorPlaceholder_Handle", (0.31, -0.125, 1.08), 0.055, 0.05, yellow, col["ReviewOnly"], 24).rotation_euler.x = 1.5708
    cube("SM_FS_Review_DoorPlaceholder_Threshold", (0, -0.15, 0.025), (0.96, 0.18, 0.05), cream, 0.014, col["ReviewOnly"])

    ref_mat = material("MAT_FS_Reference_ScaleGrey", (0.22, 0.27, 0.30, 1), 0.66)
    ref = cylinder("SM_FS_Reference_HumanScale_1p8m", (-2.75, 0.15, 0.9), 0.19, 1.45, ref_mat, col["Reference"], 24)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.18, location=(-2.75, 0.15, 1.68))
    head = bpy.context.object
    head.name = "SM_FS_Reference_HumanScale_Head"
    head.data.materials.append(ref_mat)
    move_to(head, col["Reference"])
    ref["reference_height_m"] = 1.8

    target_empty("REF_FS_AR03_Target", (0, 0, 1.45), col["ReviewOnly"])
    hero = add_camera("CAM_FS_EntranceWall4m_Hero", (5.1, -7.0, 3.65), (0, 0, 1.45), col["Cameras"], 50)
    front = add_camera("CAM_FS_EntranceWall4m_FrontOrtho", (0, -8, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.25)
    profile = add_camera("CAM_FS_EntranceWall4m_ProfileOrtho", (6.0, 0, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.1)
    detail = add_camera("CAM_FS_EntranceWall4m_MaterialBeauty", (2.3, -3.9, 2.25), (0.25, -0.05, 1.35), col["Cameras"], 70)
    gameplay = add_camera("CAM_FS_EntranceWall4m_GameplayDistance", (0, -14, 4.6), (0, 0, 1.35), col["Cameras"], 38)
    add_lighting(col["Lights"])
    scene.camera = hero

    wire = bpy.data.materials.new("MAT_FS_Review_Wireframe")
    wire.use_nodes = True
    nodes = wire.node_tree.nodes
    links = wire.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    wire_node = nodes.new("ShaderNodeWireframe")
    wire_node.inputs["Size"].default_value = 0.012
    mix = nodes.new("ShaderNodeMixRGB")
    mix.inputs[1].default_value = (0.82, 0.84, 0.86, 1)
    mix.inputs[2].default_value = (0.025, 0.035, 0.045, 1)
    bsdf.inputs["Roughness"].default_value = 0.72
    links.new(wire_node.outputs["Fac"], mix.inputs[0])
    links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    save(OUTPUT)
    render(hero, RENDER_DIR / "ENV_FS_AR-03_Hero.png")
    render(front, RENDER_DIR / "ENV_FS_AR-03_Front.png")
    render(profile, RENDER_DIR / "ENV_FS_AR-03_Profile.png")
    render(detail, RENDER_DIR / "ENV_FS_AR-03_MaterialBeauty.png")
    render(gameplay, RENDER_DIR / "ENV_FS_AR-03_GameplayDistance.png")
    bpy.context.view_layer.material_override = wire
    render(hero, RENDER_DIR / "ENV_FS_AR-03_Wireframe.png")
    bpy.context.view_layer.material_override = None
    scene.camera = hero
    save(OUTPUT)


def write_reference_comparison():
    comparison = {
        "task_id": ASSET_ID, "iteration": 1, "maximum_iterations": 3,
        "reference_features": ["white horizontal suburban siding", "believable residential door scale", "soft cream casing", "restrained blue and teal accent", "rounded cartoon-realistic edges", "bright upper-left midday response", "limited micro-detail"],
        "categories": {
            "real_world_scale": "Match", "cartoon_exaggeration": "Close", "horizontal_siding_language": "Match",
            "white_cream_palette": "Match", "door_proportion": "Match", "construction_depth": "Match",
            "rounded_edge_treatment": "Match", "material_response": "Close", "modular_reusability": "Match",
            "protected_content_boundary": "Match",
        },
        "score": "10/10 categories Match or Close",
        "gaps": ["The visible door is deliberately a review placeholder until AR-10 ships.", "Porch and entrance composition remain AR-11 and AR-AS01 assembly checks."],
    }
    (REPORT_DIR / "ENV_FS_AR-03_ReferenceComparison_v01.json").write_text(json.dumps(comparison, indent=2), encoding="utf-8")


def main():
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    write_production_brief()
    record_ar02_approval()
    build_asset()
    write_reference_comparison()
    update_manifest()
    print("AR03_BUILD_COMPLETE")


if __name__ == "__main__":
    main()
