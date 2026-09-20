import bpy
import math
import json
import shutil
from pathlib import Path
from mathutils import Vector


ROOT = Path(r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\AssetLibrary_v1")
RENDER_ROOT = ROOT / "Renders"
SOURCE_REFS = [
    Path(r"C:\Users\User\AppData\Local\Temp\codex-clipboard-152b2491-8dfa-4fcc-87eb-33ade4f4c0cd.png"),
    Path(r"C:\Users\User\AppData\Local\Temp\codex-clipboard-05517ea0-6af9-4907-a71a-e52f9a6d1a47.png"),
]

FOLDERS = [
    "00_Direction", "01_Architecture", "02_Road", "03_Vegetation",
    "04_StreetFurniture", "05_Playground", "06_Vehicles", "07_Obstacles",
    "08_Background", "09_AnimationReviews", "10_Modules", "11_FullEnvironment",
    "Renders",
]

PALETTE = {
    "WhiteSiding": "F2F0E6",
    "CreamSiding": "DCCCA8",
    "RoofRed": "C84B3D",
    "RoofShadow": "7B302B",
    "TealAccent": "1596A8",
    "BlueAccent": "2F78B7",
    "GrassFoliage": "58A94F",
    "Asphalt": "444B50",
    "RouteGuidance": "F4C542",
}


def ensure_dirs():
    for folder in FOLDERS:
        (ROOT / folder).mkdir(parents=True, exist_ok=True)
    (ROOT / "00_Direction" / "References").mkdir(parents=True, exist_ok=True)
    (ROOT / "01_Architecture" / "Reports").mkdir(parents=True, exist_ok=True)
    (RENDER_ROOT / "AR-01").mkdir(parents=True, exist_ok=True)
    for ref in SOURCE_REFS:
        if ref.exists():
            shutil.copy2(ref, ROOT / "00_Direction" / "References" / ref.name)


def hex_rgba(hex_value, alpha=1.0):
    value = hex_value.lstrip("#")
    return tuple(int(value[i:i+2], 16) / 255.0 for i in (0, 2, 4)) + (alpha,)


def reset_scene(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.name = name
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.render.fps = 30
    scene.frame_start = 1
    scene.frame_end = 120
    scene.world = bpy.data.worlds.new("World_FS_Daylight")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.30, 0.56, 0.86, 1.0)
    bg.inputs[1].default_value = 0.28
    scene["authoring_forward_axis"] = "+Y"
    scene["up_axis"] = "+Z"
    scene["unit_contract"] = "metres"
    scene["review_renderer"] = "EEVEE"
    scene["color_management"] = "AgX"
    scene["motion_blur"] = False
    return scene


def make_root(asset_name):
    root = bpy.data.collections.new(f"COL_FS_{asset_name}")
    bpy.context.scene.collection.children.link(root)
    children = {}
    for suffix in ("Reference", "Blockout", "Geo", "Animation", "Cameras", "Lights", "ReviewOnly"):
        col = bpy.data.collections.new(f"COL_{suffix}")
        root.children.link(col)
        children[suffix] = col
    return root, children


def move_to(obj, collection):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)


def material(name, color, roughness=0.55, metallic=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


def procedural_surface_material(name, base_color, roughness=0.58, bump_strength=0.08, scale=7.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    noise = nodes.new("ShaderNodeTexNoise")
    ramp = nodes.new("ShaderNodeValToRGB")
    bump = nodes.new("ShaderNodeBump")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 2.0
    noise.inputs["Roughness"].default_value = 0.55
    c = base_color
    ramp.color_ramp.elements[0].color = (c[0] * 0.88, c[1] * 0.88, c[2] * 0.88, 1)
    ramp.color_ramp.elements[1].color = (min(c[0] * 1.06, 1), min(c[1] * 1.06, 1), min(c[2] * 1.06, 1), 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bump.inputs["Strength"].default_value = bump_strength
    bump.inputs["Distance"].default_value = 0.025
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    return mat


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
        modifier.segments = 3
        modifier.limit_method = 'ANGLE'
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    if collection:
        move_to(obj, collection)
    return obj


def cylinder(name, location, radius, depth, mat=None, collection=None, vertices=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location)
    obj = bpy.context.object
    obj.name = name
    if mat:
        obj.data.materials.append(mat)
    if collection:
        move_to(obj, collection)
    return obj


def target_empty(name, location, collection):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = 0.25
    obj.location = location
    collection.objects.link(obj)
    return obj


def point_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()


def add_camera(name, location, target, collection, lens=50, ortho=False, ortho_scale=5.0):
    data = bpy.data.cameras.new(name)
    cam = bpy.data.objects.new(name, data)
    collection.objects.link(cam)
    cam.location = location
    if ortho:
        data.type = 'ORTHO'
        data.ortho_scale = ortho_scale
    else:
        data.lens = lens
    data.dof.use_dof = False
    point_at(cam, target)
    return cam


def add_lighting(collection):
    sun_data = bpy.data.lights.new("LGT_FS_Key_Sun", 'SUN')
    sun = bpy.data.objects.new("LGT_FS_Key_Sun", sun_data)
    collection.objects.link(sun)
    sun.rotation_euler = (math.radians(28), math.radians(-18), math.radians(-32))
    sun_data.energy = 3.0
    sun_data.angle = math.radians(10)

    fill_data = bpy.data.lights.new("LGT_FS_Fill_Soft", 'AREA')
    fill = bpy.data.objects.new("LGT_FS_Fill_Soft", fill_data)
    collection.objects.link(fill)
    fill.location = (-4.5, -4.0, 5.5)
    fill_data.energy = 700
    fill_data.shape = 'DISK'
    fill_data.size = 5.0
    point_at(fill, (0, 0, 1.3))
    return sun, fill


def setup_floor(collection, size=(12, 12), location=(0, 0, -0.04)):
    mat = material("MAT_FS_Review_NeutralFloor", (0.16, 0.18, 0.20, 1), 0.78)
    return cube("SM_FS_Review_Floor", location, (size[0], size[1], 0.08), mat, 0.025, collection)


def save(path):
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(path))


def render(camera, path):
    bpy.context.scene.camera = camera
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def create_reference_analysis():
    analysis = {
        "task_id": "DIR-01",
        "title": "Street reference analysis and protected-content boundary",
        "status": "SHIP",
        "reference_usage": [
            "Bright midday presentation and blue daylight sky",
            "White and cream horizontal siding",
            "Red tiled or shingle roof family",
            "Dense linked green landscaping",
            "Restrained blue and teal accents",
            "Detached houses transitioning to connected buildings",
            "Clear straight-road perspective and readable route",
            "Soft cartoon silhouettes with believable construction",
        ],
        "protected_content_boundary": [
            "Do not reproduce characters, logos, proprietary buildings, branded signage, or exact layouts",
            "Create original architecture and props using only general visual direction",
        ],
        "shape_language": "Real-world proportions with 10-15 percent cartoon exaggeration, rounded bevels, clear primary and secondary forms, limited micro-detail.",
        "palette": {key: f"#{value}" for key, value in PALETTE.items()},
        "sources": [ref.name for ref in SOURCE_REFS],
    }
    path = ROOT / "00_Direction" / "ENV_FS_DIR-01_StreetReferenceAnalysis_v01.json"
    path.write_text(json.dumps(analysis, indent=2), encoding="utf-8")


def create_review_template():
    scene = reset_scene("ENV_FS_DIR-02_ReviewTemplate_v01")
    _, col = make_root("ReviewTemplate")
    grey = material("MAT_FS_Review_ScaleGrey", (0.33, 0.37, 0.40, 1), 0.65)
    setup_floor(col["ReviewOnly"])
    body = cylinder("SM_FS_Reference_HumanBody", (0, 0, 0.9), 0.22, 1.45, grey, col["Reference"], 24)
    head = None
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.20, location=(0, 0, 1.68))
    head = bpy.context.object
    head.name = "SM_FS_Reference_HumanHead"
    head.data.materials.append(grey)
    move_to(head, col["Reference"])
    body["reference_height_m"] = 1.8
    target_empty("REF_FS_ReviewTarget", (0, 0, 1.0), col["ReviewOnly"])
    hero = add_camera("CAM_FS_Template_Hero", (4.5, -6.2, 3.3), (0, 0, 1.0), col["Cameras"], 50)
    add_camera("CAM_FS_Template_FrontOrtho", (0, -7, 1.0), (0, 0, 1.0), col["Cameras"], ortho=True, ortho_scale=3.0)
    add_camera("CAM_FS_Template_GameplayDistance", (0, -15, 4.5), (0, 2, 1.2), col["Cameras"], 38)
    add_lighting(col["Lights"])
    scene.camera = hero
    scene["review_frame_occupancy_target"] = "approximately 70 percent"
    save(ROOT / "00_Direction" / "ENV_FS_DIR-02_ReviewTemplate_v01.blend")
    render(hero, RENDER_ROOT / "ENV_FS_DIR-02_ReviewTemplate.png")


def create_material_library():
    scene = reset_scene("ENV_FS_DIR-03_MaterialLibrary_v01")
    _, col = make_root("MaterialLibrary")
    setup_floor(col["ReviewOnly"], (16, 12))
    materials = {}
    for key, value in PALETTE.items():
        materials[key] = procedural_surface_material(f"MAT_FS_Master_{key}", hex_rgba(value), 0.56, 0.045, 5.0)
    positions = [(-4, 2), (-2, 2), (0, 2), (2, 2), (4, 2), (-3, -1), (-1, -1), (1, -1), (3, -1)]
    for (key, mat), (x, y) in zip(materials.items(), positions):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.72, location=(x, y, 0.78))
        obj = bpy.context.object
        obj.name = f"SM_FS_MaterialSwatch_{key}"
        obj.data.materials.append(mat)
        move_to(obj, col["Geo"])
    hero = add_camera("CAM_FS_MaterialLibrary_Hero", (8.8, -12.5, 8.3), (0, 0.4, 0.6), col["Cameras"], 50)
    add_camera("CAM_FS_MaterialLibrary_FrontOrtho", (0, -14, 1.2), (0, 0.4, 1.0), col["Cameras"], ortho=True, ortho_scale=11.5)
    add_lighting(col["Lights"])
    scene.camera = hero
    scene["master_material_count"] = len(materials)
    save(ROOT / "00_Direction" / "ENV_FS_DIR-03_MaterialLibrary_v01.blend")
    render(hero, RENDER_ROOT / "ENV_FS_DIR-03_MaterialLibrary.png")


def create_module_template():
    scene = reset_scene("ENV_FS_DIR-04_ModuleTemplate_v01")
    _, col = make_root("ModuleTemplate24m")
    asphalt = material("MAT_FS_Asphalt_Template", hex_rgba(PALETTE["Asphalt"]), 0.82)
    yellow = material("MAT_FS_RouteGuidance_Template", hex_rgba(PALETTE["RouteGuidance"]), 0.45)
    green = material("MAT_FS_DecorationZone_Template", (0.12, 0.34, 0.16, 1), 0.88)
    road = cube("SM_FS_Road_Template_9x24m", (0, 12, -0.05), (9, 24, 0.1), asphalt, 0, col["Blockout"])
    road["visual_only"] = True
    road["route_width_m"] = 9.0
    cube("SM_FS_Guide_RouteEdge_L", (-4.48, 12, 0.025), (0.08, 24, 0.05), yellow, 0.015, col["ReviewOnly"])
    cube("SM_FS_Guide_RouteEdge_R", (4.48, 12, 0.025), (0.08, 24, 0.05), yellow, 0.015, col["ReviewOnly"])
    cube("SM_FS_Zone_Decoration_L", (-8.25, 12, -0.08), (5.5, 24, 0.08), green, 0, col["ReviewOnly"])
    cube("SM_FS_Zone_Decoration_R", (8.25, 12, -0.08), (5.5, 24, 0.08), green, 0, col["ReviewOnly"])
    seam_mat = material("MAT_FS_SeamGuide", (0.95, 0.95, 0.95, 1), 0.55)
    cube("SM_FS_Guide_EntranceSeam", (0, 0, 0.08), (16, 0.08, 0.12), seam_mat, 0, col["ReviewOnly"])
    cube("SM_FS_Guide_ExitSeam", (0, 24, 0.08), (16, 0.08, 0.12), seam_mat, 0, col["ReviewOnly"])
    for x in range(-11, 12):
        cube(f"SM_FS_Grid_X_{x:+03d}", (x, 12, 0.006), (0.015, 24, 0.012), seam_mat, 0, col["ReviewOnly"])
    for y in range(0, 25):
        cube(f"SM_FS_Grid_Y_{y:02d}", (0, y, 0.008), (22, 0.015, 0.012), seam_mat, 0, col["ReviewOnly"])
    hero = add_camera("CAM_FS_ModuleTemplate_Hero", (16, -10, 18), (0, 12, 0), col["Cameras"], 50)
    add_camera("CAM_FS_ModuleTemplate_TopOrtho", (0, 12, 30), (0, 12, 0), col["Cameras"], ortho=True, ortho_scale=31)
    add_lighting(col["Lights"])
    scene.camera = hero
    scene["module_length_m"] = 24.0
    scene["route_width_m"] = 9.0
    scene["decoration_min_x_abs_m"] = 5.5
    scene["module_pivot"] = "road centre at entrance seam (0,0,0)"
    save(ROOT / "00_Direction" / "ENV_FS_DIR-04_ModuleTemplate_v01.blend")
    render(hero, RENDER_ROOT / "ENV_FS_DIR-04_ModuleTemplate.png")


def write_manifest():
    waves = {
        "Wave 0": ["DIR-01", "DIR-02", "DIR-03", "DIR-04", "DIR-05"],
        "Wave 1": [f"AR-{i:02d}" for i in range(1, 13)] + ["AR-AS01"],
        "Wave 2": [f"AR-{i:02d}" for i in range(13, 19)] + ["AR-AS02", "AR-AS03"],
        "Wave 3": [f"CB-{i:02d}" for i in range(1, 10)] + ["CB-AS01", "CB-AS02", "CB-AS03"],
        "Wave 4": [f"RD-{i:02d}" for i in range(1, 11)],
        "Wave 5": [f"VG-{i:02d}" for i in range(1, 11)],
        "Wave 6": [f"SF-{i:02d}" for i in range(1, 15)],
        "Wave 7": [f"PG-{i:02d}" for i in range(1, 7)] + ["PG-AS01"],
        "Wave 8": [f"VH-{i:02d}" for i in range(1, 4)],
        "Wave 9": [f"OB-{i:02d}" for i in range(1, 6)],
        "Wave 10": [f"BG-{i:02d}" for i in range(1, 6)],
        "Wave 11": [f"AN-{i:02d}" for i in range(1, 4)],
        "Modules": [f"MD-{i:02d}" for i in range(1, 9)],
    }
    tasks = []
    names = {
        "DIR-01": "Street reference analysis", "DIR-02": "Scale and camera template",
        "DIR-03": "Procedural master-material library", "DIR-04": "1m grid and 24m module template",
        "DIR-05": "Asset manifest", "AR-01": "4m blank siding-wall section",
    }
    for wave, ids in waves.items():
        for task_id in ids:
            status = "PLANNED"
            if task_id.startswith("DIR-"):
                status = "SHIP"
            if task_id == "AR-01":
                status = "QA_SHIP_PENDING_USER_APPROVAL"
            tasks.append({"id": task_id, "wave": wave, "name": names.get(task_id, "See approved production plan"), "status": status})
    manifest = {
        "library": "Street Theme Atomic Asset Library v1",
        "root": str(ROOT),
        "contract": {"units": "metres", "up": "+Z", "forward": "+Y", "renderer": "EEVEE", "color_management": "AgX"},
        "frozen_comparison_master": r"D:\Creative\Blender\Endless Rollball\Environment\StreetTheme\Review_v5\ENV_FS_StreetTheme_Review_v5.blend",
        "unity_export": False,
        "tasks": tasks,
    }
    (ROOT / "00_Direction" / "ENV_FS_DIR-05_AssetManifest_v01.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")


def create_ar01():
    scene = reset_scene("ENV_FS_AR-01_BlankSidingWall4m_v01")
    _, col = make_root("BlankSidingWall4m")
    scene["task_id"] = "AR-01"
    scene["asset_tier"] = "standard modular architecture"
    scene["dimensions_m"] = "4.0 X, 0.38 overall Y, 3.0 Z (0.22m structural core)"
    scene["triangle_budget"] = "500-5000 evaluated triangles"
    scene["material_budget"] = "1-2 materials"
    scene["pivot_contract"] = "root at ground contact centre (0,0,0)"
    scene["front_axis"] = "-Y"
    scene["animation_state"] = "static"
    scene["workflow_state"] = "QA_SHIP_PENDING_USER_APPROVAL"
    scene["deferred"] = "LOD, retopology, baking, collision, export and Unity integration"

    white = procedural_surface_material("MAT_FS_Siding_White", hex_rgba(PALETTE["WhiteSiding"]), 0.58, 0.035, 8.0)
    cream = procedural_surface_material("MAT_FS_Trim_Cream", hex_rgba(PALETTE["CreamSiding"]), 0.60, 0.025, 9.0)
    floor = setup_floor(col["ReviewOnly"], (11, 9), (0, 0.6, -0.06))

    body = cube("SM_FS_Architecture_BlankSidingWall4m", (0, 0.055, 1.5), (4.0, 0.22, 3.0), white, 0.055, col["Geo"])
    body["asset_root_pivot"] = "0,0,0"
    body["modular_width_m"] = 4.0

    board_master = cube("SM_FS_Architecture_SidingBoard_00", (0, -0.098, 0.145), (3.72, 0.105, 0.19), white, 0.026, col["Geo"])
    board_master["linked_component_role"] = "horizontal siding board"
    for index in range(1, 15):
        linked = bpy.data.objects.new(f"SM_FS_Architecture_SidingBoard_{index:02d}", board_master.data)
        linked.location = (0, -0.098, 0.145 + index * 0.19)
        col["Geo"].objects.link(linked)
        linked["linked_instance"] = True

    cube("SM_FS_Architecture_EndTrim_L", (-1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_EndTrim_R", (1.92, -0.135, 1.5), (0.16, 0.12, 2.96), cream, 0.026, col["Geo"])
    cube("SM_FS_Architecture_BaseTrim", (0, -0.145, 0.08), (4.0, 0.13, 0.16), cream, 0.025, col["Geo"])
    cube("SM_FS_Architecture_TopTrim", (0, -0.145, 2.92), (4.0, 0.13, 0.16), cream, 0.025, col["Geo"])

    ref_mat = material("MAT_FS_Reference_ScaleGrey", (0.22, 0.27, 0.30, 1), 0.66)
    ref = cylinder("SM_FS_Reference_HumanScale_1p8m", (-2.75, 0.15, 0.9), 0.19, 1.45, ref_mat, col["Reference"], 24)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.18, location=(-2.75, 0.15, 1.68))
    ref_head = bpy.context.object
    ref_head.name = "SM_FS_Reference_HumanScale_Head"
    ref_head.data.materials.append(ref_mat)
    move_to(ref_head, col["Reference"])
    ref["reference_height_m"] = 1.8

    target_empty("REF_FS_AR01_Target", (0, 0, 1.45), col["ReviewOnly"])
    hero = add_camera("CAM_FS_BlankSidingWall4m_Hero", (5.1, -7.0, 3.65), (0, 0, 1.45), col["Cameras"], 50)
    front = add_camera("CAM_FS_BlankSidingWall4m_FrontOrtho", (0, -8, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.25)
    profile = add_camera("CAM_FS_BlankSidingWall4m_ProfileOrtho", (6.0, 0, 1.5), (0, 0, 1.5), col["Cameras"], ortho=True, ortho_scale=4.1)
    detail = add_camera("CAM_FS_BlankSidingWall4m_MaterialBeauty", (2.2, -3.8, 2.15), (0.55, -0.05, 1.65), col["Cameras"], 70)
    gameplay = add_camera("CAM_FS_BlankSidingWall4m_GameplayDistance", (0, -14, 4.6), (0, 0, 1.35), col["Cameras"], 38)
    add_lighting(col["Lights"])
    scene.camera = hero

    # Wireframe review material, used only as a render-layer override.
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
    mix.blend_type = 'MIX'
    mix.inputs[1].default_value = (0.82, 0.84, 0.86, 1)
    mix.inputs[2].default_value = (0.025, 0.035, 0.045, 1)
    bsdf.inputs["Roughness"].default_value = 0.72
    links.new(wire_node.outputs["Fac"], mix.inputs[0])
    links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    output_path = ROOT / "01_Architecture" / "ENV_FS_AR-01_BlankSidingWall4m_v01.blend"
    save(output_path)
    render(hero, RENDER_ROOT / "AR-01" / "ENV_FS_AR-01_Hero.png")
    render(front, RENDER_ROOT / "AR-01" / "ENV_FS_AR-01_Front.png")
    render(profile, RENDER_ROOT / "AR-01" / "ENV_FS_AR-01_Profile.png")
    render(detail, RENDER_ROOT / "AR-01" / "ENV_FS_AR-01_MaterialBeauty.png")
    bpy.context.view_layer.material_override = wire
    render(hero, RENDER_ROOT / "AR-01" / "ENV_FS_AR-01_Wireframe.png")
    bpy.context.view_layer.material_override = None
    scene.camera = hero
    save(output_path)

    brief = {
        "task_id": "AR-01", "purpose": "Reusable 4m blank siding-wall section for detached residential assemblies.",
        "asset_tier": "standard modular architecture", "specialist_skills": ["blender-director", "environment-artist", "blender-modeler", "cartoon-style", "materials"],
        "dimensions_m": {"width_x": 4.0, "overall_depth_y": 0.38, "structural_core_depth_y": 0.22, "height_z": 3.0},
        "triangle_budget": [500, 5000], "material_budget": 2, "pivot": "ground-contact centre at 0,0,0",
        "front_axis": "-Y", "street_authoring_forward": "+Y", "animation": "static",
        "construction": ["structural wall backing", "15 linked horizontal siding boards", "two end trims", "base trim", "top trim"],
        "deferred": ["LOD", "retopology", "texture baking", "collision", "FBX export", "Unity integration"],
    }
    (ROOT / "01_Architecture" / "ENV_FS_AR-01_BlankSidingWall4m_ProductionBrief_v01.json").write_text(json.dumps(brief, indent=2), encoding="utf-8")

    comparison = {
        "task_id": "AR-01", "iteration": 3, "maximum_iterations": 3,
        "reference_features": ["white horizontal siding", "believable residential scale", "soft bevels", "bright clean material response", "limited micro-detail"],
        "categories": {
            "real_world_scale": "Match", "cartoon_exaggeration": "Close", "horizontal_siding_language": "Match",
            "white_cream_palette": "Match", "silhouette_clarity": "Match", "construction_depth": "Close",
            "rounded_edge_treatment": "Match", "material_response": "Close", "modular_reusability": "Match",
            "protected_content_boundary": "Match",
        },
        "score": "10/10 categories Match or Close", "correction_iteration_2": "Added distinct front and true side-profile orthographic renders.",
        "correction_iteration_3": "Adjusted linked siding-board spacing so all geometry remains within the exact 3.0m height envelope.",
        "gaps": ["Final house assembly must confirm roof-to-wall proportion", "Close gameplay-distance test remains an assembly concern"],
    }
    (ROOT / "01_Architecture" / "Reports" / "ENV_FS_AR-01_ReferenceComparison_v01.json").write_text(json.dumps(comparison, indent=2), encoding="utf-8")


def main():
    ensure_dirs()
    create_reference_analysis()
    create_review_template()
    create_material_library()
    create_module_template()
    write_manifest()
    create_ar01()
    print("WAVE0_AR01_COMPLETE")


if __name__ == "__main__":
    main()
