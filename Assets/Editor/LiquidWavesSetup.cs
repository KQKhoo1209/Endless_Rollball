using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Uses the installed Shader Graph 17.4 node API through reflection because its
// graph/node types are internal. The saved asset remains a regular editable graph.
public static class LiquidWavesSetup
{
    private const string Folder = "Assets/EndlessRollballAssets/LiquidWaves";
    private const string GraphPath = Folder + "/SG_LiquidWaves.shadergraph";
    private const string Marker = "EndlessRollball.LiquidWaves.Setup.v5";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly sg;
    private static object graph, category, group;
    private static readonly Dictionary<string, object> properties = new Dictionary<string, object>();

    [InitializeOnLoadMethod]
    private static void QueueInitialBuild()
    {
        if (!File.Exists(GraphPath) || SessionState.GetBool(Marker, false)) return;
        // Never rebuild an already authored graph automatically.
        bool hasEdges = Regex.IsMatch(File.ReadAllText(GraphPath), "\\\"m_Edges\\\"\\s*:\\s*\\[\\s*\\{");
        bool failedSetup = File.Exists("Temp/LiquidWaves/BuildResult.txt") &&
            File.ReadAllText("Temp/LiquidWaves/BuildResult.txt").StartsWith("FAIL:");
        if (hasEdges && !failedSetup) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            SessionState.SetBool(Marker, true);
            Build();
        };
    }

    [MenuItem("Tools/Endless Rollball/Build Liquid Wave Materials")]
    public static void Build()
    {
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding liquid assets.");
            Directory.CreateDirectory("Temp/LiquidWaves");
            if (!File.Exists("Temp/LiquidWaves/BeforeBuild.shadergraph"))
                File.Copy(GraphPath, "Temp/LiquidWaves/BeforeBuild.shadergraph");
            sg = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Unity.ShaderGraph.Editor");
            graph = New("GraphData");
            Type multi = sg.GetType("UnityEditor.ShaderGraph.Serialization.MultiJson", true);
            multi.GetMethod("Deserialize").MakeGenericMethod(graph.GetType()).Invoke(null,
                new object[] { graph, File.ReadAllText(GraphPath), null, false });
            Set(graph, "assetGuid", AssetDatabase.AssetPathToGUID(GraphPath));
            Call(graph, "OnEnable");
            // Rebuild this requested graph, preserving its existing SurfaceColor input.
            object[] existing = Nodes("AbstractMaterialNode").ToArray();
            foreach (object node in existing)
                if (node.GetType().Name != "BlockNode") Call(graph, "RemoveNode", node);
            foreach (object oldGroup in ((IEnumerable)Get(graph, "groups")).Cast<object>().ToArray())
                Call(graph, "RemoveGroup", oldGroup);
            properties.Clear();
            foreach (object p in (IEnumerable)Get(graph, "properties"))
                properties[(string)Get(p, "displayName")] = p;
            category = ((IEnumerable)Get(graph, "categories")).Cast<object>().FirstOrDefault();
            if (category == null)
            {
                category = New("CategoryData");
                Call(graph, "AddCategory", category);
            }
            Set(category, "name", "Liquid Controls");

            Property("SurfaceColor", "ColorShaderProperty", new Color(.035f, .48f, .63f, 1f));
            Property("BaseTexture", "Texture2DShaderProperty", null);
            Property("WaveHeight", "Vector1ShaderProperty", .15f);
            Property("WaveFrequency", "Vector1ShaderProperty", .5f);
            Property("WaveSpeed", "Vector1ShaderProperty", 1.2f);
            Property("RippleScale", "Vector1ShaderProperty", .8f);
            Property("RippleSpeed", "Vector2ShaderProperty", new Vector4(.08f, .03f, 0, 0));
            Property("NormalStrength", "Vector1ShaderProperty", .08f);
            Property("Smoothness", "Vector1ShaderProperty", .85f);
            Property("GlowStrength", "Vector1ShaderProperty", 0f);
            Property("TextureTiling", "Vector2ShaderProperty", new Vector4(.2f, .2f, 0, 0));
            Property("TextureSpeed", "Vector2ShaderProperty", new Vector4(.02f, .005f, 0, 0));

            Group("World-space wave motion", -1800, -900);
            object position = Node("PositionNode", -1800, -900);
            SetEnum(position, "m_Space", "World");
            object split = Node("SplitNode", -1550, -900); Edge(position, 0, split, 0);
            object time = Node("TimeNode", -1800, -580);
            object frequency = Input("WaveFrequency", -1550, -560);
            object speed = Input("WaveSpeed", -1550, -350);
            object timeSpeed = Binary("MultiplyNode", time, 0, speed, 0, -1300, -350);
            object xPhase = Binary("MultiplyNode", split, 1, frequency, 0, -1300, -900);
            object xSum = Binary("AddNode", xPhase, 2, timeSpeed, 2, -1050, -900);
            object xSine = Node("SineNode", -800, -900); Edge(xSum, 2, xSine, 0);
            object zPhase = Binary("MultiplyNode", split, 3, frequency, 0, -1300, -650);
            object zScaled = Binary("MultiplyNode", zPhase, 2, Constant(.7f, -1300, -120), 0, -1050, -650);
            object zTime = Binary("MultiplyNode", timeSpeed, 2, Constant(.8f, -1300, 90), 0, -1050, -350);
            object zSum = Binary("AddNode", zScaled, 2, zTime, 2, -800, -650);
            object zSine = Node("SineNode", -550, -650); Edge(zSum, 2, zSine, 0);
            object sum = Binary("AddNode", xSine, 1, zSine, 1, -300, -900);
            object mean = Binary("MultiplyNode", sum, 2, Constant(.5f, -550, -350), 0, -50, -900);
            object wave = Binary("MultiplyNode", mean, 2, Input("WaveHeight", -50, -600), 0, 200, -900);
            object displacement = Node("CombineNode", 450, -900); Edge(wave, 2, displacement, 1);
            object displaced = Binary("AddNode", position, 0, displacement, 5, 700, -900);
            object transform = Node("TransformNode", 950, -900);
            object conversion = Get(transform, "conversion");
            SetEnum(conversion, "from", "World"); SetEnum(conversion, "to", "Object");
            Set(transform, "conversion", conversion); SetEnum(transform, "conversionType", "Position");
            Edge(displaced, 2, transform, 0); Edge(transform, 1, Block("VertexDescription.Position"), 0);

            Group("Animated surface ripples", -1800, 520);
            object xz = Node("CombineNode", -1800, 520);
            Edge(split, 1, xz, 0); Edge(split, 3, xz, 1);
            object rippleTime = Binary("MultiplyNode", time, 0, Input("RippleSpeed", -1800, 850), 0, -1500, 800);
            object rippleUV = Binary("AddNode", xz, 6, rippleTime, 2, -1250, 520);
            object noise = Node("NoiseNode", -1000, 520);
            Edge(rippleUV, 2, noise, 0); Edge(Input("RippleScale", -1250, 1000), 0, noise, 1);
            object rippleHeight = Binary("MultiplyNode", noise, 2, Input("NormalStrength", -1000, 1000), 0, -750, 520);
            object fullHeight = Binary("AddNode", rippleHeight, 2, wave, 2, -500, 520);
            object normal = Node("NormalFromHeightNode", -250, 520);
            Edge(fullHeight, 2, normal, 0); Edge(Constant(1f, -500, 850), 0, normal, 2);
            Edge(normal, 1, Block("SurfaceDescription.NormalTS"), 0);

            Group("Surface colour, flow and lava glow", -1800, 1450);
            object tiled = Binary("MultiplyNode", xz, 6, Input("TextureTiling", -1800, 1750), 0, -1500, 1450);
            object flow = Binary("MultiplyNode", time, 0, Input("TextureSpeed", -1800, 1970), 0, -1500, 1750);
            object textureUV = Binary("AddNode", tiled, 2, flow, 2, -1250, 1450);
            object sample = Node("SampleTexture2DNode", -1000, 1450);
            Edge(Input("BaseTexture", -1250, 1800), 0, sample, 1); Edge(textureUV, 2, sample, 2);
            object colour = Binary("MultiplyNode", sample, 0, Input("SurfaceColor", -1000, 1870), 0, -700, 1450);
            Edge(colour, 2, Block("SurfaceDescription.BaseColor"), 0);
            object emission = Binary("MultiplyNode", colour, 2, Input("GlowStrength", -700, 1800), 0, -400, 1450);
            Edge(emission, 2, Block("SurfaceDescription.Emission"), 0);
            Edge(Input("Smoothness", -400, 2020), 0, Block("SurfaceDescription.Smoothness"), 0);
            Set(Get(graph, "vertexContext"), "position", new Vector2(1450, -900));
            Set(Get(graph, "fragmentContext"), "position", new Vector2(1450, 520));
            Call(graph, "ValidateGraph");
            string json = (string)multi.GetMethod("Serialize").Invoke(null, new[] { graph });
            File.WriteAllText(GraphPath, json);
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Liquid graph failed shader import. Check the Shader Graph Console.");

            Mesh waterMesh = Grid("WaterWaveGrid", 128, 32);
            Mesh lavaMesh = Grid("LavaWaveGrid", 16, 32);
            Material water = LiquidMaterial("Water", shader, false);
            Material lava = LiquidMaterial("Lava", shader, true);
            AssignPrefab("Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Beach_SurfSea_24m.prefab", "SeaVisual", waterMesh, water);
            AssignPrefab("Assets/EndlessRollballAssets/Obstacles/Dungeon/PhoenixLava/Prefabs/TRK_Dungeon_PhoenixLava_24m.prefab", "LavaVisual", lavaMesh, lava);
            AssignStandaloneWater(waterMesh, water);
            AssetDatabase.SaveAssets();
            Preview(waterMesh, water, "Temp/LiquidWaves/Water.png");
            Preview(lavaMesh, lava, "Temp/LiquidWaves/Lava.png");
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Liquid shader has compile errors after preview rendering.");
            File.WriteAllText("Temp/LiquidWaves/BuildResult.txt", "PASS: graph, materials, subdivided meshes and event assignments created; shader previews rendered.");
            Debug.Log("[LiquidWaves] PASS: editable shared graph, both materials, wave meshes, event prefab assignments and preview renders ready.");
        }
        catch (Exception e)
        {
            Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            Directory.CreateDirectory("Temp/LiquidWaves");
            File.WriteAllText("Temp/LiquidWaves/BuildResult.txt", "FAIL: " + cause);
            Debug.LogException(cause);
        }
    }

    private static object New(string name)
    {
        Type type = sg.GetType("UnityEditor.ShaderGraph." + name) ?? sg.GetType("UnityEditor.ShaderGraph.Internal." + name);
        return Activator.CreateInstance(type ?? throw new TypeLoadException(name), true);
    }
    private static object Get(object obj, string name)
    {
        for (Type t = obj.GetType(); t != null; t = t.BaseType)
        {
            PropertyInfo p = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
            if (p != null) return p.GetValue(obj);
            FieldInfo f = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (f != null) return f.GetValue(obj);
        }
        throw new MissingMemberException(obj.GetType().Name, name);
    }
    private static void Set(object obj, string name, object value)
    {
        for (Type t = obj.GetType(); t != null; t = t.BaseType)
        {
            PropertyInfo p = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
            if (p != null && p.CanWrite) { p.SetValue(obj, value); return; }
            FieldInfo f = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (f != null) { f.SetValue(obj, value); return; }
        }
        throw new MissingMemberException(obj.GetType().Name, name);
    }
    private static void SetEnum(object obj, string name, string value)
    {
        Set(obj, name, Enum.Parse(Get(obj, name).GetType(), value));
    }
    private static object Call(object obj, string name, params object[] values)
    {
        MethodInfo method = obj.GetType().GetMethods(Flags).First(m => m.Name == name && !m.IsGenericMethod &&
            m.GetParameters().Length >= values.Length && m.GetParameters().Skip(values.Length).All(p => p.IsOptional) &&
            m.GetParameters().Take(values.Length).Select((p, i) => values[i] == null || p.ParameterType.IsInstanceOfType(values[i])).All(b => b));
        object[] args = method.GetParameters().Select((p, i) => i < values.Length ? values[i] : p.DefaultValue).ToArray();
        return method.Invoke(obj, args);
    }
    private static IEnumerable<object> Nodes(string type)
    {
        MethodInfo method = graph.GetType().GetMethods(Flags).First(m => m.Name == "GetNodes" && m.IsGenericMethod);
        Type t = sg.GetType("UnityEditor.ShaderGraph." + type, true);
        return ((IEnumerable)method.MakeGenericMethod(t).Invoke(graph, null)).Cast<object>();
    }
    private static object Block(string descriptor) => Nodes("BlockNode").First(n => (string)Get(n, "name") == descriptor);
    private static void Group(string title, float x, float y)
    {
        group = New("GroupData"); Set(group, "title", title); Set(group, "position", new Vector2(x, y));
        Call(graph, "CreateGroup", group);
    }
    private static object Node(string type, float x, float y)
    {
        object node = New(type);
        Call(graph, "AddNode", node);
        object draw = Get(node, "drawState");
        Set(draw, "position", new Rect(x, y, 190, 150));
        Set(node, "drawState", draw);
        Call(graph, "SetGroup", node, group);
        return node;
    }
    private static object Constant(float value, float x, float y)
    {
        object node = Node("Vector1Node", x, y);
        Set(node, "m_Value", value);
        Type slotType = sg.GetType("UnityEditor.ShaderGraph.MaterialSlot", true);
        object slot = node.GetType().GetMethod("FindInputSlot").MakeGenericMethod(slotType)
            .Invoke(node, new object[] { 1 });
        Set(slot, "value", value);
        return node;
    }
    private static void Property(string name, string type, object value)
    {
        bool add = !properties.TryGetValue(name, out object property);
        if (add) property = New(type);
        Set(property, "displayName", name);
        Set(property, "m_OverrideReferenceName", "_" + name);
        if (value != null) Set(property, "value", value);
        if (add)
        {
            Call(graph, "AddGraphInput", property);
            Call(category, "InsertItemIntoCategory", property);
            properties[name] = property;
        }
    }
    private static object Input(string name, float x, float y)
    {
        object node = Node("PropertyNode", x, y); Set(node, "property", properties[name]); return node;
    }
    private static void Edge(object output, int outputSlot, object input, int inputSlot)
    {
        if (Call(graph, "Connect", Call(output, "GetSlotReference", outputSlot), Call(input, "GetSlotReference", inputSlot)) == null)
            throw new InvalidOperationException("Could not connect " + Get(output, "name") + " to " + Get(input, "name"));
    }
    private static object Binary(string type, object a, int slotA, object b, int slotB, float x, float y)
    {
        object node = Node(type, x, y); Edge(a, slotA, node, 0); Edge(b, slotB, node, 1); return node;
    }

    private static Mesh Grid(string name, int nx, int nz)
    {
        string path = Folder + "/" + name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh(); else mesh.Clear();
        mesh.name = name;
        Vector3[] vertices = new Vector3[(nx + 1) * (nz + 1)];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector4[] tangents = new Vector4[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[nx * nz * 6];
        for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
        {
            int i = z * (nx + 1) + x;
            vertices[i] = new Vector3((float)x / nx * 10f - 5f, 0f, (float)z / nz * 10f - 5f);
            normals[i] = Vector3.up; tangents[i] = new Vector4(1, 0, 0, -1);
            uv[i] = new Vector2((float)x / nx, (float)z / nz);
        }
        int n = 0;
        for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
        {
            int i = z * (nx + 1) + x;
            triangles[n++] = i; triangles[n++] = i + nx + 1; triangles[n++] = i + 1;
            triangles[n++] = i + 1; triangles[n++] = i + nx + 1; triangles[n++] = i + nx + 2;
        }
        mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents;
        mesh.uv = uv; mesh.triangles = triangles;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(10f, 2.2f, 10f));
        if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Material LiquidMaterial(string name, Shader shader, bool lava)
    {
        string path = Folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        material.SetColor("_SurfaceColor", lava ? new Color(1f, .65f, .24f) : new Color(.035f, .48f, .63f));
        material.SetFloat("_WaveHeight", lava ? .06f : .15f);
        material.SetFloat("_WaveFrequency", lava ? .35f : .5f);
        material.SetFloat("_WaveSpeed", lava ? .35f : 1.2f);
        material.SetFloat("_RippleScale", lava ? .5f : .8f);
        material.SetVector("_RippleSpeed", lava ? new Vector4(.02f, .01f, 0, 0) : new Vector4(.08f, .03f, 0, 0));
        material.SetFloat("_NormalStrength", lava ? .04f : .08f);
        material.SetFloat("_Smoothness", lava ? .45f : .85f);
        material.SetFloat("_GlowStrength", lava ? 2f : 0f);
        material.SetVector("_TextureTiling", new Vector4(.2f, .2f, 0, 0));
        material.SetVector("_TextureSpeed", new Vector4(.02f, .005f, 0, 0));
        Texture texture = lava ? AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/EndlessRollballAssets/Obstacles/Dungeon/Texture/LavaBall_BaseColor.png") : Texture2D.whiteTexture;
        material.SetTexture("_BaseTexture", texture);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AssignPrefab(string path, string visualName, Mesh mesh, Material material)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            MeshFilter filter = root.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == visualName);
            filter.sharedMesh = mesh; filter.GetComponent<Renderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void AssignStandaloneWater(Mesh mesh, Material material)
    {
        string path = "Assets/Scenes/GameplayScene.unity";
        string scene = File.ReadAllText(path).Replace("\r\n", "\n");
        string[] blocks = Regex.Split(scene, "(?=--- !u!)");
        string go = blocks.First(b => b.StartsWith("--- !u!1 ") && b.Contains("  m_Name: WaterSurface\n"));
        string id = Regex.Match(go, "&([0-9]+)").Groups[1].Value;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string meshGuid, out long meshId);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string matGuid, out long matId);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (!blocks[i].Contains("m_GameObject: {fileID: " + id + "}")) continue;
            if (blocks[i].StartsWith("--- !u!33 "))
                blocks[i] = Regex.Replace(blocks[i], "  m_Mesh: .*", $"  m_Mesh: {{fileID: {meshId}, guid: {meshGuid}, type: 2}}");
            if (blocks[i].StartsWith("--- !u!23 "))
                blocks[i] = Regex.Replace(blocks[i], "(  m_Materials:\\s*\\n)  - .*", $"$1  - {{fileID: {matId}, guid: {matGuid}, type: 2}}");
        }
        File.WriteAllText(path, string.Concat(blocks));
        AssetDatabase.ImportAsset(path);
    }

    private static void Preview(Mesh mesh, Material material, string path)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            preview.camera.transform.position = new Vector3(0, 7, -12);
            preview.camera.transform.LookAt(Vector3.zero);
            preview.cameraFieldOfView = 45;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.16f, .23f, .28f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
            preview.lights[1].intensity = .5f;
            preview.BeginPreview(new Rect(0, 0, 640, 400), GUIStyle.none);
            preview.camera.transform.position = new Vector3(0, 7, -12);
            preview.camera.transform.LookAt(Vector3.zero);
            preview.camera.nearClipPlane = .1f;
            preview.camera.farClipPlane = 100f;
            preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
            preview.Render(true);
            File.WriteAllText(path + ".txt", "camera=" + preview.camera.transform.position +
                " rotation=" + preview.camera.transform.eulerAngles + " fov=" + preview.camera.fieldOfView +
                " mesh=" + mesh.bounds + " vertices=" + mesh.vertexCount);
            RenderTexture result = (RenderTexture)preview.EndPreview();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = result;
            Texture2D image = new Texture2D(result.width, result.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, result.width, result.height), 0, 0); image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally { preview.Cleanup(); }
    }
}
