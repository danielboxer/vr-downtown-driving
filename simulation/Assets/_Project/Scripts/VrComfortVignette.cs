using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class VrComfortVignette : MonoBehaviour
{
    [Header("Look")]
    [Range(0f, 1f)]
    [Tooltip("Opening at High comfort. 1 = no vignette, lower = tighter tunnel.")]
    [SerializeField] private float aperture = 0.4f;
    [Range(0f, 1f)]
    [SerializeField] private float feathering = 0.3f;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private static readonly int ApertureSizeId = Shader.PropertyToID("_ApertureSize");
    private static readonly int FeatheringId = Shader.PropertyToID("_FeatheringEffect");

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        VignetteLevel level = VignetteSetting.Level;
        bool active = VrActive.IsActive && level != VignetteLevel.Off;
        if (_renderer.enabled != active) _renderer.enabled = active;
        if (!active) return;

        // Low sits halfway between fully open and the High opening.
        float size = level == VignetteLevel.Low ? Mathf.Lerp(1f, aperture, 0.5f) : aperture;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(ApertureSizeId, size);
        _mpb.SetFloat(FeatheringId, feathering);
        _renderer.SetPropertyBlock(_mpb);
    }
}
