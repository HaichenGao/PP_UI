using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using System;

public class HapticsAnnotationWindow : EditorWindow
{
    private VisualElement _rootVisual;
    private HapticsRelationshipGraphView _graphView;

    [MenuItem("HapticsAnnotationWindow/Open _%#T")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<HapticsAnnotationWindow>();
        wnd.titleContent = new GUIContent("Haptic Annotation");
        wnd.Show();
    }

    private void OnEnable()
    {
        // Load the UXML and USS
        var uxmlPath = "Assets/Editor/VRHapticEditor.uxml"; 
        var uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);

        if (uxmlAsset == null)
        {
            Debug.LogError("UXML file not found. Please verify the path.");
            return;
        }

        var ussPath = "Assets/Editor/VRHapticEditor.uss"; 
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

        // Instantiate the main layout
        _rootVisual = new VisualElement();
        VisualElement clone = uxmlAsset.Instantiate();
        _rootVisual.Add(clone);

        // Apply styling
        if (styleSheet != null)
        {
            _rootVisual.styleSheets.Add(styleSheet);
        }

        // Set the rootVisualElement to our layout
        rootVisualElement.Add(_rootVisual);

        // Grab UI elements
        var scanButton = _rootVisual.Q<Button>("scanSceneButton");
        var exportButton = _rootVisual.Q<Button>("exportDataButton");
        var graphContainer = _rootVisual.Q<VisualElement>("graphViewContainer");

        // Create a new graph view and add it to the container
        _graphView = new HapticsRelationshipGraphView
        {
            name = "HapticsGraphView"
        };
        _graphView.StretchToParentSize();
        graphContainer.Add(_graphView);

        // Hook up button events
        scanButton.clicked += OnScanSceneClicked;
        exportButton.clicked += OnExportClicked;

        _graphView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        _graphView.RegisterCallback<DragPerformEvent>(OnDragPerform);
    }

    private void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _graphView.UnregisterCallback<DragPerformEvent>(OnDragPerform);
        }
    }

    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        // We only set the visual mode if we detect at least one GameObject
        bool containsGameObject = false;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is GameObject)
            {
                containsGameObject = true;
                break;
            }
        }

        if (containsGameObject)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }

    private void OnDragPerform(DragPerformEvent evt)
    {
        DragAndDrop.AcceptDrag();

        // Convert mouse position to GraphView coordinates
        Vector2 mousePosition = _graphView.ChangeCoordinatesTo(
            _graphView.contentViewContainer, evt.localMousePosition);

        foreach (var obj in DragAndDrop.objectReferences)
        {
            GameObject go = obj as GameObject;
            if (go != null)
            {
                // Create a node at the drop position
                _graphView.AddGameObjectNode(go, mousePosition);
                // Offset subsequent nodes slightly to avoid overlap
                mousePosition += new Vector2(30, 30);
            }
        }
    }

    private void OnScanSceneClicked()
    {
        // Example scanning of scene objects
        GameObject[] sceneObjects = GameObject.FindObjectsOfType<GameObject>();

        // Clear current graph
        _graphView.ClearGraph();

        // Add nodes in a basic layout
        foreach (var go in sceneObjects)
        {
            if (!go.activeInHierarchy) continue;
            // You could filter further, e.g., only "VR Relevance" objects

            _graphView.AddGameObjectNode(go);
        }
    }

    private void OnExportClicked()
    {
        // Collect all annotation data from the graph
        var exportData = _graphView.CollectAnnotationData();

        // Serialize to JSON or do something else
        string jsonResult = JsonUtility.ToJson(exportData, true);
        Debug.Log("Exported Haptic Annotation Data:\n" + jsonResult);

        // Optionally, write to a file
        // File.WriteAllText("path/to/yourfile.json", jsonResult);
    }
}