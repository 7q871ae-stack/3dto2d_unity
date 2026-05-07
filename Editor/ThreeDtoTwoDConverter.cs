/*
 * © 2026 Shapemaster. All rights reserved.
 * 3D to 2D Animation Converter - Unity Editor Extension
 */

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;

public class ThreeDtoTwoDConverter : EditorWindow
{
    private GameObject selectedModel;
    private string modelPath;
    private List<AnimationClip> availableClips = new List<AnimationClip>();
    private int selectedClipIndex = 0;
    
    private int captureFPS = 30; // Sabit akıcı hız
    private int resolution = 512;
    private float cameraDistance = 5f;
    private float cameraSize = 2f;
    private Vector3 cameraOffset = new Vector3(0, 1, 0);
    private float modelRotation = 0f;
    private string exportName = "";
    
    private PreviewRenderUtility previewUtility;
    private float previewTime = 0f;
    private bool isPreviewPlaying = false;
    private double lastUpdateTime;
    private Vector2 scrollPos;
    
    private GameObject previewInstance;
    private Material fallbackMaterial;
    private Dictionary<Material, Material> materialCache = new Dictionary<Material, Material>();
    private List<Texture2D> availableTextures = new List<Texture2D>();

    [MenuItem("Tools/3D to 2D Converter")]
    public static void ShowWindow()
    {
        var window = GetWindow<ThreeDtoTwoDConverter>("3D to 2D");
        window.minSize = new Vector2(400, 600);
    }

    private void OnEnable()
    {
        lastUpdateTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;

        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Standard");
        
        fallbackMaterial = new Material(s);
        if (fallbackMaterial.HasProperty("_Color")) fallbackMaterial.color = Color.gray;
        if (fallbackMaterial.HasProperty("_BaseColor")) fallbackMaterial.SetColor("_BaseColor", Color.gray);
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdatePreview;
        if (previewUtility != null) 
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }
        previewInstance = null;
        ClearMaterialCache();
    }

    private void ClearMaterialCache()
    {
        foreach (var mat in materialCache.Values) if (mat != null) DestroyImmediate(mat);
        materialCache.Clear();
    }

    private void UpdatePreview()
    {
        double delta = EditorApplication.timeSinceStartup - lastUpdateTime;
        lastUpdateTime = EditorApplication.timeSinceStartup;

        if (isPreviewPlaying && availableClips.Count > 0)
        {
            AnimationClip clip = availableClips[selectedClipIndex];
            previewTime += (float)delta;
            if (previewTime > clip.length) previewTime %= clip.length;
            Repaint();
        }
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        GUILayout.Label("3D to 2D Converter (v2 AutoFrame)", EditorStyles.boldLabel);

        if (GUILayout.Button("Browse FBX", GUILayout.Height(30))) BrowseFBX();

        EditorGUI.BeginChangeCheck();
        selectedModel = (GameObject)EditorGUILayout.ObjectField("3D Model (FBX)", selectedModel, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && selectedModel != null)
        {
            modelPath = AssetDatabase.GetAssetPath(selectedModel);
            LoadModelData();
        }

        if (selectedModel != null)
        {
            DrawModelControls();
            
            EditorGUILayout.Space();
            Rect previewRect = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(true));
            DrawPreview(previewRect);
            
            EditorGUILayout.Space();
            if (GUILayout.Button("CAPTURE ANIMATION", GUILayout.Height(50))) CaptureAndAnimate();
            if (GUILayout.Button("Create Ready Object in Scene", GUILayout.Height(30))) CreateSceneObject();
        }
        else
        {
            EditorGUILayout.HelpBox("Select an FBX model to preview.", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawModelControls()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        exportName = EditorGUILayout.TextField("Export Name", exportName);
        if (string.IsNullOrEmpty(exportName) && selectedModel != null) exportName = selectedModel.name;
        
        if (availableClips.Count > 0)
        {
            string[] clipNames = availableClips.ConvertAll(c => c.name).ToArray();
            int newClipIndex = EditorGUILayout.Popup("Animation", selectedClipIndex, clipNames);
            if (newClipIndex != selectedClipIndex)
            {
                selectedClipIndex = newClipIndex;
                AutoFrameCamera();
            }
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPreviewPlaying ? "Pause" : "Play")) isPreviewPlaying = !isPreviewPlaying;
            previewTime = EditorGUILayout.Slider("Time", previewTime, 0, availableClips[selectedClipIndex].length);
            EditorGUILayout.EndHorizontal();
            
            captureFPS = EditorGUILayout.IntSlider("Capture FPS", captureFPS, 1, 60);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        resolution = EditorGUILayout.IntPopup("Resolution", resolution, new string[] { "256", "512", "1024", "2048" }, new int[] { 256, 512, 1024, 2048 });
        
        cameraDistance = EditorGUILayout.Slider("Camera Distance", cameraDistance, 1f, 10f);
        cameraSize = EditorGUILayout.Slider("Camera Size (Zoom)", cameraSize, 0.5f, 15f);
        cameraOffset = EditorGUILayout.Vector3Field("Cam Offset", cameraOffset);
        modelRotation = EditorGUILayout.Slider("Model Rotation", modelRotation, 0, 360);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Center Camera")) CenterCamera();
        if (GUILayout.Button("Auto Frame (Fix Cutoff)")) AutoFrameCamera();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawPreview(Rect rect)
    {
        if (previewUtility == null)
        {
            previewUtility = new PreviewRenderUtility();
            previewUtility.camera.fieldOfView = 30f;
            
            if (selectedModel != null)
            {
                previewInstance = Instantiate(selectedModel);
                FixMaterials(previewInstance);
                previewUtility.AddSingleGO(previewInstance); // Properly adds it to the internal preview scene!
                AutoFrameCamera();
            }
        }

        if (previewInstance == null) return;

        // Animate
        if (availableClips.Count > 0)
        {
            availableClips[selectedClipIndex].SampleAnimation(previewInstance, previewTime);
        }

        previewUtility.BeginPreview(rect, GUIStyle.none);
        
        previewUtility.camera.orthographic = true;
        previewUtility.camera.orthographicSize = cameraSize;
        previewUtility.camera.backgroundColor = new Color(0,0,0,0);
        previewUtility.camera.nearClipPlane = -100f;
        previewUtility.camera.farClipPlane = 100f;
        
        // Bypass Post-Processing completely for URP preview!
        previewUtility.camera.clearFlags = CameraClearFlags.Depth;
        
        previewUtility.lights[0].intensity = 1.4f;
        previewUtility.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
        previewUtility.lights[1].intensity = 1.4f;

        previewInstance.transform.rotation = Quaternion.Euler(0, modelRotation, 0);
        previewInstance.transform.position = Vector3.zero;

        previewUtility.camera.transform.position = Vector3.forward * -cameraDistance + cameraOffset;
        previewUtility.camera.transform.LookAt(cameraOffset);
        
        // Native render handles all SkinnedMeshRenderers automatically!
        previewUtility.camera.Render();

        Texture result = previewUtility.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
    }

    private void CenterCamera()
    {
        if (previewInstance != null)
        {
            Renderer[] renderers = previewInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                cameraOffset = bounds.center;
            }
        }
    }

    private void AutoFrameCamera()
    {
        if (previewInstance == null || availableClips.Count == 0) return;

        AnimationClip clip = availableClips[selectedClipIndex];
        Bounds maxBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool initialized = false;

        int sampleCount = 20;
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = (clip.length / sampleCount) * i;
            clip.SampleAnimation(previewInstance, t);
            
            previewInstance.transform.rotation = Quaternion.Euler(0, modelRotation, 0);
            previewInstance.transform.position = Vector3.zero;

            foreach (var smr in previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                foreach (Vector3 v in bakedMesh.vertices)
                {
                    Vector3 worldPt = smr.transform.TransformPoint(v);
                    if (!initialized) { maxBounds = new Bounds(worldPt, Vector3.zero); initialized = true; }
                    else maxBounds.Encapsulate(worldPt);
                }
                DestroyImmediate(bakedMesh);
            }

            foreach (var mf in previewInstance.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 worldPt = mf.transform.TransformPoint(v);
                    if (!initialized) { maxBounds = new Bounds(worldPt, Vector3.zero); initialized = true; }
                    else maxBounds.Encapsulate(worldPt);
                }
            }
        }

        if (initialized)
        {
            cameraOffset = maxBounds.center;
            float maxDim = Mathf.Max(maxBounds.size.x, maxBounds.size.y);
            
            // Apply a robust 35% padding margin (0.5 * 1.35 = ~0.675)
            cameraSize = maxDim * 0.675f; 
            
            // Extend camera distance to ensure no Z-clipping (near/far plane)
            cameraDistance = Mathf.Max(5f, maxBounds.size.z * 3f);
            
            clip.SampleAnimation(previewInstance, previewTime);
            previewInstance.transform.rotation = Quaternion.Euler(0, modelRotation, 0);
            previewInstance.transform.position = Vector3.zero;
        }
    }

    private void FixMaterials(GameObject go)
    {
        ClearMaterialCache();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            Material[] sharedMats = r.sharedMaterials;
            for (int i = 0; i < sharedMats.Length; i++)
            {
                sharedMats[i] = GetConvertedMaterial(sharedMats[i]);
            }
            r.sharedMaterials = sharedMats;
        }
    }

    private Material GetConvertedMaterial(Material original)
    {
        if (original == null) return fallbackMaterial;
        if (materialCache.ContainsKey(original)) return materialCache[original];
        
        Texture tex = null;
        if (original.HasProperty("_BaseMap")) tex = original.GetTexture("_BaseMap");
        if (tex == null && original.HasProperty("_MainTex")) tex = original.GetTexture("_MainTex");
        if (tex == null && original.HasProperty("_Albedo")) tex = original.GetTexture("_Albedo");
        
        // Intelligent Material-to-Texture matching if Unity failed to link them
        if (tex == null && availableTextures.Count > 0)
        {
            tex = FindBestTextureForMaterialName(original.name);
        }

        // Dynamic Pipeline Shader Selection
        Shader shaderToUse = null;
        bool isURP = Shader.Find("Universal Render Pipeline/Unlit") != null;

        if (isURP)
        {
            shaderToUse = Shader.Find("Universal Render Pipeline/Unlit");
        }
        else
        {
            shaderToUse = tex != null ? Shader.Find("Unlit/Texture") : Shader.Find("Unlit/Color");
        }

        if (shaderToUse == null) shaderToUse = Shader.Find("Standard");

        Material newMat = new Material(shaderToUse);
        newMat.CopyPropertiesFromMaterial(original);

        if (tex != null)
        {
            if (newMat.HasProperty("_BaseMap")) newMat.SetTexture("_BaseMap", tex);
            if (newMat.HasProperty("_MainTex")) newMat.SetTexture("_MainTex", tex);
            
            // Reset color to white so texture shows fully
            if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", Color.white);
            if (newMat.HasProperty("_Color")) newMat.SetColor("_Color", Color.white);
        }
        else
        {
            // If NO texture exists anywhere, make it grey instead of blinding white
            if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", Color.gray);
            if (newMat.HasProperty("_Color")) newMat.SetColor("_Color", Color.gray);
        }
            
        materialCache[original] = newMat;
        return newMat;
    }

    private Texture2D FindBestTextureForMaterialName(string matNameOriginal)
    {
        string matName = matNameOriginal.ToLower().Replace(" (instance)", "").Replace(" mat", "");
        Texture2D bestMatch = null;
        int highestScore = -1;

        foreach (var t in availableTextures)
        {
            if (t == null) continue;
            string tName = t.name.ToLower();
            
            // Skip normal and mask maps
            if (tName.Contains("normal") || tName.Contains("nrm") || tName.Contains("mask") || tName.Contains("specular")) continue;

            int score = 0;
            
            // Direct name match
            if (tName.Contains(matName) || matName.Contains(tName)) score += 10;
            
            // Prefix/Word match (e.g. "ch29_body" and "ch29_diffuse" both share "ch29")
            string[] matParts = matName.Split('_', ' ', '-');
            foreach (string part in matParts) 
            {
                if (part.Length > 2 && tName.Contains(part)) 
                {
                    score += 3;
                }
            }

            // Keyword match for standard character parts
            if (matName.Contains("body") && tName.Contains("body")) score += 5;
            if (matName.Contains("head") && tName.Contains("head")) score += 5;
            if (matName.Contains("shirt") && tName.Contains("shirt")) score += 5;
            if (matName.Contains("pant") && tName.Contains("pant")) score += 5;
            if (matName.Contains("shoe") && tName.Contains("shoe")) score += 5;
            if (matName.Contains("hair") && tName.Contains("hair")) score += 5;

            // Fallback score for generic color maps (in case of single-texture models)
            if (score == 0 && (tName.Contains("albedo") || tName.Contains("diffuse") || tName.Contains("color") || tName.Contains("base"))) score += 1;

            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = t;
            }
        }
        return bestMatch;
    }

    private void AutoAssignTexturesToMaterials()
    {
        if (selectedModel == null) return;
        
        Renderer[] renderers = selectedModel.GetComponentsInChildren<Renderer>(true);
        HashSet<Material> mats = new HashSet<Material>();
        foreach (var r in renderers) 
        {
            foreach (var m in r.sharedMaterials) 
            {
                if (m != null) mats.Add(m);
            }
        }
        
        bool assetModified = false;

        foreach (Material mat in mats) 
        {
            string matPath = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrEmpty(matPath) || !matPath.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) continue;

            Texture currentTex = null;
            if (mat.HasProperty("_BaseMap")) currentTex = mat.GetTexture("_BaseMap");
            if (currentTex == null && mat.HasProperty("_MainTex")) currentTex = mat.GetTexture("_MainTex");
            if (currentTex == null && mat.HasProperty("_Albedo")) currentTex = mat.GetTexture("_Albedo");

            if (currentTex == null) 
            {
                Texture2D bestTex = FindBestTextureForMaterialName(mat.name);
                if (bestTex != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", bestTex);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", bestTex);
                    
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                    
                    EditorUtility.SetDirty(mat);
                    assetModified = true;
                }
            }
        }

        if (assetModified) 
        {
            AssetDatabase.SaveAssets();
        }
    }

    private void LoadModelData()
    {
        if (selectedModel == null) return;
        
        // Force recreation of the preview utility so it grabs the new selected model
        if (previewUtility != null) 
        {
            previewUtility.Cleanup();
            previewUtility = null;
        }
        previewInstance = null;

        availableClips.Clear();
        ClearMaterialCache();
        availableTextures.Clear();
        
        string path = AssetDatabase.GetAssetPath(selectedModel);
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var asset in assets)
        {
            if (asset is AnimationClip && !asset.name.Contains("__preview__")) availableClips.Add((AnimationClip)asset);
            if (asset is Texture2D && !availableTextures.Contains((Texture2D)asset)) availableTextures.Add((Texture2D)asset);
        }

        // Search for external textures in the same folder
        string folder = System.IO.Path.GetDirectoryName(path);
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        
        foreach(string guid in guids) 
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null && !availableTextures.Contains(tex)) availableTextures.Add(tex);
        }

        AutoAssignTexturesToMaterials();

        if (previewInstance != null) DestroyImmediate(previewInstance);
        Repaint();
    }

    private void BrowseFBX()
    {
        string path = EditorUtility.OpenFilePanel("Select FBX", "", "fbx");
        if (string.IsNullOrEmpty(path)) return;

        string destPath = path;

        // Dışarıdan geliyorsa Assets/3dmodel içine kopyala
        if (!path.StartsWith(Application.dataPath))
        {
            string targetDir = "Assets/3dmodel";
            if (!AssetDatabase.IsValidFolder(targetDir))
            {
                AssetDatabase.CreateFolder("Assets", "3dmodel");
            }
            
            string fileName = Path.GetFileName(path);
            destPath = targetDir + "/" + fileName;
            
            File.Copy(path, destPath, true);
            // AssetDatabase.Refresh() yerine doğrudan import ederek kilitlenmesini engelliyoruz
            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
        }
        else
        {
            // Eğer Unity projesi içinden seçildiyse path'i Assets ile başlatacak şekilde düzelt
            destPath = "Assets" + path.Substring(Application.dataPath.Length);
        }
        
        // FBX Materyal ayarlarını akıllı olarak belirle (Eğer mat varsa Remap, yoksa External)
        ModelImporter importer = AssetImporter.GetAtPath(destPath) as ModelImporter;
        if (importer != null)
        {
            string[] matGuids = AssetDatabase.FindAssets("t:Material");
            Dictionary<string, Material> availableProjectMats = new Dictionary<string, Material>();
            
            foreach (string guid in matGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                // FBX'in kendi içindeki materyalleri dışarıdaki materyal gibi algılamamak için:
                if (p != destPath)
                {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                    if (m != null && !availableProjectMats.ContainsKey(m.name))
                    {
                        availableProjectMats[m.name] = m;
                    }
                }
            }

            // ÖNEMLİ: FBX'in hangi materyal isimlerine ihtiyaç duyduğunu okuyabilmek için
            // geçici olarak "InPrefab" yapıp import etmeliyiz. Aksi takdirde (daha önce External yapıldıysa)
            // LoadAllAssetsAtPath bize hiçbir materyal döndürmez.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(destPath);
            bool foundAnyMatch = false;

            foreach (var asset in importedAssets)
            {
                if (asset is Material embeddedMat)
                {
                    if (availableProjectMats.ContainsKey(embeddedMat.name))
                    {
                        foundAnyMatch = true;
                        break;
                    }
                }
            }

            if (foundAnyMatch)
            {
                // Eşleşen materyal bulundu. Zaten şu an InPrefab'dayız, sadece Remap ekleyelim:
                bool needReimport = false;
                foreach (var asset in importedAssets)
                {
                    if (asset is Material embeddedMat)
                    {
                        if (availableProjectMats.TryGetValue(embeddedMat.name, out Material projectMat))
                        {
                            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embeddedMat.name), projectMat);
                            needReimport = true;
                        }
                    }
                }

                if (needReimport)
                {
                    importer.SaveAndReimport();
                }
            }
            else
            {
                // Eşleşen materyal YOK (Yeni model): O zaman External Materials (Legacy) ayarlarına geri al!
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.materialLocation = ModelImporterMaterialLocation.External;
                importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
                importer.SaveAndReimport();
            }
        }
        
        selectedModel = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
        modelPath = destPath;
        LoadModelData();
    }


    private void CaptureAndAnimate()
    {
        if (selectedModel == null || availableClips.Count == 0) return;
        string finalName = exportName;
        string exportDir = "Assets/3dto2d/Exports/" + finalName;
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        List<Sprite> sprites = new List<Sprite>();
        AnimationClip clip = availableClips[selectedClipIndex];
        
        // Automaticaly calculate how many frames to capture for 30 FPS
        int calculatedFrameCount = Mathf.RoundToInt(clip.length * captureFPS);
        if (calculatedFrameCount <= 0) calculatedFrameCount = 1;

        if (previewUtility == null) return;
        bool prevOrtho = previewUtility.camera.orthographic;
        float prevSize = previewUtility.camera.orthographicSize;
        Color prevColor = previewUtility.camera.backgroundColor;
        CameraClearFlags prevFlags = previewUtility.camera.clearFlags;

        previewUtility.camera.orthographic = true;
        previewUtility.camera.orthographicSize = cameraSize;
        previewUtility.camera.backgroundColor = new Color(0,0,0,0);
        
        // Force Depth clear only, so GL.Clear controls the color buffer!
        previewUtility.camera.clearFlags = CameraClearFlags.Depth; 
        
        // Ensure near/far planes are vastly wide to prevent Z-clipping of limbs
        previewUtility.camera.nearClipPlane = -100f;
        previewUtility.camera.farClipPlane = 100f;
        previewUtility.camera.aspect = 1f; // Force square aspect ratio!
        
        // Disable URP Post-Processing on this specific camera using Reflection
        Component urpCamData = previewUtility.camera.GetComponent("UniversalAdditionalCameraData");
        if (urpCamData != null)
        {
            var prop = urpCamData.GetType().GetProperty("renderPostProcessing");
            if (prop != null) prop.SetValue(urpCamData, false);
        }

        previewUtility.lights[0].intensity = 1.4f;
        previewUtility.lights[1].intensity = 1.4f;

        for (int i = 0; i < calculatedFrameCount; i++)
        {
            float t = (clip.length / calculatedFrameCount) * i;
            clip.SampleAnimation(previewInstance, t);
            
            previewInstance.transform.rotation = Quaternion.Euler(0, modelRotation, 0);
            previewInstance.transform.position = Vector3.zero;

            previewUtility.camera.transform.position = Vector3.forward * -cameraDistance + cameraOffset;
            previewUtility.camera.transform.LookAt(cameraOffset);
            
            // Direct rendering to bypass GUI clipping bounds!
            RenderTexture rt = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = previewUtility.camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            
            previewUtility.camera.targetTexture = rt;
            RenderTexture.active = rt;
            
            GL.Clear(true, true, new Color(0,0,0,0));
            previewUtility.camera.Render();

            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();

            previewUtility.camera.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            string filePath = exportDir + "/" + finalName + "_" + i + ".png";
            File.WriteAllBytes(filePath, tex.EncodeToPNG());
            
            // This ImportAsset call is CRITICAL! It pumps the Unity Editor event queue, 
            // forcing SkinnedMeshRenderers to update their bone matrices for the next frame.
            // Without it, the model's limbs freeze because Unity optimizes single-frame renders.
            AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceUpdate);
            
            TextureImporter importer = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                
                TextureImporterSettings texSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(texSettings);
                
                // Force strict alignment and FullRect to prevent shaking and clipping!
                texSettings.spriteMeshType = SpriteMeshType.FullRect;
                texSettings.spriteAlignment = (int)SpriteAlignment.Center;
                texSettings.spritePivot = new Vector2(0.5f, 0.5f);
                
                importer.SetTextureSettings(texSettings);
                importer.SaveAndReimport();
            }
            
            Sprite loadedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(filePath);
            if (loadedSprite != null) sprites.Add(loadedSprite);
            
            DestroyImmediate(tex);
        }

        CreateAnimationClip(sprites, finalName);
        
        previewUtility.camera.orthographic = prevOrtho;
        previewUtility.camera.orthographicSize = prevSize;
        previewUtility.camera.backgroundColor = prevColor;
        previewUtility.camera.clearFlags = prevFlags;
        
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", "Captured Successfully! Your sprite is perfectly centered and padded.", "OK");
    }

    private void CreateAnimationClip(List<Sprite> sprites, string name)
    {
        AnimationClip clip = new AnimationClip();
        clip.frameRate = captureFPS;
        
        // Auto-enable Loop Time so it animates continuously in the scene
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorCurveBinding binding = new EditorCurveBinding { type = typeof(SpriteRenderer), propertyName = "m_Sprite", path = "" };
        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[sprites.Count + 1];
        for (int i = 0; i < sprites.Count; i++) keys[i] = new ObjectReferenceKeyframe { time = i / (float)captureFPS, value = sprites[i] };
        keys[sprites.Count] = new ObjectReferenceKeyframe { time = sprites.Count / (float)captureFPS, value = sprites[0] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        AssetDatabase.CreateAsset(clip, "Assets/3dto2d/Exports/" + name + "/" + name + "_Anim.anim");
        
        // Controller oluştur ve animasyonu içine bağla
        string controllerPath = "Assets/3dto2d/Exports/" + name + "/" + name + "_Controller.controller";
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddMotion(clip);
    }

    private void CreateSceneObject()
    {
        if (string.IsNullOrEmpty(exportName)) return;
        
        GameObject go = new GameObject(exportName + "_Sprite");
        var sr = go.AddComponent<SpriteRenderer>();
        var anim = go.AddComponent<Animator>();
        
        string exportDir = "Assets/3dto2d/Exports/" + exportName;
        string spritePath = exportDir + "/" + exportName + "_0.png";
        string animPath = exportDir + "/" + exportName + "_Anim.anim";
        string ctrlPath = exportDir + "/" + exportName + "_Controller.controller";
        
        Sprite firstSprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (firstSprite != null) sr.sprite = firstSprite;
        
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
        
        UnityEditor.Animations.AnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ctrlPath);
        if (controller == null)
        {
            controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            if (clip != null) controller.AddMotion(clip);
        }
        
        if (controller != null) anim.runtimeAnimatorController = controller;
        
        Selection.activeGameObject = go;
    }
}
