#if (UNITY_EDITOR) 

using System;
using System.Collections.Generic;
using Mirage;
using MirageReactiveExtensions.Runtime;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(NetworkBehaviour), true)]
[CanEditMultipleObjects]
public class CustomNetworkBehaviourEditor : NetworkBehaviourInspector
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = BaseCreation();
        var oneFound = false;
        var so = new SerializedObject(target);

        foreach (var field in InspectorHelper.GetAllFields(target.GetType(), typeof(NetworkBehaviour)))
        {
            if (typeof(ISyncLink).IsAssignableFrom(field.FieldType))
            {
                if (!oneFound)
                {
                    root.Add(SyncListDrawer.CreateHeader("Sync Links"));
                    oneFound = true;
                }

                var tpe = field.FieldType.GetGenericArguments()[0];
                var value = field.GetValue(target);
                var propertyField = new ObjectField(field.Name);
                propertyField.SetValueWithoutNotify((dynamic)value);
                propertyField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == null)
                    {
                        ((dynamic)value).Value = null;
                    }
                    else
                    {
                        var comp = evt.newValue.GetComponent(tpe);

                        if (comp != null)
                        {
                            var conv = Convert.ChangeType(comp, tpe);
                            ((dynamic)value).Value = (dynamic)conv;
                        }
                        else
                        {
                            propertyField.SetValueWithoutNotify(null);
                            ((dynamic)value).Value = null;
                        }
                    }

                    var sp = so.FindProperty(field.Name);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                });
                propertyField.label = field.Name;
                root.Add(propertyField);
            }
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

    private static readonly HashSet<string> SkippedTypes = new(new[] { "SyncLink`1" });

    private VisualElement BaseCreation()
    {
        var root = new VisualElement();

        // Create the default inspector.
        var iterator = serializedObject.GetIterator();
        for (var enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
        {
            if (SkippedTypes.Contains(iterator.type)) continue;

            var field = new PropertyField(iterator);

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
}

#endif