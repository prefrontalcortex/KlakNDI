using Klak.Ndi.Audio;
using UnityEngine;
using UnityEditor;

namespace Klak.Ndi.Editor {

[CanEditMultipleObjects]
[CustomEditor(typeof(NdiSender))]
sealed class NdiSenderEditor : UnityEditor.Editor
{
    static class Labels
    {
        public static Label NdiName = "NDI Name";
    }

    #pragma warning disable CS0649

    AutoProperty _ndiName;
    AutoProperty _keepAlpha;
    AutoProperty _captureMethod;
    AutoProperty _sourceCamera;
    AutoProperty _sourceTexture;
    AutoProperty audioMode;
    AutoProperty virtualListenerDistance;
    private AutoProperty useCameraPositionForVirtualAttenuation;

    #pragma warning restore

    void OnEnable() => AutoProperty.Scan(this);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // NDI Name
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex != (int)CaptureMethod.GameView)
            EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        EditorGUILayout.PropertyField(audioMode);
        if (audioMode.Target.enumValueIndex != 0)
        {
            var mode = (NdiSender.AudioMode)audioMode.Target.enumValueIndex;
            if (mode == NdiSender.AudioMode.TryOrForce5point1 ||
                mode == NdiSender.AudioMode.TryOrForce7point1 ||
                mode == NdiSender.AudioMode.TryOrForceQuad)
            {
                EditorGUILayout.HelpBox("If the audio device is not supporting quad/5.1/7.1, it will create virtual Audio Listener to emulate it.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("This AudioMode will create virtual AudioListeners and is not using the Unity buildin spatializer and AudioListener.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Virtual AudioListeners does not support the AudioMixer. Each AudioSource needs the "+nameof(AudioSourceListener)+" Component, otherwise it will not be captured. "+ System.Environment.NewLine +
                "Also the Dummy Spatializer Plugin is required in the Audio Settings to bypass any spatialized data modifcations made by Unity.", MessageType.Info);
            EditorGUILayout.PropertyField(virtualListenerDistance);
            EditorGUILayout.PropertyField(useCameraPositionForVirtualAttenuation);
        }
        else
        {
            EditorGUILayout.HelpBox("This AudioMode will capture all audio from the AudioListener. The channel amount depends on the audio device capabilities and the Unity Audio Settings.", MessageType.Info);
        }
        
        // Keep Alpha
        EditorGUILayout.PropertyField(_keepAlpha);

        // Capture Method
        EditorGUILayout.PropertyField(_captureMethod);

        EditorGUI.indentLevel++;

        // Source Camera
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Camera)
            EditorGUILayout.PropertyField(_sourceCamera);

        // Source Texture
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex == (int)CaptureMethod.Texture)
            EditorGUILayout.PropertyField(_sourceTexture);

        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}

} // namespace Klak.Ndi.Editor
