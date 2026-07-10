using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared configuration for SUMO-controlled NPC vehicles.
/// Assign a single asset in SimulationController; VehicleControllers read from it
/// at runtime instead of storing per-instance copies.
/// </summary>
[CreateAssetMenu(fileName = "NpcVehicleConfig", menuName = "Simulation/NPC Vehicle Config")]
public class NpcVehicleConfig : ScriptableObject
{
    [Header("Movement Smoothing")]
    [Tooltip("How quickly SUMO NPC visuals chase the latest position sample.")]
    public float movementSharpness = 14f;
    [Tooltip("How quickly SUMO NPC visuals chase the latest rotation sample.")]
    public float rotationSharpness = 14f;

    [Header("Detail LOD")]
    [Tooltip("Distance used by NPC scripts to decide if expensive/high-detail behaviour is allowed.")]
    public float highDetailDistance = 45f;

    [Header("Horn Audio")]
    [Tooltip("Up to 3 short horn clips; a random one plays each honk.")]
    public List<AudioClip> hornClips = new List<AudioClip>();
    [Range(0f, 2f)]
    public float hornVolume = 1f;

    [Header("Bell Audio (Bikes)")]
    [Tooltip("Short bell clips used by NPC bikes instead of the car horn. A random one plays each ring.")]
    public List<AudioClip> bellClips = new List<AudioClip>();
    [Range(0f, 2f)]
    public float bellVolume = 1f;

    [Header("Horn Timing")]
    [Tooltip("Seconds an NPC must be stationary before honking.")]
    public float hornTriggerDelay = 3f;
    [Tooltip("Minimum seconds between honks from the same NPC.")]
    public float hornCooldown = 5f;
    [Tooltip("Distance in metres within which the ego car triggers honking.")]
    public float hornTriggerDistance = 18f;
    [Range(0f, 1f)]
    [Tooltip("Probability (0-1) that a qualifying NPC actually honks each cooldown window.")]
    public float hornHonkChance = 0.4f;
    [Range(0f, 1f)]
    [Tooltip("Probability (0-1) that a stopped NPC randomly honks with no ego nearby.")]
    public float hornAmbientChance = 0.1f;
    [Tooltip("How often each NPC evaluates honking/red-light checks.")]
    public float hornCheckInterval = 0.5f;

    [Header("Collision Debris")]
    [Tooltip("Seconds after a collision before the detached NPC debris is destroyed. Set 0 to never despawn.")]
    public float detachedDespawnDelay = 90f;

    // Pre-computed squared distance for fast comparison (set in OnValidate/OnEnable)
    [System.NonSerialized] public float highDetailDistanceSqr;

    private void OnEnable()
    {
        highDetailDistanceSqr = highDetailDistance * highDetailDistance;
    }

    private void OnValidate()
    {
        highDetailDistanceSqr = highDetailDistance * highDetailDistance;
    }
}
