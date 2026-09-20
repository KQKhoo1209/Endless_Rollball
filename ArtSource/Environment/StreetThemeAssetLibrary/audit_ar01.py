import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector


REPORT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1\01_Architecture\Reports\ENV_FS_AR-01_ValidationReport_v01.json")


def evaluated_triangles(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    per_object = {}
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        count = len(mesh.loop_triangles)
        per_object[obj.name] = count
        total += count
        evaluated.to_mesh_clear()
    return total, per_object


def world_bounds(objects):
    points = []
    for obj in objects:
        points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    mins = [min(p[i] for p in points) for i in range(3)]
    maxs = [max(p[i] for p in points) for i in range(3)]
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


def main():
    geo = bpy.data.collections.get("COL_Geo")
    objects = [obj for obj in geo.all_objects if obj.type == 'MESH']
    tris, per_object = evaluated_triangles(objects)
    mins, maxs, dims = world_bounds(objects)
    materials = sorted({slot.material.name for obj in objects for slot in obj.material_slots if slot.material})
    missing_uv, loose, non_manifold = topology_checks(objects)
    transform_errors = [obj.name for obj in objects if any(abs(v - 1.0) > 1e-6 for v in obj.scale) or any(abs(v) > 1e-6 for v in obj.rotation_euler)]
    board_objects = [obj for obj in objects if obj.name.startswith("SM_FS_Architecture_SidingBoard_")]
    board_meshes = {obj.data.name for obj in board_objects}
    external_libraries = [lib.filepath for lib in bpy.data.libraries]
    missing_images = [img.filepath for img in bpy.data.images if img.source == 'FILE' and not img.packed_file and not Path(bpy.path.abspath(img.filepath)).exists()]
    naming = naming_errors()

    checks = {
        "triangle_budget_500_5000": 500 <= tris <= 5000,
        "material_budget_max_2": len(materials) <= 2,
        "width_exact_4m": abs(dims[0] - 4.0) < 0.001,
        "height_exact_3m": abs(dims[2] - 3.0) < 0.001,
        "ground_contact_at_z0": abs(mins[2]) < 0.001,
        "applied_scale_and_rotation": not transform_errors,
        "valid_uv_layer_on_all_geo_meshes": not missing_uv,
        "no_loose_vertices": not loose,
        "closed_manifold_geometry": not non_manifold,
        "naming_contract": not naming,
        "linked_siding_instances": len(board_objects) == 15 and len(board_meshes) == 1,
        "no_external_libraries": not external_libraries,
        "no_missing_images": not missing_images,
        "workflow_status_pending_user_approval": bpy.context.scene.get("workflow_state") == "QA_SHIP_PENDING_USER_APPROVAL",
    }
    verdict = "SHIP" if all(checks.values()) else "NO-SHIP"
    report = {
        "task_id": "AR-01",
        "asset": bpy.data.filepath,
        "qa_verdict": verdict,
        "approval_state": "PENDING_USER_APPROVAL",
        "evaluated_triangles": tris,
        "unique_geo_mesh_datablocks": len({obj.data.name for obj in objects}),
        "geo_object_count": len(objects),
        "materials_used_by_asset": materials,
        "world_bounds_min": [round(v, 4) for v in mins],
        "world_bounds_max": [round(v, 4) for v in maxs],
        "evaluated_dimensions_m": [round(v, 4) for v in dims],
        "linked_siding_board_objects": len(board_objects),
        "linked_siding_unique_meshes": len(board_meshes),
        "checks": checks,
        "failures": {
            "transform_errors": transform_errors,
            "missing_uv": missing_uv,
            "loose_vertices": loose,
            "non_manifold_edges": non_manifold,
            "naming_errors": naming,
            "external_libraries": external_libraries,
            "missing_images": missing_images,
        },
        "review_renders": [
            "ENV_FS_AR-01_Hero.png", "ENV_FS_AR-01_Front.png", "ENV_FS_AR-01_Profile.png",
            "ENV_FS_AR-01_Wireframe.png", "ENV_FS_AR-01_MaterialBeauty.png",
        ],
        "notes": [
            "QA SHIP means the atomic asset passed technical and visual review; freezing v01 still requires the explicit user approval gate.",
            "House-level roof-to-wall proportion and gameplay-distance composition are deferred to AR-AS01.",
        ],
    }
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
