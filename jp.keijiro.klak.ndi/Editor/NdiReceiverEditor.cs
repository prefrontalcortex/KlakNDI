using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiReceiver))]
sealed class NdiReceiverEditor : UnityEditor.Editor
{
    static class Labels
    {
        public static Label NdiName = "NDI Name";
        public static Label Property = "Property";
        public static Label Select = "Select";
    }

    #pragma warning disable CS0649

    AutoProperty _ndiName;
    AutoProperty _targetTexture;
    AutoProperty _targetRenderer;
    AutoProperty _targetMaterialProperty;
    AutoProperty _audioSource;
    AutoProperty _createVirtualSpeakers;

    #pragma warning restore

    // NDI name dropdown
    void ShowNdiNameDropdown(Rect rect)
    {
        var menu = new GenericMenu();
        var sources = NdiFinder.sourceNames;

        if (sources.Any())
        {
            foreach (var name in sources)
                menu.AddItem(new GUIContent(name), false, OnSelectName, name);
        }
        else
        {
            menu.AddItem(new GUIContent("No source available"), false, null);
        }

        menu.DropDown(rect);
    }

    // NDI source name selection callback
    void OnSelectName(object name)
    {
        serializedObject.Update();
        _ndiName.Target.stringValue = (string)name;
        serializedObject.ApplyModifiedProperties();
    }

    // Request receiver restart.
    //void RequestRestart()
    //{
    //    foreach (NdiReceiver receiver in targets) receiver.Restart();
    //}

    void OnEnable()
    {
            /*
        var finder = new PropertyFinder(serializedObject);
        _ndiName = finder["_ndiName"];
        _targetTexture = finder["_targetTexture"];
        _targetRenderer = finder["_targetRenderer"];
        _targetMaterialProperty = finder["_targetMaterialProperty"];
        _audioSource = finder["_audioSource"];
            */
        AutoProperty.Scan(this);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.BeginHorizontal();

        // NDI Name
        EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        // NDI name dropdown
        var rect = EditorGUILayout.GetControlRect(false, GUILayout.Width(60));
        if (EditorGUI.DropdownButton(rect, Labels.Select, FocusType.Keyboard))
            ShowNdiNameDropdown(rect);

        EditorGUILayout.EndHorizontal();

        // Target Texture/Renderer
        EditorGUILayout.PropertyField(_targetTexture);
        EditorGUILayout.PropertyField(_targetRenderer);

        EditorGUI.indentLevel++;

        if (_targetRenderer.Target.hasMultipleDifferentValues)
        {
            // Multiple renderers selected: Show the simple text field.
            EditorGUILayout.
              PropertyField(_targetMaterialProperty, Labels.Property);
        }
        else if (_targetRenderer.Target.objectReferenceValue != null)
        {
            // Single renderer: Show the material property selection dropdown.
            MaterialPropertySelector.
              DropdownList(_targetRenderer, _targetMaterialProperty);
        }

        EditorGUI.indentLevel--;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_audioSource);
        EditorGUILayout.PropertyField(_createVirtualSpeakers);
        var audioSourceChanged = EditorGUI.EndChangeCheck();

        serializedObject.ApplyModifiedProperties();

        // if (restart) RequestRestart();

        if (audioSourceChanged)
            foreach (NdiReceiver receiver in targets) receiver.CheckAudioSource();
    }
}

} // namespace Klak.Ndi.Editor
