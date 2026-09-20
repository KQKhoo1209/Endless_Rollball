"""Generate the Endless Rollball low-poly environment source files and FBX exports.

Run with:
    D:\\blender.exe --background --factory-startup --python build_environment_kits.py

Authoring uses metres, Blender +Y forward and +Z up. FBX export converts the
assets to Unity +Z forward. All geometry is visual-only; collision remains in
the Unity track prefabs.
"""

from pathlib import Path
import math

import bpy
from mathutils import Matrix


PROJECT_ROOT = Path(__file__).resolve().parents[2]
BLENDER_WORKSPACE = Path(r"D:\Creative\Blender\Endless Rollball")
SOURCE_ROOT = BLENDER_WORKSPACE / "Environment" / "PreviousVisualKits"
UNITY_ROOT = PROJECT_ROOT / "Assets" / "EndlessRollball" / "Art" / "Environment"


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for material in list(bpy.data.materials):
        bpy.data.materials.remove(material)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0


def material(name, color, metallic=0.0, roughness=0.65, emission=None):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission:
            emission_input = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
            strength_input = bsdf.inputs.get("Emission Strength")
            if emission_input:
                emission_input.default_value = (*emission, 1.0)
            if strength_input:
                strength_input.default_value = 3.0
    return mat


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def move_to(obj, target):
    for owner in list(obj.users_collection):
        owner.objects.unlink(obj)
    target.objects.link(obj)
    return obj


def assign(obj, mat):
    if obj.data and hasattr(obj.data, "materials"):
        obj.data.materials.append(mat)
    return obj


def box(target, name, location, dimensions, mat, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to(obj, target)
    return assign(obj, mat)


def cylinder(target, name, location, radius, depth, mat, vertices=10, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    move_to(obj, target)
    return assign(obj, mat)


def cone(target, name, location, radius1, radius2, depth, mat, vertices=10):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius1,
        radius2=radius2,
        depth=depth,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    move_to(obj, target)
    return assign(obj, mat)


def sphere(target, name, location, radius, mat, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    move_to(obj, target)
    return assign(obj, mat)


def torus(target, name, location, major_radius, minor_radius, mat, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=12,
        minor_segments=6,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    move_to(obj, target)
    return assign(obj, mat)


def tube(target, name, points, radius, mat):
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 1
    curve.bevel_depth = radius
    curve.bevel_resolution = 0
    curve.resolution_u = 1
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, coordinate in zip(spline.points, points):
        point.co = (*coordinate, 1.0)
    obj = bpy.data.objects.new(name, curve)
    target.objects.link(obj)
    assign(obj, mat)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj.select_set(False)
    return obj


def arch(target, prefix, y, half_width, height, depth, column_width, body_mat, light_mat=None):
    box(target, f"{prefix}__ArchLeft", (-half_width, y, height * 0.5), (column_width, depth, height), body_mat)
    box(target, f"{prefix}__ArchRight", (half_width, y, height * 0.5), (column_width, depth, height), body_mat)
    box(target, f"{prefix}__ArchTop", (0.0, y, height + 0.3), (half_width * 2 + column_width, depth, 0.6), body_mat)
    if light_mat:
        box(target, f"TR_Emissive__ArchLight", (0.0, y - depth * 0.51, height - 0.15), (half_width * 1.6, 0.06, 0.12), light_mat)


def light_pole(target, prefix, x, y, body_mat, light_mat, height=4.2):
    cylinder(target, f"{prefix}__Pole", (x, y, height * 0.5), 0.10, height, body_mat, vertices=8)
    box(target, f"FS_Dark__LampArm", (x - math.copysign(0.35, x), y, height), (0.8, 0.12, 0.12), body_mat)
    box(target, f"{light_mat.name}__Lamp", (x - math.copysign(0.7, x), y, height - 0.05), (0.35, 0.18, 0.12), light_mat)


def export_collection(target, destination):
    bpy.ops.object.select_all(action="DESELECT")
    objects = [obj for obj in target.all_objects if obj.type in {"MESH", "CURVE"}]
    original_matrices = {obj: obj.matrix_world.copy() for obj in objects}
    unity_forward = Matrix.Rotation(math.pi, 4, "Z")
    try:
        # Blender's FBX convention maps authored +Y to Unity -Z. Rotate the
        # export selection as a group so the delivered FBX faces Unity +Z,
        # while the editable .blend remains authored in the documented +Y.
        for obj in objects:
            obj.matrix_world = unity_forward @ obj.matrix_world
            obj.select_set(True)
        if objects:
            bpy.context.view_layer.objects.active = objects[0]
        destination.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.export_scene.fbx(
            filepath=str(destination),
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
            bake_anim=False,
            path_mode="AUTO",
        )
    finally:
        for obj, matrix in original_matrices.items():
            obj.matrix_world = matrix
        bpy.ops.object.select_all(action="DESELECT")


def save_and_export(source_name, theme_folder, assets):
    SOURCE_ROOT.mkdir(parents=True, exist_ok=True)
    source_path = SOURCE_ROOT / source_name
    bpy.ops.wm.save_as_mainfile(filepath=str(source_path))
    output = UNITY_ROOT / theme_folder
    for target in assets:
        export_collection(target, output / f"{target.name}.fbx")
    print(f"Generated {source_path.name}: {len(assets)} FBX assets")


def futuristic_street():
    reset_scene()
    dark = material("FS_Dark", (0.025, 0.055, 0.10), metallic=0.65, roughness=0.35)
    mid = material("FS_Mid", (0.07, 0.19, 0.27), metallic=0.35, roughness=0.48)
    teal = material("FS_Teal", (0.02, 0.48, 0.53), metallic=0.25, roughness=0.32)
    accent = material("FS_Accent", (0.96, 0.70, 0.12), metallic=0.15, roughness=0.4)
    glow = material("FS_Emissive", (0.02, 0.75, 0.92), metallic=0.1, roughness=0.2, emission=(0.02, 0.75, 0.92))
    green = material("FS_Green", (0.08, 0.30, 0.20), roughness=0.85)

    assets = []
    for variant in "ABC":
        target = collection(f"ENV_FS_Roadside_{variant}")
        assets.append(target)
        heights = {"A": (7.5, 11.0), "B": (12.0, 8.0), "C": (9.0, 14.0)}[variant]
        y_positions = {"A": (5.0, 17.0), "B": (8.0, 19.0), "C": (4.0, 15.0)}[variant]
        for side, x in (("L", -10.0), ("R", 10.0)):
            index = 0 if side == "L" else 1
            height = heights[index]
            y = y_positions[index]
            box(target, f"FS_Dark__{variant}_{side}_Building", (x, y, height * 0.5 - 1.0), (4.2, 5.0, height), dark)
            box(target, f"FS_Mid__{variant}_{side}_Facade", (x - math.copysign(2.12, x), y, height * 0.55), (0.10, 4.1, height * 0.55), mid)
            for level in range(2, int(height), 2):
                box(target, f"FS_Emissive__{variant}_{side}_Window_{level}", (x - math.copysign(2.19, x), y, level), (0.05, 2.8, 0.14), glow)
        for y in (2.5, 9.0, 15.5, 22.0):
            light_pole(target, "FS_Dark", -6.4, y, dark, glow)
            light_pole(target, "FS_Dark", 6.4, y, dark, glow)
        box(target, f"FS_Teal__{variant}_SignFrame", (-7.0, 12.0, 3.2), (0.25, 3.2, 2.6), teal)
        box(target, f"FS_Emissive__{variant}_SignPanel", (-6.83, 12.0, 3.2), (0.08, 2.6, 1.9), glow)
        for y in (6.0, 18.0):
            box(target, f"FS_Mid__{variant}_Utility_{y}", (7.0, y, 0.6), (1.2, 1.0, 1.2), mid)
            cylinder(target, f"FS_Accent__{variant}_Bollard_{y}", (5.8, y + 1.2, 0.45), 0.16, 0.9, accent, vertices=8)
        if variant == "B":
            arch(target, "FS_Teal", 12.0, 6.15, 6.6, 0.5, 0.35, teal, glow)
        if variant == "C":
            for side in (-1, 1):
                box(target, f"FS_Green__Planter_{side}", (side * 6.7, 8.0, 0.45), (1.3, 2.0, 0.9), mid)
                sphere(target, f"FS_Green__Shrub_{side}", (side * 6.7, 8.0, 1.15), 0.7, green)

    backdrop = collection("ENV_FS_Backdrop")
    assets.append(backdrop)
    for index, (x, y, height) in enumerate(((-22, 4, 16), (-17, 15, 22), (18, 7, 18), (23, 18, 26))):
        box(backdrop, f"FS_Dark__LOD0_Tower_{index}", (x, y, height * 0.5 - 4), (6, 7, height), dark)
        for level in range(2, height, 3):
            box(backdrop, f"FS_Emissive__LOD0_Window_{index}_{level}", (x - math.copysign(3.03, x), y, level), (0.06, 4.0, 0.12), glow)
        box(backdrop, f"FS_Mid__LOD1_Tower_{index}", (x, y, height * 0.5 - 4), (6.2, 7.2, height), mid)

    prop_builders = {
        "ENV_FS_Tower": lambda t: (box(t, "FS_Dark__Tower", (0, 0, 5), (4, 4, 10), dark), cone(t, "FS_Emissive__Spire", (0, 0, 10.8), 0.7, 0.05, 1.6, glow, 8)),
        "ENV_FS_HoloSign": lambda t: (box(t, "FS_Teal__SignFrame", (0, 0, 1.8), (0.35, 3.0, 3.6), teal), box(t, "FS_Emissive__SignPanel", (0.2, 0, 1.8), (0.08, 2.5, 2.8), glow)),
        "ENV_FS_LightPole": lambda t: light_pole(t, "FS_Dark", 0.8, 0, dark, glow),
        "ENV_FS_TransitArch": lambda t: arch(t, "FS_Teal", 0, 6.15, 6.6, 0.5, 0.35, teal, glow),
        "ENV_FS_UtilityBox": lambda t: box(t, "FS_Mid__UtilityBox", (0, 0, 0.6), (1.2, 1.0, 1.2), mid),
        "ENV_FS_Bollard": lambda t: cylinder(t, "FS_Accent__Bollard", (0, 0, 0.45), 0.16, 0.9, accent, 8),
        "ENV_FS_Barrier": lambda t: box(t, "FS_Teal__Barrier", (0, 0, 0.55), (2.4, 0.35, 1.1), teal),
        "ENV_FS_Planter": lambda t: (box(t, "FS_Mid__Planter", (0, 0, 0.4), (1.5, 1.5, 0.8), mid), sphere(t, "FS_Green__Plant", (0, 0, 1.1), 0.65, green)),
    }
    for name, builder in prop_builders.items():
        target = collection(name)
        assets.append(target)
        builder(target)
    save_and_export("ENV_FuturisticStreet.blend", "FuturisticStreet", assets)


def water_theme_park():
    reset_scene()
    white = material("WP_White", (0.82, 0.90, 0.92), roughness=0.55)
    aqua = material("WP_Aqua", (0.02, 0.63, 0.67), metallic=0.1, roughness=0.32)
    blue = material("WP_Blue", (0.03, 0.28, 0.67), roughness=0.38)
    coral = material("WP_Coral", (0.95, 0.28, 0.22), roughness=0.5)
    yellow = material("WP_Yellow", (1.0, 0.76, 0.12), roughness=0.45)
    water = material("WP_Water", (0.02, 0.48, 0.76), metallic=0.05, roughness=0.15, emission=(0.01, 0.15, 0.22))

    assets = []
    for variant in "ABC":
        target = collection(f"ENV_WP_Roadside_{variant}")
        assets.append(target)
        box(target, f"WP_Water__{variant}_WaterPlane", (0, 12, -4.2), (42, 24, 0.18), water)
        for side in (-1, 1):
            x = side * 9.0
            cylinder(target, f"WP_White__{variant}_Tower_{side}", (x, 6.0 if side < 0 else 18.0, 2.5), 1.0, 5.0, white, 10)
            cone(target, f"WP_Coral__{variant}_Roof_{side}", (x, 6.0 if side < 0 else 18.0, 5.5), 1.5, 0.0, 1.2, coral, 10)
        for y in (3.0, 10.0, 17.0, 23.0):
            cylinder(target, f"WP_Aqua__{variant}_LampPost_{y}", (-6.3, y, 1.8), 0.12, 3.6, aqua, 8)
            sphere(target, f"WP_Yellow__{variant}_Lamp_{y}", (-6.3, y, 3.7), 0.26, yellow)
        if variant in ("A", "C"):
            tube(target, f"WP_Coral__{variant}_Slide", [(7.5, 4, 5), (9.5, 8, 4.3), (8.0, 13, 2.5), (10.0, 18, 1.0)], 0.42, coral)
        if variant in ("B", "C"):
            for y in (7.0, 16.0):
                cylinder(target, f"WP_White__{variant}_FountainBase_{y}", (7.2, y, -3.5), 1.3, 0.45, white, 12)
                tube(target, f"WP_Aqua__{variant}_FountainJet_{y}", [(7.2, y, -3.2), (7.2, y, 0.8)], 0.12, aqua)
        if variant == "B":
            arch(target, "WP_Aqua", 12.0, 6.2, 6.8, 0.45, 0.32, aqua, yellow)

    backdrop = collection("ENV_WP_Backdrop")
    assets.append(backdrop)
    box(backdrop, "WP_Water__LOD0_Water", (0, 12, -4.5), (60, 48, 0.2), water)
    for index, x in enumerate((-20, -13, 14, 22)):
        cylinder(backdrop, f"WP_White__LOD0_Tower_{index}", (x, 14 + (index % 2) * 8, 4), 1.5, 8, white, 10)
        cone(backdrop, f"WP_Coral__LOD0_Roof_{index}", (x, 14 + (index % 2) * 8, 8.8), 2.2, 0.0, 1.6, coral, 10)
        box(backdrop, f"WP_Aqua__LOD1_Silhouette_{index}", (x, 14 + (index % 2) * 8, 4), (3.2, 3.2, 8), aqua)

    prop_builders = {
        "ENV_WP_SlideTower": lambda t: (cylinder(t, "WP_White__SlideTower", (0, 0, 3), 1.2, 6, white, 10), tube(t, "WP_Coral__Slide", [(0, 0, 5.5), (2, 2, 4.5), (-1, 5, 2.4), (1, 8, 0.6)], 0.42, coral)),
        "ENV_WP_Fountain": lambda t: (cylinder(t, "WP_White__FountainBase", (0, 0, 0.3), 1.6, 0.6, white, 12), tube(t, "WP_Aqua__FountainJet", [(0, 0, 0.5), (0, 0, 3.2)], 0.13, aqua)),
        "ENV_WP_WaterArch": lambda t: arch(t, "WP_Aqua", 0, 6.2, 6.8, 0.45, 0.32, aqua, yellow),
        "ENV_WP_LifeguardTower": lambda t: (box(t, "WP_White__Platform", (0, 0, 2.4), (2.5, 2.5, 0.35), white), box(t, "WP_Coral__Cabin", (0, 0, 3.4), (2.0, 1.8, 1.7), coral)),
        "ENV_WP_Buoy": lambda t: torus(t, "WP_Coral__Buoy", (0, 0, 0.7), 0.6, 0.18, coral, rotation=(math.pi / 2, 0, 0)),
        "ENV_WP_Umbrella": lambda t: (cylinder(t, "WP_White__UmbrellaPole", (0, 0, 1.3), 0.07, 2.6, white, 8), cone(t, "WP_Yellow__Umbrella", (0, 0, 2.7), 1.2, 0.12, 0.5, yellow, 12)),
        "ENV_WP_Coral": lambda t: (tube(t, "WP_Coral__CoralA", [(0, 0, 0), (0, 0, 1.8), (0.5, 0, 2.4)], 0.14, coral), tube(t, "WP_Aqua__CoralB", [(0.2, 0, 0), (0.3, 0.2, 1.2), (-0.3, 0.3, 1.8)], 0.12, aqua)),
        "ENV_WP_Lamp": lambda t: (cylinder(t, "WP_Aqua__LampPole", (0, 0, 1.8), 0.12, 3.6, aqua, 8), sphere(t, "WP_Yellow__Lamp", (0, 0, 3.75), 0.28, yellow)),
    }
    for name, builder in prop_builders.items():
        target = collection(name)
        assets.append(target)
        builder(target)
    save_and_export("ENV_WaterThemePark.blend", "WaterThemePark", assets)


def ancient_dungeon():
    reset_scene()
    stone = material("AD_Stone", (0.28, 0.27, 0.25), roughness=0.9)
    dark = material("AD_DarkStone", (0.10, 0.09, 0.085), roughness=0.95)
    warm = material("AD_WarmStone", (0.43, 0.31, 0.20), roughness=0.88)
    metal = material("AD_Metal", (0.12, 0.11, 0.10), metallic=0.75, roughness=0.48)
    ember = material("AD_Ember", (1.0, 0.25, 0.03), roughness=0.3, emission=(1.0, 0.12, 0.01))
    moss = material("AD_Moss", (0.16, 0.25, 0.12), roughness=1.0)

    assets = []
    for variant in "ABC":
        target = collection(f"ENV_AD_Roadside_{variant}")
        assets.append(target)
        for side in (-1, 1):
            x = side * 7.8
            box(target, f"AD_DarkStone__{variant}_Wall_{side}", (x, 12, 2.8), (2.2, 24, 5.6), dark)
            for y in (0.5, 6.0, 12.0, 18.0, 23.5):
                box(target, f"AD_Stone__{variant}_Pillar_{side}_{y}", (side * 6.25, y, 2.4), (0.85, 0.85, 4.8), stone)
                box(target, f"AD_WarmStone__{variant}_PillarCap_{side}_{y}", (side * 6.25, y, 4.9), (1.15, 1.15, 0.25), warm)
            for y in (4.0, 14.0, 21.0):
                cylinder(target, f"AD_Metal__{variant}_Brazier_{side}_{y}", (side * 6.1, y, 1.5), 0.28, 1.1, metal, 8)
                cone(target, f"AD_Ember__{variant}_Flame_{side}_{y}", (side * 6.1, y, 2.35), 0.34, 0.0, 1.0, ember, 8)
        if variant == "B":
            arch(target, "AD_Stone", 12.0, 6.1, 6.6, 0.9, 0.8, stone, ember)
        if variant == "C":
            for y in (7.0, 17.0):
                for offset in (-0.6, 0, 0.7):
                    sphere(target, f"AD_Stone__Rubble_{y}_{offset}", (6.55 + offset, y, 0.25), 0.35, stone)
            box(target, "AD_Moss__WallMoss", (-6.05, 15.0, 2.5), (0.08, 3.5, 1.2), moss)

    backdrop = collection("ENV_AD_Backdrop")
    assets.append(backdrop)
    for index, (x, y, height) in enumerate(((-18, 6, 14), (-15, 20, 10), (16, 8, 12), (20, 20, 16))):
        box(backdrop, f"AD_DarkStone__LOD0_Ruin_{index}", (x, y, height * 0.5 - 2), (5, 5, height), dark)
        box(backdrop, f"AD_Stone__LOD0_Crenel_{index}", (x, y, height - 1.5), (6, 6, 0.8), stone)
        box(backdrop, f"AD_WarmStone__LOD1_Ruin_{index}", (x, y, height * 0.5 - 2), (5.3, 5.3, height), warm)

    def chain_post(target):
        cylinder(target, "AD_Metal__ChainPost", (0, 0, 1.0), 0.16, 2.0, metal, 8)
        for index in range(6):
            torus(target, f"AD_Metal__ChainLink_{index}", (0.0, index * 0.35, 1.7 - index * 0.18), 0.22, 0.05, metal, rotation=(math.pi / 2, 0, 0))

    prop_builders = {
        "ENV_AD_StoneArch": lambda t: arch(t, "AD_Stone", 0, 3.0, 5.5, 1.0, 0.8, stone, ember),
        "ENV_AD_RuinedTower": lambda t: (box(t, "AD_DarkStone__Tower", (0, 0, 4), (5, 5, 8), dark), box(t, "AD_Stone__BrokenCap", (0.7, 0, 8.2), (3.5, 5.3, 0.6), stone)),
        "ENV_AD_Brazier": lambda t: (cylinder(t, "AD_Metal__Brazier", (0, 0, 0.7), 0.42, 1.4, metal, 8), cone(t, "AD_Ember__Flame", (0, 0, 1.8), 0.5, 0.0, 1.4, ember, 8)),
        "ENV_AD_Gate": lambda t: (box(t, "AD_Stone__GateLeft", (-3, 0, 3), (1, 1, 6), stone), box(t, "AD_Stone__GateRight", (3, 0, 3), (1, 1, 6), stone), box(t, "AD_DarkStone__GateTop", (0, 0, 6.3), (7, 1, 0.8), dark)),
        "ENV_AD_Pillar": lambda t: (box(t, "AD_Stone__Pillar", (0, 0, 2.4), (0.9, 0.9, 4.8), stone), box(t, "AD_WarmStone__PillarCap", (0, 0, 4.9), (1.2, 1.2, 0.25), warm)),
        "ENV_AD_Rubble": lambda t: [sphere(t, f"AD_Stone__Rubble_{i}", ((i - 2) * 0.42, (i % 2) * 0.28, 0.25), 0.35 + (i % 2) * 0.1, stone) for i in range(5)],
        "ENV_AD_ChainPost": chain_post,
        "ENV_AD_Urn": lambda t: (cylinder(t, "AD_WarmStone__UrnBody", (0, 0, 0.65), 0.42, 1.0, warm, 10), cylinder(t, "AD_Stone__UrnNeck", (0, 0, 1.25), 0.25, 0.35, stone, 10)),
    }
    for name, builder in prop_builders.items():
        target = collection(name)
        assets.append(target)
        builder(target)
    save_and_export("ENV_AncientDungeon.blend", "AncientDungeon", assets)


def transition_tunnel():
    reset_scene()
    neutral = material("TR_Neutral", (0.18, 0.21, 0.27), metallic=0.4, roughness=0.5)
    dark = material("TR_Dark", (0.035, 0.045, 0.07), metallic=0.55, roughness=0.4)
    glow = material("TR_Emissive", (0.45, 0.80, 1.0), metallic=0.1, roughness=0.2, emission=(0.25, 0.65, 1.0))
    route = material("Shared_Route", (0.95, 0.72, 0.12), roughness=0.42, emission=(0.3, 0.16, 0.01))
    assets = []
    for kind in ("Entrance", "Middle", "Exit"):
        target = collection(f"ENV_TR_{kind}")
        assets.append(target)
        for y in (0.25, 6.0, 12.0, 18.0, 23.75):
            arch(target, "TR_Neutral", y, 5.2, 6.6, 0.34, 0.28, neutral, glow)
            box(target, f"TR_Emissive__{kind}_CeilingLight_{y}", (0, y, 6.15), (2.6, 0.18, 0.08), glow)
        for side in (-1, 1):
            box(target, f"TR_Dark__{kind}_OuterPanel_{side}", (side * 5.25, 12, 3.0), (0.18, 24, 5.8), dark)
            box(target, f"TR_Emissive__{kind}_SideGuide_{side}", (side * 4.56, 12, 0.35), (0.08, 23.5, 0.12), glow)
            box(target, f"Shared_Route__{kind}_RouteMarker_{side}", (side * 4.15, 12, 0.12), (0.1, 23.5, 0.04), route)
        if kind == "Entrance":
            arch(target, "TR_Dark", 0.45, 5.55, 7.2, 0.9, 0.6, dark, glow)
        if kind == "Exit":
            arch(target, "TR_Dark", 23.55, 5.55, 7.2, 0.9, 0.6, dark, glow)
            for side in (-1, 1):
                box(target, f"TR_Emissive__ExitChevron_{side}", (side * 3.7, 23.7, 6.9), (0.8, 0.12, 1.6), glow, rotation=(0, math.radians(side * 28), 0))

    portal = collection("ENV_TR_PortalFrame")
    assets.append(portal)
    arch(portal, "TR_Dark", 0, 5.55, 7.2, 0.9, 0.6, dark, glow)
    for index, x in enumerate((-3.6, -1.2, 1.2, 3.6)):
        box(portal, f"TR_Emissive__PortalBar_{index}", (x, -0.46, 5.7), (0.18, 0.08, 1.6), glow)
    save_and_export("ENV_TransitionTunnel.blend", "TransitionTunnel", assets)


def main():
    futuristic_street()
    water_theme_park()
    ancient_dungeon()
    transition_tunnel()
    print("Endless Rollball environment art generation complete.")


if __name__ == "__main__":
    main()
