// Place this file inside a folder named "Editor" anywhere in your Assets
// (e.g. Assets/Scripts/Editor/TaskStepDrawer.cs)

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TaskStep), true)]
public class TaskStepDrawer : PropertyDrawer
{
    // Cache of all concrete (non-abstract) TaskStep subclasses
    private static List<Type> _stepTypes;

    private static List<Type> StepTypes
    {
        get
        {
            if (_stepTypes == null)
            {
                _stepTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => typeof(TaskStep).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    .OrderBy(t => t.Name)
                    .ToList();
            }
            return _stepTypes;
        }
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Only works with SerializeReference-backed fields
        if (property.propertyType != SerializedPropertyType.ManagedReference)
        {
            EditorGUI.LabelField(position, label.text, "Use [SerializeReference] on the list field.");
            EditorGUI.EndProperty();
            return;
        }

        Rect dropdownRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        // Figure out the currently assigned type (or "None")
        string currentTypeName = property.managedReferenceFullTypename;
        Type currentType = string.IsNullOrEmpty(currentTypeName)
            ? null
            : StepTypes.FirstOrDefault(t => currentTypeName.EndsWith(t.Name));

        string buttonLabel = currentType != null ? currentType.Name : "<Select Step Type>";

        if (EditorGUI.DropdownButton(dropdownRect, new GUIContent(buttonLabel), FocusType.Keyboard))
        {
            GenericMenu menu = new GenericMenu();
            foreach (var type in StepTypes)
            {
                Type capturedType = type;
                menu.AddItem(new GUIContent(type.Name), type == currentType, () =>
                {
                    property.serializedObject.Update();
                    property.managedReferenceValue = Activator.CreateInstance(capturedType);
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        // Draw the fields of whichever concrete type is currently selected
        if (currentType != null)
        {
            EditorGUI.indentLevel++;
            Rect fieldRect = new Rect(position.x, dropdownRect.yMax + 2, position.width, 0);
            SerializedProperty endProp = property.GetEndProperty();
            SerializedProperty childProp = property.Copy();
            childProp.NextVisible(true); // step into the object's fields

            float y = fieldRect.y;
            while (!SerializedProperty.EqualContents(childProp, endProp))
            {
                float h = EditorGUI.GetPropertyHeight(childProp, true);
                Rect r = new Rect(position.x, y, position.width, h);
                EditorGUI.PropertyField(r, childProp, true);
                y += h + 2;
                if (!childProp.NextVisible(false)) break;
            }
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight + 2;

        if (property.propertyType != SerializedPropertyType.ManagedReference)
            return height;

        string currentTypeName = property.managedReferenceFullTypename;
        Type currentType = string.IsNullOrEmpty(currentTypeName)
            ? null
            : StepTypes.FirstOrDefault(t => currentTypeName.EndsWith(t.Name));

        if (currentType != null)
        {
            SerializedProperty endProp = property.GetEndProperty();
            SerializedProperty childProp = property.Copy();
            childProp.NextVisible(true);

            while (!SerializedProperty.EqualContents(childProp, endProp))
            {
                height += EditorGUI.GetPropertyHeight(childProp, true) + 2;
                if (!childProp.NextVisible(false)) break;
            }
        }

        return height;
    }
}