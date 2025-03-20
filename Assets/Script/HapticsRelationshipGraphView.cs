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

    public HapticsRelationshipGraphView()
    {
        style.flexGrow = 1;

        // Setup basic manipulators
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Create and add the grid background
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        // Style the grid to match Script Graph
        grid.AddToClassList("grid-background");

        // Register for graph changes to handle connections
        graphViewChanged = OnGraphViewChanged;
    }

    public void ClearGraph()
    {
        DeleteElements(graphElements);
        _nodes.Clear();
    }

    public void AddGameObjectNode(GameObject obj, Vector2 dropPosition = default(Vector2))
    {
        // Create a new HapticNode (custom node) with a reference to GameObject
        var node = new HapticNode(obj);
        node.SetPosition(new Rect(dropPosition.x, dropPosition.y, 200, 150)); 
        AddElement(node);
        _nodes.Add(node);
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

        // Handle element removals (nodes and edges)
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
                engagementLevel = hNode.EngagementLevel
            });

            // Collect tool-mediated annotations
            var toolMediatedAnnotations = hNode.GetToolMediatedAnnotations();
            foreach (var kvp in toolMediatedAnnotations)
            {
                data.relationshipAnnotations.Add(new HapticConnectionRecord
                {
                    fromObjectName = kvp.Key,
                    toObjectName = hNode.AssociatedObject.name,
                    annotationText = kvp.Value
                });
            }
        }

        // We still process edge annotations for backward compatibility
        foreach (var edge in edges)
        {
            // Make sure it's a HapticRelationshipEdge
            if (edge is HapticRelationshipEdge hapticEdge)
            {
                var fromNode = hapticEdge.output.node as HapticNode;
                var toNode = hapticEdge.input.node as HapticNode;

                if (fromNode != null && toNode != null)
                {
                    // Only add if we don't already have this relationship from the node data
                    bool alreadyExists = data.relationshipAnnotations.Any(r =>
                        r.fromObjectName == fromNode.AssociatedObject.name &&
                        r.toObjectName == toNode.AssociatedObject.name);

                    if (!alreadyExists)
                    {
                        data.relationshipAnnotations.Add(new HapticConnectionRecord
                        {
                            fromObjectName = fromNode.AssociatedObject.name,
                            toObjectName = toNode.AssociatedObject.name,
                            annotationText = hapticEdge.Annotation
                        });
                    }
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
}

// Minimal data structure to hold annotation data
[System.Serializable]
public class HapticAnnotationData
{
    public string title;
    public string summary;
    public List<HapticObjectRecord> nodeAnnotations;
    public List<HapticConnectionRecord> relationshipAnnotations;
}

[System.Serializable]
public class HapticObjectRecord
{
    public string objectName;
    public string inertia;
    public string interactivity;
    public string outline;
    public string texture;
    public string hardness;
    public string temperature;
    public int engagementLevel;
}

// Record for each connection
[System.Serializable]
public class HapticConnectionRecord
{
    public string fromObjectName;
    public string toObjectName;
    public string annotationText;
}

// Example node class: represents one VR object in the graph
public class HapticNode : Node
{
    // Add a delegate and event for engagement level changes
    public delegate void EngagementLevelChangedEventHandler(HapticNode node, int newLevel);
    public static event EngagementLevelChangedEventHandler OnEngagementLevelChanged;

    public string Inertia { get; set; } = "";
    public string Interactivity { get; set; } = "";
    public string Outline { get; set; } = "";
    public string Texture { get; set; } = "";
    public string Hardness { get; set; } = "";
    public string Temperature { get; set; } = "";

    private int _engagementLevel = 1; // Default to Medium Engagement (index 1)

    public int EngagementLevel
    {
        get => _engagementLevel;
        private set
        {
            if (_engagementLevel != value)
            {
                _engagementLevel = value;
                // Trigger the event when engagement level changes
                OnEngagementLevelChanged?.Invoke(this, value);
            }
        }
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

    public HapticNode(GameObject go)
    {
        AssociatedObject = go;
        title = go.name;

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

    private void AddToolMediatedPort()
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