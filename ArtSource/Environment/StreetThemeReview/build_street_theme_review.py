"""Build the Street Theme Blender-only review scene for Endless Rollball.

This script deliberately writes its Blender source to the external creative
workspace and its review renders to Artifacts. It does not export FBX files or
modify any Unity asset or scene.

Run with:
    D:\\blender.exe --background --factory-startup --python build_street_theme_review.py
"""

from pathlib import Path
import math
import random

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[3]
BLENDER_WORKSPACE = Path(r"D:\Creative\Blender\Endless Rollball")
SOURCE_DIR = BLENDER_WORKSPACE / "Environment" / "StreetTheme" / "Review_v4"
OUTPUT_BLEND = SOURCE_DIR / "ENV_FS_StreetTheme_Review_v4.blend"
RENDER_DIR = PROJECT_ROOT / "Artifacts" / "EnvironmentReview" / "StreetTheme" / "v4"


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(datablocks):
            if item.users == 0:
                datablocks.remove(item)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)


def collection(name):
    target = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(target)
    return target


def move_to_collection(obj, target):
    for owner in list(obj.users_collection):
        owner.objects.unlink(obj)
    target.objects.link(obj)


def create_material(name, color, metallic=0.0, roughness=0.55, emission=None, emission_strength=0.0):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    metallic_input = principled.inputs.get("Metallic IOR Level") or principled.inputs.get("Metallic")
    if metallic_input:
        metallic_input.default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if emission:
        principled.inputs["Emission Color"].default_value = (*emission, 1.0)
        principled.inputs["Emission Strength"].default_value = emission_strength
    return material


def create_noise_material(name, dark, light, scale, roughness=0.7, bump_strength=0.18):
    material = create_material(name, dark, roughness=roughness)
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 3.0
    noise.inputs["Roughness"].default_value = 0.65
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*dark, 1.0)
    ramp.color_ramp.elements[1].color = (*light, 1.0)
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = bump_strength
    bump.inputs["Distance"].default_value = 0.12
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], principled.inputs["Base Color"])
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], principled.inputs["Normal"])
    return material


def add_material(obj, material):
    if material:
        obj.data.materials.append(material)


def bevel(obj, width=0.08, segments=2):
    modifier = obj.modifiers.new("Soft cartoon bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"


def box(target, name, location, dimensions, material, bevel_width=0.04, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel_width > 0.0:
        bevel(obj, bevel_width, 2)
    add_material(obj, material)
    move_to_collection(obj, target)
    return obj


def cylinder(target, name, location, radius, depth, material, vertices=16, rotation=(0.0, 0.0, 0.0), bevel_width=0.03):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    if bevel_width > 0.0:
        bevel(obj, bevel_width, 2)
    add_material(obj, material)
    move_to_collection(obj, target)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def cylinder_between(target, name, start, end, radius, material, vertices=12):
    start_point = Vector(start)
    end_point = Vector(end)
    direction = end_point - start_point
    obj = cylinder(
        target,
        name,
        (start_point + end_point) * 0.5,
        radius,
        direction.length,
        material,
        vertices,
        bevel_width=0.02,
    )
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    return obj


def tapered_branch(target, name, start, end, base_radius, tip_radius, material, vertices=16):
    start_point = Vector(start)
    end_point = Vector(end)
    direction = end_point - start_point
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=base_radius,
        radius2=tip_radius,
        depth=direction.length,
        location=(start_point + end_point) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    bevel(obj, min(base_radius * 0.16, 0.045), 3)
    add_material(obj, material)
    move_to_collection(obj, target)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def sphere(target, name, location, radius, material, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    add_material(obj, material)
    move_to_collection(obj, target)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def curve_tube(target, name, points, radius, material):
    data = bpy.data.curves.new(name + "_Curve", "CURVE")
    data.dimensions = "3D"
    data.bevel_depth = radius
    data.bevel_resolution = 2
    spline = data.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for point, coordinate in zip(spline.bezier_points, points):
        point.co = coordinate
        point.handle_left_type = "AUTO"
        point.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, data)
    target.objects.link(obj)
    add_material(obj, material)
    return obj


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def gable_roof(target, name, center, depth_x, width_y, eave_z, ridge_z, material):
    x, y = center
    hx = depth_x * 0.5
    hy = width_y * 0.5
    vertices = [
        (x - hx, y - hy, eave_z),
        (x + hx, y - hy, eave_z),
        (x - hx, y + hy, eave_z),
        (x + hx, y + hy, eave_z),
        (x, y - hy, ridge_z),
        (x, y + hy, ridge_z),
    ]
    faces = [
        (0, 1, 4),
        (2, 5, 3),
        (0, 4, 5, 2),
        (1, 3, 5, 4),
        (0, 2, 3, 1),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    add_material(obj, material)
    bevel(obj, 0.06, 2)
    return obj


def tapered_bin_body(target, name, material):
    bottom_x, bottom_y = 0.30, 0.32
    top_x, top_y = 0.38, 0.40
    z0, z1 = 0.12, 1.05
    vertices = [
        (-bottom_x, -bottom_y, z0), (bottom_x, -bottom_y, z0),
        (bottom_x, bottom_y, z0), (-bottom_x, bottom_y, z0),
        (-top_x, -top_y, z1), (top_x, -top_y, z1),
        (top_x, top_y, z1), (-top_x, top_y, z1),
    ]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (4, 0, 3, 7)]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    add_material(obj, material)
    bevel(obj, 0.055, 3)
    return obj


def create_house(target, name, side, y, colors, materials, variant=0):
    road_direction = -side
    x = side * (9.2 + (variant % 2) * 0.4)
    width_y = 6.8 + (variant % 2) * 0.6
    wall_height = 4.4 + (variant % 3) * 0.25
    wall = colors[variant % len(colors)]
    body = box(target, name + "_Walls", (x, y, wall_height * 0.5), (5.0, width_y, wall_height), wall, 0.12)
    gable_roof(target, name + "_Roof", (x, y), 5.7, width_y + 0.7, wall_height, wall_height + 2.25, materials["roof_red"] if variant % 2 == 0 else materials["roof_blue"])
    facade_x = x + road_direction * 2.54
    box(target, name + "_Foundation", (x, y, 0.28), (5.2, width_y + 0.15, 0.55), materials["foundation"], 0.08)
    door_y = y - width_y * 0.22
    box(target, name + "_Door", (facade_x + road_direction * 0.035, door_y, 1.15), (0.10, 1.05, 2.3), materials["door"], 0.04)
    box(target, name + "_DoorGlass", (facade_x + road_direction * 0.095, door_y, 1.55), (0.035, 0.46, 0.65), materials["window"], 0.015)
    for wy, wz in ((y + width_y * 0.23, 1.55), (y + width_y * 0.23, 3.25), (y - width_y * 0.23, 3.25)):
        box(target, name + f"_Window_{wy:.1f}_{wz:.1f}", (facade_x + road_direction * 0.06, wy, wz), (0.08, 1.25, 1.1), materials["window_frame"], 0.04)
        box(target, name + f"_Glass_{wy:.1f}_{wz:.1f}", (facade_x + road_direction * 0.11, wy, wz), (0.025, 1.02, 0.88), materials["window"], 0.015)
    box(target, name + "_Porch", (facade_x + road_direction * 0.75, door_y, 0.18), (1.4, 2.0, 0.34), materials["concrete"], 0.07)
    box(target, name + "_Awning", (facade_x + road_direction * 0.48, door_y, 2.55), (0.95, 1.7, 0.16), materials["trim"], 0.04, rotation=(0.0, math.radians(road_direction * 12.0), 0.0))
    chimney_x = x - road_direction * 1.2
    box(target, name + "_Chimney", (chimney_x, y + width_y * 0.2, wall_height + 1.4), (0.55, 0.65, 2.1), materials["brick"], 0.06)
    return body


def create_row_buildings(target, side, start_y, count, materials):
    road_direction = -side
    x = side * 9.0
    palette = [materials["plaster_blue"], materials["plaster_yellow"], materials["plaster_coral"], materials["plaster_cream"]]
    for index in range(count):
        y = start_y + index * 5.6
        height = 6.4 + (index % 2) * 0.8
        name = f"FS_Row_{'R' if side > 0 else 'L'}_{index + 1}"
        box(target, name + "_Body", (x, y, height * 0.5), (5.2, 5.58, height), palette[index % len(palette)], 0.08)
        facade_x = x + road_direction * 2.64
        box(target, name + "_GroundBand", (facade_x + road_direction * 0.04, y, 1.25), (0.10, 5.15, 2.35), materials["foundation"], 0.03)
        box(target, name + "_Door", (facade_x + road_direction * 0.10, y - 1.75, 1.2), (0.08, 0.95, 2.4), materials["door"], 0.03)
        for floor_z in (1.55, 4.15):
            for window_y in (y - 0.5, y + 1.45):
                box(target, name + f"_Frame_{floor_z}_{window_y}", (facade_x + road_direction * 0.11, window_y, floor_z), (0.08, 1.2, 1.15), materials["window_frame"], 0.035)
                box(target, name + f"_Window_{floor_z}_{window_y}", (facade_x + road_direction * 0.16, window_y, floor_z), (0.025, 0.96, 0.9), materials["window"], 0.012)
        box(target, name + "_Awning", (facade_x + road_direction * 0.58, y + 0.15, 2.65), (1.05, 3.8, 0.18), materials["awning_red"] if index % 2 == 0 else materials["awning_teal"], 0.04, rotation=(0.0, math.radians(road_direction * 8.0), 0.0))
        box(target, name + "_Parapet", (x, y, height + 0.28), (5.4, 5.58, 0.55), materials["trim"], 0.05)


def create_tree(target, name, x, y, scale, materials):
    """Create a cartoon-realistic high-poly tree with tapered branching."""
    origin = Vector((x, y, 0.0))
    tapered_branch(target, name + "_MainTrunk", origin, origin + Vector((0.0, 0.0, 3.95 * scale)), 0.40 * scale, 0.22 * scale, materials["bark"], 24)
    tapered_branch(target, name + "_UpperTrunk", origin + Vector((0.0, 0.0, 3.35 * scale)), origin + Vector((0.06, -0.04, 5.15 * scale)), 0.24 * scale, 0.075 * scale, materials["bark_light"], 20)

    for root_index in range(6):
        angle = root_index * math.tau / 6.0
        direction = Vector((math.cos(angle), math.sin(angle), 0.0))
        tapered_branch(
            target,
            name + f"_Root_{root_index + 1:02d}",
            origin + Vector((0.0, 0.0, 0.14 * scale)),
            origin + direction * (0.78 * scale) + Vector((0.0, 0.0, 0.035 * scale)),
            0.18 * scale,
            0.025 * scale,
            materials["bark_dark"],
            16,
        )

    branches = (
        ((0.00, 0.00, 2.45), (-1.05, 0.26, 4.10)),
        ((0.00, 0.00, 2.65), (1.02, 0.36, 4.28)),
        ((0.00, 0.00, 2.82), (-0.34, -1.02, 4.42)),
        ((0.00, 0.00, 3.00), (0.38, 1.00, 4.58)),
        ((0.00, 0.00, 3.22), (-0.80, -0.68, 4.82)),
        ((0.00, 0.00, 3.38), (0.86, -0.54, 4.95)),
        ((0.02, 0.00, 3.56), (-0.70, 0.76, 5.15)),
        ((0.02, 0.00, 3.72), (0.66, 0.78, 5.30)),
        ((0.04, -0.02, 3.92), (-0.42, -0.48, 5.48)),
        ((0.04, -0.02, 4.08), (0.45, -0.36, 5.62)),
    )
    for index, (start, end) in enumerate(branches):
        start_point = origin + Vector(start) * scale
        end_point = origin + Vector(end) * scale
        tapered_branch(target, name + f"_Branch_{index + 1:02d}", start_point, end_point, 0.15 * scale, 0.045 * scale, materials["bark"], 16)
        branch_direction = end_point - start_point
        side_vector = Vector((-branch_direction.y, branch_direction.x, 0.0)).normalized()
        twig_start = start_point + branch_direction * 0.64
        twig_end = end_point + side_vector * ((0.30 if index % 2 == 0 else -0.30) * scale) + Vector((0.0, 0.0, 0.34 * scale))
        tapered_branch(target, name + f"_Twig_{index + 1:02d}", twig_start, twig_end, 0.07 * scale, 0.018 * scale, materials["bark_light"], 12)

    crown_specs = (
        (-1.00, 0.22, 4.35, 0.94, 0.82, 1.03, 0.90),
        (0.96, 0.34, 4.48, 0.98, 0.84, 1.05, 0.92),
        (-0.34, -0.98, 4.58, 0.91, 0.82, 1.08, 0.88),
        (0.36, 0.96, 4.72, 0.92, 0.84, 1.06, 0.90),
        (-0.78, -0.62, 4.96, 0.96, 0.86, 1.12, 0.92),
        (0.82, -0.50, 5.06, 0.94, 0.84, 1.10, 0.90),
        (-0.68, 0.70, 5.22, 0.90, 0.82, 1.08, 0.86),
        (0.64, 0.72, 5.34, 0.92, 0.84, 1.10, 0.88),
        (-0.38, -0.40, 5.48, 0.98, 0.90, 1.14, 0.94),
        (0.42, -0.30, 5.62, 0.96, 0.88, 1.12, 0.92),
        (-0.28, 0.30, 5.76, 0.93, 0.86, 1.13, 0.89),
        (0.30, 0.26, 5.88, 0.92, 0.85, 1.12, 0.88),
        (-0.18, -0.04, 6.04, 0.88, 0.82, 1.10, 0.84),
        (0.18, 0.04, 6.16, 0.84, 0.80, 1.08, 0.82),
        (0.00, 0.00, 6.30, 0.78, 0.76, 1.02, 0.78),
    )
    leaf_materials = (
        materials["leaves"],
        materials["leaves_light"],
        materials["leaves_dark"],
        materials["leaves_sunlit"],
    )
    for index, (offset_x, offset_y, offset_z, scale_x, scale_y, scale_z, radius) in enumerate(crown_specs):
        crown = sphere(
            target,
            name + f"_Crown_{index + 1:02d}",
            (x + offset_x * scale, y + offset_y * scale, offset_z * scale),
            radius * scale,
            leaf_materials[index % len(leaf_materials)],
            4,
        )
        crown.scale = (scale_x, scale_y, scale_z)


def create_grass_tuft_mesh(name, materials, height_scale=1.0):
    """Build one reusable high-poly tuft from curved segmented grass ribbons."""
    vertices = []
    faces = []
    blade_count = 16
    segment_count = 6
    for blade_index in range(blade_count):
        angle = blade_index * 2.399963229728653
        radius = 0.035 + 0.018 * (blade_index % 4)
        height = (0.30 + 0.035 * (blade_index % 5)) * height_scale
        width = 0.055 + 0.006 * (blade_index % 3)
        bend = 0.055 + 0.012 * (blade_index % 4)
        direction = Vector((math.cos(angle), math.sin(angle), 0.0))
        side = Vector((-direction.y, direction.x, 0.0))
        center = direction * radius
        base = len(vertices)
        for segment in range(segment_count + 1):
            t = segment / segment_count
            curved_center = center + direction * (bend * t * t) + Vector((0.0, 0.0, height * t))
            half_width = width * (1.0 - 0.88 * t)
            vertices.extend((tuple(curved_center - side * half_width), tuple(curved_center + side * half_width)))
        for segment in range(segment_count):
            index = base + segment * 2
            faces.append((index, index + 1, index + 3, index + 2))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(materials["grass_blade"])
    mesh.materials.append(materials["grass_blade_light"])
    mesh.materials.append(materials["grass_blade_dark"])
    for index, polygon in enumerate(mesh.polygons):
        polygon.material_index = (index // segment_count) % 3
    return mesh


def scatter_grass(target, materials):
    """Scatter linked high-poly grass tufts outside the playable road."""
    rng = random.Random(240817)
    tuft_meshes = (
        create_grass_tuft_mesh("FS_GrassTuft_01_Mesh", materials, 0.78),
        create_grass_tuft_mesh("FS_GrassTuft_02_Mesh", materials, 0.90),
        create_grass_tuft_mesh("FS_GrassTuft_03_Mesh", materials, 1.00),
        create_grass_tuft_mesh("FS_GrassTuft_04_Mesh", materials, 1.12),
        create_grass_tuft_mesh("FS_GrassTuft_05_Mesh", materials, 1.24),
    )
    tuft_index = 0
    for side in (-1, 1):
        for row, base_x in enumerate((6.42, 6.92, 7.42)):
            y = 0.65 + row * 0.47
            while y < 96.0:
                obj = bpy.data.objects.new(f"FS_GrassTuft_{tuft_index + 1:03d}", tuft_meshes[tuft_index % len(tuft_meshes)])
                target.objects.link(obj)
                obj.location = (
                    side * (base_x + rng.uniform(-0.22, 0.22)),
                    y + rng.uniform(-0.30, 0.30),
                    0.03,
                )
                uniform_scale = rng.uniform(0.82, 1.22)
                obj.scale = (uniform_scale, uniform_scale, uniform_scale)
                obj.rotation_euler.z = rng.uniform(0.0, math.tau)
                tuft_index += 1
                y += 1.48 + rng.uniform(-0.14, 0.14)
    return tuft_index


def create_fence(target, name, side, y_center, length, materials):
    x = side * 6.85
    box(target, name + "_RailLow", (x, y_center, 0.65), (0.10, length, 0.12), materials["fence"], 0.02)
    box(target, name + "_RailHigh", (x, y_center, 1.15), (0.10, length, 0.12), materials["fence"], 0.02)
    count = int(length / 0.65)
    for index in range(count + 1):
        y = y_center - length * 0.5 + index * (length / max(1, count))
        box(target, name + f"_Picket_{index}", (x, y, 0.78), (0.12, 0.12, 1.45), materials["fence"], 0.025)


def create_street_lamp(target, name, side, y, materials, add_light=False):
    x = side * 5.65
    cylinder(target, name + "_Pole", (x, y, 2.35), 0.10, 4.7, materials["lamp_metal"], 12, bevel_width=0.025)
    road_direction = -side
    box(target, name + "_Arm", (x + road_direction * 0.42, y, 4.62), (0.85, 0.12, 0.12), materials["lamp_metal"], 0.035)
    lamp_x = x + road_direction * 0.82
    box(target, name + "_Fixture", (lamp_x, y, 4.48), (0.48, 0.32, 0.20), materials["lamp_metal"], 0.06)
    box(target, name + "_Glow", (lamp_x, y, 4.35), (0.37, 0.24, 0.05), materials["lamp_glow"], 0.02)
    if add_light:
        data = bpy.data.lights.new(name + "_LightData", "AREA")
        data.energy = 90.0
        data.color = (1.0, 0.72, 0.38)
        data.shape = "DISK"
        data.size = 2.2
        light = bpy.data.objects.new(name + "_Light", data)
        target.objects.link(light)
        light.location = (lamp_x, y, 4.2)
        light.rotation_euler = (0.0, 0.0, 0.0)


def create_car(target, name, materials, paint, location=(0.0, 0.0, 0.0)):
    root = bpy.data.objects.new(name + "_MotionRoot", None)
    target.objects.link(root)
    root.empty_display_type = "CUBE"
    root.empty_display_size = 0.8
    body = box(target, name + "_Body", (0.0, 0.0, 0.72), (2.05, 4.15, 0.78), paint, 0.16)
    hood = box(target, name + "_Hood", (0.0, 1.42, 1.12), (1.92, 1.15, 0.36), paint, 0.14)
    cabin = box(target, name + "_Cabin", (0.0, -0.25, 1.38), (1.72, 2.05, 0.88), materials["car_glass"], 0.18)
    roof = box(target, name + "_Roof", (0.0, -0.25, 1.83), (1.55, 1.65, 0.18), paint, 0.08)
    bumper_front = box(target, name + "_FrontBumper", (0.0, 2.06, 0.57), (1.85, 0.14, 0.28), materials["car_dark"], 0.04)
    bumper_back = box(target, name + "_BackBumper", (0.0, -2.06, 0.57), (1.85, 0.14, 0.28), materials["car_dark"], 0.04)
    parts = [body, hood, cabin, roof, bumper_front, bumper_back]
    for side in (-1, 1):
        for y in (-1.35, 1.35):
            wheel = cylinder(target, name + f"_Wheel_{side}_{y}", (side * 1.0, y, 0.55), 0.37, 0.26, materials["tire"], 16, rotation=(0.0, math.pi / 2.0, 0.0), bevel_width=0.04)
            hub = cylinder(target, name + f"_Hub_{side}_{y}", (side * 1.14, y, 0.55), 0.18, 0.05, materials["hub"], 12, rotation=(0.0, math.pi / 2.0, 0.0), bevel_width=0.02)
            parts.extend((wheel, hub))
    for side in (-1, 1):
        headlight = box(target, name + f"_Headlight_{side}", (side * 0.62, 2.12, 0.92), (0.42, 0.06, 0.23), materials["headlight"], 0.035)
        taillight = box(target, name + f"_Taillight_{side}", (side * 0.62, -2.12, 0.88), (0.42, 0.06, 0.22), materials["hazard_red"], 0.035)
        parts.extend((headlight, taillight))
    for part in parts:
        part.parent = root
    root.location = location
    return root


def create_rubbish_bin(target, name, materials):
    root = bpy.data.objects.new(name + "_DropRoot", None)
    target.objects.link(root)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 1.0
    body = tapered_bin_body(target, name + "_Body", materials["bin_green"])
    lid = box(target, name + "_Lid", (0.0, -0.03, 1.10), (0.84, 0.88, 0.13), materials["bin_dark"], 0.07)
    handle = box(target, name + "_Handle", (0.0, -0.46, 1.10), (0.54, 0.08, 0.10), materials["bin_dark"], 0.03)
    wheel_left = cylinder(target, name + "_WheelLeft", (-0.34, -0.31, 0.18), 0.14, 0.10, materials["tire"], 12, rotation=(0.0, math.pi / 2.0, 0.0), bevel_width=0.02)
    wheel_right = cylinder(target, name + "_WheelRight", (0.34, -0.31, 0.18), 0.14, 0.10, materials["tire"], 12, rotation=(0.0, math.pi / 2.0, 0.0), bevel_width=0.02)
    for part in (body, lid, handle, wheel_left, wheel_right):
        part.parent = root
    return root


def create_cone(target, name, location, materials, scale=1.0):
    box(target, name + "_Base", (location[0], location[1], 0.08), (0.58 * scale, 0.58 * scale, 0.14 * scale), materials["hazard_dark"], 0.04)
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.24 * scale, radius2=0.07 * scale, depth=0.72 * scale, location=(location[0], location[1], 0.46 * scale))
    cone = bpy.context.object
    cone.name = name + "_Orange"
    add_material(cone, materials["hazard_orange"])
    move_to_collection(cone, target)
    bevel(cone, 0.025, 2)
    cylinder(target, name + "_Stripe", (location[0], location[1], 0.50 * scale), 0.245 * scale, 0.12 * scale, materials["marking_white"], 16, bevel_width=0.015)


def create_mailbox(target, name, side, y, materials):
    x = side * 6.25
    cylinder(target, name + "_Post", (x, y, 0.65), 0.08, 1.3, materials["fence"], 10, bevel_width=0.02)
    box(target, name + "_Box", (x, y, 1.35), (0.55, 0.78, 0.48), materials["mailbox_blue"], 0.10)
    box(target, name + "_Flag", (x - side * 0.32, y, 1.55), (0.05, 0.42, 0.08), materials["hazard_red"], 0.02, rotation=(math.radians(25), 0.0, 0.0))


def build_scene():
    reset_scene()
    scene = bpy.context.scene
    scene.name = "Street Theme Review v4"
    scene["review_status"] = "Blender-only visual review; Unity integration not performed"
    scene["theme"] = "Street Theme"
    scene["vegetation_detail"] = "Cartoon-realistic high-poly tapered trees and curved linked-mesh grass"
    scene["review_version"] = "v4"
    scene["authoring_forward"] = "Blender +Y"
    scene["module_length_metres"] = 24.0
    scene["route_width_metres"] = 9.0
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.frame_start = 1
    scene.frame_end = 120
    scene.frame_set(55)

    road_collection = collection("01_ROAD_PLATFORM")
    house_collection = collection("02_RESIDENTIAL_HOUSES")
    row_collection = collection("03_CONNECTED_STREET_BUILDINGS")
    lamp_collection = collection("04_ROAD_LAMPS_AND_UTILITIES")
    obstacle_collection = collection("05_OBSTACLE_MOCKUPS")
    dressing_collection = collection("06_STREET_DRESSING")
    vegetation_collection = collection("06A_HIGHPOLY_VEGETATION")
    camera_collection = collection("07_REVIEW_CAMERAS")
    guide_collection = collection("08_MODULE_GUIDES_NO_RENDER")

    materials = {
        "asphalt": create_noise_material("MAT_FS_Asphalt", (0.035, 0.045, 0.055), (0.12, 0.14, 0.16), 28.0, 0.82, 0.24),
        "concrete": create_noise_material("MAT_FS_Concrete", (0.43, 0.45, 0.47), (0.66, 0.67, 0.66), 8.0, 0.74, 0.12),
        "grass": create_noise_material("MAT_FS_Grass", (0.055, 0.18, 0.045), (0.22, 0.48, 0.10), 5.0, 0.88, 0.16),
        "marking_white": create_material("MAT_FS_RoadWhite", (0.92, 0.92, 0.86), roughness=0.58),
        "route_yellow": create_material("MAT_FS_RouteYellow", (1.0, 0.62, 0.035), roughness=0.45),
        "foundation": create_material("MAT_FS_Foundation", (0.16, 0.18, 0.20), roughness=0.74),
        "plaster_cream": create_noise_material("MAT_FS_PlasterCream", (0.52, 0.43, 0.30), (0.91, 0.76, 0.53), 4.0, 0.72, 0.08),
        "plaster_blue": create_noise_material("MAT_FS_PlasterBlue", (0.15, 0.32, 0.42), (0.36, 0.63, 0.72), 4.0, 0.69, 0.08),
        "plaster_coral": create_noise_material("MAT_FS_PlasterCoral", (0.48, 0.19, 0.14), (0.83, 0.38, 0.24), 4.0, 0.72, 0.08),
        "plaster_yellow": create_noise_material("MAT_FS_PlasterYellow", (0.55, 0.39, 0.12), (0.92, 0.68, 0.25), 4.0, 0.72, 0.08),
        "roof_red": create_noise_material("MAT_FS_RoofRed", (0.22, 0.045, 0.025), (0.58, 0.13, 0.06), 12.0, 0.78, 0.18),
        "roof_blue": create_noise_material("MAT_FS_RoofBlue", (0.045, 0.09, 0.12), (0.12, 0.25, 0.32), 12.0, 0.74, 0.16),
        "brick": create_noise_material("MAT_FS_Brick", (0.20, 0.055, 0.035), (0.49, 0.13, 0.07), 16.0, 0.82, 0.14),
        "trim": create_material("MAT_FS_Trim", (0.88, 0.84, 0.73), roughness=0.61),
        "door": create_material("MAT_FS_Door", (0.08, 0.16, 0.22), roughness=0.46),
        "window_frame": create_material("MAT_FS_WindowFrame", (0.92, 0.88, 0.78), roughness=0.45),
        "window": create_material("MAT_FS_WindowGlass", (0.08, 0.30, 0.42), metallic=0.18, roughness=0.16),
        "fence": create_material("MAT_FS_Fence", (0.88, 0.84, 0.69), roughness=0.70),
        "bark": create_material("MAT_FS_Bark", (0.16, 0.07, 0.025), roughness=0.91),
        "bark_light": create_material("MAT_FS_BarkLight", (0.27, 0.12, 0.045), roughness=0.89),
        "bark_dark": create_material("MAT_FS_BarkDark", (0.085, 0.030, 0.012), roughness=0.94),
        "leaves": create_material("MAT_FS_Leaves", (0.08, 0.38, 0.09), roughness=0.86),
        "leaves_light": create_material("MAT_FS_LeavesLight", (0.16, 0.53, 0.12), roughness=0.84),
        "leaves_dark": create_material("MAT_FS_LeavesDark", (0.035, 0.24, 0.055), roughness=0.90),
        "leaves_sunlit": create_material("MAT_FS_LeavesSunlit", (0.30, 0.64, 0.14), roughness=0.82),
        "grass_blade": create_material("MAT_FS_GrassBlade", (0.075, 0.30, 0.045), roughness=0.91),
        "grass_blade_light": create_material("MAT_FS_GrassBladeLight", (0.20, 0.48, 0.075), roughness=0.88),
        "grass_blade_dark": create_material("MAT_FS_GrassBladeDark", (0.035, 0.19, 0.025), roughness=0.94),
        "lamp_metal": create_material("MAT_FS_LampMetal", (0.055, 0.075, 0.09), metallic=0.72, roughness=0.36),
        "lamp_glow": create_material("MAT_FS_LampGlow", (1.0, 0.68, 0.26), roughness=0.18, emission=(1.0, 0.42, 0.08), emission_strength=5.0),
        "cable": create_material("MAT_FS_UtilityCable", (0.015, 0.018, 0.022), metallic=0.35, roughness=0.55),
        "awning_red": create_material("MAT_FS_AwningRed", (0.66, 0.09, 0.045), roughness=0.62),
        "awning_teal": create_material("MAT_FS_AwningTeal", (0.03, 0.38, 0.42), roughness=0.55),
        "car_blue": create_material("MAT_FS_CarBlue", (0.025, 0.26, 0.58), metallic=0.52, roughness=0.22),
        "car_red": create_material("MAT_FS_CarRed", (0.66, 0.035, 0.02), metallic=0.46, roughness=0.24),
        "car_glass": create_material("MAT_FS_CarGlass", (0.025, 0.09, 0.14), metallic=0.32, roughness=0.12),
        "car_dark": create_material("MAT_FS_CarDark", (0.018, 0.021, 0.025), metallic=0.34, roughness=0.48),
        "tire": create_material("MAT_FS_Tire", (0.012, 0.013, 0.014), roughness=0.86),
        "hub": create_material("MAT_FS_Hub", (0.36, 0.40, 0.44), metallic=0.82, roughness=0.28),
        "headlight": create_material("MAT_FS_Headlight", (0.92, 0.88, 0.66), roughness=0.16, emission=(1.0, 0.78, 0.30), emission_strength=2.0),
        "hazard_red": create_material("MAT_FS_HazardRed", (0.95, 0.035, 0.018), roughness=0.42),
        "hazard_orange": create_material("MAT_FS_HazardOrange", (1.0, 0.25, 0.015), roughness=0.48),
        "hazard_dark": create_material("MAT_FS_HazardDark", (0.10, 0.12, 0.13), roughness=0.64),
        "bin_green": create_material("MAT_FS_BinGreen", (0.025, 0.30, 0.18), roughness=0.68),
        "bin_dark": create_material("MAT_FS_BinDark", (0.025, 0.08, 0.065), roughness=0.72),
        "mailbox_blue": create_material("MAT_FS_MailboxBlue", (0.04, 0.26, 0.46), metallic=0.32, roughness=0.44),
        "player": create_material("MAT_FS_PlayerGuide", (0.015, 0.62, 0.78), metallic=0.35, roughness=0.20, emission=(0.0, 0.20, 0.35), emission_strength=1.2),
    }

    # A non-playable ground/backdrop prevents the modular review strip from
    # appearing to float in an empty world. The four playable modules below
    # remain separate and measurable.
    box(road_collection, "FS_NeighbourhoodGround_Backdrop", (0.0, 88.0, -0.50), (90.0, 230.0, 0.24), materials["grass"], 0.0)
    box(road_collection, "FS_DistantRoad_Backdrop", (0.0, 150.0, -0.25), (9.2, 108.0, 0.46), materials["asphalt"], 0.025)
    for side in (-1, 1):
        box(road_collection, f"FS_DistantCurb_{side}", (side * 4.72, 150.0, 0.08), (0.26, 108.0, 0.36), materials["concrete"], 0.025)
        box(road_collection, f"FS_DistantSidewalk_{side}", (side * 5.55, 150.0, 0.14), (1.42, 108.0, 0.28), materials["concrete"], 0.035)

    # Four independent 24 m modules make the modular rhythm reviewable.
    for module_index in range(4):
        y = module_index * 24.0 + 12.0
        module = module_index + 1
        box(road_collection, f"FS_Road_Module_{module:02d}", (0.0, y, -0.24), (9.2, 24.0, 0.48), materials["asphalt"], 0.035)
        for side in (-1, 1):
            box(road_collection, f"FS_Curb_{module:02d}_{side}", (side * 4.72, y, 0.09), (0.26, 24.0, 0.38), materials["concrete"], 0.035)
            box(road_collection, f"FS_Sidewalk_{module:02d}_{side}", (side * 5.55, y, 0.15), (1.42, 24.0, 0.30), materials["concrete"], 0.045)
            box(road_collection, f"FS_GrassVerge_{module:02d}_{side}", (side * 9.25, y, -0.08), (6.0, 24.0, 0.22), materials["grass"], 0.05)
            box(road_collection, f"FS_YellowEdge_{module:02d}_{side}", (side * 4.37, y, 0.025), (0.10, 23.5, 0.025), materials["route_yellow"], 0.005)
        for dash_y in (module_index * 24.0 + 3.0, module_index * 24.0 + 9.0, module_index * 24.0 + 15.0, module_index * 24.0 + 21.0):
            for lane_x in (-1.52, 1.52):
                box(road_collection, f"FS_LaneDash_{lane_x}_{dash_y}", (lane_x, dash_y, 0.026), (0.09, 2.9, 0.026), materials["marking_white"], 0.004)
        guide = bpy.data.objects.new(f"GUIDE_24M_SEAM_{module_index * 24:03d}", None)
        guide.empty_display_type = "PLAIN_AXES"
        guide.empty_display_size = 2.0
        guide.location = (0.0, module_index * 24.0, 0.0)
        guide.hide_render = True
        guide_collection.objects.link(guide)

    # Crosswalk makes the moving-car hazard understandable from the gameplay camera.
    for stripe in range(9):
        box(road_collection, f"FS_Crosswalk_{stripe}", (-3.6 + stripe * 0.9, 46.0, 0.03), (0.52, 2.7, 0.03), materials["marking_white"], 0.005)

    house_colors = [materials["plaster_cream"], materials["plaster_blue"], materials["plaster_yellow"], materials["plaster_coral"]]
    for index, (side, y) in enumerate(((-1, 10.0), (1, 16.0), (-1, 30.0), (1, 34.0), (-1, 49.0))):
        create_house(house_collection, f"FS_House_{index + 1:02d}", side, y, house_colors, materials, index)
        create_fence(dressing_collection, f"FS_Fence_{index + 1:02d}", side, y, 7.4, materials)
        create_mailbox(dressing_collection, f"FS_Mailbox_{index + 1:02d}", side, y - 2.5, materials)

    create_row_buildings(row_collection, -1, 62.0, 6, materials)
    create_row_buildings(row_collection, 1, 59.0, 6, materials)

    grass_tuft_count = scatter_grass(vegetation_collection, materials)
    scene["midpoly_grass_tuft_count"] = grass_tuft_count

    for index, y in enumerate((5.5, 18.0, 31.0, 43.0, 57.0, 70.0, 83.0, 94.0)):
        for side in (-1, 1):
            create_street_lamp(lamp_collection, f"FS_Lamp_{index + 1:02d}_{side}", side, y, materials, add_light=(index in (1, 4, 7)))

    # Utility poles and slightly sagging cables echo the suburban reference without copying it.
    for side in (-1, 1):
        x = side * 7.3
        pole_points = []
        for index, y in enumerate((0.0, 24.0, 48.0, 72.0, 96.0)):
            cylinder(lamp_collection, f"FS_UtilityPole_{side}_{index}", (x, y, 3.5), 0.14, 7.0, materials["bark"], 10, bevel_width=0.025)
            box(lamp_collection, f"FS_CrossArm_{side}_{index}", (x, y, 6.55), (1.6, 0.14, 0.15), materials["bark"], 0.035)
            pole_points.append((x, y, 6.85))
        for cable_index, x_offset in enumerate((-0.45, 0.0, 0.45)):
            points = []
            for span in range(4):
                y0 = span * 24.0
                points.extend(((x + x_offset, y0, 6.75), (x + x_offset, y0 + 12.0, 6.15), (x + x_offset, y0 + 24.0, 6.75)))
            curve_tube(lamp_collection, f"FS_Cable_{side}_{cable_index}", points, 0.025, materials["cable"])

    for index, (x, y, scale) in enumerate(((-7.9, 4.0, 1.0), (7.8, 8.0, 1.15), (-7.8, 22.0, 0.9), (7.8, 29.0, 1.05), (-7.9, 43.0, 1.1), (7.8, 52.0, 0.95))):
        create_tree(vegetation_collection, f"FS_Tree_{index + 1:02d}", x, y, scale, materials)

    player = sphere(dressing_collection, "FS_PlayerScaleGuide", (0.0, 5.0, 0.62), 0.62, materials["player"], 3)
    player["purpose"] = "Scale and gameplay-camera review only"

    # Moving-car mock-up: crosses the road near the marked intersection.
    crossing_car = create_car(obstacle_collection, "FS_Obstacle_CrossingCar", materials, materials["car_red"])
    crossing_car.rotation_euler.z = -math.pi / 2.0
    for frame, x in ((1, -8.0), (34, -8.0), (55, 0.0), (78, 8.0), (120, 8.0)):
        crossing_car.location = (x, 46.0, 0.0)
        crossing_car.keyframe_insert(data_path="location", frame=frame)

    # A second vehicle moves forward in a lane, demonstrating another readable traffic pattern.
    forward_car = create_car(obstacle_collection, "FS_Obstacle_ForwardCar", materials, materials["car_blue"])
    for frame, y in ((1, 57.0), (45, 64.0), (90, 86.0), (120, 94.0)):
        forward_car.location = (-2.6, y, 0.0)
        forward_car.keyframe_insert(data_path="location", frame=frame)

    # Sudden rubbish-bin obstacle. Its final lying height is below 0.8 m for a jump-over review.
    rubbish_bin = create_rubbish_bin(obstacle_collection, "FS_Obstacle_DroppedRubbishBin", materials)
    rubbish_bin.rotation_mode = "XYZ"
    for frame, location, rotation in (
        (1, (5.35, 31.5, 0.0), (0.0, 0.0, 0.0)),
        (32, (5.35, 31.5, 0.0), (0.0, 0.0, 0.0)),
        (45, (3.75, 32.3, 0.15), (math.radians(38), math.radians(10), math.radians(-18))),
        (55, (2.05, 33.5, 0.34), (math.radians(92), math.radians(4), math.radians(-28))),
        (120, (2.05, 33.5, 0.34), (math.radians(92), math.radians(4), math.radians(-28))),
    ):
        rubbish_bin.location = location
        rubbish_bin.rotation_euler = rotation
        rubbish_bin.keyframe_insert(data_path="location", frame=frame)
        rubbish_bin.keyframe_insert(data_path="rotation_euler", frame=frame)
    rubbish_bin["review_jumpable_height_metres"] = 0.75
    rubbish_bin["gameplay_note"] = "Visual mock-up only; jump validation remains a later Unity task"

    # Additional simple obstacle family for review.
    for index, location in enumerate(((-2.8, 73.0, 0.0), (-1.9, 74.1, 0.0), (-3.5, 74.4, 0.0))):
        create_cone(obstacle_collection, f"FS_Obstacle_Cone_{index + 1}", location, materials, 1.0)
    box(obstacle_collection, "FS_Obstacle_RoadworkBarrier", (-2.7, 76.0, 0.62), (3.0, 0.32, 0.72), materials["hazard_orange"], 0.08)
    for x in (-3.8, -1.6):
        box(obstacle_collection, f"FS_Obstacle_BarrierLeg_{x}", (x, 76.0, 0.32), (0.18, 0.65, 0.65), materials["hazard_dark"], 0.04)

    # Timeline markers make the proposed visual behaviour easy to inspect.
    scene.timeline_markers.new("BIN_DROP_START", frame=32)
    scene.timeline_markers.new("CAR_CROSSING_START", frame=34)
    scene.timeline_markers.new("REVIEW_POSE", frame=55)
    scene.timeline_markers.new("CAR_CROSSING_END", frame=78)

    # Daylight scene with grounded shadows and realistic-cartoon contrast.
    world = bpy.data.worlds.new("Street Theme Daylight")
    world.use_nodes = True
    scene.world = world
    nodes = world.node_tree.nodes
    background = nodes.get("Background")
    background.inputs["Color"].default_value = (0.16, 0.46, 0.88, 1.0)
    background.inputs["Strength"].default_value = 0.88

    sun_data = bpy.data.lights.new("Street Sun Data", "SUN")
    sun_data.energy = 2.6
    sun_data.angle = math.radians(18.0)
    sun = bpy.data.objects.new("Street Sun", sun_data)
    camera_collection.objects.link(sun)
    sun.rotation_euler = (math.radians(37.0), math.radians(-18.0), math.radians(132.0))

    fill_data = bpy.data.lights.new("Soft Fill Data", "AREA")
    fill_data.energy = 1200.0
    fill_data.shape = "DISK"
    fill_data.size = 18.0
    fill = bpy.data.objects.new("Soft Fill", fill_data)
    camera_collection.objects.link(fill)
    fill.location = (-6.0, 18.0, 18.0)
    look_at(fill, (0.0, 38.0, 0.0))

    rim_data = bpy.data.lights.new("Neighbourhood Fill Data", "AREA")
    rim_data.energy = 950.0
    rim_data.shape = "DISK"
    rim_data.size = 24.0
    rim = bpy.data.objects.new("Neighbourhood Fill", rim_data)
    camera_collection.objects.link(rim)
    rim.location = (14.0, 66.0, 19.0)
    look_at(rim, (0.0, 48.0, 1.0))

    camera_specs = {
        "CAM_FS_GameplayReview": ((0.0, -11.5, 6.4), (0.0, 40.0, 1.25), 31.0),
        "CAM_FS_StreetOverview": ((19.0, -8.0, 21.0), (0.0, 44.0, 0.9), 40.0),
        "CAM_FS_ObstacleReview": ((3.8, 19.0, 4.6), (0.3, 41.0, 0.8), 46.0),
        "CAM_FS_ArchitectureReview": ((-0.5, 43.0, 8.2), (8.0, 69.0, 3.2), 45.0),
    }
    cameras = {}
    for name, (location, target, lens) in camera_specs.items():
        data = bpy.data.cameras.new(name + "_Data")
        data.lens = lens
        data.sensor_width = 36.0
        data.dof.use_dof = False
        camera = bpy.data.objects.new(name, data)
        camera_collection.objects.link(camera)
        camera.location = location
        look_at(camera, target)
        cameras[name] = camera

    scene.camera = cameras["CAM_FS_GameplayReview"]
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        pass

    # Module guides remain available in the viewport without entering renders.
    guide_collection.hide_render = True
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))

    render_jobs = (
        ("CAM_FS_GameplayReview", "FS_Street_v4_GameplayReview.png"),
        ("CAM_FS_StreetOverview", "FS_Street_v4_Overview.png"),
        ("CAM_FS_ObstacleReview", "FS_Street_v4_ObstacleReview.png"),
        ("CAM_FS_ArchitectureReview", "FS_Street_v4_ArchitectureReview.png"),
    )
    for camera_name, filename in render_jobs:
        scene.camera = cameras[camera_name]
        scene.render.filepath = str(RENDER_DIR / filename)
        bpy.ops.render.render(write_still=True)

    scene.camera = cameras["CAM_FS_GameplayReview"]
    scene.render.filepath = str(RENDER_DIR / "FS_Street_v4_GameplayReview.png")
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))
    print(f"Saved Blender review: {OUTPUT_BLEND}")
    print(f"Saved review renders: {RENDER_DIR}")
    print("Unity integration: not performed")


if __name__ == "__main__":
    build_scene()
