using System;
using System.Linq;
using Klak.Ndi.Audio;
using UnityEditor;
using UnityEngine;

namespace Klak.Ndi.Editor
{
    [CustomEditor(typeof(AudioSource))]
    [CanEditMultipleObjects]
    public class AudioSourceHelper : UnityEditor.Editor
    {
        private UnityEditor.Editor _defaultEditor;
        private AudioSource[] _audioSources;
        private bool _allHasListener = false;
        private bool[] _hasListener;
        
        private NdiSender[] _ndiSenders;
        private bool _ignoreListenerCheck = false;
        
        private void OnEnable()
        {
            if (targets == null || targets.Length == 0)
                return;

            var forType = Type.GetType("UnityEditor.AudioSourceInspector, UnityEditor");
            if (forType == null)
                return;
            _defaultEditor = CreateEditor(targets, forType);
            if (!_defaultEditor)
                return;

            if (!Application.isPlaying)
            {
                _ndiSenders = FindObjectsOfType<NdiSender>();
                _ignoreListenerCheck = _ndiSenders.Any(sender => sender.addMissingAudioSourceListenersAtRuntime);
                UpdateChecks();
            }
        }

        private void OnDisable()
        {
            if (_defaultEditor)
                DestroyImmediate(_defaultEditor);
        }

        private void UpdateChecks()
        {
            _audioSources = new AudioSource[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                _audioSources[i] = targets[i] as AudioSource;
            }

            _hasListener = new bool[_audioSources.Length];

            int listenerCount = 0;
            for (int i = 0; i < _audioSources.Length; i++)
            {
                _hasListener[i] = _audioSources[i].GetComponent<AudioSourceListener>();
                if (_hasListener[i])
                    listenerCount++;
            }

            _allHasListener = listenerCount == _hasListener.Length;
        }

        public override void OnInspectorGUI()
        {
            if (!_defaultEditor)
                _defaultEditor = CreateEditor(targets, Type.GetType("UnityEditor.AudioSourceInspector, UnityEditor"));
            if (!_defaultEditor)
                return;
            
            _defaultEditor.OnInspectorGUI();
            
            if (!_ignoreListenerCheck && !_allHasListener && !Application.isPlaying)
            {
                GUILayout.Space(30);
                GUI.backgroundColor = Color.cyan;
                EditorGUILayout.BeginVertical(GUI.skin.window);
                GUILayout.Space(-20);

                GUILayout.Label("NDI Virtual Audio", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("When using Virtual Audio on NDISender, you need to add an AudioSourceListener Component to AudioSources that should be captured", MessageType.Info);
                GUILayout.FlexibleSpace()
                    ;
                if (GUILayout.Button("Add AudioSourceListener"))
                {
                    for (int i = 0; i < _audioSources.Length; i++)
                    {
                        if (!_hasListener[i])
                            _audioSources[i].gameObject.AddComponent<AudioSourceListener>();
                    }

                    UpdateChecks();
                }

                EditorGUILayout.EndVertical();
                GUI.color = Color.white;
            }
        }
    }
}