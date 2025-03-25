using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HapticAnnotationComponent))]
public class HapticAnnotationComponentEditor : Editor
{
    private SerializedProperty graphProperty;

    private void OnEnable()
    {
        graphProperty = serializedObject.FindProperty("graph");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(10);

        // Draw the graph field with an object field
        EditorGUILayout.PropertyField(graphProperty, new GUIContent("Haptic Annotation Graph"));

        EditorGUILayout.Space(5);

        // Create a horizontal layout for the buttons
        EditorGUILayout.BeginHorizontal();

        // Add a "New" button to create a new graph
        if (GUILayout.Button("New Graph", GUILayout.Height(20)))
        {
            CreateNewGraph();
        }

        // Add an "Edit" button to open the graph in the editor
        if (GUILayout.Button("Edit Graph", GUILayout.Height(20)))
        {
            OpenGraphEditor();
        }

        EditorGUILayout.EndHorizontal();

        // Display a help box if no graph is assigned
        HapticAnnotationComponent component = (HapticAnnotationComponent)target;
        if (component.Graph == null)
        {
            EditorGUILayout.HelpBox("Create a new graph or assign an existing one.", MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void CreateNewGraph()
    {
        // Create a save file dialog to choose where to save the new graph
        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Haptic Annotation Graph",
            "New Haptic Annotation Graph",
            "asset",
            "Choose a location to save the new graph"
        );

        if (!string.IsNullOrEmpty(path))
        {
            // Create a new graph asset
            HapticAnnotationGraph newGraph = ScriptableObject.CreateInstance<HapticAnnotationGraph>();

            // Save the asset to the selected path
            AssetDatabase.CreateAsset(newGraph, path);
            AssetDatabase.SaveAssets();

            // Assign the new graph to the component
            HapticAnnotationComponent component = (HapticAnnotationComponent)target;
            component.Graph = newGraph;

            // Mark the object as dirty to ensure the change is saved
            EditorUtility.SetDirty(target);

            // Open the graph editor
            OpenGraphEditor();
        }
    }

    private void OpenGraphEditor()
    {
        HapticAnnotationComponent component = (HapticAnnotationComponent)target;

        if (component.Graph != null)
        {
            // Open the Haptic Annotation Window
            HapticsAnnotationWindow.ShowWindow();

            // For now, we'll just open the window
            // In a future step, we'll add code to load the graph
            EditorUtility.DisplayDialog("Graph Editor",
                "The graph editor is open. Loading functionality will be added in the next step.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("No Graph Assigned",
                "Please create a new graph or assign an existing one first.", "OK");
        }
    }
}