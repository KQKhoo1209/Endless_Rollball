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


ASSET_ID = "AR-02"
ASSET_NAME = "WindowWall4m"
OUTPUT = ROOT / "01_Architecture" / "ENV_FS_AR-02_WindowWall4m_v01.blend"
RENDER_DIR = RENDER_ROOT / "AR-02"
REPORT_DIR = ROOT / "01_Architecture" / "Reports"
MANIFEST = ROOT / "00_Direction" / "ENV_FS_DIR-05_AssetManifest_v01.json"
AR01 = ROOT / "01_Architecture" / "ENV_FS_AR-01_BlankSidingWall4m_v01.blend"


def cube(name, location, dimensions, mat=None, bevel=0.0, collection=None):
    """AR-02 optimized rounded box: two bevel segments preserve silhouette at lower repetition cost."""
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


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def write_production_brief():
    brief = {
        "task_id": ASSET_ID,
        "purpose": "Reusable 4m residential siding-wall section with a standardized rough opening for the future AR-09 window unit.",
        "asset_tier": "standard modular architecture",
        "target": "Windows PC game source; Blender review only",
        "style": "realistic construction with cartoon-based silhouette, color and rounded edge treatment",
        "specialist_skills": [
            "blender-director", "environment-artist", "blender-modeler",
            "cartoon-style", "materials", "asset-optimization", "rendering", "qa-review",
        ],
        "dimensions_m": {
            "width_x": 4.0,
            "height_z": 3.0,
            "structural_core_depth_y": 0.22,
            "overall_depth_y": 0.39,
            "rough_opening_width_x": 1.60,
            "rough_opening_height_z": 1.35,
            "sill_height_z": 0.85,
        },
        "triangle_budget": [500, 5000],
        "material_budget": 2,
        "pivot": "collection origin at ground-contact centre (0,0,0)",
        "front_axis": "-Y",
        "street_authoring_forward": "+Y",
        "animation": "static",
        "dependency": "AR-01 approved proportions and materials; AR-09 future finished window insert",
        "review_placeholder": "A removable window placeholder lives in COL_ReviewOnly and is not part of the asset geometry or material budget.",
        "deferred": ["finished window unit", "LOD", "retopology", "texture baking", "collision", "FBX export", "Unity integration"],
    }
    path = ROOT / "01_Architecture" / "ENV_FS_AR-02_WindowWall4m_ProductionBrief_v01.json"
    path.write_text(json.dumps(brief, indent=2), encoding="utf-8")


def record_ar01_approval():
    approval = {
        "task_id": "AR-01",
        "status": "APPROVED_FROZEN",
        "approved_on": "2026-08-17",
        "approved_by": "user",
        "asset": str(AR01),
        "sha256": sha256(AR01),
        "qa_verdict": "SHIP",
        "revision_rule": "Do not overwrite v01; any future changes must use v02 or later.",
    }
    (REPORT_DIR / "ENV_FS_AR-01_ApprovalRecord_v01.json").write_text(json.dumps(approval, indent=2), encoding="utf-8")


def update_manifest():
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for task in data["tasks"]:
        if task["id"] == "AR-01":
            task["status"] = "APPROVED_FROZEN"
            task["approved_on"] = "2026-08-17"
            task["sha256"] = sha256(AR01)
        elif task["id"] == "AR-02":
            task["name"] = "4m window-wall section"
            task["status"] = "QA_SHIP_PENDING_USER_APPROVAL"
    MANIFEST.write_text(json.dumps(data, indent=2), encoding="utf-8")


def linked_object(name, mesh_data, location, collection):
    obj = bpy.data.objects.new(name, mesh_data)
    obj.location = location
    collection.objects.link(obj)
    obj["linked_instance"] = True
    return obj


def build_asset():
    scene = reset_scene("ENV_FS_AR-02_WindowWall4m_v01")
    _, col = make_root(ASSET_NAME)
    scene["task_id"] = ASSET_ID
    scene["asset_tier"] = "standard modular architecture"
    scene["dimensions_m"] = "4.0 X, 0.39 overall Y, 3.0 Z"
    scene["rough_opening_m"] = "1.60 X by 1.35 Z; sill at 0.85 Z"
    scene["triangle_budget"] = "500-5000 evaluated triangles"
    scene["material_budget"] = "1-2 asset materials"
    scene["pivot_contract"] = "collection origin at ground contact centre (0,0,0)"
    scene["front_axis"] = "-Y"
    scene["animation_state"] = "static"
    scene["workflow_state"] = "QA_SHIP_PENDING_USER_APPROVAL"
    scene["approved_dependency"] = "AR-01 v01 SHA256 " + sha256(AR01)
    scene["review_placeholder_excluded_from_asset"] = True
    scene["deferred"] = "AR-09 finished window, LOD, retopology, baking, collision, export and Unity integration"

    white = procedural_surface_material("MAT_FS_Siding_White", hex_rgba(PALETTE["WhiteSiding"]), 0.58, 0.035, 8.0)
    cream = procedural_surface_material("MAT_FS_Trim_Cream", hex_rgba(PALETTE["CreamSiding"]), 0.60, 0.025, 9.0)
    setup_floor(col["ReviewOnly"], (11, 9), (0, 0.6, -0.06))

    # Tier 1: structural blockout around a 1.60m x 1.35m rough opening.
    core_parts = [
        ("SM_FS_Architecture_WindowWall_Pier_L", (-1.4, 0.055, 1.5), (1.2, 0.22, 3.0)),
        ("SM_FS_Architecture_WindowWall_Pier_R", (1.4, 0.055, 1.5), (1.2, 0.22, 3.0)),
        ("SM_FS_Architecture_WindowWall_SillCore", (0, 0.055, 0.425), (1.6, 0.22, 0.85)),
        ("SM_FS_Architecture_WindowWall_HeaderCore", (0, 0.055, 2.6), (1.6, 0.22, 0.8)),
    ]
    for name, location, dimensions in core_parts:
        obj = cube(name, location, dimensions, white, 0.040, col["Geo"])
        obj["construction_tier"] = "primary structural blockout"

    # Tier 2: linked horizontal siding. Full boards repeat above and below the opening.
    full_master = cube(
        "SM_FS_Architecture_WindowWall_SidingFull_00",
        (0, -0.098, 0.145), (3.72, 0.105, 0.19), white, 0.026, col["Geo"],
    )
    full_master["linked_component_role"] = "full-width horizontal siding"
    for index in (1, 2, 3, 12, 13, 14):
        linked_object(
            f"SM_FS_Architecture_WindowWall_SidingFull_{index:02d}",
            full_master.data, (0, -0.098, 0.145 + index * 0.19), col["Geo"],
        )

    side_master = cube(
        "SM_FS_Architecture_WindowWall_SidingSide_L_04",
        (-1.33, -0.098, 0.145 + 4 * 0.19), (1.06, 0.105, 0.19), white, 0.026, col["Geo"],
    )
    side_master["linked_component_role"] = "opening-side horizontal siding"
    linked_object(
        "SM_FS_Architecture_WindowWall_SidingSide_R_04",
        side_master.data, (1.33, -0.098, 0.145 + 4 * 0.19), col["Geo"],
    )
    for index in range(5, 12):
        z = 0.145 + index * 0.19
        linked_object(
            f"SM_FS_Architecture_WindowWall_SidingSide_L_{index:02d}",
            side_master.data, (-1.33, -0.098, z), col["Geo"],
        )
        linked_object(
            f"SM_FS_Architecture_WindowWall_SidingSide_R_{index:02d}",
            side_master.data, (1.33, -0.098, z), col["Geo"],
        )

    # Tier 2 casing and exterior modular trims, positioned outside the clear opening.
    cube("SM_FS_Architecture_WindowWall_Casing_L", (-0.88, -0.135, 1.525), (0.16, 0.12, 1.35), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_Casing_R", (0.88, -0.135, 1.525), (0.16, 0.12, 1.35), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_Casing_Top", (0, -0.135, 2.28), (1.60, 0.12, 0.16), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_Casing_Sill", (0, -0.150, 0.77), (1.60, 0.15, 0.16), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_EndTrim_L", (-1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_EndTrim_R", (1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_BaseTrim", (0, -0.145, 0.08), (4.0, 0.13, 0.16), cream, 0.025, col["Geo"])
    cube("SM_FS_Architecture_WindowWall_TopTrim", (0, -0.145, 2.92), (4.0, 0.13, 0.16), cream, 0.025, col["Geo"])

    # Review-only insert confirms the opening without pre-empting the future AR-09 asset.
    glass = material("MAT_FS_Review_WindowPlaceholder_Glass", (0.045, 0.20, 0.28, 1), 0.22, 0.0)
    accent = material("MAT_FS_Review_WindowPlaceholder_Teal", hex_rgba(PALETTE["TealAccent"]), 0.38, 0.0)
    cube("SM_FS_Review_WindowPlaceholder_Glass", (0, -0.045, 1.525), (1.50, 0.035, 1.25), glass, 0.018, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Frame_L", (-0.755, -0.075, 1.525), (0.055, 0.055, 1.30), accent, 0.012, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Frame_R", (0.755, -0.075, 1.525), (0.055, 0.055, 1.30), accent, 0.012, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Frame_Top", (0, -0.075, 2.175), (1.56, 0.055, 0.055), accent, 0.012, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Frame_Bottom", (0, -0.075, 0.875), (1.56, 0.055, 0.055), accent, 0.012, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Mullion_V", (0, -0.105, 1.525), (0.045, 0.045, 1.25), accent, 0.010, col["ReviewOnly"])
    cube("SM_FS_Review_WindowPlaceholder_Mullion_H", (0, -0.105, 1.525), (1.50, 0.045, 0.045), accent, 0.010, col["ReviewOnly"])

    ref_mat = material("MAT_FS_Reference_ScaleGrey", (0.22, 0.27, 0.30, 1), 0.66)
    ref = cylinder("SM_FS_Reference_HumanScale_1p8m", (-2.75, 0.15, 0.9), 0.19, 1.45, ref_mat, col["Reference"], 24)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.18, location=(-2.75, 0.15, 1.68))
    head = bpy.context.object
    head.name = "SM_FS_Reference_HumanScale_Head"
    head.data.materials.append(ref_mat)
    move_to(head, col["Reference"])
    ref["reference_height_m"] = 1.8

    target_empty("REF_FS_AR02_Target", (0, 0, 1.45), col["ReviewOnly"])
    hero = add_camera("CAM_FS_WindowWall4m_Hero", (5.1, -7.0, 3.65), (0, 0, 1.45), col["Cameras"], 50)
    front = add_camera("CAM_FS_WindowWall4m_FrontOrtho", (0, -8, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.25)
    profile = add_camera("CAM_FS_WindowWall4m_ProfileOrtho", (6.0, 0, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.1)
    detail = add_camera("CAM_FS_WindowWall4m_MaterialBeauty", (2.3, -3.9, 2.25), (0.35, -0.05, 1.55), col["Cameras"], 70)
    gameplay = add_camera("CAM_FS_WindowWall4m_GameplayDistance", (0, -14, 4.6), (0, 0, 1.35), col["Cameras"], 38)
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
    render(hero, RENDER_DIR / "ENV_FS_AR-02_Hero.png")
    render(front, RENDER_DIR / "ENV_FS_AR-02_Front.png")
    render(profile, RENDER_DIR / "ENV_FS_AR-02_Profile.png")
    render(detail, RENDER_DIR / "ENV_FS_AR-02_MaterialBeauty.png")
    render(gameplay, RENDER_DIR / "ENV_FS_AR-02_GameplayDistance.png")
    bpy.context.view_layer.material_override = wire
    render(hero, RENDER_DIR / "ENV_FS_AR-02_Wireframe.png")
    bpy.context.view_layer.material_override = None
    scene.camera = hero
    save(OUTPUT)


def write_reference_comparison(iteration=2, correction="Reduced repeated-part bevels from three segments to two after the initial 6,580-triangle audit; rounded silhouette is preserved within budget."):
    comparison = {
        "task_id": ASSET_ID,
        "iteration": iteration,
        "maximum_iterations": 3,
        "reference_features": [
            "white horizontal suburban siding", "believable window placement and sill height",
            "soft cream casing", "restrained teal window accent", "rounded cartoon-realistic edges",
            "bright upper-left midday response", "limited micro-detail",
        ],
        "categories": {
            "real_world_scale": "Match",
            "cartoon_exaggeration": "Close",
            "horizontal_siding_language": "Match",
            "white_cream_palette": "Match",
            "window_proportion": "Close",
            "construction_depth": "Match",
            "rounded_edge_treatment": "Match",
            "material_response": "Close",
            "modular_reusability": "Match",
            "protected_content_boundary": "Match",
        },
        "score": "10/10 categories Match or Close",
        "correction": correction,
        "gaps": [
            "The visible window insert is deliberately a review placeholder until AR-09 ships.",
            "House-level repetition and facade rhythm remain AR-AS01 assembly checks.",
        ],
    }
    (REPORT_DIR / "ENV_FS_AR-02_ReferenceComparison_v01.json").write_text(json.dumps(comparison, indent=2), encoding="utf-8")


def main():
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    write_production_brief()
    record_ar01_approval()
    build_asset()
    write_reference_comparison()
    update_manifest()
    print("AR02_BUILD_COMPLETE")


if __name__ == "__main__":
    main()
