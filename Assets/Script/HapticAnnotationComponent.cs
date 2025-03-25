using UnityEngine;
using System.Collections.Generic;
using System;

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

// Define the graph asset type that will store our haptic annotation data
[Serializable]
public class HapticAnnotationGraph : ScriptableObject
{
    // Store the graph data as JSON
    [SerializeField] private string graphData;

    // Store references to GameObjects in the graph
    [SerializeField] private List<GameObject> referencedObjects = new List<GameObject>();

    // Store the graph summary
    [SerializeField] private string summary;

    // Properties to access the data
    public string GraphData
    {
        get { return graphData; }
        set { graphData = value; }
    }

    public List<GameObject> ReferencedObjects
    {
        get { return referencedObjects; }
    }

    public string Summary
    {
        get { return summary; }
        set { summary = value; }
    }

    // Method to add a referenced object
    public void AddReferencedObject(GameObject obj)
    {
        if (!referencedObjects.Contains(obj))
        {
            referencedObjects.Add(obj);
        }
    }

    // Method to remove a referenced object
    public void RemoveReferencedObject(GameObject obj)
    {
        referencedObjects.Remove(obj);
    }
}
