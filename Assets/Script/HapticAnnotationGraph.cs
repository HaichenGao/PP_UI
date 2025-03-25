using UnityEngine;
using System;
using System.Collections.Generic;

// Make the class properly serializable by Unity
[CreateAssetMenu(fileName = "New Haptic Annotation Graph", menuName = "Haptics/Haptic Annotation Graph")]
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