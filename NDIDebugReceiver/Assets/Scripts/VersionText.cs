using TMPro;
using UnityEngine;

public class VersionText : MonoBehaviour
{
    void Start()
    {
        OnValidate();
    }

    private void OnValidate()
    {
        var textMesh = GetComponent<TextMeshProUGUI>();
        if (textMesh != null)
        {
            textMesh.text = "Version: " + Application.version;
        }
    }
    
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Increment Version/Add Minor Version")]
    public static void AddMinorVersion()
    {
        string version = UnityEditor.PlayerSettings.bundleVersion;
        string[] versionParts = version.Split('.');
        if (versionParts.Length < 2)
        {
            version = "1.0";
            versionParts = version.Split('.');
        }

        int minorVersion = int.Parse(versionParts[1]);
        minorVersion++;
        versionParts[1] = minorVersion.ToString();
        string newVersion = string.Join(".", versionParts);
        UnityEditor.PlayerSettings.bundleVersion = newVersion;

        var versionsTexts = FindObjectsOfType<VersionText>();
        foreach (var versionText in versionsTexts)
            versionText.OnValidate();
        
        Debug.Log("Version updated to: " + newVersion);
    }
    
    [UnityEditor.MenuItem("Increment Version/Add Major Version")]
    public static void AddMajorVersion()
    {
        string version = UnityEditor.PlayerSettings.bundleVersion;
        string[] versionParts = version.Split('.');
        if (versionParts.Length < 2)
        {
            version = "1.0";
            versionParts = version.Split('.');
        }

        int majorVersion = int.Parse(versionParts[0]);
        majorVersion++;
        versionParts[0] = majorVersion.ToString();
        versionParts[1] = "0";
        string newVersion = string.Join(".", versionParts);
        UnityEditor.PlayerSettings.bundleVersion = newVersion;

        var versionsTexts = FindObjectsOfType<VersionText>();
        foreach (var versionText in versionsTexts)
            versionText.OnValidate();
        
        Debug.Log("Version updated to: " + newVersion);
    }
    
    #endif
}
