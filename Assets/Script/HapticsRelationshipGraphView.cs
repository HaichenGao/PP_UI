using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

public class HapticsRelationshipGraphView : GraphView
{
    public void RegisterCallback<T>(EventCallback<T> callback) where T : EventBase<T>, new()
    {
        base.RegisterCallback(callback);
    }

    private readonly List<HapticNode> _nodes = new List<HapticNode>();

    public delegate void GraphChangedEventHandler();
    public static event GraphChangedEventHandler OnGraphChanged;

    private bool _isGroupSelectionActive = false;
    private Vector2 _groupSelectionStartPosition;
    private VisualElement _selectionRectangle;
    private List<HapticNodeGroup> _nodeGroups = new List<HapticNodeGroup>();

    public HapticsRelationshipGraphView()
    {
        style.flexGrow = 1;

        // Setup basic manipulators
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Update the selection rectangle styling in the constructor
        _selectionRectangle = new VisualElement();
        _selectionRectangle.name = "selection-rectangle";
        _selectionRectangle.style.position = Position.Absolute;
        _selectionRectangle.style.backgroundColor = new Color(0.2f, 0.4f, 0.9f, 0.1f);
        _selectionRectangle.style.borderLeftWidth =
        _selectionRectangle.style.borderRightWidth =
        _selectionRectangle.style.borderTopWidth =
        _selectionRectangle.style.borderBottomWidth = 1;
        _selectionRectangle.style.borderLeftColor =
        _selectionRectangle.style.borderRightColor =
        _selectionRectangle.style.borderTopColor =
        _selectionRectangle.style.borderBottomColor = new Color(0.2f, 0.4f, 0.9f, 0.8f);
        _selectionRectangle.visible = true;
        Add(_selectionRectangle);

        // Register for mouse events
        RegisterCallback<MouseDownEvent>(OnMouseDown);
        RegisterCallback<MouseMoveEvent>(OnMouseMove);
        RegisterCallback<MouseUpEvent>(OnMouseUp);

        // Add a context menu for creating groups
        RegisterCallback<ContextualMenuPopulateEvent>(BuildContextualMenu);

        // Create and add the grid background
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        // Style the grid to match Script Graph
        grid.AddToClassList("grid-background");

        // Register for graph changes to handle connections
        graphViewChanged = OnGraphViewChanged;
    }

    // Update the OnMouseDown method to handle platform differences
    private void OnMouseDown(MouseDownEvent evt)
    {
        // Check if Ctrl/Cmd is pressed and it's a left click
        if (evt.shiftKey && evt.button == 0)
        {
            // Start group selection
            _isGroupSelectionActive = true;
            _groupSelectionStartPosition = evt.localMousePosition;

            // Show and position the selection rectangle
            _selectionRectangle.visible = true;
            _selectionRectangle.style.left = _groupSelectionStartPosition.x;
            _selectionRectangle.style.top = _groupSelectionStartPosition.y;
            _selectionRectangle.style.width = _selectionRectangle.style.height = 0;

            // Prevent other handlers from processing this event
            evt.StopPropagation();
        }
    }

    // Update the OnMouseMove method to ensure the selection rectangle is visible
    private void OnMouseMove(MouseMoveEvent evt)
    {
        if (_isGroupSelectionActive)
        {
            // Calculate rectangle dimensions
            float left = Mathf.Min(_groupSelectionStartPosition.x, evt.localMousePosition.x);
            float top = Mathf.Min(_groupSelectionStartPosition.y, evt.localMousePosition.y);
            float width = Mathf.Abs(evt.localMousePosition.x - _groupSelectionStartPosition.x);
            float height = Mathf.Abs(evt.localMousePosition.y - _groupSelectionStartPosition.y);

            // Update selection rectangle
            _selectionRectangle.style.left = left;
            _selectionRectangle.style.top = top;
            _selectionRectangle.style.width = width;
            _selectionRectangle.style.height = height;

            // Ensure the selection rectangle is visible
            _selectionRectangle.visible = true;

            // Prevent other handlers from processing this event
            evt.StopPropagation();
        }
    }

    // Update the OnMouseUp method to ensure proper cleanup
    private void OnMouseUp(MouseUpEvent evt)
    {
        if (_isGroupSelectionActive)
        {
            // End group selection
            _isGroupSelectionActive = false;

            // Calculate final rectangle
            Rect selectionRect = new Rect(
                _selectionRectangle.style.left.value.value,
                _selectionRectangle.style.top.value.value,
                _selectionRectangle.style.width.value.value,
                _selectionRectangle.style.height.value.value
            );

            // Hide the selection rectangle
            _selectionRectangle.visible = false;

            // Only create a group if the selection has some size
            if (selectionRect.width > 10 && selectionRect.height > 10)
            {
                // Find all nodes within the selection rectangle
                var selectedNodes = new List<HapticNode>();
                foreach (var node in _nodes)
                {
                    Rect nodeRect = node.GetPosition();
                    // Check if the node is at least partially inside the selection rectangle
                    if (nodeRect.Overlaps(selectionRect))
                    {
                        selectedNodes.Add(node);
                    }
                }

                // If we have selected nodes, create a group
                if (selectedNodes.Count > 0)
                {
                    CreateNodeGroup(selectedNodes, selectionRect);
                }
            }

            // Prevent other handlers from processing this event
            evt.StopPropagation();
        }
    }

    // Add a context menu option to create a group from the current selection
    private void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        // Only add the menu item if we have nodes selected
        var selectedNodes = selection.OfType<HapticNode>().ToList();
        if (selectedNodes.Count > 0)
        {
            evt.menu.AppendAction("Create Group", (a) => CreateGroupFromSelection());
        }
    }

    // Create a group from the current selection
    private void CreateGroupFromSelection()
    {
        var selectedNodes = selection.OfType<HapticNode>().ToList();
        if (selectedNodes.Count > 0)
        {
            // Calculate the bounding rectangle of all selected nodes
            Rect boundingRect = CalculateBoundingRect(selectedNodes);

            // Add some padding
            boundingRect.x -= 20;
            boundingRect.y -= 40; // Extra space for the header
            boundingRect.width += 40;
            boundingRect.height += 60;

            // Create the group
            CreateNodeGroup(selectedNodes, boundingRect);
        }
    }

    // Calculate the bounding rectangle for a set of nodes
    private Rect CalculateBoundingRect(List<HapticNode> nodes)
    {
        if (nodes.Count == 0)
            return Rect.zero;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var node in nodes)
        {
            Rect nodeRect = node.GetPosition();
            minX = Mathf.Min(minX, nodeRect.x);
            minY = Mathf.Min(minY, nodeRect.y);
            maxX = Mathf.Max(maxX, nodeRect.x + nodeRect.width);
            maxY = Mathf.Max(maxY, nodeRect.y + nodeRect.height);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // Create a node group with the given nodes and rectangle
    private void CreateNodeGroup(List<HapticNode> nodes, Rect rect)
    {
        // Create a new group
        var group = new HapticNodeGroup(rect, nodes);

        // Add the group to the graph
        AddElement(group);

        // Add to our list of groups
        _nodeGroups.Add(group);

        // Make sure the group is behind the nodes
        group.SendToBack();
    }

    // Override DeleteElements to handle group deletion
    //public override void DeleteElements(IEnumerable<GraphElement> elements)
    //{
    //    // Find any groups that need to be removed
    //    var groupsToRemove = elements.OfType<HapticNodeGroup>().ToList();

    //    // Remove them from our tracking list
    //    foreach (var group in groupsToRemove)
    //    {
    //        _nodeGroups.Remove(group);
    //    }

    //    // Call the base implementation to actually delete the elements
    //    base.DeleteElements(elements);
    //}

    public void ClearGraph()
    {
        DeleteElements(graphElements);
        _nodes.Clear();
    }

    public HapticNode AddGameObjectNode(GameObject obj, Vector2 dropPosition = default(Vector2))
    {
        // Create a new HapticNode (custom node) with a reference to GameObject
        var node = new HapticNode(obj);
        node.SetPosition(new Rect(dropPosition.x, dropPosition.y, 200, 150));
        AddElement(node);
        _nodes.Add(node);

        // Return the created node
        return node;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        // Gather all ports by examining every node in this GraphView
        var compatiblePorts = new List<Port>();

        // 'nodes' is a built-in property that returns all Node elements in this GraphView
        var allNodes = nodes.ToList();
        foreach (var node in allNodes)
        {
            // Each node has inputContainer and outputContainer,
            // but you can query all Port elements if you prefer
            var nodePorts = node.Query<Port>().ToList();
            foreach (var port in nodePorts)
            {
                // 1. Skip the same port
                if (port == startPort)
                    continue;

                // 2. Skip ports on the same node
                if (port.node == startPort.node)
                    continue;

                // 3. Skip if the direction is the same (both Output or both Input)
                if (port.direction == startPort.direction)
                {
                    // We only connect Output → Input or vice versa
                    // so we require opposite directions
                    continue;
                }

                // 4. Check data type matching
                // GraphView compares 'portType' to control whether the two ports share a data type
                if (port.portType == startPort.portType)
                {
                    compatiblePorts.Add(port);
                }
            }
        }

        return compatiblePorts;
    }

    // Intercept edge creation so we can instantiate HapticRelationshipEdge
    // Modify the existing OnGraphViewChanged method to also handle groups
    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        bool graphChanged = false;

        // Handle new edges (connections)
        if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
        {
            graphChanged = true;

            foreach (var edge in change.edgesToCreate)
            {
                // Notify the input port's node about the connection
                if (edge.input.node is HapticNode inputNode)
                {
                    inputNode.OnPortConnected(edge.input);
                }

                // Add a new direct port to the output node if needed
                if (edge.output.node is HapticNode outputNode)
                {
                    outputNode.AddDirectPort();
                }
            }
        }

        // Handle element removals (nodes, edges, and groups)
        if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
        {
            graphChanged = true;
            // Create a list to store additional elements that need to be removed
            List<GraphElement> additionalElementsToRemove = new List<GraphElement>();

            foreach (var element in change.elementsToRemove)
            {
                if (element is HapticNode node)
                {
                    // Find all edges connected to this node
                    var connectedEdges = edges.ToList().Where(edge =>
                        edge.input.node == node || edge.output.node == node).ToList();

                    // For each connected edge, notify the other node about the disconnection
                    foreach (var edge in connectedEdges)
                    {
                        // If this node is the input node, notify the output node
                        if (edge.input.node == node && edge.output.node is HapticNode outputNode)
                        {
                            outputNode.OnPortDisconnected(edge.output);
                        }
                        // If this node is the output node, notify the input node
                        else if (edge.output.node == node && edge.input.node is HapticNode inputNode)
                        {
                            inputNode.OnPortDisconnected(edge.input);
                        }
                    }

                    // Add connected edges to removal list
                    additionalElementsToRemove.AddRange(connectedEdges);

                    // Call PrepareForDeletion to disconnect all ports
                    node.PrepareForDeletion();

                    // Remove the node from our tracking list
                    _nodes.Remove(node);
                }
                else if (element is Edge edge)
                {
                    Debug.Log($"Removing edge: {edge.output?.node?.title} -> {edge.input?.node?.title}");

                    // Make sure to disconnect the edge from both ports
                    edge.output?.Disconnect(edge);
                    edge.input?.Disconnect(edge);

                    // Notify the nodes about the disconnection
                    if (edge.input?.node is HapticNode inputNode)
                    {
                        inputNode.OnPortDisconnected(edge.input);
                    }

                    if (edge.output?.node is HapticNode outputNode)
                    {
                        outputNode.OnPortDisconnected(edge.output);
                    }
                }
                else if (element is HapticNodeGroup group)
                {
                    // Remove the group from our tracking list
                    _nodeGroups.Remove(group);
                }
            }

            // Add the additional elements to the removal list
            if (additionalElementsToRemove.Count > 0)
            {
                change.elementsToRemove.AddRange(additionalElementsToRemove);
            }
        }

        if (graphChanged)
        {
            OnGraphChanged?.Invoke();
        }

        return change;
    }

    public HapticAnnotationData CollectAnnotationData()
    {
        // Example of collecting annotation data from each node
        var data = new HapticAnnotationData();
        data.nodeAnnotations = new List<HapticObjectRecord>();
        data.relationshipAnnotations = new List<HapticConnectionRecord>();

        foreach (var hNode in _nodes)
        {
            data.nodeAnnotations.Add(new HapticObjectRecord
            {
                objectName = hNode.AssociatedObject.name,
                inertia = hNode.Inertia,
                interactivity = hNode.Interactivity,
                outline = hNode.Outline,
                texture = hNode.Texture,
                hardness = hNode.Hardness,
                temperature = hNode.Temperature,
                engagementLevel = hNode.EngagementLevel,
                isDirectContacted = hNode.IsDirectContacted,
                description = hNode.Description,

                // Add slider values
                inertiaValue = hNode.InertiaValue,
                interactivityValue = hNode.InteractivityValue,
                outlineValue = hNode.OutlineValue,
                textureValue = hNode.TextureValue,
                hardnessValue = hNode.HardnessValue,
                temperatureValue = hNode.TemperatureValue
            });

            // Collect tool-mediated annotations
            var toolMediatedAnnotations = hNode.GetToolMediatedAnnotations();
            foreach (var kvp in toolMediatedAnnotations)
            {
                data.relationshipAnnotations.Add(new HapticConnectionRecord
                {
                    directContactObject = kvp.Key,
                    toolMediatedObject = hNode.AssociatedObject.name,
                    annotationText = kvp.Value
                });
            }
        }

        // Process all edges in the graph to ensure we capture all connections
        var allEdges = edges.ToList();
        foreach (var edge in allEdges)
        {
            var outputNode = edge.output?.node as HapticNode;
            var inputNode = edge.input?.node as HapticNode;

            if (outputNode != null && inputNode != null)
            {
                // Check if this relationship is already in the list
                bool alreadyExists = data.relationshipAnnotations.Any(r =>
                    r.directContactObject == outputNode.AssociatedObject.name &&
                    r.toolMediatedObject == inputNode.AssociatedObject.name);

                if (!alreadyExists)
                {
                    // Get the annotation text from the input node for this specific port
                    string annotationText = inputNode.GetAnnotationTextForPort(edge.input);

                    // Add the relationship
                    data.relationshipAnnotations.Add(new HapticConnectionRecord
                    {
                        directContactObject = outputNode.AssociatedObject.name,
                        toolMediatedObject = inputNode.AssociatedObject.name,
                        annotationText = annotationText
                    });
                }
            }
        }

        return data;
    }

    public List<HapticNode> GetNodes()
    {
        return _nodes;
    }

    public void FrameAndFocusNode(HapticNode node, bool select = false)
    {
        if (node == null) return;

        // Store the current selection
        var currentSelection = selection.ToList();

        // Temporarily select the node we want to frame
        ClearSelection();
        AddToSelection(node);

        // Use the built-in method to frame the selection
        FrameSelection();

        // If we don't want to keep the node selected, restore the previous selection
        if (!select)
        {
            ClearSelection();
            foreach (var item in currentSelection)
            {
                AddToSelection(item);
            }
        }
    }

    public void ConnectNodes(HapticNode sourceNode, HapticNode targetNode, string annotationText)
    {
        // Find an output port on the source node
        var outputPort = sourceNode.outputContainer.Q<Port>();

        // Find an input port on the target node
        var inputPort = targetNode.inputContainer.Q<Port>();

        if (outputPort != null && inputPort != null)
        {
            // Create an edge between the ports
            var edge = new Edge
            {
                output = outputPort,
                input = inputPort
            };

            // Connect the ports
            outputPort.Connect(edge);
            inputPort.Connect(edge);

            // Add the edge to the graph
            AddElement(edge);

            // Set the annotation text
            targetNode.SetAnnotationTextForPort(inputPort, annotationText);
        }
    }
}

// Minimal data structure to hold annotation data
[System.Serializable]
public class HapticAnnotationData
{
    public string summary;
    public List<HapticObjectRecord> nodeAnnotations;
    public List<HapticConnectionRecord> relationshipAnnotations;
}

[System.Serializable]
public class HapticObjectRecord
{
    public string objectName;
    public bool isDirectContacted;
    public string description;
    public int engagementLevel;
    public string snapshotPath;

    public string inertia;
    public string interactivity;
    public string outline;
    public string texture;
    public string hardness;
    public string temperature;

    // Add slider values
    public float inertiaValue;
    public float interactivityValue;
    public float outlineValue;
    public float textureValue;
    public float hardnessValue;
    public float temperatureValue;
}

// Record for each connection
[System.Serializable]
public class HapticConnectionRecord
{
    public string directContactObject;
    public string toolMediatedObject;
    public string annotationText;
}

// Example node class: represents one VR object in the graph
public class HapticNode : Node
{
    // Add a delegate and event for engagement level changes
    public delegate void EngagementLevelChangedEventHandler(HapticNode node, int newLevel);
    public static event EngagementLevelChangedEventHandler OnEngagementLevelChanged;

    public bool IsDirectContacted { get; set; } = false;
    public string Description { get; set; } = "";

    public string Inertia { get; set; } = "";
    public string Interactivity { get; set; } = "";
    public string Outline { get; set; } = "";
    public string Texture { get; set; } = "";
    public string Hardness { get; set; } = "";
    public string Temperature { get; set; } = "";

    // Add float values for sliders (0-1 range)
    public float InertiaValue { get; set; } = 0f;
    public float InteractivityValue { get; set; } = 0f;
    public float OutlineValue { get; set; } = 0f;
    public float TextureValue { get; set; } = 0f;
    public float HardnessValue { get; set; } = 0f;
    public float TemperatureValue { get; set; } = 0f;

    public Dictionary<string, bool> PropertyFoldoutStates { get; private set; } = new Dictionary<string, bool>()
    {
        { "Inertia", false },
        { "Interactivity", false },
        { "Outline", false },
        { "Texture", false },
        { "Hardness", false },
        { "Temperature", false }
    };

    private int _engagementLevel = 1; // Default to Medium Engagement (index 1)

    public int EngagementLevel
    {
        get => _engagementLevel;
        set
        {
            if (_engagementLevel != value)
            {
                _engagementLevel = value;
                // Trigger the event when engagement level changes
                OnEngagementLevelChanged?.Invoke(this, value);
            }
        }
    }

    // Add this method to the HapticNode class
    public Texture2D CaptureNodeSnapshot()
    {
        // Create a render texture to capture the preview
        RenderTexture renderTexture = new RenderTexture(256, 256, 24);
        RenderTexture.active = renderTexture;

        // Create a texture to store the snapshot
        Texture2D snapshot = new Texture2D(256, 256, TextureFormat.RGBA32, false);

        // If we have a valid GameObject and editor
        if (AssociatedObject != null && _gameObjectEditor != null)
        {
            // Draw the preview to the render texture
            _gameObjectEditor.OnPreviewGUI(new Rect(0, 0, 256, 256), GUIStyle.none);

            // Read the pixels from the render texture
            snapshot.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            snapshot.Apply();
        }
        else
        {
            // Fill with a default color if no valid preview
            Color[] pixels = new Color[256 * 256];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.3f, 0.3f, 0.3f, 1.0f);
            snapshot.SetPixels(pixels);
            snapshot.Apply();
        }

        // Clean up
        RenderTexture.active = null;

        return snapshot;
    }

    public GameObject AssociatedObject { get; private set; }

    private List<Port> _outputPorts = new List<Port>();
    private List<ToolMediatedPortData> _inputPorts = new List<ToolMediatedPortData>();
    private IMGUIContainer _previewContainer;
    private Editor _gameObjectEditor;
    private bool _needsEditorUpdate = true;

    // Class to hold port and its associated text field
    private class ToolMediatedPortData
    {
        public Port Port;
        public TextField AnnotationField;
        public VisualElement Container;
    }

    public string GetAnnotationTextForPort(Port port)
    {
        var portData = _inputPorts.Find(p => p.Port == port);
        return portData?.AnnotationField?.value ?? "";
    }

    public HapticNode(GameObject go)
    {
        AssociatedObject = go;

        // Truncate long names for display purposes
        title = TruncateNodeTitle(go.name);

        // Set tooltip to show full name on hover
        tooltip = go.name;

        // Create a container for the preview and radio buttons with proper layout
        var previewAndControlsContainer = new VisualElement();
        previewAndControlsContainer.AddToClassList("preview-controls-container");


        // Create a preview container using IMGUI
        _previewContainer = new IMGUIContainer(() => {
            // Check if we need to update the editor
            if (_needsEditorUpdate || _gameObjectEditor == null)
            {
                if (_gameObjectEditor != null)
                {
                    Object.DestroyImmediate(_gameObjectEditor);
                }

                if (AssociatedObject != null)
                {
                    _gameObjectEditor = Editor.CreateEditor(AssociatedObject);
                }

                _needsEditorUpdate = false;
            }

            // Draw the preview
            if (AssociatedObject != null && _gameObjectEditor != null)
            {
                // Calculate the preview rect
                Rect previewRect = GUILayoutUtility.GetRect(150, 150);

                // Draw the preview
                _gameObjectEditor.OnInteractivePreviewGUI(previewRect, GUIStyle.none);
            }
        });

        _previewContainer.AddToClassList("preview-container");

        // Add the preview to the container
        previewAndControlsContainer.Add(_previewContainer);

        // Create the radio button group container
        var radioGroupContainer = new VisualElement();
        radioGroupContainer.AddToClassList("radio-group-container");

        // Add a title for the radio group
        var radioGroupTitle = new Label("Levels of Participation");
        radioGroupTitle.AddToClassList("radio-group-title");
        radioGroupContainer.Add(radioGroupTitle);

        // Create the radio buttons
        var highEngagementRadio = CreateRadioButton("High Engagement", 2, EngagementLevel == 2);
        var mediumEngagementRadio = CreateRadioButton("Medium Engagement", 1, EngagementLevel == 1);
        var lowEngagementRadio = CreateRadioButton("Low Engagement", 0, EngagementLevel == 0);

        // Add the radio buttons to the container
        radioGroupContainer.Add(highEngagementRadio);
        radioGroupContainer.Add(mediumEngagementRadio);
        radioGroupContainer.Add(lowEngagementRadio);

        // Add both containers to the main container
        previewAndControlsContainer.Add(_previewContainer);
        previewAndControlsContainer.Add(radioGroupContainer);

        // Add the container to the node
        mainContainer.Add(previewAndControlsContainer);

        // Create the initial direct port
        AddDirectPort();

        // Create the initial tool-mediated port with its text field
        AddToolMediatedPort();

        // Register for scene changes to update the preview
        EditorApplication.update += OnEditorUpdate;

        // Register for cleanup when the node is removed
        RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

        RefreshExpandedState();
        RefreshPorts();
    }

    private string TruncateNodeTitle(string originalName)
    {
        const int maxLength = 25; // Maximum characters to display

        if (originalName.Length <= maxLength)
            return originalName;

        // Truncate and add ellipsis
        return originalName.Substring(0, maxLength - 3) + "...";
    }

    private VisualElement CreateRadioButton(string label, int value, bool isSelected)
    {
        var container = new VisualElement();
        container.AddToClassList("radio-button-container");

        var radioButton = new Toggle();
        radioButton.value = isSelected;
        radioButton.AddToClassList("radio-button");

        var radioLabel = new Label(label);
        radioLabel.AddToClassList("radio-label");

        container.Add(radioButton);
        container.Add(radioLabel);

        // Add click handler
        radioButton.RegisterValueChangedCallback(evt => {
            if (evt.newValue)
            {
                // Deselect all other radio buttons in the group
                VisualElement parent = container.parent;
                if (parent != null)
                {
                    var allRadioButtons = parent.Query<Toggle>().ToList();
                    foreach (var rb in allRadioButtons)
                    {
                        if (rb != radioButton)
                        {
                            rb.SetValueWithoutNotify(false);
                        }
                    }
                }

                // Set the engagement level
                EngagementLevel = value;
            }
            else
            {
                // Don't allow deselecting without selecting another option
                radioButton.SetValueWithoutNotify(true);
            }
        });

        return container;
    }

    private void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        // Clean up resources
        EditorApplication.update -= OnEditorUpdate;

        // Use a safer approach to clean up the editor
        if (_gameObjectEditor != null)
        {
            try
            {
                // Check if the editor is still valid before destroying it
                if (_gameObjectEditor.target != null)
                {
                    Object.DestroyImmediate(_gameObjectEditor);
                }
            }
            catch (System.Exception e)
            {
                // Log the error but don't let it crash the application
                Debug.LogWarning($"Error cleaning up editor: {e.Message}");
            }
            finally
            {
                _gameObjectEditor = null;
            }
        }
    }

    private void OnEditorUpdate()
    {
        // Mark that we need to update the editor on the next IMGUI pass
        if (AssociatedObject != null)
        {
            // Force a repaint to update the preview
            _previewContainer.MarkDirtyRepaint();
        }
    }

    public void AddDirectPort()
    {
        // Create a new direct port
        var newOutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        newOutputPort.portName = "Direct →";

        // Add to containers
        outputContainer.Add(newOutputPort);
        _outputPorts.Add(newOutputPort);

        Debug.Log($"Added new direct port. Total ports: {_outputPorts.Count}");

        RefreshExpandedState();
        RefreshPorts();

    }

    public void AddToolMediatedPort()
    {
        // Create a container for the port and its text field
        var portContainer = new VisualElement();
        portContainer.style.flexDirection = FlexDirection.Row;
        portContainer.style.alignItems = Align.Center;

        // Create the port
        var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
        inputPort.portName = "→ Mediated";

        // Create the text field (disabled by default) without a label
        var textField = new TextField();
        textField.SetEnabled(false);

        // Remove the label to save space
        textField.label = "";

        // Expand the width to use available space
        textField.style.width = 150;
        textField.style.marginLeft = 2;
        textField.style.marginRight = 5;

        // Add tooltip for better UX
        textField.tooltip = "Expected haptic feedback derived from object contact";

        // Add port to container
        portContainer.Add(inputPort);
        portContainer.Add(textField);

        // Add container to the node's input container
        inputContainer.Add(portContainer);

        // Store the port data
        _inputPorts.Add(new ToolMediatedPortData
        {
            Port = inputPort,
            AnnotationField = textField,
            Container = portContainer
        });

        RefreshExpandedState();
        RefreshPorts();
    }

    // Called by the GraphView when a connection is made to this port
    public void OnPortConnected(Port port)
    {
        Debug.Log($"Port connected on {title}: {port.portName}");

        // Find the corresponding data
        var portData = _inputPorts.Find(p => p.Port == port);
        if (portData != null)
        {
            Debug.Log($"Found port data, enabling text field");
            // Enable the text field
            portData.AnnotationField.SetEnabled(true);

            // Add a new tool-mediated port if this was the last one
            if (_inputPorts.IndexOf(portData) == _inputPorts.Count - 1)
            {
                AddToolMediatedPort();
            }
        }
        else
        {
            Debug.LogWarning($"Could not find port data for {port.portName} on {title}");
        }
    }

    // Called by the GraphView when a port is disconnected
    public void OnPortDisconnected(Port port)
    {
        Debug.Log($"Port disconnected on {title}: {port.portName}");

        // Handle tool-mediated port disconnection
        if (port.direction == Direction.Input)
        {
            var portData = _inputPorts.Find(p => p.Port == port);
            if (portData != null)
            {
                // Disable the text field and clear its value
                portData.AnnotationField.SetEnabled(false);
                portData.AnnotationField.value = "";

                // If this is not the last port, remove it
                int index = _inputPorts.IndexOf(portData);
                if (index < _inputPorts.Count - 1)
                {
                    inputContainer.Remove(portData.Container);
                    _inputPorts.Remove(portData);
                    RefreshPorts();
                }
            }
        }
        // Handle direct port disconnection
        else if (port.direction == Direction.Output)
        {
            Debug.Log($"Direct port disconnected: connected={port.connected}, connections={port.connections.Count()}");

            // Only remove if it has no connections AND it's not the last port
            if (_outputPorts.Count > 1)
            {
                // Check if the port has any remaining connections
                bool hasConnections = port.connections.Count() > 0;

                if (!hasConnections)
                {
                    Debug.Log($"Removing direct port");
                    outputContainer.Remove(port);
                    _outputPorts.Remove(port);
                    RefreshPorts();
                }
            }
        }
    }

    public void PrepareForDeletion()
    {
        Debug.Log($"Preparing node {title} for deletion");

        // Disconnect all input ports
        foreach (var portData in _inputPorts)
        {
            if (portData.Port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = portData.Port.connections.ToList();
                foreach (var connection in connections)
                {
                    // Get the source node before disconnecting
                    var sourceNode = connection.output.node as HapticNode;
                    var sourcePort = connection.output;

                    // Disconnect the connection
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);

                    // Notify the source node about the disconnection
                    if (sourceNode != null)
                    {
                        // Explicitly call OnPortDisconnected on the source node
                        sourceNode.OnPortDisconnected(sourcePort);
                    }
                }
            }
        }

        // Disconnect all output ports
        foreach (var port in _outputPorts)
        {
            if (port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = port.connections.ToList();
                foreach (var connection in connections)
                {
                    // Get the target node before disconnecting
                    var targetNode = connection.input.node as HapticNode;
                    var targetPort = connection.input;

                    // Disconnect the connection
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);

                    // Notify the target node about the disconnection
                    if (targetNode != null)
                    {
                        // Explicitly call OnPortDisconnected on the target node
                        targetNode.OnPortDisconnected(targetPort);
                    }
                }
            }
        }
    }

    // Method to collect all tool-mediated annotations
    public Dictionary<string, string> GetToolMediatedAnnotations()
    {
        var annotations = new Dictionary<string, string>();

        foreach (var portData in _inputPorts)
        {
            if (portData.Port.connected && !string.IsNullOrEmpty(portData.AnnotationField.value))
            {
                // Use the connected node's name as the key
                foreach (var connection in portData.Port.connections)
                {
                    var sourceNode = connection.output.node as HapticNode;
                    if (sourceNode != null)
                    {
                        annotations[sourceNode.AssociatedObject.name] = portData.AnnotationField.value;
                    }
                }
            }
        }

        return annotations;
    }

    public void SetAnnotationTextForPort(Port port, string text)
    {
        var portData = _inputPorts.Find(p => p.Port == port);
        if (portData != null)
        {
            portData.AnnotationField.value = text;
            portData.AnnotationField.SetEnabled(true);
        }
    }

    // Add this method to the HapticNode class
    public void SetEngagementLevel(int level)
    {
        // Validate the level (0-2)
        if (level < 0 || level > 2)
            return;

        // Find the radio buttons in the radio group container
        // We need to use the correct path to find the radio buttons
        var radioContainers = this.Query<VisualElement>(className: "radio-button-container").ToList();

        // If we have the expected radio containers
        if (radioContainers.Count >= 3)
        {
            // Get the toggle from each container
            var radioButtons = new List<Toggle>();
            foreach (var container in radioContainers)
            {
                var toggle = container.Q<Toggle>();
                if (toggle != null)
                {
                    radioButtons.Add(toggle);
                }
            }

            // If we found the radio buttons
            if (radioButtons.Count >= 3)
            {
                // Set the appropriate radio button based on level
                // The radio buttons are in order: High (2), Medium (1), Low (0)
                // So we need to convert the level to the correct index
                int buttonIndex = 2 - level; // Convert level to button index

                // Trigger the radio button click
                radioButtons[buttonIndex].SetValueWithoutNotify(true);

                // Deselect other radio buttons
                for (int i = 0; i < radioButtons.Count; i++)
                {
                    if (i != buttonIndex)
                    {
                        radioButtons[i].SetValueWithoutNotify(false);
                    }
                }

                // Set the engagement level field
                EngagementLevel = level;
            }
        }
        else
        {
            // If we can't find the radio buttons, set the property directly
            // This is a fallback
            EngagementLevel = level;
        }
    }

    // Add this method to the HapticNode class
    public int GetPortIndex(Port port)
    {
        if (port.direction == Direction.Output)
        {
            // Find the index in output ports
            var outputPorts = outputContainer.Query<Port>().ToList();
            return outputPorts.IndexOf(port);
        }
        else
        {
            // Find the index in input ports
            var inputPorts = inputContainer.Query<Port>().ToList();
            return inputPorts.IndexOf(port);
        }
    }

}

public class HapticRelationshipEdge : Edge
{
    private TextField _annotationField; // Inline text field on the edge
    public string Annotation
    {
        get => _annotationField?.value ?? string.Empty;
        set
        {
            if (_annotationField != null)
                _annotationField.value = value;
        }
    }

    public HapticRelationshipEdge() : base()
    {
        // A small container to hold the TextField
        VisualElement annotationContainer = new VisualElement
        {
            style =
        {
            flexDirection = FlexDirection.Row,
            alignItems = Align.Center
        }
        };

        _annotationField = new TextField("Annotation:")
        {
            value = ""
        };
        annotationContainer.Add(_annotationField);

        // Place the annotation container visually in the middle of the edge
        // GraphView doesn't do this automatically, so you can experiment with styling
        Add(annotationContainer);
    }

}

// Add this class to HapticsRelationshipGraphView.cs
public class HapticNodeGroup : GraphElement
{
    private List<HapticNode> _nodes;
    private VisualElement _header;
    private TextField _titleField;
    private string _title = "Node Group";

    public HapticNodeGroup(Rect rect, List<HapticNode> nodes)
    {
        _nodes = new List<HapticNode>(nodes);

        // Set up the group element
        this.SetPosition(rect);
        this.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        this.style.borderLeftWidth = this.style.borderRightWidth =
        this.style.borderTopWidth = this.style.borderBottomWidth = 1;
        this.style.borderLeftColor = this.style.borderRightColor =
        this.style.borderTopColor = this.style.borderBottomColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);

        // Create the header
        _header = new VisualElement();
        _header.style.height = 24;
        _header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        _header.style.flexDirection = FlexDirection.Row;
        _header.style.alignItems = Align.Center;
        _header.style.paddingLeft = 8;
        _header.style.paddingRight = 8;

        // Add a title field
        _titleField = new TextField();
        _titleField.value = _title;
        _titleField.style.flexGrow = 1;
        _titleField.RegisterValueChangedCallback(evt => _title = evt.newValue);

        // Add the title field to the header
        _header.Add(_titleField);

        // Add the header to the group
        Add(_header);

        // Make the group selectable
        capabilities |= Capabilities.Selectable | Capabilities.Movable | Capabilities.Deletable;

        // Register for position change events to move the contained nodes
        RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    // Track the initial positions of nodes relative to the group
    private Dictionary<HapticNode, Vector2> _nodeOffsets = new Dictionary<HapticNode, Vector2>();
    private Vector2 _lastPosition;

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        // If this is the first time, initialize the node offsets
        if (_nodeOffsets.Count == 0)
        {
            Rect groupRect = GetPosition();
            _lastPosition = new Vector2(groupRect.x, groupRect.y);

            foreach (var node in _nodes)
            {
                Rect nodeRect = node.GetPosition();
                _nodeOffsets[node] = new Vector2(
                    nodeRect.x - groupRect.x,
                    nodeRect.y - groupRect.y
                );
            }
        }
        else
        {
            // Calculate how much the group has moved
            Rect groupRect = GetPosition();
            Vector2 newPosition = new Vector2(groupRect.x, groupRect.y);
            Vector2 delta = newPosition - _lastPosition;

            // Only move nodes if the group actually moved
            if (delta.sqrMagnitude > 0.001f)
            {
                // Move all nodes by the same delta
                foreach (var node in _nodes)
                {
                    Rect nodeRect = node.GetPosition();
                    nodeRect.x += delta.x;
                    nodeRect.y += delta.y;
                    node.SetPosition(nodeRect);
                }

                _lastPosition = newPosition;
            }
        }
    }

    // Override the hit test to allow clicking through the body (but not the header)
    public override bool ContainsPoint(Vector2 localPoint)
    {
        // Always hit test the header
        if (localPoint.y <= _header.layout.height)
            return true;

        // For the body, only return true if we're near the border
        float borderWidth = 5f;
        Rect rect = GetPosition();
        rect.x = rect.y = 0; // Convert to local space

        bool nearBorder =
            localPoint.x <= borderWidth ||
            localPoint.x >= rect.width - borderWidth ||
            localPoint.y <= borderWidth ||
            localPoint.y >= rect.height - borderWidth;

        return nearBorder;
    }
}