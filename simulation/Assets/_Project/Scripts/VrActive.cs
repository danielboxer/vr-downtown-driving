using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public static class VrActive
{
    private static readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();

    public static bool IsActive
    {
        get
        {
            SubsystemManager.GetSubsystems(_displays);
            for (int i = 0; i < _displays.Count; i++)
            {
                if (_displays[i].running) return true;
            }
            return false;
        }
    }
}
