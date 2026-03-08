using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using UnityEngine.VFX;

[System.Serializable]
public class GalaxyManifest
{
    public FamilyManifest gas;
    public FamilyManifest star;
    public FamilyManifest dm;
}

[System.Serializable]
public class FamilyManifest
{
    public int count;
    public string position_file;
    public List<AttributeManifest> attributes;
}

[System.Serializable]
public class AttributeManifest
{
    public string name;
    public string file;
    public float min;
    public float max;
    public bool is_log;
    public string units;
}



public class GalaxyDataManager : MonoBehaviour
{
    [Header("Configuration")]
    public string dataFolderName = "GalaxyExport";

    // We will drag the VFX GameObjects here in the Inspector later
    public VisualEffect gasVisual;
    public VisualEffect starVisual;
    public VisualEffect dmVisual;

    private GalaxyManifest _manifest;
    private string _basePath;

    // Dictionaries to hold preloaded attributes per family
    private Dictionary<string, Dictionary<string, Texture2D>> _attributeTextures = new Dictionary<string, Dictionary<string, Texture2D>>();
    private Dictionary<string, Dictionary<string, AttributeManifest>> _attributeManifests = new Dictionary<string, Dictionary<string, AttributeManifest>>();

    public enum VisualChannel
    {
        Color,
        Size,
        Alpha,
        Emission,       // brightness / HDR bloom strength
        // HueShift,    // optional later
        // Custom1,     // room for future ideas
    }

    private GalaxyControls controls; 

    void Awake()
    {
        controls = new GalaxyControls();
        controls.Debug.StandardMappings.performed += ctx =>
        {
            SwitchMapping("gas", "smooth", VisualChannel.Size, gasVisual);
            SwitchMapping("gas", "temp", VisualChannel.Color, gasVisual);
            SwitchMapping("gas", "density", VisualChannel.Alpha, gasVisual);
        };
        controls.Debug.ReturnToDefaults.performed += ctx =>
        {
            SwitchMapping("gas", "mass", VisualChannel.Size, gasVisual);
            SwitchMapping("gas", "temp", VisualChannel.Color, gasVisual);
            SwitchMapping("gas", "density", VisualChannel.Alpha, gasVisual);
        };
    }

    void OnEnable()
    {
        controls.Enable();
    }
    void OnDisable()
    {
        controls.Disable();
    }

    void Start()
    {
        // Points to Assets/StreamingAssets/GalaxyExport
        _basePath = Path.Combine(Application.streamingAssetsPath, dataFolderName);
        LoadManifest();

        // Load each family if the Visual Effect is assigned
        if (gasVisual != null) LoadFamily("gas", _manifest.gas, gasVisual);
        if (starVisual != null) LoadFamily("star", _manifest.star, starVisual);
        if (dmVisual != null) LoadFamily("dm", _manifest.dm, dmVisual);
    }

    void LoadManifest()
    {
        string path = Path.Combine(_basePath, "manifest.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            _manifest = JsonUtility.FromJson<GalaxyManifest>(json);
            Debug.Log("Manifest loaded successfully.");
        }
        else
        {
            Debug.LogError("Manifest not found at: " + path);
        }
    }

    void LoadFamily(string familyName, FamilyManifest family, VisualEffect vfx)
    {
        if (family == null || family.count == 0 || vfx == null) return;

        Debug.Log($"Loading {familyName}: {family.count} particles...");

        // 1. Set Particle Count in VFX Graph
        vfx.SetInt("ParticleCount", family.count);

        // 2. Load Positions 
        // We pass '4' because we padded the Python export (X, Y, Z, W)
        string posPath = Path.Combine(_basePath, family.position_file);
        Texture2D posTex = LoadBinaryToTexture(posPath, family.count, 4);

        if (posTex != null)
        {
            vfx.SetTexture("PositionMap", posTex);
            vfx.SetInt("PositionMapWidth", posTex.width);

            Debug.Log($"ParticleCount set to = {family.count}");
            Debug.Log($"[Success] {familyName} PositionMap: {posTex.width}x{posTex.height}");
        }
        // 3. Preload ALL Attributes
        _attributeTextures[familyName] = new Dictionary<string, Texture2D>();
        _attributeManifests[familyName] = new Dictionary<string, AttributeManifest>();
        foreach (var attr in family.attributes)
        {
            string attrPath = Path.Combine(_basePath, attr.file);
            Texture2D attrTex = LoadBinaryToTexture(attrPath, family.count, 1); // RFloat for attributes
            if (attrTex != null)
            {
                _attributeTextures[familyName][attr.name] = attrTex;
                _attributeManifests[familyName][attr.name] = attr;
                Debug.Log($"[Preloaded] {familyName} {attr.name}: {attrTex.width}x{attrTex.height}");
            }
        }

        // 4. Set Default Attributes
        if (family.attributes.Count > 2)
        {
            SwitchMapping(familyName, family.attributes[0].name, VisualChannel.Color, vfx);
            SwitchMapping(familyName, family.attributes[1].name, VisualChannel.Size, vfx);
            SwitchMapping(familyName, family.attributes[2].name, VisualChannel.Alpha, vfx);
        }
        else if (family.attributes.Count > 1)
        {
            SwitchMapping(familyName, family.attributes[0].name, VisualChannel.Color, vfx);
            SwitchMapping(familyName, family.attributes[1].name, VisualChannel.Size, vfx);
        }
        else if (family.attributes.Count > 0)
        {
            SwitchMapping(familyName, family.attributes[0].name, VisualChannel.Color, vfx);
        }

        vfx.Play();
    }

    public void SwitchMapping(string familyName, string attrName, VisualChannel channel, VisualEffect vfx)
    {
        if (!_attributeTextures.TryGetValue(familyName, out var attrs) ||
            !attrs.TryGetValue(attrName, out var tex))
        {
            Debug.LogError($"Cannot switch: {attrName} not found for {familyName}");
            return;
        }

        var manifest = _attributeManifests[familyName][attrName];

        if (familyName == "gas")
        {
            vfx.SetFloat("BaseSize", 1f);
            vfx.SetFloat("BaseOpacity", 1f);
        }
        else if (familyName == "star")
        {
            vfx.SetFloat("BaseSize", 4f);
            vfx.SetFloat("BaseOpacity", 3f);
        }
        else if (familyName == "dm")
        {
            vfx.SetFloat("BaseSize", 20f);
            vfx.SetFloat("BaseOpacity", 0.05f);
        }

        switch (channel)
        {
            case VisualChannel.Color:
                vfx.SetTexture("ColorMap", tex);
                vfx.SetFloat("ColorMinVal", manifest.min);
                vfx.SetFloat("ColorMaxVal", manifest.max);
                break;

            case VisualChannel.Size:
                vfx.SetTexture("SizeMap", tex);
                vfx.SetFloat("SizeMinVal", manifest.min);
                vfx.SetFloat("SizeMaxVal", manifest.max);
                break;

            case VisualChannel.Alpha:
                vfx.SetTexture("AlphaMap", tex);
                vfx.SetFloat("AlphaMinVal", manifest.min);
                vfx.SetFloat("AlphaMaxVal", manifest.max);
                break;

            case VisualChannel.Emission:
                vfx.SetTexture("EmissionMap", tex);
                vfx.SetFloat("EmissionMinVal", manifest.min);
                vfx.SetFloat("EmissionMaxVal", manifest.max);
                break;

            default:
                Debug.Log($"Channel [{nameof(channel)}] does not exist. Mapping change FAILED.");
                return;
        }

        Debug.Log($"[{familyName}] {attrName} → {channel}  (min:{manifest.min:F2}, max:{manifest.max:F2})");
        return;
    }

    public void LoadAttribute(AttributeManifest attr, string familyName, VisualEffect vfx)
    {
        string path = Path.Combine(_basePath, attr.file);

        // Attributes are just 1 float per particle (Stride = 1)
        Texture2D attrTex = LoadBinaryToTexture(path, GetParticleCount(familyName), 1);

        if (attrTex != null)
        {
            vfx.SetTexture("ColorMap", attrTex);
            vfx.SetFloat("MinVal", attr.min);
            vfx.SetFloat("MaxVal", attr.max);
            Debug.Log($"Switched {familyName} to {attr.name}");
        }
    }

    private int GetParticleCount(string familyName)
    {
        if (familyName == "gas") return _manifest.gas.count;
        if (familyName == "star") return _manifest.star.count;
        if (familyName == "dm") return _manifest.dm.count;
        return 0;
    }

    Texture2D LoadBinaryToTexture(string filePath, int particleCount, int stride)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[Error] File missing: {filePath}");
            return null;
        }

        byte[] fileData = File.ReadAllBytes(filePath);

        // Calculate texture size
        int width = Mathf.CeilToInt(Mathf.Sqrt(particleCount));
        int height = Mathf.CeilToInt((float)particleCount / width);

        // Format Selection
        TextureFormat format = (stride >= 4) ? TextureFormat.RGBAFloat : TextureFormat.RFloat;

        // --- NEW PADDING LOGIC ---
        // Calculate exactly how many bytes Unity expects for this texture size
        // (Width * Height) * (4 bytes per float) * (Stride usually 1 or 4)
        // Note: RFloat is 4 bytes per pixel. RGBAFloat is 16 bytes per pixel.
        int bytesPerPixel = (format == TextureFormat.RGBAFloat) ? 16 : 4;
        int expectedBytes = width * height * bytesPerPixel;

        // If file is smaller than the square texture, pad it with zeros
        if (fileData.Length < expectedBytes)
        {
            // Create a new array of the correct size
            byte[] paddedData = new byte[expectedBytes];

            // Copy the file data into it
            System.Array.Copy(fileData, paddedData, fileData.Length);

            // Swap fileData to use the padded version
            fileData = paddedData;

            // Optional: Log this so we know it happened
            // Debug.Log($"[Padding] Padded {filePath} with {expectedBytes - fileData.Length} bytes.");
        }
        // -------------------------

        Texture2D tex = new Texture2D(width, height, format, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        try
        {
            tex.LoadRawTextureData(fileData);
            tex.Apply();
        }
        catch (UnityException e)
        {
            Debug.LogError($"[Error] Failed to load texture {filePath}. Error: {e.Message}");
            return null;
        }

        return tex;
    }
}