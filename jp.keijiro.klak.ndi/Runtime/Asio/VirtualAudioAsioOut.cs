using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NAudio.Wave;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Klak.Ndi.Audio.NAudio
{
    [AddComponentMenu("NDI/ASIO Out")]
    public class VirtualAudioAsioOut : MonoBehaviour
    {
        public enum SenderReceiverMode
        {
            NdiSender,
            NdiReceiver,
            AudioListener
        }
        
        public SenderReceiverMode senderReceiverMode = SenderReceiverMode.NdiSender;
        [SerializeField] private NdiReceiver _receiver;

        [SerializeField] private string _defaultDeviceName = "";
        
        private string[] _driverNames;
        private IDisposable _sampleProvider;
        private AsioOut _asioOut;

        public string[] DriverNames => _driverNames;
        
        private string _currentDriverName;
        
        public void SetAsioDevice(string name)
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _sampleProvider.Dispose();
            }

            _currentDriverName = name;
            _asioOut = new AsioOut(_currentDriverName);
            
            ISampleProvider virtualAudioSampleProvider;

            switch (senderReceiverMode)
            {
                case SenderReceiverMode.NdiSender:
                    virtualAudioSampleProvider = new VirtualAudioSampleProvider(_asioOut);
                    break;
                case SenderReceiverMode.NdiReceiver:
                    virtualAudioSampleProvider = new ReceiverSampleProvider(_asioOut, _receiver);
                    break;
                case SenderReceiverMode.AudioListener:
                    virtualAudioSampleProvider = new AudioListenerSampleProvider(_asioOut);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _sampleProvider = virtualAudioSampleProvider as IDisposable;
            _asioOut.Init(virtualAudioSampleProvider);
            _asioOut.Play();
        }
        
        private void Awake()
        {
            _currentDriverName = _defaultDeviceName;
        }

        private void OnEnable()
        {
            SetAsioDevice(_currentDriverName);
        }

        private void OnDisable()
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _sampleProvider.Dispose();
                _asioOut = null;
            }
        }
        
#if UNITY_EDITOR
        
        [CustomEditor(typeof(VirtualAudioAsioOut))]
        public class VirtualAudioAsioOutEditor : Editor
        {
            private string[] _asioDevices;

            private void OnEnable()
            {
                _asioDevices = AsioOut.GetDriverNames();
            }

            public override void OnInspectorGUI()
            {
                var t = target as VirtualAudioAsioOut;
                if (t == null) return;
                if (t.senderReceiverMode == SenderReceiverMode.NdiSender
                    || t.senderReceiverMode == SenderReceiverMode.AudioListener)
                {
                    if (t.senderReceiverMode == SenderReceiverMode.NdiSender)
                        EditorGUILayout.HelpBox(new GUIContent("Asio Out is only supported when using a Virtual Audio Mode on the NDI Sender"));
                    
                    EditorGUI.BeginChangeCheck();
                    DrawPropertiesExcluding(serializedObject, "_receiver");
                    if (EditorGUI.EndChangeCheck())
                        serializedObject.ApplyModifiedProperties();
                }
                else
                    base.OnInspectorGUI();

                if (Application.isPlaying)
                    return;
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("ASIO Devices on this machine", EditorStyles.boldLabel);
                if (_asioDevices.Length == 0)
                {
                    EditorGUILayout.LabelField("No ASIO devices found.");
                }
                else
                {
                    EditorGUI.indentLevel++;
                    foreach (var device in _asioDevices)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(device);
                        
                        if (GUILayout.Button("Set as default"))
                        {
                            serializedObject.FindProperty("_defaultDeviceName").stringValue = device;
                            serializedObject.ApplyModifiedProperties();
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }
#endif
    }
}