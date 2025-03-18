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

        // Register for engagement level changes
        HapticNode.OnEngagementLevelChanged += OnNodeEngagementLevelChanged;
    }

    private void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _graphView.UnregisterCallback<DragPerformEvent>(OnDragPerform);
        }

        EditorApplication.update -= CheckSelectionChange;

        // Unregister from engagement level changes
        HapticNode.OnEngagementLevelChanged -= OnNodeEngagementLevelChanged;
    }

    private void OnNodeEngagementLevelChanged(HapticNode node, int newLevel)
    {
        // If we're showing the graph inspector, update it to reflect the new engagement levels
        if (_graphView.selection.Count == 0)
        {
            UpdateInspector(null);
        }
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

            // Add engagement level lists
            AddEngagementLevelLists();
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

    private void AddEngagementLevelLists()
    {
        // Get all nodes from the graph view
        var allNodes = _graphView.GetNodes();

        // Group nodes by engagement level
        var highEngagementNodes = allNodes.Where(n => n.EngagementLevel == 2).ToList();
        var mediumEngagementNodes = allNodes.Where(n => n.EngagementLevel == 1).ToList();
        var lowEngagementNodes = allNodes.Where(n => n.EngagementLevel == 0).ToList();

        // Create a container for all lists
        var listsContainer = new VisualElement();
        listsContainer.style.marginTop = 20;

        // Add High Engagement list
        AddReorderableList(listsContainer, "High Engagement", highEngagementNodes);

        // Add Medium Engagement list
        AddReorderableList(listsContainer, "Medium Engagement", mediumEngagementNodes);

        // Add Low Engagement list
        AddReorderableList(listsContainer, "Low Engagement", lowEngagementNodes);

        _inspectorContent.Add(listsContainer);
    }

    private void AddReorderableList(VisualElement container, string title, List<HapticNode> nodes)
    {
        // Create a foldout for the list
        var foldout = new Foldout();
        foldout.text = title;
        foldout.value = true; // Expanded by default
        foldout.AddToClassList("engagement-foldout");

        // Create the list container
        var listContainer = new VisualElement();
        listContainer.AddToClassList("reorderable-list-container");

        // Add each node to the list
        foreach (var node in nodes)
        {
            var itemContainer = CreateReorderableListItem(node);
            listContainer.Add(itemContainer);
        }

        foldout.Add(listContainer);
        container.Add(foldout);
    }

    private VisualElement CreateReorderableListItem(HapticNode node)
    {
        // Create the container for the list item
        var itemContainer = new VisualElement();
        itemContainer.AddToClassList("reorderable-list-item");

        // Create the equals sign
        var equalsSign = new Label("=");
        equalsSign.AddToClassList("equals-sign");

        // Create the label for the node name
        var nodeLabel = new Label(node.title);
        nodeLabel.AddToClassList("node-label");

        // Add elements to the item container
        itemContainer.Add(equalsSign);
        itemContainer.Add(nodeLabel);

        // Make the item draggable
        itemContainer.userData = node; // Store the node reference for drag operations

        // Add drag functionality
        SetupDragAndDrop(itemContainer);

        // Add click handler to select the node in the graph
        itemContainer.RegisterCallback<ClickEvent>(evt => {
            // Select the node in the graph view
            _graphView.ClearSelection();
            _graphView.AddToSelection(node);
        });

        return itemContainer;
    }

    private void SetupDragAndDrop(VisualElement itemContainer)
    {
        // Make the item draggable
        itemContainer.RegisterCallback<MouseDownEvent>(evt => {
            // Start drag operation
            itemContainer.CaptureMouse();
            itemContainer.AddToClassList("dragging");

            // Store the original position and prevent selection during drag
            itemContainer.userData = new Vector2(evt.mousePosition.x, evt.mousePosition.y);
            evt.StopPropagation();
        });

        itemContainer.RegisterCallback<MouseMoveEvent>(evt => {
            if (itemContainer.HasMouseCapture())
            {
                // Move the item
                var originalPos = (Vector2)itemContainer.userData;
                var delta = new Vector2(evt.mousePosition.x - originalPos.x, evt.mousePosition.y - originalPos.y);

                // Apply the movement
                itemContainer.style.top = delta.y;
            }
        });

        itemContainer.RegisterCallback<MouseUpEvent>(evt => {
            if (itemContainer.HasMouseCapture())
            {
                // End drag operation
                itemContainer.ReleaseMouse();
                itemContainer.RemoveFromClassList("dragging");

                // Reset position
                itemContainer.style.top = 0;

                // Find the new position in the list
                var parent = itemContainer.parent;
                var mouseY = evt.mousePosition.y;

                // Find the closest item to drop position
                var siblings = parent.Children().ToList();
                int currentIndex = siblings.IndexOf(itemContainer);
                int targetIndex = currentIndex;

                for (int i = 0; i < siblings.Count; i++)
                {
                    if (i == currentIndex) continue;

                    var sibling = siblings[i];
                    var siblingRect = sibling.worldBound;

                    if (mouseY > siblingRect.yMin && mouseY < siblingRect.yMax)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                // Reorder the item
                if (targetIndex != currentIndex)
                {
                    parent.Remove(itemContainer);

                    if (targetIndex >= siblings.Count)
                    {
                        parent.Add(itemContainer);
                    }
                    else
                    {
                        parent.Insert(targetIndex, itemContainer);
                    }
                }
            }
        });
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