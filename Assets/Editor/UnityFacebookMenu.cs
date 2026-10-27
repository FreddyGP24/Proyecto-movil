using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnityFacebookMenu
{
    [MenuItem("Unity Facebook/Abrir aplicación")]
    private static void AbrirAplicacion()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
    }

    [MenuItem("Unity Facebook/Editar integrantes y fotografías")]
    private static void EditarContenido()
    {
        Selection.activeObject = Resources.Load<AppContent>("Nexo/AppContent");
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    [MenuItem("Unity Facebook/Editar diseño")]
    private static void EditarDiseno()
    {
        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/Nexo/App.uxml"));
    }
}
