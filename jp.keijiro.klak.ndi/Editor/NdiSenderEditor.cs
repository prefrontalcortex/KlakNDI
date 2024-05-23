using System;
using System.Collections.Generic;
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

    private AudioSource[] _audioSourcesInScene;
    private Vector2 _audioSourcesScrollPos;

    void OnEnable()
    {
        AutoProperty.Scan(this);
        SearchForAudioSources();
    }

    private void SearchForAudioSources()
    {
        _audioSourcesInScene = Array.Empty<AudioSource>();
        if (((NdiSender.AudioMode)audioMode.Target.enumValueIndex) != NdiSender.AudioMode.AudioListener)
        {
            var audioSources = GameObject.FindObjectsByType<AudioSource>( FindObjectsInactive.Include, FindObjectsSortMode.None);
            var audioSourcesList = new List<AudioSource>();
            foreach (var a in audioSources)
            {
                if (a.GetComponent<AudioSourceListener>() == null)
                {
                    audioSourcesList.Add(a);
                }
            }
            _audioSourcesInScene = audioSourcesList.ToArray();
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // NDI Name
        if (_captureMethod.Target.hasMultipleDifferentValues ||
            _captureMethod.Target.enumValueIndex != (int)CaptureMethod.GameView)
            EditorGUILayout.DelayedTextField(_ndiName, Labels.NdiName);

        EditorGUI.BeginChangeCheck(); 
        EditorGUILayout.PropertyField(audioMode);
        if (EditorGUI.EndChangeCheck())
        {
            if (audioMode.Target.enumValueIndex != (int)NdiSender.AudioMode.AudioListener && _audioSourcesInScene.Length == 0)
            {
                SearchForAudioSources();
            }
        }
        var audioModeEnum = (NdiSender.AudioMode)audioMode.Target.enumValueIndex;
        if (audioModeEnum != NdiSender.AudioMode.AudioListener)
        {
            if (audioModeEnum == NdiSender.AudioMode.TryOrForce5point1 ||
                audioModeEnum == NdiSender.AudioMode.TryOrForce7point1 ||
                audioModeEnum == NdiSender.AudioMode.TryOrForceQuad)
            {
                EditorGUILayout.HelpBox("If the audio device is not supporting quad/5.1/7.1, it will create virtual Audio Listener to emulate it.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("This AudioMode will create virtual AudioListeners and is not using the Unity buildin spatializer and AudioListener.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Virtual AudioListeners does not supporting the AudioMixer. Each AudioSource needs the "+nameof(AudioSourceListener)+" Component, otherwise it will not be captured. "+ System.Environment.NewLine +
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

        if (_audioSourcesInScene.Length > 0 && audioModeEnum != NdiSender.AudioMode.AudioListener)
        {
            GUI.backgroundColor = Color.cyan;
            GUILayout.Space(30);
            EditorGUILayout.BeginVertical(GUI.skin.window);
            GUILayout.Space(-20);
            GUILayout.Label("Virtual Audio", EditorStyles.boldLabel);
            GUILayout.Label("Missing AudioSourceListener Components:", EditorStyles.boldLabel);

            _audioSourcesScrollPos = EditorGUILayout.BeginScrollView(_audioSourcesScrollPos, GUILayout.MaxHeight(200));
            for (int i = 0; i < _audioSourcesInScene.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.LinkButton(_audioSourcesInScene[i].name))
                {
                    Selection.activeGameObject = _audioSourcesInScene[i].gameObject;
                }
                GUILayout.FlexibleSpace();
                //EditorGUILayout.LabelField(_audioSourcesInScene[i].name);
                if (GUILayout.Button("Add AudioSourceListener"))
                {
                    _audioSourcesInScene[i].gameObject.AddComponent<AudioSourceListener>();
                    ArrayUtility.RemoveAt(ref _audioSourcesInScene, i);
                    return;
                }
                
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            if (GUILayout.Button("Add to all"))
            {
                foreach (var audioSource in _audioSourcesInScene)
                {
                    audioSource.gameObject.AddComponent<AudioSourceListener>();
                }
                _audioSourcesInScene = Array.Empty<AudioSource>();
            }
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = Color.white;
        }
    }
}

} // namespace Klak.Ndi.Editor
