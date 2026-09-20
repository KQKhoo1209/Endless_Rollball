"""Create the Blender-only Street Theme Review v5 high-poly suburban master.

The script opens the preserved v4 Blender review, adds project-authored detail,
and saves only to the external Blender workspace. It never exports FBX files or
modifies Unity assets, prefabs, scenes, colliders, or gameplay code.
"""

from pathlib import Path
import importlib.util
import math
import random

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLENDER_WORKSPACE = Path(r"D:\Creative\Blender\Endless Rollball")
V4_BLEND = BLENDER_WORKSPACE / "Environment" / "StreetTheme" / "Review_v4" / "ENV_FS_StreetTheme_Review_v4.blend"
V4_SCRIPT = Path(__file__).with_name("build_street_theme_review.py")
SOURCE_DIR = BLENDER_WORKSPACE / "Environment" / "StreetTheme" / "Review_v5"
OUTPUT_BLEND = SOURCE_DIR / "ENV_FS_StreetTheme_Review_v5.blend"
RENDER_DIR = PROJECT_ROOT / "Artifacts" / "EnvironmentReview" / "StreetTheme" / "v5"


def load_base_helpers():
    spec = importlib.util.spec_from_file_location("street_review_v4_helpers", V4_SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


base = load_base_helpers()


def ensure_collection(name):
    existing = bpy.data.collections.get(name)
    if existing:
        return existing
    target = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(target)
    return target


def material(name):
    found = bpy.data.materials.get(name)
    if not found:
        raise RuntimeError(f"Required v4 material is missing: {name}")
    return found


def create_detail_materials():
    return {
        "asphalt_detail": base.create_noise_material("MAT_FS_V5_AsphaltAggregate", (0.055, 0.060, 0.064), (0.20, 0.21, 0.20), 58.0, 0.80, 0.16),
        "road_wear": base.create_noise_material("MAT_FS_V5_RoadWear", (0.16, 0.16, 0.15), (0.30, 0.29, 0.26), 18.0, 0.74, 0.10),
        "drain": base.create_material("MAT_FS_V5_DrainMetal", (0.055, 0.065, 0.072), metallic=0.78, roughness=0.40),
        "siding_white": base.create_noise_material("MAT_FS_V5_SidingWhite", (0.66, 0.68, 0.65), (0.96, 0.95, 0.89), 7.0, 0.68, 0.08),
        "siding_cream": base.create_noise_material("MAT_FS_V5_SidingCream", (0.60, 0.54, 0.41), (0.93, 0.84, 0.65), 7.0, 0.70, 0.08),
        "siding_teal": base.create_noise_material("MAT_FS_V5_SidingTeal", (0.035, 0.22, 0.25), (0.10, 0.52, 0.55), 7.0, 0.68, 0.08),
        "siding_blue": base.create_noise_material("MAT_FS_V5_SidingBlue", (0.055, 0.21, 0.34), (0.15, 0.48, 0.64), 7.0, 0.67, 0.08),
        "shingle_red": base.create_noise_material("MAT_FS_V5_ShingleRed", (0.24, 0.030, 0.018), (0.78, 0.14, 0.050), 25.0, 0.79, 0.18),
        "shingle_light": base.create_noise_material("MAT_FS_V5_ShingleLight", (0.38, 0.050, 0.020), (0.92, 0.24, 0.070), 25.0, 0.77, 0.17),
        "gutter": base.create_material("MAT_FS_V5_Gutter", (0.58, 0.62, 0.64), metallic=0.48, roughness=0.38),
        "hedge": base.create_material("MAT_FS_V5_Hedge", (0.025, 0.22, 0.035), roughness=0.90),
        "hedge_light": base.create_material("MAT_FS_V5_HedgeLight", (0.12, 0.46, 0.075), roughness=0.87),
        "flower_red": base.create_material("MAT_FS_V5_FlowerRed", (0.92, 0.035, 0.040), roughness=0.70),
        "flower_yellow": base.create_material("MAT_FS_V5_FlowerYellow", (1.0, 0.58, 0.025), roughness=0.68),
        "flower_pink": base.create_material("MAT_FS_V5_FlowerPink", (0.96, 0.17, 0.42), roughness=0.68),
        "flower_white": base.create_material("MAT_FS_V5_FlowerWhite", (0.94, 0.93, 0.84), roughness=0.66),
        "play_red": base.create_material("MAT_FS_V5_PlayRed", (0.78, 0.025, 0.018), metallic=0.16, roughness=0.36),
        "play_blue": base.create_material("MAT_FS_V5_PlayBlue", (0.025, 0.22, 0.62), metallic=0.22, roughness=0.32),
        "play_sand": base.create_noise_material("MAT_FS_V5_PlaySand", (0.40, 0.25, 0.10), (0.78, 0.58, 0.28), 32.0, 0.91, 0.14),
        "bench_wood": base.create_noise_material("MAT_FS_V5_BenchWood", (0.16, 0.055, 0.018), (0.48, 0.20, 0.055), 13.0, 0.80, 0.12),
        "cloud": base.create_material("MAT_FS_V5_Cloud", (0.94, 0.97, 1.0), roughness=0.94, emission=(0.15, 0.18, 0.22), emission_strength=0.18),
        "curtain": base.create_material("MAT_FS_V5_Curtain", (0.60, 0.035, 0.055), roughness=0.86),
        "rubber": material("MAT_FS_Tire"),
        "glass": material("MAT_FS_CarGlass"),
        "metal": material("MAT_FS_LampMetal"),
        "white": material("MAT_FS_Trim"),
        "grass": material("MAT_FS_Grass"),
        "bark": material("MAT_FS_Bark"),
        "marking_white": material("MAT_FS_RoadWhite"),
        "hazard_orange": material("MAT_FS_HazardOrange"),
        "hazard_dark": material("MAT_FS_HazardDark"),
    }


def hide_prefixes(prefixes):
    hidden = []
    for obj in bpy.data.objects:
        if any(obj.name.startswith(prefix) for prefix in prefixes):
            obj.hide_render = True
            obj.hide_viewport = True
            hidden.append(obj.name)
    return hidden


def create_crowned_road(target, materials):
    x_values = (-4.58, -3.25, -1.62, 0.0, 1.62, 3.25, 4.58)
    for module in range(4):
        y0 = module * 24.0
        vertices = []
        faces = []
        y_steps = 24
        for yi in range(y_steps + 1):
            y = y0 + yi
            for x in x_values:
                crown = 0.008 + 0.021 * (1.0 - abs(x) / 4.58)
                wear = 0.0015 * math.sin((y + x * 2.2) * 1.7)
                vertices.append((x, y, crown + wear))
        width = len(x_values)
        for yi in range(y_steps):
            for xi in range(width - 1):
                a = yi * width + xi
                faces.append((a, a + 1, a + width + 1, a + width))
        mesh = bpy.data.meshes.new(f"FS_V5_RoadCrown_{module + 1:02d}_Mesh")
        mesh.from_pydata(vertices, [], faces)
        mesh.update()
        obj = bpy.data.objects.new(f"FS_V5_RoadCrown_{module + 1:02d}", mesh)
        target.objects.link(obj)
        base.add_material(obj, materials["asphalt_detail"])

        for side in (-1, 1):
            base.box(target, f"FS_V5_Gutter_{module + 1:02d}_{side}", (side * 4.48, y0 + 12.0, 0.035), (0.20, 23.85, 0.06), materials["drain"], 0.018)
            for drain_index, drain_y in enumerate((y0 + 5.5, y0 + 18.5)):
                grate = base.box(target, f"FS_V5_Drain_{module + 1:02d}_{side}_{drain_index}", (side * 4.47, drain_y, 0.075), (0.18, 0.62, 0.035), materials["drain"], 0.01)
                for slot in range(5):
                    base.box(target, f"FS_V5_DrainSlot_{module + 1:02d}_{side}_{drain_index}_{slot}", (side * 4.47, drain_y - 0.22 + slot * 0.11, 0.095), (0.12, 0.025, 0.012), materials["hazard_dark"], 0.003)

        for seam in range(1, 12):
            slab_y = y0 + seam * 2.0
            for side in (-1, 1):
                base.box(target, f"FS_V5_SidewalkJoint_{module + 1:02d}_{side}_{seam}", (side * 5.55, slab_y, 0.313), (1.34, 0.025, 0.012), materials["drain"], 0.002)


def create_lawn_slope(target, name, side, y_center, length, materials, outer_height=0.72):
    inner_x = side * 6.30
    outer_x = side * 12.20
    y0 = y_center - length * 0.5
    y1 = y_center + length * 0.5
    vertices = [
        (inner_x, y0, 0.02), (inner_x, y1, 0.02),
        (outer_x, y0, outer_height), (outer_x, y1, outer_height),
        (inner_x, y0, -0.16), (inner_x, y1, -0.16),
        (outer_x, y0, outer_height - 0.20), (outer_x, y1, outer_height - 0.20),
    ]
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 4, 6, 2), (1, 3, 7, 5), (0, 1, 5, 4), (2, 6, 7, 3)]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    base.add_material(obj, materials["grass"])
    base.bevel(obj, 0.08, 3)
    return obj


def create_shared_hedge_mesh(name, material_value, subdivisions=4):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0)
    source = bpy.context.object
    source.name = name + "_Source"
    source.data.name = name + "_Mesh"
    source.data.materials.append(material_value)
    mesh = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    return mesh


def create_shared_box_mesh(name, material_value, bevel_width=0.035, bevel_segments=3):
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    source = bpy.context.object
    source.name = name + "_Source"
    source.data.name = name + "_Mesh"
    source.data.materials.append(material_value)
    modifier = source.modifiers.new("Edge Softness", "BEVEL")
    modifier.width = bevel_width
    modifier.segments = bevel_segments
    modifier.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = source
    source.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    mesh = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    return mesh


def create_shared_cylinder_mesh(name, material_value, vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=0.5, depth=1.0)
    source = bpy.context.object
    source.name = name + "_Source"
    source.data.name = name + "_Mesh"
    source.data.materials.append(material_value)
    mesh = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    return mesh


def create_shared_sphere_mesh(name, material_value, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0)
    source = bpy.context.object
    source.name = name + "_Source"
    source.data.name = name + "_Mesh"
    source.data.materials.append(material_value)
    for polygon in source.data.polygons:
        polygon.use_smooth = True
    mesh = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    return mesh


def linked_object(target, name, mesh, location, scale=(1.0, 1.0, 1.0), rotation=(0.0, 0.0, 0.0)):
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    obj.location = location
    obj.scale = scale
    obj.rotation_euler = rotation
    return obj


def create_hedge_run(target, name, x, y_center, length, materials, rng, along_y=True, layers=2):
    meshes = materials["hedge_meshes"]
    spacing = 0.42
    count = max(2, int(length / spacing))
    for layer in range(layers):
        for index in range(count + 1):
            offset = -length * 0.5 + index * (length / count)
            obj = bpy.data.objects.new(f"{name}_{layer}_{index:03d}", meshes[(index + layer) % len(meshes)])
            target.objects.link(obj)
            if along_y:
                obj.location = (x + rng.uniform(-0.06, 0.06), y_center + offset, 0.42 + layer * 0.34 + rng.uniform(-0.04, 0.04))
                obj.scale = (0.34 + rng.uniform(-0.04, 0.05), 0.38 + rng.uniform(-0.04, 0.05), 0.31 + rng.uniform(-0.03, 0.05))
            else:
                obj.location = (x + offset, y_center + rng.uniform(-0.06, 0.06), 0.42 + layer * 0.34 + rng.uniform(-0.04, 0.04))
                obj.scale = (0.38 + rng.uniform(-0.04, 0.05), 0.34 + rng.uniform(-0.04, 0.05), 0.31 + rng.uniform(-0.03, 0.05))
            obj.rotation_euler = (rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), rng.uniform(0.0, math.tau))


def add_house_detail(target, name, side, y, variant, materials):
    road_direction = -side
    x = side * (9.2 + (variant % 2) * 0.4)
    width_y = 6.8 + (variant % 2) * 0.6
    wall_height = 4.4 + (variant % 3) * 0.25
    facade_x = x + road_direction * 2.57
    siding_material = materials["siding_white"] if variant % 3 else materials["siding_cream"]

    board_index = 0
    z = 0.62
    while z < wall_height - 0.08:
        siding_mesh = materials["siding_meshes"][siding_material.name]
        linked_object(target, f"{name}_Siding_{board_index:02d}", siding_mesh, (facade_x, y, z), (0.075, width_y - 0.18, 0.17))
        z += 0.215
        board_index += 1

    hx = 2.85
    roof_width = width_y + 0.7
    eave_z = wall_height
    ridge_z = wall_height + 2.25
    roof_angle = math.atan((ridge_z - eave_z) / hx)
    rows = 11
    columns = max(10, int(roof_width / 0.52))
    for roof_side in (-1, 1):
        for row in range(rows):
            t = (row + 0.5) / rows
            tile_x = x + roof_side * hx * t
            tile_z = ridge_z - (ridge_z - eave_z) * t + 0.055
            for column in range(columns):
                tile_y = y - roof_width * 0.5 + (column + 0.5) * roof_width / columns
                if row % 2:
                    tile_y += 0.5 * roof_width / columns
                tile_material = materials["shingle_light"] if (row + column) % 5 == 0 else materials["shingle_red"]
                tile_mesh = materials["shingle_meshes"][tile_material.name]
                linked_object(
                    target,
                    f"{name}_Shingle_{roof_side}_{row:02d}_{column:02d}",
                    tile_mesh,
                    (tile_x, tile_y, tile_z),
                    (0.54, roof_width / columns * 0.94, 0.075),
                    (0.0, roof_side * roof_angle, 0.0),
                )

    for roof_side in (-1, 1):
        eave_x = x + roof_side * hx
        base.cylinder(target, f"{name}_Gutter_{roof_side}", (eave_x, y, eave_z + 0.02), 0.075, roof_width, materials["gutter"], 20, rotation=(math.pi / 2.0, 0.0, 0.0), bevel_width=0.015)
        downpipe_y = y - roof_width * 0.38
        base.cylinder(target, f"{name}_Downpipe_{roof_side}", (eave_x, downpipe_y, 2.0), 0.055, 3.9, materials["gutter"], 16, bevel_width=0.012)

    door_y = y - width_y * 0.22
    for post_offset in (-0.78, 0.78):
        base.cylinder(target, f"{name}_PorchPost_{post_offset}", (facade_x + road_direction * 0.86, door_y + post_offset, 1.25), 0.065, 2.5, materials["white"], 16, bevel_width=0.015)
    for window_y in (y + width_y * 0.23, y - width_y * 0.23):
        base.box(target, f"{name}_Curtain_{window_y:.2f}", (facade_x + road_direction * 0.15, window_y, 2.95), (0.028, 0.38, 0.75), materials["curtain"], 0.008)
        base.box(target, f"{name}_Sill_{window_y:.2f}", (facade_x + road_direction * 0.18, window_y, 2.66), (0.18, 1.42, 0.10), materials["white"], 0.025)

    dormer_y = y + width_y * 0.16
    dormer_x = x + road_direction * 1.05
    base.box(target, f"{name}_DormerBody", (dormer_x, dormer_y, wall_height + 1.12), (1.22, 1.42, 1.45), siding_material, 0.06)
    base.gable_roof(target, f"{name}_DormerRoof", (dormer_x, dormer_y), 1.65, 1.72, wall_height + 1.55, wall_height + 2.22, materials["shingle_red"])
    base.box(target, f"{name}_DormerWindow", (dormer_x + road_direction * 0.64, dormer_y, wall_height + 1.18), (0.07, 0.68, 0.76), material("MAT_FS_WindowGlass"), 0.025)


def add_row_detail(target, side, start_y, count, materials, start_index=0):
    road_direction = -side
    x = side * 9.0
    palette = (materials["siding_blue"], materials["siding_teal"], materials["siding_white"], materials["siding_cream"])
    for index in range(start_index, count):
        y = start_y + index * 5.6
        height = 6.4 + (index % 2) * 0.8
        name = f"FS_V5_RowDetail_{'R' if side > 0 else 'L'}_{index + 1}"
        facade_x = x + road_direction * 2.70
        panel_material = palette[index % len(palette)]
        z = 2.55
        board = 0
        while z < height - 0.20:
            siding_mesh = materials["siding_meshes"][panel_material.name]
            linked_object(target, f"{name}_Siding_{board:02d}", siding_mesh, (facade_x, y, z), (0.065, 5.12, 0.19))
            z += 0.26
            board += 1
        base.box(target, name + "_Cornice", (facade_x, y, height - 0.10), (0.28, 5.38, 0.34), materials["white"], 0.055)
        base.box(target, name + "_RoofCap", (x, y, height + 0.62), (5.48, 5.62, 0.20), materials["gutter"], 0.045)
        for floor_z in (1.55, 4.15):
            for window_y in (y - 0.5, y + 1.45):
                base.box(target, f"{name}_MullionV_{floor_z}_{window_y}", (facade_x + road_direction * 0.20, window_y, floor_z), (0.035, 0.08, 0.90), materials["white"], 0.008)
                base.box(target, f"{name}_MullionH_{floor_z}_{window_y}", (facade_x + road_direction * 0.21, window_y, floor_z), (0.035, 0.92, 0.07), materials["white"], 0.008)
        gutter_y = y + 2.45
        base.cylinder(target, name + "_Downpipe", (facade_x + road_direction * 0.10, gutter_y, height * 0.48), 0.05, height * 0.92, materials["gutter"], 16, bevel_width=0.012)


def create_pointed_fence(target, name, x, y_center, length, materials, along_y=True):
    count = max(3, int(length / 0.48))
    for index in range(count + 1):
        offset = -length * 0.5 + index * length / count
        location = (x, y_center + offset, 0.73) if along_y else (x + offset, y_center, 0.73)
        base.box(target, f"{name}_Picket_{index:02d}", location, (0.105, 0.105, 1.24), materials["white"], 0.025)
        base.tapered_branch(
            target,
            f"{name}_Point_{index:02d}",
            (location[0], location[1], 1.35),
            (location[0], location[1], 1.59),
            0.105,
            0.0,
            materials["white"],
            4,
        )
    if along_y:
        for z in (0.48, 1.02):
            base.box(target, f"{name}_Rail_{z}", (x, y_center, z), (0.12, length + 0.16, 0.11), materials["white"], 0.022)
    else:
        for z in (0.48, 1.02):
            base.box(target, f"{name}_Rail_{z}", (x, y_center, z), (length + 0.16, 0.12, 0.11), materials["white"], 0.022)


def create_flowerbed(target, name, x, y_center, length, materials, rng, along_y=True):
    flower_materials = (materials["flower_red"], materials["flower_yellow"], materials["flower_pink"], materials["flower_white"])
    count = max(6, int(length / 0.38))
    for index in range(count):
        offset = -length * 0.5 + (index + 0.5) * length / count
        lateral = rng.uniform(-0.22, 0.22)
        px = x + lateral if along_y else x + offset
        py = y_center + offset if along_y else y_center + lateral
        stem_height = rng.uniform(0.20, 0.38)
        linked_object(target, f"{name}_Stem_{index:02d}", materials["flower_stem_mesh"], (px, py, 0.32 + stem_height * 0.5), (0.036, 0.036, stem_height))
        petal_material = flower_materials[index % len(flower_materials)]
        for petal in range(5):
            angle = petal * math.tau / 5.0
            linked_object(
                target,
                f"{name}_Petal_{index:02d}_{petal}",
                materials["flower_meshes"][petal_material.name],
                (px + math.cos(angle) * 0.09, py + math.sin(angle) * 0.09, 0.34 + stem_height),
                (0.094, 0.052, 0.026),
                (0.0, 0.0, angle),
            )
        linked_object(target, f"{name}_Centre_{index:02d}", materials["flower_centre_mesh"], (px, py, 0.35 + stem_height), (0.055, 0.055, 0.055))


def create_bench(target, name, location, rotation_z, materials):
    root = bpy.data.objects.new(name, None)
    target.objects.link(root)
    root.location = location
    root.rotation_euler.z = rotation_z
    for y in (-0.54, 0.54):
        leg = base.box(target, f"{name}_Leg_{y}", (0.0, y, 0.32), (0.55, 0.10, 0.60), materials["metal"], 0.035)
        leg.parent = root
        leg.location = (0.0, y, 0.32)
    for index, z in enumerate((0.62, 0.76, 0.90)):
        seat = base.box(target, f"{name}_Seat_{index}", (0.0, 0.0, z), (0.86, 2.2, 0.10), materials["bench_wood"], 0.035)
        seat.parent = root
        seat.location = (0.0, 0.0, z)
    for index, z in enumerate((1.12, 1.36, 1.60)):
        back = base.box(target, f"{name}_Back_{index}", (0.34, 0.0, z), (0.10, 2.2, 0.16), materials["bench_wood"], 0.035)
        back.parent = root
        back.location = (0.34, 0.0, z)
    return root


def create_playground(target, landscape_target, materials, rng):
    base.box(target, "FS_V5_Playground_SandPad", (-9.55, 60.3, 0.33), (5.1, 17.2, 0.22), materials["play_sand"], 0.20)
    create_pointed_fence(target, "FS_V5_Playground_FenceRoad", -6.90, 60.3, 17.2, materials)
    create_pointed_fence(target, "FS_V5_Playground_FenceRear", -12.10, 60.3, 17.2, materials)
    create_pointed_fence(target, "FS_V5_Playground_FenceNear", -9.50, 51.7, 5.2, materials, along_y=False)
    create_pointed_fence(target, "FS_V5_Playground_FenceFar", -9.50, 68.9, 5.2, materials, along_y=False)

    # Slide assembly: a raised platform, stairs, safety rails and a broad red slide.
    base.box(target, "FS_V5_Playground_SlidePlatform", (-9.80, 56.0, 2.35), (2.2, 2.2, 0.18), materials["play_blue"], 0.07)
    for x in (-10.65, -8.95):
        for y in (55.15, 56.85):
            base.cylinder(target, f"FS_V5_Playground_SlidePost_{x}_{y}", (x, y, 1.35), 0.075, 2.7, materials["metal"], 16, bevel_width=0.018)
    slide = base.box(target, "FS_V5_Playground_SlideSurface", (-9.80, 58.2, 1.43), (1.10, 4.65, 0.13), materials["play_red"], 0.10, rotation=(math.radians(24), 0.0, 0.0))
    for x in (-10.42, -9.18):
        base.cylinder_between(target, f"FS_V5_Playground_SlideRail_{x}", (x, 56.5, 2.68), (x, 60.2, 0.58), 0.055, materials["metal"], 16)
    for step in range(7):
        y = 54.95 - step * 0.34
        z = 2.15 - step * 0.25
        base.box(target, f"FS_V5_Playground_LadderStep_{step}", (-9.80, y, z), (1.15, 0.18, 0.10), materials["play_blue"], 0.025)
    base.cylinder_between(target, "FS_V5_Playground_LadderRailL", (-10.48, 55.0, 2.55), (-10.48, 52.9, 0.72), 0.055, materials["metal"], 16)
    base.cylinder_between(target, "FS_V5_Playground_LadderRailR", (-9.12, 55.0, 2.55), (-9.12, 52.9, 0.72), 0.055, materials["metal"], 16)

    # Swing set with two readable seats; all parts remain well outside the 9 m route.
    for y in (62.0, 66.0):
        base.cylinder_between(target, f"FS_V5_Playground_SwingA_L_{y}", (-11.25, y, 0.35), (-10.45, y, 3.25), 0.075, materials["play_blue"], 16)
        base.cylinder_between(target, f"FS_V5_Playground_SwingA_R_{y}", (-8.15, y, 0.35), (-8.95, y, 3.25), 0.075, materials["play_blue"], 16)
    base.cylinder_between(target, "FS_V5_Playground_SwingTop", (-9.70, 61.7, 3.25), (-9.70, 66.3, 3.25), 0.095, materials["play_red"], 18)
    for seat_y in (63.0, 65.0):
        for x in (-10.05, -9.35):
            base.cylinder_between(target, f"FS_V5_Playground_SwingRope_{x}_{seat_y}", (x, seat_y, 3.17), (x, seat_y, 1.12), 0.018, materials["hazard_dark"], 10)
        base.box(target, f"FS_V5_Playground_SwingSeat_{seat_y}", (-9.70, seat_y, 1.05), (0.95, 0.42, 0.10), materials["play_blue"], 0.04)

    create_bench(target, "FS_V5_Playground_BenchNear", (-11.20, 52.8, 0.35), 0.0, materials)
    create_bench(target, "FS_V5_Playground_BenchFar", (-11.20, 68.0, 0.35), math.pi, materials)
    create_hedge_run(landscape_target, "FS_V5_Playground_HedgeRear", -12.35, 60.3, 17.5, materials, rng, layers=2)
    create_flowerbed(landscape_target, "FS_V5_Playground_FlowersRoad", -6.65, 60.3, 15.6, materials, rng)


def add_utility_details(target, materials):
    for side in (-1, 1):
        x = side * 7.3
        for index, y in enumerate((0.0, 24.0, 48.0, 72.0, 96.0)):
            for offset in (-0.48, 0.0, 0.48):
                base.cylinder(target, f"FS_V5_Insulator_{side}_{index}_{offset}", (x + offset, y, 6.82), 0.075, 0.28, materials["gutter"], 20, bevel_width=0.018)
                for ring in (-0.08, 0.08):
                    base.cylinder(target, f"FS_V5_InsulatorRing_{side}_{index}_{offset}_{ring}", (x + offset, y, 6.82 + ring), 0.12, 0.035, materials["siding_blue"], 20, bevel_width=0.008)
            if index in (1, 3):
                base.cylinder(target, f"FS_V5_Transformer_{side}_{index}", (x + side * 0.28, y, 5.45), 0.32, 0.85, materials["gutter"], 24, bevel_width=0.045)
                base.box(target, f"FS_V5_TransformerBracket_{side}_{index}", (x, y, 5.55), (0.80, 0.22, 0.12), materials["metal"], 0.025)
    for lamp_index, y in enumerate((5.5, 18.0, 31.0, 43.0, 57.0, 70.0, 83.0, 94.0)):
        for side in (-1, 1):
            x = side * 6.15
            for ring_z in (1.1, 3.3):
                base.cylinder(target, f"FS_V5_LampCollar_{lamp_index}_{side}_{ring_z}", (x, y, ring_z), 0.14, 0.08, materials["gutter"], 20, bevel_width=0.012)


def parent_local(obj, root, location, rotation=(0.0, 0.0, 0.0)):
    obj.parent = root
    obj.location = location
    obj.rotation_euler = rotation
    return obj


def add_obstacle_details(target, materials):
    for root_name, paint in (
        ("FS_Obstacle_CrossingCar_MotionRoot", material("MAT_FS_CarRed")),
        ("FS_Obstacle_ForwardCar_MotionRoot", material("MAT_FS_CarBlue")),
    ):
        root = bpy.data.objects.get(root_name)
        if not root:
            raise RuntimeError(f"Missing animated obstacle root: {root_name}")
        short = root_name.replace("_MotionRoot", "")
        for side in (-1, 1):
            mirror = base.box(target, f"{short}_V5_Mirror_{side}", (0, 0, 0), (0.18, 0.28, 0.14), paint, 0.055)
            parent_local(mirror, root, (side * 0.93, 0.35, 1.25))
            for wheel_y in (-0.80, 0.80):
                bpy.ops.mesh.primitive_torus_add(major_radius=0.33, minor_radius=0.045, major_segments=32, minor_segments=10)
                tread = bpy.context.object
                tread.name = f"{short}_V5_WheelTread_{side}_{wheel_y}"
                base.move_to_collection(tread, target)
                base.add_material(tread, materials["rubber"])
                parent_local(tread, root, (side * 0.83, wheel_y, 0.48), (0.0, math.pi / 2.0, 0.0))
        for y in (-1.18, 1.18):
            plate = base.box(target, f"{short}_V5_Plate_{y}", (0, 0, 0), (0.62, 0.035, 0.22), materials["white"], 0.025)
            parent_local(plate, root, (0.0, y, 0.65))
        for side in (-1, 1):
            seam = base.box(target, f"{short}_V5_DoorSeam_{side}", (0, 0, 0), (0.025, 1.15, 0.055), materials["hazard_dark"], 0.006)
            parent_local(seam, root, (side * 0.856, 0.0, 1.10))

    bin_root = bpy.data.objects.get("FS_Obstacle_DroppedRubbishBin_DropRoot")
    if not bin_root:
        raise RuntimeError("Missing animated rubbish-bin root")
    for rib_x in (-0.44, -0.22, 0.0, 0.22, 0.44):
        rib = base.box(target, f"FS_Obstacle_DroppedRubbishBin_V5_Rib_{rib_x}", (0, 0, 0), (0.045, 0.94, 0.92), material("MAT_FS_BinGreen"), 0.018)
        parent_local(rib, bin_root, (rib_x, 0.0, 0.76))
    axle = base.cylinder(target, "FS_Obstacle_DroppedRubbishBin_V5_Axle", (0, 0, 0), 0.055, 1.35, materials["metal"], 18, rotation=(0.0, math.pi / 2.0, 0.0), bevel_width=0.012)
    parent_local(axle, bin_root, (0.0, -0.48, 0.25), (0.0, math.pi / 2.0, 0.0))
    for x in (-0.46, 0.46):
        handle = base.cylinder(target, f"FS_Obstacle_DroppedRubbishBin_V5_Handle_{x}", (0, 0, 0), 0.045, 0.45, materials["metal"], 16, rotation=(math.pi / 2.0, 0.0, 0.0), bevel_width=0.01)
        parent_local(handle, bin_root, (x, -0.60, 1.48), (math.pi / 2.0, 0.0, 0.0))

    for index, x in enumerate((-3.8, -2.7, -1.6)):
        base.box(target, f"FS_V5_BarrierReflector_{index}", (x, 75.80, 0.62), (0.54, 0.035, 0.36), materials["marking_white"], 0.025, rotation=(0.0, math.radians(-22 if index % 2 else 22), 0.0))
    for index, (x, y) in enumerate(((-2.8, 73.0), (-1.9, 74.1), (-3.5, 74.4))):
        base.cylinder(target, f"FS_V5_ConeReflector_{index}", (x, y, 0.53), 0.22, 0.10, materials["marking_white"], 24, bevel_width=0.02)


def add_clouds(target, materials):
    cloud_meshes = []
    for index, scale in enumerate(((1.0, 0.70, 0.55), (1.15, 0.80, 0.62), (0.85, 0.60, 0.48))):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=1.0)
        source = bpy.context.object
        source.name = f"FS_V5_CloudSource_{index}"
        source.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        source.data.materials.append(materials["cloud"])
        mesh = source.data
        bpy.data.objects.remove(source, do_unlink=True)
        cloud_meshes.append(mesh)
    for bank_index, (x, y, z) in enumerate(((-22, 58, 16), (18, 78, 18), (-14, 98, 20), (24, 35, 17))):
        for puff in range(7):
            obj = bpy.data.objects.new(f"FS_V5_Cloud_{bank_index}_{puff}", cloud_meshes[puff % len(cloud_meshes)])
            target.objects.link(obj)
            obj.location = (x + puff * 1.05, y + math.sin(puff) * 1.4, z + math.sin(puff * 1.6) * 0.55)
            obj.scale = (1.3 + (puff % 3) * 0.25, 1.0, 1.0)


def create_review_camera(target, name, location, look_target, lens):
    camera_data = bpy.data.cameras.new(name + "_Data")
    camera = bpy.data.objects.new(name, camera_data)
    target.objects.link(camera)
    camera.location = location
    camera_data.lens = lens
    camera_data.sensor_width = 36.0
    camera_data.dof.use_dof = False
    base.look_at(camera, Vector(look_target))
    return camera


def evaluated_triangle_count(scene):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    total = 0
    for obj in scene.objects:
        if obj.type != "MESH" or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            total += len(mesh.loop_triangles)
        finally:
            evaluated.to_mesh_clear()
    return total


def build_v5():
    if not V4_BLEND.exists():
        raise FileNotFoundError(f"Preserved v4 master not found: {V4_BLEND}")
    if V4_BLEND.resolve() == OUTPUT_BLEND.resolve():
        raise RuntimeError("v5 output must never overwrite v4")

    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(V4_BLEND))

    scene = bpy.context.scene
    scene.name = "Street Theme Review v5"
    scene["review_version"] = "v5"
    scene["art_direction"] = "High-poly cartoon-realistic suburban street; reference-led palette and density only"
    scene["reference_boundary"] = "No protected characters, logos, branding, or proprietary asset reproduction"
    scene["route_width_metres"] = 9.0
    scene["module_length_metres"] = 24.0
    scene["module_count"] = 4
    scene["authoring_forward"] = "+Y"
    scene["unity_integration"] = "None; Blender review master only"
    scene["triangle_target"] = "1,000,000-2,000,000 evaluated triangles"

    road_target = ensure_collection("01A_V5_ROAD_DETAIL")
    architecture_target = ensure_collection("02A_V5_ARCHITECTURE_DETAIL")
    utility_target = ensure_collection("04A_V5_UTILITY_DETAIL")
    obstacle_target = ensure_collection("05A_V5_OBSTACLE_DETAIL")
    landscape_target = ensure_collection("06B_V5_LANDSCAPE")
    playground_target = ensure_collection("06C_V5_PLAYGROUND")
    sky_target = ensure_collection("06D_V5_SKY_DETAIL")
    camera_target = ensure_collection("07_REVIEW_CAMERAS")

    materials = create_detail_materials()
    materials["hedge_meshes"] = (
        create_shared_hedge_mesh("FS_V5_HedgeDense", materials["hedge"], 4),
        create_shared_hedge_mesh("FS_V5_HedgeLight", materials["hedge_light"], 4),
    )
    materials["shingle_meshes"] = {
        materials["shingle_red"].name: create_shared_box_mesh("FS_V5_ShingleRed", materials["shingle_red"], 0.08, 3),
        materials["shingle_light"].name: create_shared_box_mesh("FS_V5_ShingleLight", materials["shingle_light"], 0.08, 3),
    }
    materials["siding_meshes"] = {
        siding_material.name: create_shared_box_mesh(f"FS_V5_Siding_{index}", siding_material, 0.07, 3)
        for index, siding_material in enumerate((materials["siding_white"], materials["siding_cream"], materials["siding_teal"], materials["siding_blue"]))
    }
    flower_materials = (materials["flower_red"], materials["flower_yellow"], materials["flower_pink"], materials["flower_white"])
    materials["flower_meshes"] = {
        flower_material.name: create_shared_sphere_mesh(f"FS_V5_FlowerPetal_{index}", flower_material, 2)
        for index, flower_material in enumerate(flower_materials)
    }
    materials["flower_centre_mesh"] = create_shared_sphere_mesh("FS_V5_FlowerCentre", materials["flower_yellow"], 2)
    materials["flower_stem_mesh"] = create_shared_cylinder_mesh("FS_V5_FlowerStem", materials["grass"], 8)
    rng = random.Random(50721)
    print("V5_PHASE=shared-assets-ready", flush=True)

    hidden = hide_prefixes(("FS_House_05", "FS_Fence_05", "FS_Mailbox_05", "FS_Row_L_1", "FS_Row_L_2"))
    scene["v5_hidden_v4_objects"] = len(hidden)

    create_crowned_road(road_target, materials)
    for side in (-1, 1):
        for module in range(4):
            create_lawn_slope(landscape_target, f"FS_V5_Lawn_{module + 1}_{side}", side, module * 24.0 + 12.0, 23.8, materials, 0.55 + module * 0.06)
    print("V5_PHASE=road-and-lawns-ready", flush=True)

    for index, (side, y) in enumerate(((-1, 10.0), (1, 16.0), (-1, 30.0), (1, 34.0))):
        add_house_detail(architecture_target, f"FS_V5_HouseDetail_{index + 1:02d}", side, y, index, materials)
    add_row_detail(architecture_target, 1, 59.0, 6, materials)
    add_row_detail(architecture_target, -1, 62.0, 6, materials, start_index=2)
    print("V5_PHASE=architecture-ready", flush=True)

    hedge_specs = (
        (-7.10, 10.0, 8.0), (7.10, 16.0, 8.0), (-7.10, 30.0, 8.0), (7.10, 34.0, 8.0),
        (7.00, 63.5, 10.0), (7.00, 78.0, 10.0), (-7.00, 79.5, 9.0), (-7.00, 91.0, 8.0),
    )
    for index, (x, y, length) in enumerate(hedge_specs):
        create_hedge_run(landscape_target, f"FS_V5_HedgeRun_{index + 1:02d}", x, y, length, materials, rng, layers=2)
        create_flowerbed(landscape_target, f"FS_V5_Flowerbed_{index + 1:02d}", x - math.copysign(0.46, x), y, length * 0.72, materials, rng)

    create_playground(playground_target, landscape_target, materials, rng)
    print("V5_PHASE=landscape-and-playground-ready", flush=True)
    add_utility_details(utility_target, materials)
    add_obstacle_details(obstacle_target, materials)
    add_clouds(sky_target, materials)
    print("V5_PHASE=props-and-obstacles-ready", flush=True)

    cameras = {obj.name: obj for obj in bpy.data.objects if obj.type == "CAMERA"}
    cameras["CAM_FS_PlaygroundReview"] = create_review_camera(camera_target, "CAM_FS_PlaygroundReview", (-1.0, 47.0, 6.6), (-9.6, 60.0, 1.45), 47.0)
    cameras["CAM_FS_MaterialDetail"] = create_review_camera(camera_target, "CAM_FS_MaterialDetail", (2.2, 6.5, 5.2), (-8.4, 10.2, 2.6), 57.0)

    required_markers = {"BIN_DROP_START": 32, "CAR_CROSSING_START": 34, "REVIEW_POSE": 55, "CAR_CROSSING_END": 78}
    actual_markers = {marker.name: marker.frame for marker in scene.timeline_markers}
    for marker_name, frame in required_markers.items():
        if actual_markers.get(marker_name) != frame:
            raise RuntimeError(f"Timeline marker changed: {marker_name} expected {frame}, got {actual_markers.get(marker_name)}")

    scene.frame_set(55)
    print("V5_PHASE=triangle-evaluation-start", flush=True)
    triangle_count = evaluated_triangle_count(scene)
    scene["evaluated_triangle_count_frame_55"] = triangle_count
    scene["linked_hedge_mesh_count"] = 2
    scene["procedural_materials_only"] = True
    print(f"V5_EVALUATED_TRIANGLES={triangle_count}", flush=True)

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"

    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    print("V5_PHASE=master-saved-before-renders", flush=True)

    render_jobs = (
        ("CAM_FS_GameplayReview", "FS_Street_v5_GameplayReview.png"),
        ("CAM_FS_StreetOverview", "FS_Street_v5_Overview.png"),
        ("CAM_FS_ObstacleReview", "FS_Street_v5_ObstacleReview.png"),
        ("CAM_FS_ArchitectureReview", "FS_Street_v5_ArchitectureReview.png"),
        ("CAM_FS_PlaygroundReview", "FS_Street_v5_PlaygroundReview.png"),
        ("CAM_FS_MaterialDetail", "FS_Street_v5_MaterialDetail.png"),
    )
    for camera_name, filename in render_jobs:
        camera = cameras.get(camera_name) or bpy.data.objects.get(camera_name)
        if not camera:
            raise RuntimeError(f"Review camera missing: {camera_name}")
        scene.camera = camera
        scene.render.filepath = str(RENDER_DIR / filename)
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED={scene.render.filepath}", flush=True)

    scene.camera = cameras["CAM_FS_GameplayReview"]
    scene.render.filepath = str(RENDER_DIR / "FS_Street_v5_GameplayReview.png")
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    print(f"BLEND_SAVED={OUTPUT_BLEND}", flush=True)
    print("UNITY_CHANGES=NONE", flush=True)


if __name__ == "__main__":
    build_v5()
