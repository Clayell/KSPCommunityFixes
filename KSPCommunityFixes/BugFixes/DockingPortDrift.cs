﻿using System.Collections.Generic;
using HarmonyLib;

namespace KSPCommunityFixes
{
    class DockingPortDrift : BasePatch
    {
        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleDockingNode), "ApplyCoordsUpdate"),
                GetType()));
        }

        // Credit to JPLRepo : https://github.com/JPLRepo/FixDockingNodes
        static bool ModuleDockingNode_ApplyCoordsUpdate_Prefix(ModuleDockingNode __instance)
        {
            return __instance.canRotate && !__instance.nodeIsLocked && __instance.otherNode != null;
        }
    }
}
