using UnityEngine;

/// <summary>
/// Marks a serialized field as read-only in the Inspector.
/// Works with any field that has [SerializeField] or is public.
/// </summary>
public class ReadOnlyAttribute : PropertyAttribute { }
