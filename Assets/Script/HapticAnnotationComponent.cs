using UnityEngine;

// Make the component appear in the Add Component menu under "Haptics"
[AddComponentMenu("Haptics/Haptic Annotation")]
public class HapticAnnotationComponent : MonoBehaviour
{
    // Reference to the haptic annotation graph asset
    [SerializeField] private HapticAnnotationGraph graph;

    // Property to access the graph from code
    public HapticAnnotationGraph Graph
    {
        get { return graph; }
        set { graph = value; }
    }

    // Optional: Add methods to interact with the graph at runtime if needed
}