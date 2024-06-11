using System.Collections.Generic;
using UnityEngine;

namespace Klak.Ndi
{
    public class AdmData
    {
        public IEnumerable<Vector3> positions;
        public IEnumerable<float> gains;
    }
    
    public delegate void AdmDataChangedDelegate(AdmData data);
    
    public interface IAdmDataProvider
    {
        /// <summary>
        /// Be aware: This event will be called from a different thread than the main thread.
        /// </summary>
        void RegisterAdmDataChangedEvent(AdmDataChangedDelegate callback);
        void UnregisterAdmDataChangedEvent(AdmDataChangedDelegate callback);
    }

}