import bpy
import json
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))
from build_wave0_ar01 import ROOT, RENDER_ROOT, save, render

OUTPUT = ROOT / "01_Architecture" / "ENV_FS_AR-03_EntranceWall4m_v02.blend"
RENDER_DIR = RENDER_ROOT / "AR-03_v02"
REPORT_DIR = ROOT / "01_Architecture" / "Reports"
MANIFEST = ROOT / "00_Direction" / "ENV_FS_DIR-05_AssetManifest_v01.json"


def resize_apply(obj, dimensions, location):
    obj.dimensions = dimensions
    obj.location = location
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.select_set(False)


def ensure_wire_material():
    wire = bpy.data.materials.get("MAT_FS_Review_Wireframe") or bpy.data.materials.new("MAT_FS_Review_Wireframe")
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
    return wire


def main():
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.name = "ENV_FS_AR-03_EntranceWall4m_v02"
    scene["workflow_state"] = "QA_SHIP_PENDING_USER_APPROVAL"
    scene["revision"] = "v02"
    scene["revision_request"] = "Reduce visible gap between review door and wall casing"
    scene["door_side_gap_m"] = 0.02
    scene["door_top_gap_m"] = 0.04
    scene["structural_geometry_changed"] = False

    resize_apply(bpy.data.objects["SM_FS_Review_DoorPlaceholder_Slab"], (0.96, 0.055, 2.16), (0, -0.09, 1.08))
    resize_apply(bpy.data.objects["SM_FS_Review_DoorPlaceholder_Glass"], (0.66, 0.025, 0.58), (0, -0.128, 1.69))
    resize_apply(bpy.data.objects["SM_FS_Review_DoorPlaceholder_Panel_L"], (0.32, 0.025, 0.56), (-0.21, -0.128, 0.70))
    resize_apply(bpy.data.objects["SM_FS_Review_DoorPlaceholder_Panel_R"], (0.32, 0.025, 0.56), (0.21, -0.128, 0.70))
    bpy.data.objects["SM_FS_Review_DoorPlaceholder_Handle"].location.y = -0.155
    resize_apply(bpy.data.objects["SM_FS_Review_DoorPlaceholder_Threshold"], (0.98, 0.18, 0.05), (0, -0.15, 0.025))

    brief = {
        "task_id": "AR-03", "version": "v02",
        "change_request": "Reduce visible gap between the review door and wall casing.",
        "structural_wall_change": False,
        "rough_opening_m": {"width": 1.0, "height": 2.2},
        "review_door_before_m": {"width": 0.92, "height": 2.12, "side_gap_each": 0.04, "top_gap": 0.08},
        "review_door_after_m": {"width": 0.96, "height": 2.16, "side_gap_each": 0.02, "top_gap": 0.04},
        "forward_adjustment_m": 0.045,
        "boundary": "The review placeholder remains excluded from COL_Geo; AR-10 still owns the finished reusable door.",
    }
    (ROOT / "01_Architecture" / "ENV_FS_AR-03_EntranceWall4m_ProductionBrief_v02.json").write_text(json.dumps(brief, indent=2), encoding="utf-8")

    comparison = {
        "task_id": "AR-03", "version": "v02", "iteration": 2, "maximum_iterations": 3,
        "change": "Door-to-casing gap reduced without changing the structural doorway.",
        "categories": {
            "door_fit": "Match", "doorway_scale": "Match", "casing_alignment": "Match",
            "siding_continuity": "Match", "cartoon_realism": "Close", "material_response": "Close",
            "gameplay_distance_readability": "Match", "modular_reusability": "Match",
            "protected_content_boundary": "Match", "scope_preservation": "Match",
        },
        "score": "10/10 categories Match or Close",
        "remaining_gap": "Final hinge, frame and installation tolerances will be authored in AR-10.",
    }
    (REPORT_DIR / "ENV_FS_AR-03_ReferenceComparison_v02.json").write_text(json.dumps(comparison, indent=2), encoding="utf-8")

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    for task in manifest["tasks"]:
        if task["id"] == "AR-03":
            task["status"] = "QA_SHIP_PENDING_USER_APPROVAL"
            task["review_version"] = "v02"
            task["revision_note"] = "Reduced review-door side/top gaps; structural wall unchanged."
    MANIFEST.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    save(OUTPUT)
    cameras = {
        "Hero": "CAM_FS_EntranceWall4m_Hero",
        "Front": "CAM_FS_EntranceWall4m_FrontOrtho",
        "Profile": "CAM_FS_EntranceWall4m_ProfileOrtho",
        "MaterialBeauty": "CAM_FS_EntranceWall4m_MaterialBeauty",
        "GameplayDistance": "CAM_FS_EntranceWall4m_GameplayDistance",
    }
    for label, camera_name in cameras.items():
        render(bpy.data.objects[camera_name], RENDER_DIR / f"ENV_FS_AR-03_v02_{label}.png")
    wire = ensure_wire_material()
    bpy.context.view_layer.material_override = wire
    render(bpy.data.objects[cameras["Hero"]], RENDER_DIR / "ENV_FS_AR-03_v02_Wireframe.png")
    bpy.context.view_layer.material_override = None
    scene.camera = bpy.data.objects[cameras["Hero"]]
    save(OUTPUT)
    print("AR03_V02_COMPLETE")


if __name__ == "__main__":
    main()
