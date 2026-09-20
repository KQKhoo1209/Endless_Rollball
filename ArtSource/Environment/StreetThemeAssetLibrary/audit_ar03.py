import bpy
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_ar02 import evaluated_triangles, world_bounds, topology_checks, naming_errors, bounds_for_object

VERSION = "v02" if "_v02.blend" in bpy.data.filepath else "v01"
REPORT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1\01_Architecture\Reports") / f"ENV_FS_AR-03_ValidationReport_{VERSION}.json"


def opening_intrusions(objects):
    # Exact doorway interior: X -0.5..0.5, Z 0.0..2.2. Boundary contact is permitted.
    intrusions = []
    for obj in objects:
        mins, maxs = bounds_for_object(obj)
        inside_x = maxs[0] > -0.499 and mins[0] < 0.499
        inside_z = maxs[2] > 0.001 and mins[2] < 2.199
        if inside_x and inside_z:
            intrusions.append(obj.name)
    return intrusions


def main():
    geo = bpy.data.collections.get("COL_Geo")
    objects = [obj for obj in geo.all_objects if obj.type == 'MESH']
    tris = evaluated_triangles(objects)
    mins, maxs, dims = world_bounds(objects)
    materials = sorted({slot.material.name for obj in objects for slot in obj.material_slots if slot.material})
    missing_uv, loose, non_manifold = topology_checks(objects)
    transform_errors = [obj.name for obj in objects if any(abs(v - 1.0) > 1e-6 for v in obj.scale) or any(abs(v) > 1e-6 for v in obj.rotation_euler)]
    full_boards = [obj for obj in objects if "SidingFull_" in obj.name]
    side_boards = [obj for obj in objects if "SidingSide_" in obj.name]
    full_meshes = {obj.data.name for obj in full_boards}
    side_meshes = {obj.data.name for obj in side_boards}
    libraries = [lib.filepath for lib in bpy.data.libraries]
    missing_images = [img.filepath for img in bpy.data.images if img.source == 'FILE' and not img.packed_file and not Path(bpy.path.abspath(img.filepath)).exists()]
    naming = naming_errors()
    intrusions = opening_intrusions(objects)
    gameplay = [obj.name for obj in bpy.data.objects if obj.rigid_body or obj.type == 'ARMATURE']
    door = bpy.data.objects.get("SM_FS_Review_DoorPlaceholder_Slab")
    door_side_gap = (1.0 - door.dimensions.x) / 2 if door else None
    door_top_gap = 2.2 - (door.location.z + door.dimensions.z / 2) if door else None

    checks = {
        "triangle_budget_500_5000": 500 <= tris <= 5000,
        "material_budget_max_2": len(materials) <= 2,
        "width_exact_4m": abs(dims[0] - 4.0) < 0.001,
        "height_exact_3m": abs(dims[2] - 3.0) < 0.001,
        "ground_contact_at_z0": abs(mins[2]) < 0.001,
        "doorway_clear_1m_x_2p2m": not intrusions,
        "applied_scale_and_rotation": not transform_errors,
        "valid_uv_layer_on_all_geo_meshes": not missing_uv,
        "no_loose_vertices": not loose,
        "closed_manifold_geometry": not non_manifold,
        "naming_contract": not naming,
        "linked_full_siding": len(full_boards) == 3 and len(full_meshes) == 1,
        "linked_door_side_siding": len(side_boards) == 24 and len(side_meshes) == 1,
        "no_external_libraries": not libraries,
        "no_missing_images": not missing_images,
        "visual_only_no_gameplay_components": not gameplay,
        "workflow_status_pending_user_approval": bpy.context.scene.get("workflow_state") == "QA_SHIP_PENDING_USER_APPROVAL",
    }
    if VERSION == "v02":
        checks["review_door_side_gap_max_0p02m"] = door_side_gap is not None and abs(door_side_gap - 0.02) < 0.001
        checks["review_door_top_gap_max_0p04m"] = door_top_gap is not None and abs(door_top_gap - 0.04) < 0.001
        checks["structural_geometry_unchanged"] = bpy.context.scene.get("structural_geometry_changed") is False
    verdict = "SHIP" if all(checks.values()) else "NO-SHIP"
    report = {
        "task_id": "AR-03", "version": VERSION, "asset": bpy.data.filepath, "qa_verdict": verdict,
        "approval_state": "PENDING_USER_APPROVAL", "evaluated_triangles": tris,
        "triangle_budget": [500, 5000], "unique_geo_mesh_datablocks": len({obj.data.name for obj in objects}),
        "geo_object_count": len(objects), "materials_used_by_asset": materials,
        "world_bounds_min": [round(v, 4) for v in mins], "world_bounds_max": [round(v, 4) for v in maxs],
        "evaluated_dimensions_m": [round(v, 4) for v in dims],
        "rough_opening_m": {"width_x": 1.0, "height_z": 2.2, "threshold_z": 0.0},
        "review_door_fit_m": {"side_gap_each": round(door_side_gap, 4) if door_side_gap is not None else None, "top_gap": round(door_top_gap, 4) if door_top_gap is not None else None},
        "linked_full_siding": {"objects": len(full_boards), "unique_meshes": len(full_meshes)},
        "linked_side_siding": {"objects": len(side_boards), "unique_meshes": len(side_meshes)},
        "checks": checks,
        "failures": {
            "opening_intrusions": intrusions, "transform_errors": transform_errors,
            "missing_uv": missing_uv, "loose_vertices": loose, "non_manifold_edges": non_manifold,
            "naming_errors": naming, "external_libraries": libraries,
            "missing_images": missing_images, "gameplay_components": gameplay,
        },
        "review_placeholder": "Excluded from COL_Geo statistics and future assembly geometry.",
        "review_renders": [
            f"ENV_FS_AR-03_{VERSION}_Hero.png", f"ENV_FS_AR-03_{VERSION}_Front.png",
            f"ENV_FS_AR-03_{VERSION}_Profile.png", f"ENV_FS_AR-03_{VERSION}_Wireframe.png",
            f"ENV_FS_AR-03_{VERSION}_MaterialBeauty.png", f"ENV_FS_AR-03_{VERSION}_GameplayDistance.png",
        ],
        "notes": [
            f"QA SHIP means the atomic asset passed technical and visual review; freezing {VERSION} requires explicit user approval.",
            "The finished reusable exterior door is intentionally deferred to AR-10.",
        ],
    }
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
