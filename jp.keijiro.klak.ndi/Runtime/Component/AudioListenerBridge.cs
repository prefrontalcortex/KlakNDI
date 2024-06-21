using System;
using UnityEngine;

namespace Klak.Ndi
{
    public class AudioListenerBridge : MonoBehaviour
    {
        private static object _lock = new object();

        private static Action<float[], int> _onAudioFilterReadEvent;

        public static Action<float[], int> OnAudioFilterReadEvent
        {
            get
            {
                lock (_lock)
                {
                    return _onAudioFilterReadEvent;
                }
            }
            set
            {
                lock (_lock)
                {
                    _onAudioFilterReadEvent = value;
                }
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                if (_onAudioFilterReadEvent != null)
                    _onAudioFilterReadEvent(data, channels);
            }
        }
    }
}