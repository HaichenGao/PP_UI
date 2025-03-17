using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using System;
using System.Linq;
using System.Collections.Generic;

public class HapticsAnnotationWindow : EditorWindow
{
    private HapticsRelationshipGraphView _graphView;
    private VisualElement _inspectorContainer;
    private VisualElement _inspectorContent;
    private bool _showInspector = true;
    private List<ISelectable> _lastSelection = new List<ISelectable>();

    // Fields to store graph metadata
    private string _graphTitle = "Haptic Annotation";
    private string _graphSummary = "Describe the haptic relationships in this scene.";

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

        // Set the rootVisualElement directly
        rootVisualElement.Clear();
        uxmlAsset.CloneTree(rootVisualElement);

        // Apply styling
        if (styleSheet != null)
        {
            rootVisualElement.styleSheets.Add(styleSheet);
        }

        // Set up the graph view
        var graphContainer = rootVisualElement.Q<VisualElement>("graphViewContainer");
        _graphView = new HapticsRelationshipGraphView();
        _graphView.style.flexGrow = 1;
        graphContainer.Add(_graphView);

        // Set up the inspector
        _inspectorContainer = rootVisualElement.Q<VisualElement>("inspectorContainer");
        _inspectorContent = rootVisualElement.Q<VisualElement>("inspectorContent");

        var existingLabels = _inspectorContainer.Query<Label>().ToList();
        foreach (var label in existingLabels)
        {
            if (label.text == "Inspector")
            {
                _inspectorContainer.Remove(label);
            }
        }

        // Set up button handlers
        var scanButton = rootVisualElement.Q<Button>("scanSceneButton");
        var exportButton = rootVisualElement.Q<Button>("exportDataButton");
        var inspectorToggleButton = rootVisualElement.Q<Button>("inspectorToggleButton");

        scanButton.clicked += OnScanSceneClicked;
        exportButton.clicked += OnExportClicked;
        inspectorToggleButton.clicked += ToggleInspector;

        // Set up drag and drop
        _graphView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        _graphView.RegisterCallback<DragPerformEvent>(OnDragPerform);

        // Initialize the inspector
        UpdateInspector(null);

        // Set initial inspector visibility
        SetInspectorVisibility(_showInspector);

        // Start checking for selection changes
        EditorApplication.update += CheckSelectionChange;
    }

    private void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _graphView.UnregisterCallback<DragPerformEvent>(OnDragPerform);
        }

        EditorApplication.update -= CheckSelectionChange;
    }

    private void CheckSelectionChange()
    {
        if (_graphView == null) return;

        // Check if selection has changed
        var currentSelection = _graphView.selection.ToList();
        bool selectionChanged = false;

        if (currentSelection.Count != _lastSelection.Count)
        {
            selectionChanged = true;
        }
        else
        {
            // Check if any elements are different
            for (int i = 0; i < currentSelection.Count; i++)
            {
                if (!_lastSelection.Contains(currentSelection[i]))
                {
                    selectionChanged = true;
                    break;
                }
            }
        }

        if (selectionChanged)
        {
            _lastSelection = currentSelection;
            UpdateInspectorBasedOnSelection();
        }
    }

    private void UpdateInspectorBasedOnSelection()
    {
        var selectedNodes = _graphView.selection.OfType<HapticNode>().ToList();

        if (selectedNodes.Count == 1)
        {
            // Show node inspector
            UpdateInspector(selectedNodes[0]);
        }
        else
        {
            // Show graph inspector
            UpdateInspector(null);
        }
    }

    private void UpdateInspector(HapticNode selectedNode)
    {
        _inspectorContent.Clear();

        if (selectedNode == null)
        {
            // Create graph-level inspector
            var titleLabel = new Label("Title");
            titleLabel.AddToClassList("inspector-field-label");

            var titleField = new TextField();
            titleField.value = _graphTitle;
            titleField.AddToClassList("inspector-field");
            titleField.RegisterValueChangedCallback(evt => {
                _graphTitle = evt.newValue;
            });

            var summaryLabel = new Label("Summary");
            summaryLabel.AddToClassList("inspector-field-label");

            var summaryField = new TextField();
            summaryField.multiline = true;
            summaryField.value = _graphSummary;
            summaryField.style.height = 80;
            summaryField.AddToClassList("inspector-field");
            summaryField.RegisterValueChangedCallback(evt => {
                _graphSummary = evt.newValue;
            });

            _inspectorContent.Add(titleLabel);
            _inspectorContent.Add(titleField);
            _inspectorContent.Add(summaryLabel);
            _inspectorContent.Add(summaryField);

            // Graph Statistics section removed as requested
        }
        else
        {
            // Create node-level inspector
            var nodeNameLabel = new Label("Node: " + selectedNode.title);
            nodeNameLabel.AddToClassList("inspector-section-title");

            _inspectorContent.Add(nodeNameLabel);

            // For now, we'll leave the node inspector blank as requested
            // This is where you would add node-specific properties
        }
    }

    private void ToggleInspector()
    {
        _showInspector = !_showInspector;
        SetInspectorVisibility(_showInspector);
    }

    private void SetInspectorVisibility(bool visible)
    {
        _inspectorContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

        // Update the inspector after adding nodes
        UpdateInspectorBasedOnSelection();
    }

    private void OnScanSceneClicked()
    {
        // Example scanning of scene objects
        GameObject[] sceneObjects = GameObject.FindObjectsOfType<GameObject>();

        // Clear current graph
        _graphView.ClearGraph();

        // Add nodes in a basic layout
        Vector2 position = new Vector2(100, 100);
        foreach (var go in sceneObjects)
        {
            if (!go.activeInHierarchy) continue;
            // You could filter further, e.g., only "VR Relevance" objects

            _graphView.AddGameObjectNode(go, position);
            position += new Vector2(250, 0);

            // Start a new row if we've gone too far right
            if (position.x > 1000)
            {
                position = new Vector2(100, position.y + 300);
            }
        }

        // Update the inspector after scanning
        UpdateInspectorBasedOnSelection();
    }

    private void OnExportClicked()
    {
        // Collect all annotation data from the graph
        var exportData = _graphView.CollectAnnotationData();

        // Add title and summary to the export data
        exportData.title = _graphTitle;
        exportData.summary = _graphSummary;

        // Serialize to JSON or do something else
        string jsonResult = JsonUtility.ToJson(exportData, true);
        Debug.Log("Exported Haptic Annotation Data:\n" + jsonResult);

        // Optionally, write to a file
        // File.WriteAllText("path/to/yourfile.json", jsonResult);
    }
}