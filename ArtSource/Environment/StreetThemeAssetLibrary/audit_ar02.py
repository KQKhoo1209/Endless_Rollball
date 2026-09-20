import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector


REPORT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1\01_Architecture\Reports\ENV_FS_AR-02_ValidationReport_v01.json")


def evaluated_triangles(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        total += len(mesh.loop_triangles)
        evaluated.to_mesh_clear()
    return total


def bounds_for_object(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]


def world_bounds(objects):
    all_points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    mins = [min(p[i] for p in all_points) for i in range(3)]
    maxs = [max(p[i] for p in all_points) for i in range(3)]
    return mins, maxs, [maxs[i] - mins[i] for i in range(3)]


def topology_checks(objects):
    missing_uv = []
    loose_vertices = {}
    non_manifold_edges = {}
    for obj in objects:
        mesh = obj.data
        if not mesh.uv_layers:
            missing_uv.append(obj.name)
        referenced = {v for poly in mesh.polygons for v in poly.vertices}
        loose = len(mesh.vertices) - len(referenced)
        if loose:
            loose_vertices[obj.name] = loose
        bm = bmesh.new()
        bm.from_mesh(mesh)
        non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
        bm.free()
        if non_manifold:
            non_manifold_edges[obj.name] = non_manifold
    return missing_uv, loose_vertices, non_manifold_edges


def naming_errors():
    errors = []
    for obj in bpy.data.objects:
        expected = {"MESH": "SM_", "CAMERA": "CAM_", "LIGHT": "LGT_"}.get(obj.type)
        if expected and not obj.name.startswith(expected):
            errors.append(f"{obj.type}:{obj.name}")
        if " " in obj.name:
            errors.append(f"SPACE:{obj.name}")
    for mat in bpy.data.materials:
        if not mat.name.startswith("MAT_") or " " in mat.name:
            errors.append(f"MATERIAL:{mat.name}")
    for col in bpy.data.collections:
        if not col.name.startswith("COL_") or " " in col.name:
            errors.append(f"COLLECTION:{col.name}")
    return errors


def opening_intrusions(objects):
    # Clear opening interior: X -0.8..0.8 and Z 0.85..2.20. Boundary contact is permitted.
    intrusions = []
    for obj in objects:
        mins, maxs = bounds_for_object(obj)
        overlaps_x_interior = maxs[0] > -0.799 and mins[0] < 0.799
        overlaps_z_interior = maxs[2] > 0.851 and mins[2] < 2.199
        if overlaps_x_interior and overlaps_z_interior:
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
    external_libraries = [lib.filepath for lib in bpy.data.libraries]
    missing_images = [img.filepath for img in bpy.data.images if img.source == 'FILE' and not img.packed_file and not Path(bpy.path.abspath(img.filepath)).exists()]
    naming = naming_errors()
    intrusions = opening_intrusions(objects)
    gameplay_components = [obj.name for obj in bpy.data.objects if obj.rigid_body or obj.type == 'ARMATURE']

    checks = {
        "triangle_budget_500_5000": 500 <= tris <= 5000,
        "material_budget_max_2": len(materials) <= 2,
        "width_exact_4m": abs(dims[0] - 4.0) < 0.001,
        "height_exact_3m": abs(dims[2] - 3.0) < 0.001,
        "ground_contact_at_z0": abs(mins[2]) < 0.001,
        "rough_opening_clear_1p60x1p35": not intrusions,
        "applied_scale_and_rotation": not transform_errors,
        "valid_uv_layer_on_all_geo_meshes": not missing_uv,
        "no_loose_vertices": not loose,
        "closed_manifold_geometry": not non_manifold,
        "naming_contract": not naming,
        "linked_full_siding": len(full_boards) == 7 and len(full_meshes) == 1,
        "linked_opening_side_siding": len(side_boards) == 16 and len(side_meshes) == 1,
        "no_external_libraries": not external_libraries,
        "no_missing_images": not missing_images,
        "visual_only_no_gameplay_components": not gameplay_components,
        "workflow_status_pending_user_approval": bpy.context.scene.get("workflow_state") == "QA_SHIP_PENDING_USER_APPROVAL",
    }
    verdict = "SHIP" if all(checks.values()) else "NO-SHIP"
    report = {
        "task_id": "AR-02",
        "asset": bpy.data.filepath,
        "qa_verdict": verdict,
        "approval_state": "PENDING_USER_APPROVAL",
        "evaluated_triangles": tris,
        "triangle_budget": [500, 5000],
        "unique_geo_mesh_datablocks": len({obj.data.name for obj in objects}),
        "geo_object_count": len(objects),
        "materials_used_by_asset": materials,
        "world_bounds_min": [round(v, 4) for v in mins],
        "world_bounds_max": [round(v, 4) for v in maxs],
        "evaluated_dimensions_m": [round(v, 4) for v in dims],
        "rough_opening_m": {"width_x": 1.6, "height_z": 1.35, "sill_z": 0.85},
        "linked_full_siding": {"objects": len(full_boards), "unique_meshes": len(full_meshes)},
        "linked_side_siding": {"objects": len(side_boards), "unique_meshes": len(side_meshes)},
        "checks": checks,
        "failures": {
            "opening_intrusions": intrusions,
            "transform_errors": transform_errors,
            "missing_uv": missing_uv,
            "loose_vertices": loose,
            "non_manifold_edges": non_manifold,
            "naming_errors": naming,
            "external_libraries": external_libraries,
            "missing_images": missing_images,
            "gameplay_components": gameplay_components,
        },
        "review_placeholder": "Excluded from COL_Geo statistics and future assembly geometry.",
        "review_renders": [
            "ENV_FS_AR-02_Hero.png", "ENV_FS_AR-02_Front.png", "ENV_FS_AR-02_Profile.png",
            "ENV_FS_AR-02_Wireframe.png", "ENV_FS_AR-02_MaterialBeauty.png", "ENV_FS_AR-02_GameplayDistance.png",
        ],
        "notes": [
            "QA SHIP means the atomic asset passed technical and visual review; freezing v01 requires explicit user approval.",
            "The finished reusable window is intentionally deferred to AR-09.",
        ],
    }
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
