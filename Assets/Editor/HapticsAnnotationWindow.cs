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

    // Add these fields to HapticsAnnotationWindow.cs
    private List<HapticNode> _orderedHighEngagementNodes = new List<HapticNode>();
    private List<HapticNode> _orderedMediumEngagementNodes = new List<HapticNode>();
    private List<HapticNode> _orderedLowEngagementNodes = new List<HapticNode>();

    [MenuItem("HapticsAnnotationWindow/Open _%#T")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<HapticsAnnotationWindow>();
        wnd.titleContent = new GUIContent("Haptic Annotation");
        wnd.Show();
    }

    private void OnEnable()
    {
        // Initialize ordered lists
        _orderedHighEngagementNodes = new List<HapticNode>();
        _orderedMediumEngagementNodes = new List<HapticNode>();
        _orderedLowEngagementNodes = new List<HapticNode>();

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

        // Register for graph changes
        HapticsRelationshipGraphView.OnGraphChanged += OnGraphChanged;
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

        // Unregister from graph changes
        HapticsRelationshipGraphView.OnGraphChanged -= OnGraphChanged;
    }

    private void OnGraphChanged()
    {
        // Update the inspector to reflect the changes in the graph
        UpdateInspector(null);
    }

    private void OnNodeEngagementLevelChanged(HapticNode node, int newLevel)
    {
        // Always update the inspector to reflect the new engagement levels
        // This ensures the scroll containers are properly updated
        UpdateInspector(null);
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

            var summaryLabel = new Label("Description");
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

        // Update our ordered lists, keeping existing order for nodes that are still present
        UpdateOrderedList(_orderedHighEngagementNodes, highEngagementNodes);
        UpdateOrderedList(_orderedMediumEngagementNodes, mediumEngagementNodes);
        UpdateOrderedList(_orderedLowEngagementNodes, lowEngagementNodes);

        // Create a container for all lists
        var listsContainer = new VisualElement();
        listsContainer.style.marginTop = 20;

        // Add High Engagement list
        AddReorderableList(listsContainer, "High Engagement", _orderedHighEngagementNodes);

        // Add Medium Engagement list
        AddReorderableList(listsContainer, "Medium Engagement", _orderedMediumEngagementNodes);

        // Add Low Engagement list
        AddReorderableList(listsContainer, "Low Engagement", _orderedLowEngagementNodes);

        _inspectorContent.Add(listsContainer);
    }

    // Helper method to update ordered lists while preserving existing order
    private void UpdateOrderedList(List<HapticNode> orderedList, List<HapticNode> currentNodes)
    {
        // Remove nodes that are no longer in the current list
        orderedList.RemoveAll(n => !currentNodes.Contains(n));

        // Add any new nodes that aren't already in the ordered list
        foreach (var node in currentNodes)
        {
            if (!orderedList.Contains(node))
            {
                orderedList.Add(node);
            }
        }
    }

    private void AddReorderableList(VisualElement container, string title, List<HapticNode> nodes)
    {
        // Create a foldout for the list
        var foldout = new Foldout();
        foldout.text = title;
        foldout.value = true; // Expanded by default
        foldout.AddToClassList("engagement-foldout");

        // Create a scrollable container if needed
        var scrollContainer = new ScrollView();
        scrollContainer.mode = ScrollViewMode.Vertical;
        scrollContainer.verticalScrollerVisibility = ScrollerVisibility.Auto;

        // Add the appropriate class
        if (nodes.Count > 5)
        {
            scrollContainer.AddToClassList("scrollable-list-container");
        }
        else
        {
            // Still add some styling for consistency
            scrollContainer.AddToClassList("reorderable-list-container");
        }

        // Add each node to the list
        foreach (var node in nodes)
        {
            var itemContainer = CreateReorderableListItem(node);
            scrollContainer.Add(itemContainer);
        }

        foldout.Add(scrollContainer);
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
        nodeLabel.tooltip = node.title; // Add tooltip for long names

        // Add elements to the item container
        itemContainer.Add(equalsSign);
        itemContainer.Add(nodeLabel);

        // Make the item draggable
        itemContainer.userData = node; // Store the node reference for drag operations

        // Add drag functionality
        SetupDragAndDrop(itemContainer);

        // Add click handler to focus on the node in the graph without selecting it
        itemContainer.RegisterCallback<ClickEvent>(evt => {
            // Focus on the node in the graph view without selecting it
            _graphView.FrameAndFocusNode(node, false);

            // Prevent event propagation to avoid any default selection behavior
            evt.StopPropagation();
        });

        return itemContainer;
    }

    private void SetupDragAndDrop(VisualElement itemContainer)
    {
        // Store original data for drag operation
        Vector2 startPosition = Vector2.zero;
        VisualElement placeholder = null;
        HapticNode draggedNode = itemContainer.userData as HapticNode;

        // Make the item draggable
        itemContainer.RegisterCallback<MouseDownEvent>(evt => {
            // Start drag operation
            itemContainer.CaptureMouse();
            itemContainer.AddToClassList("dragging");

            // Store the original position
            startPosition = evt.mousePosition;

            // Create a placeholder element with the same size as the dragged item
            placeholder = new VisualElement();
            placeholder.AddToClassList("reorderable-list-placeholder");
            placeholder.style.height = itemContainer.layout.height;

            // Insert the placeholder at the same position as the dragged item
            var parent = itemContainer.parent;
            int index = parent.IndexOf(itemContainer);
            parent.Insert(index, placeholder);

            // Make the dragged item absolute positioned
            itemContainer.style.position = Position.Absolute;

            evt.StopPropagation();
        });

        itemContainer.RegisterCallback<MouseMoveEvent>(evt => {
            if (itemContainer.HasMouseCapture())
            {
                // Calculate delta from start position
                var delta = evt.mousePosition - startPosition;

                // Apply the movement to the dragged item
                itemContainer.style.top = delta.y;

                // Find the position to insert the placeholder
                var parent = placeholder.parent;
                var mouseY = evt.mousePosition.y;

                // Get all siblings except the dragged item and placeholder
                var siblings = parent.Children().Where(c => c != itemContainer && c != placeholder).ToList();

                // Find the closest sibling to insert the placeholder
                int targetIndex = -1;
                float minDistance = float.MaxValue;

                for (int i = 0; i < siblings.Count; i++)
                {
                    var sibling = siblings[i];
                    var siblingRect = sibling.worldBound;
                    var siblingCenter = siblingRect.center.y;

                    // Calculate distance to the center of this sibling
                    float distance = Mathf.Abs(mouseY - siblingCenter);

                    if (distance < minDistance)
                    {
                        minDistance = distance;

                        // Determine if we should insert before or after this sibling
                        if (mouseY < siblingCenter)
                            targetIndex = parent.IndexOf(sibling);
                        else
                            targetIndex = parent.IndexOf(sibling) + 1;
                    }
                }

                // If we found a valid position and it's different from the current position
                if (targetIndex >= 0 && parent.IndexOf(placeholder) != targetIndex)
                {
                    // Move the placeholder to the new position
                    parent.Remove(placeholder);

                    if (targetIndex >= parent.childCount)
                        parent.Add(placeholder);
                    else
                        parent.Insert(targetIndex, placeholder);
                }
            }
        });

        itemContainer.RegisterCallback<MouseUpEvent>(evt => {
            if (itemContainer.HasMouseCapture())
            {
                // End drag operation
                itemContainer.ReleaseMouse();
                itemContainer.RemoveFromClassList("dragging");

                // Reset position style
                itemContainer.style.position = Position.Relative;
                itemContainer.style.top = 0;

                // Get the parent and the placeholder position
                var parent = placeholder.parent;
                int placeholderIndex = parent.IndexOf(placeholder);

                // Remove the placeholder
                parent.Remove(placeholder);
                placeholder = null;

                // Move the item to the placeholder's position
                parent.Remove(itemContainer);

                if (placeholderIndex >= parent.childCount)
                    parent.Add(itemContainer);
                else
                    parent.Insert(placeholderIndex, itemContainer);

                // Update the ordered lists based on the new order
                UpdateOrderedListsFromUI();
            }
        });
    }

    // Method to update our ordered lists based on the current UI order
    private void UpdateOrderedListsFromUI()
    {
        // Find all ScrollView containers in the inspector
        var scrollContainers = _inspectorContent.Query<ScrollView>().ToList();

        if (scrollContainers.Count >= 3)
        {
            // Clear our ordered lists
            _orderedHighEngagementNodes.Clear();
            _orderedMediumEngagementNodes.Clear();
            _orderedLowEngagementNodes.Clear();

            // Get the nodes in each container in their current order
            var highEngagementContainer = scrollContainers[0];
            var mediumEngagementContainer = scrollContainers[1];
            var lowEngagementContainer = scrollContainers[2];

            // Update high engagement nodes
            foreach (var child in highEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    _orderedHighEngagementNodes.Add(node);
                }
            }

            // Update medium engagement nodes
            foreach (var child in mediumEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    _orderedMediumEngagementNodes.Add(node);
                }
            }

            // Update low engagement nodes
            foreach (var child in lowEngagementContainer.Children())
            {
                if (child.userData is HapticNode node)
                {
                    _orderedLowEngagementNodes.Add(node);
                }
            }
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