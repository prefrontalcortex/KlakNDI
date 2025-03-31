using UnityEngine;

namespace Klak.Ndi {

// Platform specific helper functions for Android

static class AndroidHelper
{
    #if UNITY_ANDROID && !UNITY_EDITOR

    static AndroidJavaObject _mcastLock;
    static AndroidJavaObject _nsdManager;

    public static void SetupNetwork()
    {
        // Multicast lock acquisition
        var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
        _mcastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "lock");
        _mcastLock.Call("acquire");

        // Create a network service discovery manager object.
        _nsdManager = activity.Call<AndroidJavaObject>("getSystemService", "servicediscovery");

        // Get applicationInfo and targetSdkVersion from activity.
        var context = activity.Call<AndroidJavaObject>("getApplicationContext");
        var info = context.Call<AndroidJavaObject>("getApplicationInfo");
        var targetSdkVersion = info.Get<int>("targetSdkVersion");
        
        // Start service discovery for NDI TCP and UDP services. Android API Level 31+ doesn't do that automatically anymore when calling `getSystemService`.
        if (targetSdkVersion >= 31)
        {
            var tcpListener = new AndroidJavaObject("jp.keijiro.klak.ndi.DiscoveryListener");
            var udpListener = new AndroidJavaObject("jp.keijiro.klak.ndi.DiscoveryListener");
            _nsdManager.Call("discoverServices", "_ndi._tcp", 1, tcpListener);
            _nsdManager.Call("discoverServices", "_ndi._udp", 1, udpListener);
        }
    }

    #else

    public static void SetupNetwork() {}

    #endif
}

} // namespace Klak.Ndi
