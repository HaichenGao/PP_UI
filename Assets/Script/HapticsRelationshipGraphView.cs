using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Collections.Generic;
using System.Linq;

public class HapticsRelationshipGraphView : GraphView
{
    private readonly List<HapticNode> _nodes = new List<HapticNode>();

    public HapticsRelationshipGraphView()
    {
        style.flexGrow = 1;

        // Setup basic manipulators
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Optional background grid
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

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
        // Handle new edges (connections)
        if (change.edgesToCreate != null)
        {
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
        if (change.elementsToRemove != null)
        {
            // First, prepare nodes for deletion to ensure edges are properly disconnected
            foreach (var element in change.elementsToRemove)
            {
                if (element is HapticNode node)
                {
                    Debug.Log($"Preparing node for deletion: {node.title}");
                    node.PrepareForDeletion();

                    // Update our nodes list
                    _nodes.Remove(node);
                }
                else if (element is Edge edge)
                {
                    Debug.Log($"Removing edge: {edge.output.node.title} -> {edge.input.node.title}");

                    // Notify the input port's node about the disconnection
                    if (edge.input.node is HapticNode inputNode)
                    {
                        inputNode.OnPortDisconnected(edge.input);
                    }

                    // Notify the output port's node about the disconnection
                    if (edge.output.node is HapticNode outputNode)
                    {
                        outputNode.OnPortDisconnected(edge.output);
                    }
                }
            }
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
                directContact = hNode.DirectContact,
                expectedWeight = hNode.ExpectedWeight
                // Add other fields as needed
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
}

// Minimal data structure to hold annotation data
[System.Serializable]
public class HapticAnnotationData
{
    public List<HapticObjectRecord> nodeAnnotations;
    public List<HapticConnectionRecord> relationshipAnnotations;
}

[System.Serializable]
public class HapticObjectRecord
{
    public string objectName;
    public bool directContact;
    public float expectedWeight;
    // Extend as necessary to store additional haptic parameters
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
    public GameObject AssociatedObject { get; private set; }
    public bool DirectContact { get; set; }
    public float ExpectedWeight { get; set; }

    private Port _outputPort;
    private List<ToolMediatedPortData> _inputPorts = new List<ToolMediatedPortData>();

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

        // Output port (e.g., for a Direct Contact Object)
        _outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        _outputPort.portName = "Direct →";
        outputContainer.Add(_outputPort);

        // Create the initial tool-mediated port with its text field
        AddToolMediatedPort();

        // Example UI elements on the node
        var directContactToggle = new Toggle("Direct Contact")
        {
            value = false
        };
        directContactToggle.RegisterValueChangedCallback(evt =>
        {
            DirectContact = evt.newValue;
        });
        mainContainer.Add(directContactToggle);

        var weightField = new FloatField("Expected Weight (kg)")
        {
            value = 1.0f
        };
        weightField.RegisterValueChangedCallback(evt =>
        {
            ExpectedWeight = evt.newValue;
        });
        mainContainer.Add(weightField);

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
        inputPort.portName = "← Tool-Mediated";

        // Create the text field (disabled by default) without a label
        var textField = new TextField();
        textField.SetEnabled(false);

        // Remove the label to save space
        textField.label = "";

        // Expand the width to use available space
        textField.style.width = 180;
        textField.style.marginLeft = 5;
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
            // Only remove if it's not the first/original port and has no connections
            if (outputContainer.IndexOf(port) > 0 && !port.connected)
            {
                outputContainer.Remove(port);
                RefreshPorts();
            }
        }

    }

    public void PrepareForDeletion()
    {
        // Disconnect all ports before node is deleted
        // This ensures edges will be properly removed

        // Disconnect all input ports
        foreach (var portData in _inputPorts)
        {
            if (portData.Port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = portData.Port.connections.ToList();
                foreach (var connection in connections)
                {
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);
                }
            }
        }

        // Disconnect all output ports
        foreach (var port in outputContainer.Children().OfType<Port>())
        {
            if (port.connected)
            {
                // Create a copy of connections to avoid modification during enumeration
                var connections = port.connections.ToList();
                foreach (var connection in connections)
                {
                    connection.output.Disconnect(connection);
                    connection.input.Disconnect(connection);
                }
            }
        }
    }

    public void AddDirectPort()
    {
        // Always add a new direct port when requested
        var newOutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        newOutputPort.portName = "Direct →";
        outputContainer.Add(newOutputPort);

        RefreshExpandedState();
        RefreshPorts();
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