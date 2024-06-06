#if OSC_JACK
using System;
using System.Collections.Generic;
using Codice.CM.SEIDInfo;
using OscJack;
using UnityEngine;

namespace Klak.Ndi
{
    internal class AdmOscSender : IDisposable
    {
        [Serializable]
        public struct AdmSettings
        {
            public float nearDistance;
            public float farDistance;

            public AdmSettings(float near, float far)
            {
                nearDistance = near;
                farDistance = far;
            }
        }
        
        private OscConnection _connection = null;
        private OscClient _client;
        private AdmSettings _settings = new AdmSettings(0.1f,10f);
        
        public AdmOscSender(OscConnection connection)
        {
            _connection = connection;
            _client = OscMaster.GetSharedClient(_connection.host, _connection.port);
        }

        public void SetSettings(AdmSettings settings)
        {
            _settings = settings;
        }

        private void SendPosition(Vector3 pos, int id)
        {
            pos = pos.normalized * Mathf.Max(0.001f, Mathf.InverseLerp(_settings.nearDistance, _settings.farDistance, pos.magnitude));
            _client.Send($"/adm/obj/{id.ToString()}/xyz", pos.x, pos.z, pos.y);
        }

        public void SendMeta(List<Vector3> positions)
        {
            for (int i = 0; i < positions.Count; i++)
                SendPosition(positions[i], i+1);
        }

        public void SendMeta(Vector3[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
                SendPosition(positions[i], i+1);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
#endif