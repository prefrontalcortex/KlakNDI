using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif
public class VersionText : MonoBehaviour
#if UNITY_EDITOR
    ,IPreprocessBuildWithReport
#endif
{
    // Start is called before the first frame update
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
    
    // Automatic add minor version when building the project

    
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Increment Version/Add Minor Version")]
    public static void AddMinorVersion()
    {
        string version = UnityEditor.PlayerSettings.bundleVersion;
        string[] versionParts = version.Split('.');
        if (versionParts.Length < 3)
        {
            version = "1.0.0";
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
        if (versionParts.Length < 3)
        {
            version = "1.0.0";
            versionParts = version.Split('.');
        }

        int majorVersion = int.Parse(versionParts[0]);
        majorVersion++;
        versionParts[0] = majorVersion.ToString();
        string newVersion = string.Join(".", versionParts);
        UnityEditor.PlayerSettings.bundleVersion = newVersion;

        var versionsTexts = FindObjectsOfType<VersionText>();
        foreach (var versionText in versionsTexts)
            versionText.OnValidate();
        
        Debug.Log("Version updated to: " + newVersion);
    }
    
    public static void AddBuildVersion()
    {
        string version = UnityEditor.PlayerSettings.bundleVersion;
        string[] versionParts = version.Split('.');
        if (versionParts.Length < 3)
        {
            version = "1.0.0";
            versionParts = version.Split('.');
        }
        int buildVersion = int.Parse(versionParts[2]);
        buildVersion++;
        versionParts[2] = buildVersion.ToString();
        string newVersion = string.Join(".", versionParts);
        UnityEditor.PlayerSettings.bundleVersion = newVersion;

        var versionsTexts = FindObjectsOfType<VersionText>();
        foreach (var versionText in versionsTexts)
            versionText.OnValidate();
        
        Debug.Log("Version updated to: " + newVersion);
    }

    public int callbackOrder
    {
        get => 0;
    }
    
    public void OnPreprocessBuild(BuildReport report)
    {
        AddBuildVersion();
    }
    #endif
}
