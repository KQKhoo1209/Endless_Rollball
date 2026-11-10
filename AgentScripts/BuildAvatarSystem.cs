using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public static class BuildAvatarSystem
{
    private const string Folder = "Assets/EndlessRollballAssets/Avatar/Presentation";
    private static TMP_Text textStyle;
    private static UnityEngine.UI.Button buttonStyle;
    private static GameObject[] prefabs;
    private static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("EndlessRollball.Avatars." + name)).FirstOrDefault(t => t != null)
        ?? throw new InvalidOperationException("Waiting for Unity to import avatar source: " + name);
    private static Component Add(GameObject go, string type) => go.AddComponent(Type(type));
    private static void Set(Component owner, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(owner); var p = so.FindProperty(field);
        if (p == null) throw new InvalidOperationException("Missing authored field: " + field);
        p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static Material Mat(string name, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
        if (existing != null) return existing;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        AssetDatabase.CreateAsset(m, Folder + "/" + name + ".mat"); return m;
    }
    private static GameObject Shape(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(primitive); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
    private static Material Variant(Material original, string avatar, int index)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + avatar + "Variant" + index + ".mat");
        if (existing != null) return existing;
        var m = new Material(original) { name = avatar + "Variant" + index };
        string property = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        var source = m.GetTexture(property);
        if (source != null)
        {
            const int size = 512;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, rt); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
                var pixels = tex.GetPixels();
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int i = y * size + x; var c = pixels[i];
                    if (avatar == "whale")
                    {
                        float wave = Mathf.Sin(x / (float)size * Mathf.PI * 6f) * .045f;
                        float stripe = Mathf.Repeat(y / (float)size + wave, .22f);
                        if (stripe < .025f) pixels[i] = Color.Lerp(c, new Color(1f, .43f, .38f), .75f);
                    }
                    else if (avatar == "phoenix")
                    {
                        Color.RGBToHSV(c, out float h, out float s, out float v);
                        pixels[i] = s > .18f ? Color.HSVToRGB(.57f + h * .08f, s, v) : c;
                    }
                    else pixels[i] = new Color(c.r * .32f, c.g * .36f, c.b * .48f, c.a);
                }
                tex.SetPixels(pixels); tex.Apply();
                string path = Folder + "/" + avatar + "Variant" + index + ".png";
                File.WriteAllBytes(path, tex.EncodeToPNG()); AssetDatabase.ImportAsset(path);
                m.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(tex); }
        }
        else m.color = avatar == "phoenix" ? new Color(.18f, .6f, 1) : avatar == "cow" ? new Color(.16f, .19f, .25f) : new Color(.4f, .65f, .8f);
        AssetDatabase.CreateAsset(m, Folder + "/" + m.name + ".mat"); return m;
    }
    public static string CreateAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Author avatars in Edit Mode.");
        if (new[] {"Cow", "Whale", "Phoenix"}.All(n => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + n + "Presentation.prefab") != null))
            return "Avatar presentation assets already exist; retained.";
        Directory.CreateDirectory(Folder);
        // Targeted import is for asset authoring, not a script refresh or compilation check.
        AssetDatabase.ImportAsset("Assets/EndlessRollballAssets/Avatar");
        var brown = Mat("CowboyBrown", new Color(.25f,.11f,.045f));
        var white = Mat("SailorWhite", new Color(.96f,.96f,.9f));
        var blue = Mat("SailorBlue", new Color(.035f,.12f,.3f));
        var gold = Mat("SunfireGold", new Color(1f,.63f,.08f));
        var ember = Mat("AuraEmber", new Color(1f,.27f,.025f));
        string[] ids = {"cow", "whale", "phoenix"};
        string[] sources = {"Cow/Cow.fbx", "Prefabs/Whale.prefab", "Prefabs/Phoenix.prefab"};
        for (int i = 0; i < 3; i++)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+(i==0?"Cow":i==1?"Whale":"Phoenix")+"Presentation.prefab") != null) continue;
            string id = ids[i]; var root = new GameObject(id + "Presentation");
            try
            {
                var normal = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Avatar/" + sources[i]), root.transform);
                normal.name = "DefaultModel"; normal.transform.localPosition = Vector3.zero;
                normal.transform.localRotation = Quaternion.Euler(0, id == "whale" ? -40 : 180, 0) * normal.transform.localRotation;
                foreach (var b in normal.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
                foreach (var c in normal.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var b in normal.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(b);
                foreach (var a in normal.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                var renderers = normal.GetComponentsInChildren<Renderer>(true); var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float size = 2f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                normal.transform.localPosition -= bounds.center;
                normal.transform.localPosition *= size; normal.transform.localScale *= size;
                var alternate = UnityEngine.Object.Instantiate(normal, root.transform); alternate.name = "AlternateModel";
                int mi = 0;
                foreach (var r in alternate.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("Eye") || r.name.Contains("Mouth") || r.name.Contains("Leg") || id == "cow" && (r.name == "tripo_part_3" || r.name == "tripo_part_6")) continue;
                    r.sharedMaterials = r.sharedMaterials.Select(m => Variant(m, id, mi++)).ToArray();
                }
                var head = normal.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => id == "cow" ? r.name == "tripo_part_2" : id == "phoenix" ? r.name == "Head" : r.name == "Body");
                var anchor = head != null ? head.bounds.center + Vector3.up * head.bounds.extents.y : new Vector3(0,.8f,0);
                var hat = new GameObject("Accessory"); hat.transform.SetParent(root.transform, false); hat.transform.localPosition = anchor;
                if (id == "cow")
                {
                    Shape(hat.transform,"Brim",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.65f,.022f,.48f),brown);
                    Shape(hat.transform,"Crown",PrimitiveType.Cylinder,new Vector3(0,.12f,0),new Vector3(.34f,.12f,.29f),brown);
                    Shape(hat.transform,"Band",PrimitiveType.Cylinder,new Vector3(0,.04f,0),new Vector3(.35f,.025f,.3f),gold);
                }
                else if (id == "whale")
                {
                    Shape(hat.transform,"Cap",PrimitiveType.Cylinder,new Vector3(0,.06f,0),new Vector3(.5f,.09f,.4f),white);
                    Shape(hat.transform,"Band",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.51f,.025f,.41f),blue);
                    Shape(hat.transform,"Badge",PrimitiveType.Sphere,new Vector3(0,.065f,-.205f),new Vector3(.1f,.1f,.025f),gold);
                }
                else
                {
                    hat.transform.localPosition += Vector3.up * .15f;
                    for(int j=0;j<8;j++)
                    {
                        float a=j*Mathf.PI/4f;
                        Shape(hat.transform,"SunfirePoint",PrimitiveType.Capsule,new Vector3(Mathf.Cos(a)*.27f,.08f,Mathf.Sin(a)*.27f),new Vector3(.07f,j%2==0?.19f:.12f,.07f),gold);
                    }
                }
                GameObject aura = null;
                if(id == "phoenix")
                {
                    aura = new GameObject("RebirthAura"); aura.transform.SetParent(root.transform,false);
                    for(int j=0;j<32;j++)
                    {
                        float a=j*Mathf.PI/16f;
                        Shape(aura.transform,"Ember",PrimitiveType.Sphere,new Vector3(Mathf.Cos(a)*.9f,-.8f,Mathf.Sin(a)*.9f),Vector3.one*(j%4==0?.08f:.04f),ember);
                    }
                    aura.SetActive(false);
                }
                var presentation = Add(root,"AvatarPresentation"); Set(presentation,"defaultModel",normal); Set(presentation,"appearanceVariant",alternate);
                Set(presentation,"accessory",hat); Set(presentation,"aura",aura);
                alternate.SetActive(false); hat.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+(i==0?"Cow":i==1?"Whale":"Phoenix")+"Presentation.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets(); return "Created three presentation prefabs, seven cosmetic options, and material/texture assets.";
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false); r.gameObject.layer=5;
        r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(width,height); return r;
    }
    private static void Place(RectTransform r,float x,float y,float width,float height)
    {r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);}
    private static TMP_Text Text(Transform parent,string name,string text,float x,float y,float w,float h,float size)
    {
        var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>(); t.font=textStyle.font;t.fontSharedMaterial=textStyle.fontSharedMaterial;
        t.fontSize=size;t.color=new Color(.94f,.97f,1);t.alignment=TextAlignmentOptions.MidlineLeft;t.text=text;t.raycastTarget=false;return t;
    }
    private static UnityEngine.UI.Button Button(Transform parent,string name,string text,float x,float y,float w,float h)
    {
        var r=Rect(parent,name,x,y,w,h);var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();
        var style=buttonStyle.GetComponent<UnityEngine.UI.Image>(); image.sprite=style.sprite;image.type=style.type;image.color=new Color(.12f,.31f,.43f);
        var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=image;b.colors=buttonStyle.colors;
        var audio = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("EndlessRollball.UI.UIButtonClickAudio")).FirstOrDefault(t => t != null);
        if(audio != null) Bind(b, r.gameObject.AddComponent(audio), "PlayClick");
        var t=Text(r,"Label",text,12,3,w-24,h-6,26); t.alignment=TextAlignmentOptions.Center;return b;
    }
    private static void Bind(UnityEngine.UI.Button button,Component component,string method)
    {
        UnityEventTools.AddPersistentListener(button.onClick,(UnityAction)Delegate.CreateDelegate(typeof(UnityAction),component,method));
    }
    private static Component Preview(Transform parent,string name,float x,float y,float size)
    {
        var go=Rect(parent,name,x,y,size,size).gameObject;
        var raw=go.AddComponent<UnityEngine.UI.RawImage>();raw.raycastTarget=false;
        var preview=Add(go,"AvatarPreview");Set(preview,"image",raw);Set(preview,"cow",prefabs[0]);Set(preview,"whale",prefabs[1]);Set(preview,"phoenix",prefabs[2]);return preview;
    }
    private static Transform Find(Scene scene,string name) => scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name);
    public static string WireMenu()
    {
        var scene=SceneManager.GetActiveScene();if(Application.isPlaying||scene.path!="Assets/Scenes/MainMenuScene.unity")throw new InvalidOperationException("Open MainMenuScene in Edit Mode.");
        var canvas=Find(scene,"Canvas");if(canvas.Find("AvatarSelectionPage")!=null)return "Avatar menu already authored; retained.";
        buttonStyle=Find(scene,"AvatarButton").GetComponent<UnityEngine.UI.Button>();textStyle=buttonStyle.GetComponentInChildren<TMP_Text>();LoadPrefabs();
        var controller=Add(canvas.gameObject,"AvatarMenuUI");
        var select=Page(canvas,"AvatarSelectionPage");var custom=Page(canvas,"AvatarCustomizationPage");
        Set(controller,"mainPage",Find(scene,"MainMenuPage").gameObject);Set(controller,"selectionPage",select.gameObject);Set(controller,"customizationPage",custom.gameObject);
        Text(select,"Heading","Choose Your Companion",70,45,1600,70,48);
        Set(controller,"selectionTitle",Text(select,"AvatarName","Charging Cow",85,135,700,65,38));
        Set(controller,"selectionBalance",Text(select,"Balance","Avatar Coins: 0",1100,45,700,70,30));
        Set(controller,"selectionPreview",Preview(select,"CharacterPreview",125,230,610));
        Set(controller,"selectionStatus",Text(select,"Status","Choose your result companion.",85,855,1720,78,26));
        var so=new SerializedObject(controller);var cards=so.FindProperty("cards");cards.arraySize=3;
        string[] ids={"cow","whale","phoenix"};
        for(int i=0;i<3;i++)
        {
            var b=Button(select,ids[i]+"Card",ids[i],900,215+i*180,880,140);var element=cards.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("avatarId").stringValue=ids[i];element.FindPropertyRelative("button").objectReferenceValue=b;element.FindPropertyRelative("label").objectReferenceValue=b.GetComponentInChildren<TMP_Text>();
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        var customize=Button(select,"CustomizeButton","Customize Selected Avatar",1100,945,680,80);Set(controller,"customizeButton",customize);Bind(customize,controller,"Customize");
        Bind(Button(select,"BackButton","Back",85,945,320,80),controller,"Close");
        Set(controller,"customizationTitle",Text(custom,"Heading","Customize",70,45,1400,70,45));
        Set(controller,"customizationBalance",Text(custom,"Balance","Avatar Coins: 0",1360,125,440,65,28));
        Set(controller,"customizationPreview",Preview(custom,"CharacterPreview",100,225,610));
        Bind(Button(custom,"PreviewReaction","Preview Reaction",200,850,420,70),controller,"PreviewReaction");
        Set(controller,"customizationStatus",Text(custom,"Status","",780,790,1010,130,25));
        var data=new SerializedObject(controller);var rows=data.FindProperty("rows");rows.arraySize=3;
        string[] slots={"appearance","accessory","effect"};
        for(int i=0;i<3;i++)
        {
            var row=Rect(custom,slots[i]+"Row",800,225+i*180,1010,160);
            Text(row,"Slot",slots[i].ToUpperInvariant(),0,0,1000,38,24);
            var d=Button(row,"Default","Default",0,48,430,100);var o=Button(row,"Option","Option",455,48,555,100);
            var e=rows.GetArrayElementAtIndex(i);e.FindPropertyRelative("slot").stringValue=slots[i];e.FindPropertyRelative("root").objectReferenceValue=row.gameObject;
            e.FindPropertyRelative("useDefault").objectReferenceValue=d;e.FindPropertyRelative("option").objectReferenceValue=o;
            e.FindPropertyRelative("defaultLabel").objectReferenceValue=d.GetComponentInChildren<TMP_Text>();e.FindPropertyRelative("optionLabel").objectReferenceValue=o.GetComponentInChildren<TMP_Text>();
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        Bind(Button(custom,"BackButton","Back to Selection",85,945,470,80),controller,"BackToSelection");
        var save=Button(custom,"SaveButton","Save Appearance",1260,945,520,80);Set(controller,"saveButton",save);Bind(save,controller,"Save");
        Bind(buttonStyle,controller,"Open");select.gameObject.SetActive(false);custom.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Saved functional Avatar Selection and Customization pages in MainMenuScene.";
    }
    private static RectTransform Page(Transform canvas,string name)
    {
        var r=Rect(canvas,name,0,0,1920,1080);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.035f,.095f,.15f,.995f);return r;
    }
    private static void LoadPrefabs()
    {
        prefabs=new[]{"Cow","Whale","Phoenix"}.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+n+"Presentation.prefab")).ToArray();
        if(prefabs.Any(p=>p==null))throw new InvalidOperationException("Create avatar presentation assets first.");
    }
    public static string WireResult()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Author results in Edit Mode.");
        var original=SceneManager.GetActiveScene();string path="Assets/Scenes/GameplayScene.unity";var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            var panel=Find(scene,"AvatarFeedbackPanel");if(panel.GetComponent(Type("SessionResultAvatarPresenter"))!=null)return "Result avatar already authored; retained.";
            buttonStyle=Find(scene,"ReturnButton").GetComponent<UnityEngine.UI.Button>();textStyle=buttonStyle.GetComponentInChildren<TMP_Text>();LoadPrefabs();
            var flow=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Single(c=>c!=null&&c.GetType().Name=="SessionFlowUI");
            var presenter=Add(panel.gameObject,"SessionResultAvatarPresenter");Set(presenter,"flow",flow);Set(flow,"avatarPresenter",presenter);
            // Reuse the existing empty avatar placeholder; preserve the authored metrics and result actions.
            var empty=panel.Find("AvatarAnimation");Place((RectTransform)empty,380,120,420,420);
            var preview=Preview(empty,"CharacterPreview",0,0,420);Set(presenter,"preview",preview);
            var name=panel.Find("Title").GetComponent<TMP_Text>();Place(name.rectTransform,50,15,1080,65);Set(presenter,"avatarName",name);
            Place((RectTransform)panel.Find("OutcomeText"),50,80,1080,40);
            Set(presenter,"feedback",Text(panel,"AvatarFeedback","Your companion's feedback appears here.",50,600,1080,125,27));
            Text(panel,"ControllerHint","You may remove the controller, or keep it on for another session.",50,740,1080,42,23);
            var status=Text(panel,"AvatarSaveStatus","",50,545,700,50,23);Set(presenter,"saveStatus",status);status.gameObject.SetActive(false);
            var retry=Button(panel,"RetryAvatarSave","Retry Save",790,545,330,50);Set(presenter,"retrySaveButton",retry);Bind(retry,presenter,"RetrySave");retry.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Saved avatar preview, supportive feedback, controller hint and reward-save retry on the existing result panel.";
        }
        finally {if(opened)EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
    }
}
