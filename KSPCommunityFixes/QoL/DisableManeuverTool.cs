﻿using HarmonyLib;
using KSP.UI;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;

namespace KSPCommunityFixes.QoL
{
    class DisableManeuverTool : BasePatch
    {

        protected override Version VersionMin => new Version(1, 12, 0);

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix, 
                AccessTools.Method(typeof(ManeuverTool), "OnAppAboutToStart"), 
                this));
        }

        public static bool enableManeuverTool = true;

        protected override void OnLoadData(ConfigNode node)
        {
            if (!node.TryGetValue(nameof(enableManeuverTool), ref enableManeuverTool))
            {
                enableManeuverTool = true;
            }
        }

        private static bool ManeuverTool_OnAppAboutToStart_Prefix(ref bool __result)
        {
            if (enableManeuverTool)
                return true;

            __result = false;
            return false;
        }

        public static void OnToggleApp(bool enabled)
        {
            enableManeuverTool = enabled;

            if (!enableManeuverTool && ManeuverTool.Instance != null)
            {
                UnityEngine.Object.Destroy(ManeuverTool.Instance.gameObject);
            }
            else if (enableManeuverTool && ManeuverTool.Instance == null)
            {
                UIAppSpawner appSpawner = UIMasterController.Instance.transform.Find("PrefabSpawners")?.GetComponentInChildren<UIAppSpawner>();
                if (appSpawner != null)
                {
                    foreach (UIAppSpawner.AppWrapper appWrapper in appSpawner.apps)
                    {
                        if (appWrapper.prefab.GetComponent<ManeuverTool>() != null && appWrapper.scenes.Contains(HighLogic.LoadedScene))
                        {
                            appWrapper.instantiatedApp = UnityEngine.Object.Instantiate(appWrapper.prefab);
                            appWrapper.instantiatedApp.GetComponent<ManeuverTool>().ForceAddToAppLauncher();
                        }
                    }
                }
            }
        }
    }
}
