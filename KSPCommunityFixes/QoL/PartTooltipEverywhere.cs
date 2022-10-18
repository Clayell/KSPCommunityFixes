// TODO :
// - PAW access
// - customizable hotkey for hover
// - close button
// - pin toggle ?
// - 


using HarmonyLib;
using KSP.Localization;
using KSP.UI;
using KSP.UI.Screens.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KSPCommunityFixes.QoL
{
    class PartTooltipEverywhere : BasePatch
    {
        public static InstantiatedPartTooltip tooltipPrefab;
        public static PartListTooltipWidget extInfoModuleWidgetPrefab;
        public static PartListTooltipWidget extInfoRscWidgePrefab;

        protected override void ApplyPatches(List<PatchInfo> patches)
        {
            CreatePrefab();

            patches.Add(new PatchInfo(PatchMethodType.Postfix,
                    AccessTools.Method(typeof(Part), nameof(Part.Start)),
                    this));
        }

        private void CreatePrefab()
        {
            //pointerLineMaterial = Resources.FindObjectsOfTypeAll<KSP.UI.Screens.SpaceCenter.BuildingPickerItem>().FirstOrDefault().lineMaterial;

            // Create tooltip object
            PartListTooltip stockComponentPrefab = Resources.FindObjectsOfTypeAll<PartListTooltipController>().FirstOrDefault().tooltipPrefab;
            PartListTooltip stockComponent = Object.Instantiate(stockComponentPrefab);

            // get module/resource widgets prefabs
            extInfoModuleWidgetPrefab = stockComponent.extInfoModuleWidgetPrefab;
            extInfoRscWidgePrefab = stockComponent.extInfoRscWidgePrefab;
            GameObject prefabObject = stockComponent.gameObject;
            
            prefabObject.name = "KSPCF part tooltip";
            prefabObject.SetActive(false);
            Object.DontDestroyOnLoad(prefabObject);
            prefabObject.transform.SetParent(KSPCommunityFixes.Instance.transform, false);

            tooltipPrefab = prefabObject.AddComponent<InstantiatedPartTooltip>();

            // Get various UI GameObjects
            tooltipPrefab.rectTransform = tooltipPrefab.transform as RectTransform;
            tooltipPrefab.panelExtended = stockComponent.panelExtended;
            tooltipPrefab.descriptionText = prefabObject.GetChild("DescriptionText");
            tooltipPrefab.descriptionTopObject = prefabObject.GetChild("Scroll View");
            tooltipPrefab.descriptionContent = tooltipPrefab.descriptionText.transform.parent.gameObject;
            tooltipPrefab.manufacturerPanel = prefabObject.GetChild("ManufacturerPanel");
            tooltipPrefab.primaryInfoTopObject = prefabObject.GetChild("ThumbAndPrimaryInfo").GetChild("Scroll View");
            tooltipPrefab.footerTopObject = prefabObject.GetChild("Footer");
            tooltipPrefab.extWidgetsContent = stockComponent.panelExtended.GetChild("Content");

            tooltipPrefab.textName = stockComponent.textName;
            tooltipPrefab.textInfoBasic = stockComponent.textInfoBasic;
            tooltipPrefab.textGreyoutMessage = stockComponent.textGreyoutMessage;
            tooltipPrefab.textManufacturer = stockComponent.textManufacturer;
            tooltipPrefab.textDescription = stockComponent.textDescription;
            tooltipPrefab.textCost = stockComponent.textCost;

            tooltipPrefab.extInfoListContainer = stockComponent.extInfoListContainer;
            tooltipPrefab.extInfoListSpacer = stockComponent.extInfoListSpacer;

            // Remove part thumbnail
            prefabObject.GetChild("ThumbContainer").DestroyGameObject();
            // Remove RMBHint
            prefabObject.GetChild("RMBHint").DestroyGameObject();
            // Remove module widget list variant spacer
            stockComponent.extInfoListSpacerVariants.gameObject.DestroyGameObject();

            // Adjust size
            LayoutElement leftPanel = prefabObject.GetChild("StandardInfo").GetComponent<LayoutElement>();
            leftPanel.preferredWidth = 240;
            leftPanel.minWidth = 240;
            LayoutElement costPanel = prefabObject.GetChild("CostPanel").GetComponent<LayoutElement>();
            costPanel.preferredWidth = 150;

            Object.Destroy(stockComponent);
        }

        static void Part_Start_Postfix(Part __instance)
        {

        }
    }

    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class InstantiatedPartTooltipManager : MonoBehaviour
    {
        public static InstantiatedPartTooltipManager Instance { get; private set; }

        private Stack<InstantiatedPartTooltip> tooltipPool = new Stack<InstantiatedPartTooltip>();
        private Dictionary<Part, InstantiatedPartTooltip> activeTooltips = new Dictionary<Part, InstantiatedPartTooltip>();
        private List<Part> tooltipsToClose = new List<Part>();
        private HashSet<Part> tooltipsToUpdate = new HashSet<Part>();

        private TimingManager.UpdateAction editorFixedUpdate;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                GameEvents.onEditorShipCrewModified.Add(OnEditorShipCrewModified);
                editorFixedUpdate = EditorFixedUpdate;
                TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, editorFixedUpdate);
            }
            else
            {
                GameEvents.OnMapEntered.Add(OnMapEntered);
            }
        }

        private void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                GameEvents.onEditorShipCrewModified.Add(OnEditorShipCrewModified);
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, editorFixedUpdate);
            }
            else
            {
                GameEvents.OnMapEntered.Remove(OnMapEntered);
            }

            foreach (InstantiatedPartTooltip tooltip in activeTooltips.Values)
                Destroy(tooltip.gameObject);

            while (tooltipPool.TryPop(out InstantiatedPartTooltip tooltip))
                Destroy(tooltip.gameObject);

            Instance = null;
        }

        private void OnEditorShipModified(ShipConstruct data)
        {
            OnEditorShipModifiedImpl();
        }

        private void OnEditorShipCrewModified(VesselCrewManifest data)
        {
            OnEditorShipModifiedImpl();
        }

        private void OnEditorShipModifiedImpl()
        {
            if (activeTooltips.Count == 0)
                return;

            foreach (Part tooltipPart in activeTooltips.Keys)
                tooltipsToUpdate.Add(tooltipPart);
        }

        private void OnEditorPartEvent(ConstructionEventType eventType, Part part)
        {
            if (activeTooltips.Count == 0)
                return;

            if (activeTooltips.TryGetValue(part, out InstantiatedPartTooltip tooltip))
            {
                if (eventType == ConstructionEventType.PartTweaked)
                {
                    tooltipsToUpdate.Add(part);
                }
                else
                {
                    CloseTooltip(tooltip);
                    activeTooltips.Remove(part);
                }
            }
        }

        private void OnMapEntered()
        {
            foreach (InstantiatedPartTooltip activeTooltip in activeTooltips.Values)
                CloseTooltip(activeTooltip);

            activeTooltips.Clear();
        }

        private void LateUpdate()
        {
            if (Input.GetKeyUp(KeyCode.Mouse2) 
                && Cursor.lockState != CursorLockMode.Locked
                && !EventSystem.current.IsPointerOverGameObject()
                && Mouse.HoveredPart.IsNotNullOrDestroyed()
                && (Mouse.HoveredPart.state == PartStates.ACTIVE || Mouse.HoveredPart.state == PartStates.IDLE))
            {
                if (activeTooltips.Remove(Mouse.HoveredPart, out InstantiatedPartTooltip tooltip))
                {
                    CloseTooltip(tooltip);
                }
                else
                {
                    if (!tooltipPool.TryPop(out tooltip))
                        tooltip = Instantiate(PartTooltipEverywhere.tooltipPrefab);

                    activeTooltips.Add(Mouse.HoveredPart, tooltip);
                    tooltip.transform.SetParent(UIMasterController.Instance.actionCanvas.transform, false);
                    tooltip.gameObject.SetActive(true);
                    tooltip.UpdateInfo(Mouse.HoveredPart, true);
                }
            }
            else if (Input.GetKeyDown(KeyCode.Mouse1))
            {
                foreach (InstantiatedPartTooltip tooltip in activeTooltips.Values)
                {
                    CloseTooltip(tooltip);
                }
                activeTooltips.Clear();
            }
            else
            {
                foreach (KeyValuePair<Part, InstantiatedPartTooltip> activeTooltip in activeTooltips)
                {
                    if (activeTooltip.Key.IsDestroyed()
                        || (activeTooltip.Key.state != PartStates.ACTIVE && activeTooltip.Key.state != PartStates.IDLE))
                    {
                        CloseTooltip(activeTooltip.Value);
                        tooltipsToClose.Add(activeTooltip.Key);
                    }
                }

                if (tooltipsToClose.Count != 0)
                {
                    foreach (Part tooltipPart in tooltipsToClose)
                        activeTooltips.Remove(tooltipPart);

                    tooltipsToClose.Clear();
                }
            }
        }

        private void EditorFixedUpdate()
        {
            if (tooltipsToUpdate.Count != 0)
            {
                try
                {
                    foreach (Part tooltipPart in tooltipsToUpdate)
                        if (!tooltipPart.IsDestroyed() && activeTooltips.TryGetValue(tooltipPart, out InstantiatedPartTooltip tooltip))
                            tooltip.UpdateInfo(tooltipPart, false);
                }
                finally
                {
                    tooltipsToUpdate.Clear();
                }
            }
        }

        public void CloseTooltip(InstantiatedPartTooltip tooltip)
        {
            tooltipPool.Push(tooltip);
            tooltip.transform.SetParent(transform, false);
            tooltip.gameObject.SetActive(false);
        }
    }

    public class InstantiatedPartTooltip : MonoBehaviour, IPointerDownHandler, IDragHandler, IEventSystemHandler
    {
        private struct ModuleInfo
        {
            public string displayName;
            public string info;
            public string primaryInfo;

            public ModuleInfo(string displayName, string info, string primaryInfo)
            {
                this.displayName = displayName;
                this.info = info;
                this.primaryInfo = primaryInfo;
            }
        }

        public RectTransform rectTransform;
        public RectTransform extInfoListContainer;
        public RectTransform extInfoListSpacer;
        public GameObject panelExtended;
        public GameObject descriptionText;
        public GameObject descriptionTopObject;
        public GameObject descriptionContent;
        public GameObject manufacturerPanel;
        public GameObject primaryInfoTopObject;
        public GameObject footerTopObject;
        public GameObject extWidgetsContent;
        public TextMeshProUGUI textName;
        public TextMeshProUGUI textInfoBasic;
        public TextMeshProUGUI textGreyoutMessage;
        public TextMeshProUGUI textManufacturer;
        public TextMeshProUGUI textDescription;
        public TextMeshProUGUI textCost;

        public Vector2 edgeOffset = new Vector2(20f, 20f);

        private bool init = false;
        private List<PartListTooltipWidget> moduleWidgets;
        private List<PartListTooltipWidget> resourceWidgets;
        private List<ModuleInfo> moduleInfos;
        private List<ModuleInfo> resourceInfos;
        private HashSet<string> appliedUpgrades;
        private float cargoMass;

        public void OnPointerDown(PointerEventData data)
        {
            rectTransform.SetAsLastSibling();
        }
        public void OnDrag(PointerEventData data)
        {
            if (rectTransform == null)
                return;

            UIMasterController.DragTooltip(rectTransform, data.delta, edgeOffset);
        }

        private static ProfilerMarker pmUpdateInfo = new ProfilerMarker("InstantiatedPartTooltip.UpdateInfo");

        public void UpdateInfo(Part part, bool reposition)
        {
            pmUpdateInfo.Begin();

            int moduleCount = part.Modules.Count;
            int resourceCount = part.Resources.Count;

            if (!init)
            {
                init = true;
                moduleWidgets = new List<PartListTooltipWidget>(moduleCount);
                resourceWidgets = new List<PartListTooltipWidget>(resourceCount);
                moduleInfos = new List<ModuleInfo>(moduleCount);
                resourceInfos = new List<ModuleInfo>(resourceCount);
                appliedUpgrades = new HashSet<string>();
            }
            else
            {
                appliedUpgrades.Clear();
                moduleInfos.Clear();
                resourceInfos.Clear();
                cargoMass = 0f;
            }

            GetModulesInfo(part);
            GetResourcesInfo(part);

            // set main info
            textName.text = part.partInfo.title;
            textManufacturer.text = part.partInfo.manufacturer;
            textDescription.text = part.partInfo.description;
            textInfoBasic.text = GetPartPrimaryInfo(part);
            textCost.text = GetPartCost(part);

            bool hasResources = resourceInfos.Count > 0;
            bool hasModules = moduleInfos.Count > 0;

            if (hasResources || hasModules)
            {
                panelExtended.SetActive(true);

                int moduleWidgetCount = 0;
                foreach (ModuleInfo moduleInfo in moduleInfos)
                {
                    if (string.IsNullOrEmpty(moduleInfo.info))
                        continue;

                    PartListTooltipWidget widget;
                    if (moduleWidgetCount >= moduleWidgets.Count)
                    {
                        widget = Instantiate(PartTooltipEverywhere.extInfoModuleWidgetPrefab);
                        widget.transform.SetParent(extInfoListContainer, false);
                        widget.transform.SetSiblingIndex(moduleWidgetCount);
                        moduleWidgets.Add(widget);
                    }
                    else
                    {
                        widget = moduleWidgets[moduleWidgetCount];
                    }

                    widget.Setup(moduleInfo.displayName, moduleInfo.info);

                    if (!widget.gameObject.activeSelf)
                        widget.gameObject.SetActive(true);

                    moduleWidgetCount++;
                }

                for (int i = moduleWidgetCount; i < moduleWidgets.Count; i++)
                {
                    GameObject disabledWidget = moduleWidgets[i].gameObject;
                    if (disabledWidget.activeSelf)
                        disabledWidget.SetActive(false);
                }

                int resourceWidgetCount = 0;
                foreach (ModuleInfo resourceInfo in resourceInfos)
                {
                    if (string.IsNullOrEmpty(resourceInfo.info))
                        continue;

                    PartListTooltipWidget widget;
                    if (resourceWidgetCount >= resourceWidgets.Count)
                    {
                        widget = Instantiate(PartTooltipEverywhere.extInfoRscWidgePrefab);
                        widget.transform.SetParent(extInfoListContainer, false);
                        resourceWidgets.Add(widget);
                    }
                    else
                    {
                        widget = resourceWidgets[resourceWidgetCount];
                    }

                    widget.Setup(resourceInfo.displayName, resourceInfo.info);

                    if (!widget.gameObject.activeSelf)
                        widget.gameObject.SetActive(true);

                    resourceWidgetCount++;
                }

                for (int i = resourceWidgetCount; i < resourceWidgets.Count; i++)
                {
                    GameObject disabledWidget = resourceWidgets[i].gameObject;
                    if (disabledWidget.activeSelf)
                        disabledWidget.SetActive(false);
                }

                // Move or hide resource spacer
                if (hasResources)
                {
                    extInfoListSpacer.gameObject.SetActive(true);
                    extInfoListSpacer.SetSiblingIndex(moduleWidgetCount);
                }
                else
                {
                    extInfoListSpacer.gameObject.SetActive(false);
                }
            }
            else
            {
                panelExtended.SetActive(false);
            }

            // Reposition tooltip if new part selected
            if (reposition)
            {
                UIMasterController.RepositionTooltip(rectTransform, Vector2.one, 8f);
                UIMasterController.ClampToWindow(UIMasterController.Instance.mainCanvasRt, rectTransform, edgeOffset);
            }

            pmUpdateInfo.End();
        }

        private static ProfilerMarker pmGetPartPrimaryInfo = new ProfilerMarker("InstantiatedPartTooltip.GetPartPrimaryInfo");

        private string GetPartPrimaryInfo(Part part)
        {
            pmGetPartPrimaryInfo.Begin();
            int crewCount = 0;
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                cargoMass += part.kerbalInventoryMass;

                if (part.partCrew != null)
                    for (int i = part.partCrew.Length; i-- > 0;)
                        if (part.partCrew[i] != null)
                            crewCount++;
            }
            else
            {
                if (part.protoModuleCrew != null)
                {
                    crewCount = part.protoModuleCrew.Count;
                    for (int i = crewCount; i-- > 0;)
                    {
                        cargoMass += part.protoModuleCrew[i].KerbalInventoryModule.DestroyedAsNull()?.GetModuleMass(0f, ModifierStagingSituation.CURRENT) ?? 0f;
                    }
                }
            }

            float crewMass = part.kerbalMass + part.kerbalResourceMass;
            float dryMass = part.mass - crewMass - cargoMass;
            float totalMass = part.mass + part.resourceMass;

            string additionalMassInfo;
            if (dryMass != totalMass)
            {
                StringBuilder massSb = StringBuilderCache.Acquire();
                massSb.Append("\n<b>");
                massSb.Append(Localizer.Format("#autoLOC_8002186")); // "Dry Mass:"
                massSb.Append(" </b>");
                massSb.Append(dryMass.ToString("0.0### t"));

                if (part.applyKerbalMassModification && crewCount > 0)
                {
                    massSb.Append("\n<b>");
                    massSb.Append(Localizer.Format("#autoLOC_6005097")); // "Passenger Mass"
                    massSb.Append(": </b>");
                    massSb.Append(crewMass.ToString("0.0### t"));
                }

                if (cargoMass > 0f)
                {
                    massSb.Append("\n<b>");
                    massSb.Append(Localizer.Format("#autoLOC_8320000")); // "Inventory"
                    massSb.Append(": </b>");
                    massSb.Append(cargoMass.ToString("0.0### t"));
                }

                additionalMassInfo = massSb.ToStringAndRelease();
            }
            else
            {
                additionalMassInfo = string.Empty;
            }

            StringBuilder sb = StringBuilderCache.Acquire();
            sb.Append("<color=");
            sb.Append(XKCDColors.HexFormat.LightCyan);
            sb.Append(">");

            // "<b>Mass: </b><<1>> t <<2>>\n<b>Tolerance: </b><<3>>m/s Impact\n<b>Tolerance: </b><<4>> Gees, <<5>> kPA Pressure\n<b>Max. Temp. Int/Skin: </b><<6>>/<<7>> K"
            sb.Append(Localizer.Format("#autoLOC_6005098",
                totalMass.ToString("0.0###"),
                additionalMassInfo,
                part.crashTolerance.ToString("0.0###"),
                part.gTolerance.ToString("N0"),
                part.maxPressure.ToString("N0"),
                part.maxTemp.ToString("F0"),
                part.skinMaxTemp.ToString("F0")));

            if (part.CrewCapacity > 0)
            {
                if (crewCount > 0)
                {
                    sb.Append("\n<b>");
                    sb.Append(Localizer.Format("#autoLOC_900979")); // "Crew:"
                    sb.Append(" </b>");
                    sb.Append(crewCount).Append(" / ").Append(part.CrewCapacity);
                }
                else
                {
                    sb.Append(PartListTooltip.cacheAutoLOC_456346); // "\n<b>Crew capacity:</b> "
                    sb.Append(part.CrewCapacity);
                }
            }

            bool canToggleCrossfeedEditor = false;
            bool canToggleCrossfeedFlight = false;
            bool canToggleCrossfeedRequireTech = true;
            string techID = string.Empty;
            int midx = part.Modules.Count - 1;
            while (midx >= 0 && (!canToggleCrossfeedEditor || !canToggleCrossfeedFlight))
            {
                if (part.Modules[midx] is IToggleCrossfeed toggleCrossfeed)
                {
                    canToggleCrossfeedEditor |= toggleCrossfeed.CrossfeedToggleableEditor();
                    canToggleCrossfeedFlight |= toggleCrossfeed.CrossfeedToggleableFlight();
                    if (canToggleCrossfeedRequireTech && toggleCrossfeed.CrossfeedRequiresTech() && !toggleCrossfeed.CrossfeedHasTech())
                    {
                        techID = toggleCrossfeed.CrossfeedTech();
                    }
                    else
                    {
                        canToggleCrossfeedRequireTech = false;
                    }
                }
                midx--;
            }

            if (!canToggleCrossfeedEditor && !canToggleCrossfeedFlight)
            {
                if (!part.fuelCrossFeed)
                    sb.Append(PartListTooltip.cacheAutoLOC_456368); // "\n<color=orange>No Fuel Crossfeed</color>"
            }
            else
            {
                int crossfeedMode = 0;
                if (canToggleCrossfeedEditor && canToggleCrossfeedFlight)
                    crossfeedMode = 0;
                else if (canToggleCrossfeedEditor)
                    crossfeedMode = 1;
                else if (canToggleCrossfeedFlight)
                    crossfeedMode = 2;

                sb.Append(Localizer.Format("#autoLOC_456374", crossfeedMode)); // "\n<color=orange>Crossfeed toggles in <<1[Editor and Flight/Editor/Flight]>>."

                if (canToggleCrossfeedRequireTech)
                {
                    sb.Append(" ");
                    sb.Append(Localizer.Format("#autoLOC_456381", Localizer.Format(ResearchAndDevelopment.GetTechnologyTitle(techID)))); // "Requires <<1>>."
                }

                sb.Append(" ");
                sb.Append(Localizer.Format("#autoLOC_456384", Convert.ToInt32(part.fuelCrossFeed)));
            }

            if (ResearchAndDevelopment.IsExperimentalPart(part.partInfo.partPrefab.partInfo))
                sb.Append(PartListTooltip.cacheAutoLOC_456391); // "\n\n** EXPERIMENTAL **";

            sb.Append("</color>\n\n<color=");
            sb.Append(XKCDColors.HexFormat.KSPBadassGreen);
            sb.Append(">");

            bool hasModuleInfo = false;
            foreach (ModuleInfo moduleInfo in moduleInfos)
            {
                if (!string.IsNullOrEmpty(moduleInfo.primaryInfo))
                {
                    sb.Append(moduleInfo.primaryInfo);
                    sb.Append("\n");
                    hasModuleInfo = true;
                }
            }

            // note : the stock tooltip also list resources here. They already are in the PAW
            // and in the extended list, so we don't do that.

            sb.Append("</color>");

            if (hasModuleInfo)
                sb.Append("\n");

            if (appliedUpgrades.Count != 0)
            {
                sb.Append(Localizer.Format("#autoLOC_140995")); //#autoLOC_140995 = Part Upgrades
                sb.Append(": </b>");

                foreach (string appliedUpgrade in appliedUpgrades)
                {
                    if (!PartUpgradeManager.handler.upgrades.TryGetValue(appliedUpgrade, out PartUpgradeHandler.Upgrade upgrade))
                        continue;

                    sb.Append("\n<b><color=#BFFF00>");
                    sb.Append(upgrade.title);
                    sb.Append("</b></color>");

                    if (!string.IsNullOrEmpty(upgrade.description))
                    {
                        sb.Append("\n<color=#C2C2C2>");
                        sb.Append(upgrade.description);
                        sb.Append("</color>");
                    }
                }
            }

            pmGetPartPrimaryInfo.End();
            return sb.ToStringAndRelease();
        }

        private static ProfilerMarker pmGetPartCost = new ProfilerMarker("InstantiatedPartTooltip.GetPartCost");

        private static string GetPartCost(Part part)
        {
            pmGetPartCost.Begin();

            float dryCost;
            if (part.partInfo == null)
                dryCost = 0f;
            else
                dryCost = part.partInfo.cost;

            dryCost += part.GetModuleCosts(dryCost);
            float resCost = 0f;

            for (int i = part.Resources.Count; i-- > 0;)
            {
                PartResource partResource = part.Resources[i];
                PartResourceDefinition info = partResource.info;
                dryCost -= info.unitCost * (float)partResource.maxAmount;
                resCost += info.unitCost * (float)partResource.amount;
            }

            float cost = Mathf.Max(0f, dryCost + resCost);

            string costLoc = Localizer.Format("#autoLOC_8100136"); // "Cost"
            string fundsSprite = "<sprite=\"CurrencySpriteAsset\" name=\"Funds\" tint=1>  ";
            StringBuilder sb = StringBuilderCache.Acquire();

            sb.Append("<b>").Append(costLoc).Append(": </b>");
            sb.Append(fundsSprite);
            sb.Append("<b>");
            sb.Append(cost.ToString("N2"));
            sb.Append("</b>");

            pmGetPartCost.End();
            return sb.ToStringAndRelease();
        }

        private static ProfilerMarker pmGetModulesInfo = new ProfilerMarker("InstantiatedPartTooltip.GetModulesInfo");

        private void GetModulesInfo(Part part)
        {
            pmGetModulesInfo.Begin();
            foreach (PartModule pm in part.Modules)
            {
                // ModulePartVariants uses a different "gold" widget and I'm too
                // lazy to implement it, so just skip it (and that module info
                // is pretty useless anyway since you have it in the PAW).
                if (!pm.enabled || !pm.isEnabled || pm is ModulePartVariants)
                    continue;

                string info, primaryInfo, displayName;

                if (pm is IModuleInfo iModuleInfo)
                {
                    info = iModuleInfo.GetInfo();
                    primaryInfo = iModuleInfo.GetPrimaryField();

                    if (string.IsNullOrWhiteSpace(info) && string.IsNullOrWhiteSpace(primaryInfo))
                        continue;

                    info = info.Trim();
                    primaryInfo = primaryInfo?.Trim();

                    displayName = pm.GetModuleDisplayName();
                    if (string.IsNullOrEmpty(displayName))
                        displayName = iModuleInfo.GetModuleTitle();
                }
                else
                {
                    info = pm.GetInfo();
                    if (string.IsNullOrWhiteSpace(info))
                        continue;

                    info = info.Trim();
                    primaryInfo = string.Empty;

                    displayName = pm.GetModuleDisplayName();
                    if (string.IsNullOrEmpty(displayName))
                        displayName = pm.GUIName ?? KSPUtil.PrintModuleName(pm.moduleName);
                }

                if (info.Length != 0 && pm.upgradesApplied.Count != 0)
                    foreach (string upgradeName in pm.upgradesApplied)
                        appliedUpgrades.Add(upgradeName);

                moduleInfos.Add(new ModuleInfo(displayName, info, primaryInfo));

                if (pm is ModuleInventoryPart inventory)
                    cargoMass += inventory.GetModuleMass(0f, ModifierStagingSituation.CURRENT);
            }

            // note : stock sort modules by their "moduleName" which will be different from "displayName" if
            // GetModuleDisplayName() is implemented and return something different, so we aren't following 
            // stock ordering 1:1 here, but arguably it make more sense to order modules by the name that is
            // actually displayed...
            moduleInfos.Sort((x, y) => x.displayName.CompareTo(y.displayName));
            pmGetModulesInfo.End();
        }

        private static ProfilerMarker pmGetResourcesInfo = new ProfilerMarker("InstantiatedPartTooltip.GetResourcesInfo");
        private void GetResourcesInfo(Part part)
        {
            pmGetResourcesInfo.Begin();
            foreach (PartResource pr in part.Resources)
            {
                if (!pr.isVisible)
                    continue;

                ModuleInfo resourceInfo = new ModuleInfo();
                resourceInfo.displayName = pr.info.displayName.LocalizeRemoveGender();
                StringBuilder sb = StringBuilderCache.Acquire();
                sb.Append(Localizer.Format("#autoLOC_166269", pr.amount.ToString("F1")));
                if (pr.amount != pr.maxAmount)
                {
                    sb.Append(" ");
                    sb.Append(Localizer.Format("#autoLOC_6004042", pr.maxAmount.ToString("F1")));
                }

                if (pr.info.density > 0f)
                {
                    sb.Append(Localizer.Format("#autoLOC_166270", (pr.amount * pr.info.density).ToString("F2")));
                }

                if (pr.info.unitCost > 0f)
                {
                    sb.Append(Localizer.Format("#autoLOC_166271", (pr.amount * pr.info.unitCost).ToString("F2")));
                }

                resourceInfo.info = sb.ToStringAndRelease();

                resourceInfos.Add(resourceInfo);
            }

            resourceInfos.Sort((x, y) => x.displayName.CompareTo(y.displayName));
            pmGetResourcesInfo.End();
        }
    }
}
