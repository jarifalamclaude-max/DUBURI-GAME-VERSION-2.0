// COMPILE-CHECK STUB (never shipped): UnityEditor members used by LifeAsDuburi/Editor.
// Signatures copied from UnityCsReference 6000.3.27f2; '// file:line' = where the member is declared.
using System;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    // Editor/Mono/MenuItem.cs:17
    public sealed partial class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }                                              // Editor/Mono/MenuItem.cs:22
        public MenuItem(string itemName, bool isValidateFunction) { }                     // Editor/Mono/MenuItem.cs:25
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }       // Editor/Mono/MenuItem.cs:31
    }

    // Editor/Mono/EditorUtility.cs:48 (+ Editor/Mono/EditorUtility.bindings.cs:22)
    public partial class EditorUtility
    {
        public static void RevealInFinder(string path) { }                                                 // Editor/Mono/EditorUtility.bindings.cs:35
        public static void SetDirty(Object target) { }                                                     // Editor/Mono/EditorUtility.bindings.cs:81
        public static void DisplayProgressBar(string title, string info, float progress) { }               // Editor/Mono/EditorUtility.bindings.cs:144
        public static void ClearProgressBar() { }                                                          // Editor/Mono/EditorUtility.bindings.cs:149
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => false; // Editor/Mono/EditorUtility.cs:201
        public static bool DisplayDialog(string title, string message, string ok) => false;                // Editor/Mono/EditorUtility.cs:206
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 0; // Editor/Mono/EditorUtility.cs:212
    }

    // Editor/Mono/EditorApplication.bindings.cs:27
    public sealed partial class EditorApplication
    {
        public static bool isPlaying { get; set; }                         // Editor/Mono/EditorApplication.bindings.cs:82
        public static bool isPlayingOrWillChangePlaymode => false;         // Editor/Mono/EditorApplication.bindings.cs:100
        public static bool isCompiling => false;                           // Editor/Mono/EditorApplication.bindings.cs:124
    }

    // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:125
    public sealed partial class AssetDatabase
    {
        public static string CreateFolder(string parentFolder, string newFolderName) => null;  // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:155
        public static void ImportAsset(string path) { }                                         // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:307
        public static bool IsValidFolder(string path) => false;                                 // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:325
        public static void CreateAsset(Object asset, string path) { }                           // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:331
        public static void AddObjectToAsset(Object objectToAdd, Object assetObject) { }         // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:341
        public static string GetAssetPath(Object assetObject) => null;                          // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:353
        public static Object LoadAssetAtPath(string assetPath, Type type) => null;              // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:383
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => null;          // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:385
        public static void Refresh() { }                                                        // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:463
        public static string GUIDToAssetPath(string guid) => null;                              // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:525
        public static void SaveAssets() { }                                                     // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:577
        public static void SaveAssetIfDirty(Object obj) { }                                     // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:584
        public static string[] FindAssets(string filter, string[] searchInFolders) => null;     // Editor/Mono/AssetDatabase/AssetDatabaseSearching.cs:38
        public static bool TryGetGUIDAndLocalFileIdentifier(Object obj, out string guid, out long localId) { guid = null; localId = 0; return false; } // Modules/AssetDatabase/Editor/ScriptBindings/AssetDatabase.bindings.cs:879
    }

    // Editor/Mono/SerializedObject.bindings.cs:18
    public partial class SerializedObject : IDisposable
    {
        public SerializedObject(Object obj) { }                                   // Editor/Mono/SerializedObject.bindings.cs:24
        public void Dispose() { }                                                 // Editor/Mono/SerializedObject.bindings.cs:53
        public SerializedProperty GetIterator() => null;                          // Editor/Mono/SerializedObject.bindings.cs:66
        public SerializedProperty FindProperty(string propertyPath) => null;      // Editor/Mono/SerializedObject.bindings.cs:76
        public bool ApplyModifiedProperties() => false;                           // Editor/Mono/SerializedObject.bindings.cs:120
        public void Update() { }                                                  // Editor/Mono/SerializedObject.bindings.cs:129
        public bool ApplyModifiedPropertiesWithoutUndo() => false;                // Editor/Mono/SerializedObject.bindings.cs:178
    }

    // Editor/Mono/SerializedProperty.bindings.cs:118
    public partial class SerializedProperty : IDisposable
    {
        public SerializedProperty Copy() => null;                                 // Editor/Mono/SerializedProperty.bindings.cs:230
        public SerializedProperty FindPropertyRelative(string relativePropertyPath) => null; // Editor/Mono/SerializedProperty.bindings.cs:240
        public SerializedProperty GetArrayElementAtIndex(int index) => null;      // Editor/Mono/SerializedProperty.bindings.cs:270
        public void Dispose() { }                                                 // Editor/Mono/SerializedProperty.bindings.cs:531
        public string name => null;                                               // Editor/Mono/SerializedProperty.bindings.cs:634
        public string propertyPath => null;                                       // Editor/Mono/SerializedProperty.bindings.cs:709
        public int intValue { get; set; }                                         // Editor/Mono/SerializedProperty.bindings.cs:1010
        public long longValue { get; set; }                                       // Editor/Mono/SerializedProperty.bindings.cs:1031
        public bool boolValue { get; set; }                                       // Editor/Mono/SerializedProperty.bindings.cs:1074
        public float floatValue { get; set; }                                     // Editor/Mono/SerializedProperty.bindings.cs:1095
        public Object objectReferenceValue { get; set; }                          // Editor/Mono/SerializedProperty.bindings.cs:1246
        public bool Next(bool enterChildren) => false;                            // Editor/Mono/SerializedProperty.bindings.cs:1817
        public int arraySize { get; set; }                                        // Editor/Mono/SerializedProperty.bindings.cs:1923
        public void InsertArrayElementAtIndex(int index) { }                      // Editor/Mono/SerializedProperty.bindings.cs:1962
    }

    // Editor/Mono/AssetPipeline/AssetImporter.bindings.cs:21
    public partial class AssetImporter : Object
    {
        public string assetPath => null;                                          // Editor/Mono/AssetPipeline/AssetImporter.bindings.cs:63
        public static AssetImporter GetAtPath(string path) => null;               // Editor/Mono/AssetPipeline/AssetImporter.bindings.cs:115
        public void SaveAndReimport() { }                                         // Editor/Mono/AssetPipeline/AssetImporter.bindings.cs:117
    }

    // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:22
    public sealed partial class TextureImporter : AssetImporter
    {
        public int maxTextureSize { get; set; }                                   // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:42
        public TextureImporterNPOTScale npotScale { get; set; }                   // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:235
        public bool isReadable { get; set; }                                      // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:238
        public bool mipmapEnabled { get; set; }                                   // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:257
        public bool sRGBTexture { get; set; }                                     // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:262
        public UnityEngine.TextureWrapMode wrapMode { get; set; }                 // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:347
        public TextureImporterType textureType { get; set; }                      // Editor/Mono/AssetPipeline/TextureImporter.bindings.cs:440
    }

    // Editor/Mono/AssetPipeline/TextureImporterEnums.cs:284
    public enum TextureImporterNPOTScale { None = 0, ToNearest = 1, ToLarger = 2, ToSmaller = 3 }

    // Editor/Mono/AssetPipeline/TextureImporterEnums.cs:326
    public enum TextureImporterType { Default = 0, NormalMap = 1, GUI = 2 }
}
