using System;
#if OSC_JACK
using OscJack;
#endif
using UnityEngine;

namespace Klak.Ndi
{
    [RequireComponent(typeof(IAdmDataProvider))]
    [AddComponentMenu("NDI/ADM OSC Sender")]
    public class AdmOscSender : MonoBehaviour
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
        
#if !OSC_JACK
        [Header("[Please add OSC JACK to your project > https://github.com/keijiro/OscJack]")]
#endif
        public AdmSettings _settings = new AdmSettings(0.1f, 10f);
        
#if OSC_JACK
        public OscConnection _connection = null;

        private OscClient _client;
        private OscConnection _customConnection;
        private IAdmDataProvider _admDataProvider;
        private object _lock = new object();
        
        private void Awake()
        {
            _admDataProvider = GetComponent<IAdmDataProvider>();
        }

        public float GetNearDistance()
        {
            lock (_lock)
                return _settings.nearDistance;
        }
        
        public float GetFarDistance()
        {
            lock (_lock) 
                return _settings.farDistance;
        }
        
        public void SetNearDistance(float value)
        {
            lock (_lock)
                _settings.nearDistance = value;
        }
        
        public void SetFarDistance(float value)
        {
            lock (_lock)
                _settings.farDistance = value;
        }
        
        private void OnAdmDataChanged(AdmData data)
        {
            SendAdm(data);
        }

        private void OnEnable()
        {
            lock (_lock)
            {
                if (_customConnection)
                {
                    _client = new OscClient(_customConnection.host, _customConnection.port);
                }
                else
                {
                    if (!_connection)
                    {
                        Debug.LogError("No connection set for OSC sender. Disabling component.");
                        enabled = false;
                        return;
                    }
                    else
                        _client = new OscClient(_connection.host, _connection.port);
                }

                if (_client != null && _admDataProvider != null)
                {
                    _admDataProvider.RegisterAdmDataChangedEvent(OnAdmDataChanged);
                }
            }
        }

        private void OnDisable()
        {
            lock (_lock)
            {
                if (_client != null)
                    _client.Dispose();
                _client = null;

                if (_admDataProvider != null)
                {
                    _admDataProvider.UnregisterAdmDataChangedEvent(OnAdmDataChanged);
                }
            }
        }

        public void SetSettings(AdmSettings settings)
        {
            lock (_lock)
                _settings = settings;
        }

        private void SendPosition_Cartesian(Vector3 pos, int id)
        {
            string strId = id.ToString();
            pos = pos.normalized * Mathf.Max(0.001f,
                Mathf.InverseLerp(_settings.nearDistance, _settings.farDistance, pos.magnitude));
            _client.Send($"/adm/obj/{strId}/xyz", pos.x, pos.z, pos.y);
            _client.Send($"/adm/obj/{strId}/x", pos.x);
            _client.Send($"/adm/obj/{strId}/y", pos.y);
            _client.Send($"/adm/obj/{strId}/z", pos.z);
        }

        private void SendDistanceMax(int id)
        {
            string strId = id.ToString();
            _client.Send($"/adm/config/obj/{strId}/dMax", _settings.farDistance);
        }

        private void SendPosition_Spherical(Vector3 pos, int id)
        {
            string strId = id.ToString();
            float azim = 0;
            float elev = 0;
            // Calculate azimuth and elevation
            if (pos.x != 0 || pos.z != 0)
            {
                azim = Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg;
                elev = Mathf.Atan2(pos.y, Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z)) * Mathf.Rad2Deg;
            }
            
            float dist = Mathf.Max(0.001f,
                Mathf.InverseLerp(_settings.nearDistance, _settings.farDistance, pos.magnitude));
            _client.Send($"/adm/obj/{strId}/azim", azim);
            _client.Send($"/adm/obj/{strId}/elev", elev);
            _client.Send($"/adm/obj/{strId}/dist", dist);
        }

        private void SendCartesianConfig(bool cartesianActive, int id)
        {
            _client.Send($"/adm/config/obj/{id.ToString()}/cartesian", cartesianActive ? 1 : 0);
        }
        private void SendGain(float gain, int id)
        {
            _client.Send($"/adm/obj/{id.ToString()}/gain", gain);
        }

        private void SendAdm(AdmData data)
        {
            lock (_lock)
            {
                if (_client == null)
                    return;
                
                int id = 1;
                foreach (var pos in data.positions)
                {
                    SendCartesianConfig(true, id);
                    SendPosition_Cartesian(pos, id);
                    SendPosition_Spherical(pos, id);
                    SendDistanceMax(id);
                    id++;
                }

                id = 1;
                foreach (var gain in data.gains)
                {
                    SendGain(gain, id);
                    id++;
                }
            }
        }

        public void OnDestroy()
        {
            lock (_lock)
            {
                _client?.Dispose();
                _client = null;
            }
            
            if (_customConnection)
                Destroy(_customConnection);
        }

        public void GetHostIpAndPort(out string ipText, out int port)
        {
            if (_customConnection)
            {
                ipText = _customConnection.host;
                port = _customConnection.port;
            }
            else
            {
                ipText = _connection.host;
                port = _connection.port;
            }
        }

        public void SendCmd(string cmd)
        {
            _client.Send(cmd);
        }

        public void SendCmd(string cmd, int[] para)
        {
            if (para.Length == 0)
                _client.Send(cmd);
            else if (para.Length == 1)
                _client.Send(cmd, para[0]);
            else if (para.Length == 2)
                _client.Send(cmd, para[0], para[1]);
            else
            {
                Debug.LogError("Can't send OSC command. Max supported para length for ints is 2. Current= "+para.Length); 
            }
        }

        public void SendCmd(string cmd, float[] para)
        {
            if (para.Length == 0)
                _client.Send(cmd);
            else if (para.Length == 1)
                _client.Send(cmd, para[0]);
            else if (para.Length == 2)
                _client.Send(cmd, para[0], para[1]);
            else
            {
                Debug.LogError("Can't send OSC command. Max supported para length for floats is 2. Current= "+para.Length); 
            }
        }

        
        public void ChangeHostIpAndPort(string ipText, int port)
        {
            if (!_customConnection)
            {
                _customConnection = ScriptableObject.CreateInstance<OscConnection>();
            }

            _customConnection.host = ipText;
            _customConnection.port = port;
            if (!enabled)
                return;
            
            lock (_lock)
            {
                if (_client != null)
                    _client.Dispose();
                _client = new OscClient(_customConnection.host, _customConnection.port);
            }
        }
#endif
    }
}