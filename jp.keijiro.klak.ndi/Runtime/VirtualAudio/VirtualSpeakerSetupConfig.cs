using System;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    [Serializable]
    public class VirtualAudioSetupConfig
    {
        [Serializable]
        public class Speaker
        {
            public Vector3 position;
            public float volume;
        }

        [Serializable]
        public class OscSetting
        {
            public bool enabled;
            public string host;
            public int port;
            public float nearDistance;
            public float farDistance;
        }

        [Serializable]
        public class OscCommand
        {
            public string command;
        }
        
        [Serializable]
        public class OscCommandInt : OscCommand
        {
            public int[] parameters;
        }
        
        [Serializable]
        public class OscCommandFloat : OscCommand
        {
            public float[] parameters;
        }
        
        public bool objectBasedAudio = false;
        public int maxObjectBasedChannels = 16;
        public bool centeredAudioOnAllSpeakers;
        public bool useAudioOriginPositionForVirtualAttenuation;
        
        public Speaker[] speakers;
        public OscSetting oscSetting;

        public OscCommand[] oscInitCommands;
        public OscCommandInt[] oscInitCommandsInts;
        public OscCommandFloat[] oscInitCommandsFloats;

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static VirtualAudioSetupConfig FromJson(string json)
        {
            var loadedConfig = JsonUtility.FromJson<VirtualAudioSetupConfig>(json);
            return loadedConfig;
        }
    }

}