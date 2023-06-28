#if (UNITY_EDITOR)

using Mirage;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(NetworkBehaviour), true)]
[CanEditMultipleObjects]
public class CustomNetworkBehaviourEditor : NetworkBehaviourInspector
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        var iterator = serializedObject.GetIterator();
        for (var enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
        {
            var field = iterator.type is "SyncVar`1" or "SyncLink`1"
                ? new PropertyField(iterator.FindPropertyRelative("latestValue"), iterator.displayName)
                : new PropertyField(iterator);

            // Disable the script field.
            if (iterator.propertyPath == "m_Script")
            {
                field.SetEnabled(false);
            }

            root.Add(field);
        }

        // Create the sync lists editor.
        var syncLists = _syncListDrawer.Create();
        if (syncLists != null)
        {
            root.Add(syncLists);
        }

        return root;
    }

    private SyncListDrawer _syncListDrawer;

    private void OnEnable()
    {
        // If target's base class is changed from NetworkBehaviour to MonoBehaviour
        // then Unity temporarily keep using this Inspector causing things to break
        if (!(target is NetworkBehaviour))
        {
            return;
        }

        _syncListDrawer = new SyncListDrawer(serializedObject.targetObject);
    }
}

#endif
