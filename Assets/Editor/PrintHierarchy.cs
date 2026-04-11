using UnityEditor;
using UnityEngine;

public class PrintHierarchy
{
    public static void Print()
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var c in canvases) {
            if(c.name == "SessionMenuCanvas") {
                string s = "Hierarchy of SessionMenuCanvas:\n";
                s += GetHierarchy(c.transform, 0);
                Debug.LogError("HIERARCHY_DUMP:\n" + s);
                return;
            }
        }
        Debug.LogError("HIERARCHY_DUMP: Canvas not found");
    }

    static string GetHierarchy(Transform t, int depth)
    {
        string s = new string(' ', depth * 2) + t.name;
        var comps = t.GetComponents<Component>();
        string cNames = "";
        foreach(var c in comps) {
            if(c != null) cNames += c.GetType().Name + ", ";
        }
        s += " [" + cNames + "]\n";
        for(int i = 0; i < t.childCount; i++)
            s += GetHierarchy(t.GetChild(i), depth + 1);
        return s;
    }
}