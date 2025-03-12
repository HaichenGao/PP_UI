using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Collections.Generic;

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
        if (change.edgesToCreate != null)
        {
            for (int i = 0; i < change.edgesToCreate.Count; i++)
            {
                // The default edge is a standard Edge; we want ours
                Edge originalEdge = change.edgesToCreate[i];

                var newEdge = new HapticRelationshipEdge
                {
                    output = originalEdge.output,
                    input = originalEdge.input
                };

                // Connect up the ports properly
                newEdge.output.Connect(newEdge);
                newEdge.input.Connect(newEdge);

                // Actually add the new edge to the GraphView
                AddElement(newEdge);
            }

            // Clear out the original edges so GraphView doesn't add them again
            change.edgesToCreate.Clear();
        }
        return change;
    }

    public HapticAnnotationData CollectAnnotationData()
    {
        // Example of collecting annotation data from each node
        var data = new HapticAnnotationData();
        data.nodeAnnotations = new List<HapticObjectRecord>();

        foreach (var hNode in _nodes)
        {
            data.nodeAnnotations.Add(new HapticObjectRecord
            {
                objectName = hNode.AssociatedObject.name,
                directContact = hNode.DirectContact,
                expectedWeight = hNode.ExpectedWeight
                // Add other fields as needed
            });
        }

        foreach (var edge in edges)
        {
            // Make sure it's a HapticRelationshipEdge
            if (edge is HapticRelationshipEdge hapticEdge)
            {
                var fromNode = hapticEdge.output.node as HapticNode;
                var toNode = hapticEdge.input.node as HapticNode;

                if (fromNode != null && toNode != null)
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

        // If you have edge-based relationships, parse them here
        // e.g., for each edge, see which nodes it connects and record the relationship data
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
    private Port _inputPort;
    public HapticNode(GameObject go)
    {
        AssociatedObject = go;
        title = go.name;

        // Output port (e.g., for a Direct Contact Object)
        _outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        _outputPort.portName = "Direct →";
        outputContainer.Add(_outputPort);

        // Input port (e.g., for a Tool-Mediated Object)
        _inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
        _inputPort.portName = "← Tool-Mediated";
        inputContainer.Add(_inputPort);

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