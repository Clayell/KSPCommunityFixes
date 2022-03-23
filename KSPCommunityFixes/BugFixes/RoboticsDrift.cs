﻿using System;
using Expansions;
using Expansions.Serenity;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    public class RoboticsDrift : BasePatch
    {
        protected override Version VersionMin => new Version(1, 8, 0);

        protected override bool CanApplyPatch(out string reason)
        {
            if (!Directory.Exists(Path.Combine(KSPExpansionsUtils.ExpansionsGameDataPath, "Serenity")))
            {
                reason = "Breaking Grounds DLC not installed";
                return false;
            }

            return base.CanApplyPatch(out reason);
        }

        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnStart)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnDestroy)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.RecurseCoordUpdate)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnSave)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.ModifyLocked)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnPartPack)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(BaseServo), nameof(BaseServo.OnPartPack)),
                this));

            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }

        private static readonly Dictionary<Part, ServoInfo> servoInfos = new Dictionary<Part, ServoInfo>();

        private void OnSceneSwitch(GameScenes data)
        {
            servoInfos.Clear();
        }

        private static void BaseServo_OnStart_Postfix(BaseServo __instance, GameObject ___movingPartObject, bool ___servoTransformPosLoaded, bool ___servoTransformRotLoaded)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return;

            ServoInfo servoInfo;
            Vector3d movingPartObjectPos;
            QuaternionD movingPartObjectRot;

            if (___servoTransformPosLoaded)
                movingPartObjectPos = __instance.servoTransformPosition;
            else
                movingPartObjectPos = ___movingPartObject.transform.localPosition;

            if (___servoTransformRotLoaded)
                movingPartObjectRot = __instance.servoTransformRotation;
            else
                movingPartObjectRot = ___movingPartObject.transform.localRotation;

            if (__instance is ModuleRoboticServoPiston)
                servoInfo = new TranslationServoInfo(__instance, ___movingPartObject, movingPartObjectPos, movingPartObjectRot);
            else
                servoInfo = new RotationServoInfo(__instance, ___movingPartObject, movingPartObjectPos, movingPartObjectRot);

            servoInfos.Add(__instance.part, servoInfo);
        }

        private static void BaseServo_OnDestroy_Postfix(BaseServo __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                servoInfos.Remove(__instance.part);
        }

        private static bool BaseServo_RecurseCoordUpdate_Prefix(BaseServo __instance, Part p, ConfigurableJoint ___servoJoint, GameObject ___movingPartObject)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
                return true;

            // don't update when called from OnStart() / OnStartBeforePartAttachJoint()
            // we need the joint to exist to know where child parts are attached, and we don't want
            // the stock logic to alter orgPos/orgRot. I con't find a reason why updating
            // coords at this point could be useful anyway, at least in flight. It might be necessary
            // in the editor, since parts are parented and the initial position of the moving object
            // might have been modified.
            if (p.attachJoint == null)
                return false;

            if (!servoInfos.TryGetValue(p, out ServoInfo servoInfo))
            {
                Debug.LogWarning($"[RoboticsDrift] Servo info not found for {__instance.GetType()} on {p}, drift correction won't be applied !");
                return true;
            }

            servoInfo.PristineCoordsUpdate();

            return false;
        }

        private static bool BaseServo_OnSave_Prefix(BaseServo __instance, ConfigNode node)
        {
            if (!__instance.servoInitComplete)
                return false;

            if (__instance.movingPartObject != null)
            {
                if (HighLogic.LoadedScene == GameScenes.EDITOR)
                {
                    __instance.ApplyCoordsUpdate();
                    __instance.servoTransformPosition = __instance.movingPartObject.transform.localPosition;
                    __instance.servoTransformRotation = __instance.movingPartObject.transform.localRotation;
                }
                else
                {
                    if (__instance.vessel == null || !__instance.vessel.loaded)
                        return false;

                    __instance.ApplyCoordsUpdate();

                    if (!__instance.servoIsLocked)
                    {
                        if (servoInfos.TryGetValue(__instance.part, out ServoInfo servoInfo))
                        {
                            servoInfo.GetMovingPartPristineCoords(out Vector3d position, out QuaternionD rotation);
                            __instance.servoTransformPosition = position;
                            __instance.servoTransformRotation = rotation;
                        }
                        else
                        {
                            Debug.LogWarning($"[RoboticsDrift] Servo info not found for {__instance.GetType()} on {__instance.part}, drift correction won't be applied !");
                            __instance.servoTransformPosition = __instance.movingPartObject.transform.localPosition;
                            __instance.servoTransformRotation = __instance.movingPartObject.transform.localRotation;
                        }
                    }
                }

                node.SetValue("servoTransformPosition", __instance.servoTransformPosition);
                node.SetValue("servoTransformRotation", __instance.servoTransformRotation);
            }

            // note : "jointParent" is unused in stock parts, and I'm unsure what its purpose is. It seem to be
            // an extra configuration option for a more complicated part model setup / hierarchy. It is likely
            // that using that option will cause weird things when our patch is used anyway.
            if (__instance.jointParent != null)
            {
                node.SetValue("jointParentRotation", __instance.jointParent.localRotation);
            }

            return false;
        }

        private static void BaseServo_ModifyLocked_Prefix(BaseServo __instance)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
                return;

            if (__instance.servoIsLocked && !__instance.prevServoIsLocked)
            {
                if (!servoInfos.TryGetValue(__instance.part, out ServoInfo servoInfo))
                {
                    Debug.LogWarning($"[RoboticsDrift] Servo info not found for {__instance.GetType()} on {__instance.part}, drift correction won't be applied !");
                    return;
                }

                servoInfo.RestoreMovingPartPristineCoords();
            }
        }

        private static void BaseServo_OnPartPack_Prefix(BaseServo __instance, out ServoInfo __state)
        {
            if (__instance.servoInitComplete && HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (!servoInfos.TryGetValue(__instance.part, out __state))
                {
                    Debug.LogWarning($"[RoboticsDrift] Servo info not found for {__instance.GetType()} on {__instance.part}, drift correction won't be applied !");
                    return;
                }

                __state.RestoreMovingPartPristineCoords();
            }
            else
            {
                __state = null;
            }
        }

        // When packing a vessel (docking, timewarping, getting out of physics range...), KSP update the parts transform position
        // based on Part.orgPos, but it doesn't update the transform rotation based on Part.orgRot. The rotation stays to whatever
        // it was in physics. This probably can be qualified as a bug, but fixing that globally might have side issues, so instead
        // of patching Part.Pack(), we only do it for child of robotic parts.
        private static void BaseServo_OnPartPack_Postfix(ServoInfo __state)
        {
            if (__state != null)
            {
                __state.UpdatePartsRotation();
            }
        }

        private abstract class ServoInfo
        {
            private static Queue<Part> partQueue = new Queue<Part>();

            protected readonly BaseServo servo;
            protected readonly GameObject movingPart;
            protected readonly Vector3d mainAxis;
            protected bool isInverted;
            private bool isInitialized;

            public ServoInfo(BaseServo servo, GameObject movingPartObject)
            {
                this.servo = servo;
                movingPart = movingPartObject;
                mainAxis = servo.GetMainAxis();
                isInitialized = false;
            }

            public void PristineCoordsUpdate()
            {
                if (!isInitialized)
                {
                    isInverted = servo.part.attachJoint.Joint.gameObject == movingPart;
                    isInitialized = true;
                }

                UpdateOffset();

                Part p = servo.part;
                for (int i = 0; i < p.children.Count; i++)
                {
                    // Dont move the child if :
                    // - child is attached the servo moving part, and servo is attached to its parent by its moving part
                    // - child is attached to the servo non-moving part, and servo is attached to its parent by its non-moving part
                    if (p.children[i].attachJoint.Joint.connectedBody.gameObject == movingPart)
                    {
                        if (isInverted)
                            continue;
                    }
                    else if (!isInverted)
                    {
                        continue;
                    }

                    RecurseChildCoordsUpdate(p.children[i]);
                }
            }

            public void RestoreMovingPartPristineCoords()
            {
                GetMovingPartPristineCoords(out Vector3d localPos, out QuaternionD localRot);
                movingPart.transform.localPosition = localPos;
                movingPart.transform.localRotation = localRot;
            }

            public void UpdatePartsRotation()
            {
                Quaternion vesselRotation = servo.vessel.vesselTransform.rotation;

                partQueue.Clear();
                partQueue.Enqueue(servo.part);

                while (partQueue.TryDequeue(out Part part))
                {
                    int childCount = part.children.Count;
                    for (int i = 0; i < childCount; i++)
                        partQueue.Enqueue(part.children[i]);

                    part.transform.rotation = vesselRotation * part.orgRot;
                }
            }

            public abstract void GetMovingPartPristineCoords(out Vector3d localPos, out QuaternionD localRot);

            protected abstract void UpdateOffset();

            protected abstract void RecurseChildCoordsUpdate(Part part);

            protected QuaternionD GetLocalToVesselSpace()
            {
                // for some reason not normalizing can end up with a quaternion with near infinity components 
                // when it should be identity, leading to infinity and NaN down the line...
                return (QuaternionD.Inverse(servo.part.orgRot) * (QuaternionD)servo.part.vessel.rootPart.orgRot).Normalize();
            }
        }

        private class TranslationServoInfo : ServoInfo
        {
            private readonly Quaternion movingPartPristineLocalRot;
            private Vector3d lastLocalOffset;
            private Vector3d posOffset;

            public TranslationServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3d movingPartLocalPos, Quaternion movingPartLocalRot) : base(baseServo, movingPartObject)
            {
                movingPartPristineLocalRot = movingPartLocalRot;
                lastLocalOffset = mainAxis * movingPartLocalPos.magnitude;
            }

            protected override void UpdateOffset()
            {
                Quaternion localToVesselSpace = GetLocalToVesselSpace();

                // using the magnitude *feels* like what we should be doing. But this isn't what stock is doing, it only get the component
                // on the translation axis. I can't detect a visible difference after a 8 servos setup "torture test" moving during ~10 hours,
                // so I guess it doesn't really matter
                Vector3d localOffset = mainAxis * ((Vector3d)movingPart.transform.localPosition).magnitude;

                // get translation offset of the moving part since last update, and transform from the servo local space to the vessel space
                posOffset = localToVesselSpace.Inverse() * (localOffset - lastLocalOffset);

                // save the moving part position
                lastLocalOffset = localOffset;

                // if servo is attached to its parent by its moving part, we need to :
                // - invert the offset
                // - translate the servo part itself
                if (isInverted)
                {
                    posOffset = -posOffset;
                    servo.part.orgPos += posOffset;
                }
            }

            protected override void RecurseChildCoordsUpdate(Part part)
            {
                // apply the offset to the original position
                part.orgPos += posOffset;

                // propagate to childrens
                for (int i = 0; i < part.children.Count; i++)
                    RecurseChildCoordsUpdate(part.children[i]);
            }

            public override void GetMovingPartPristineCoords(out Vector3d localPos, out QuaternionD localRot)
            {
                localPos = mainAxis * ((Vector3d)movingPart.transform.localPosition).magnitude;
                localRot = movingPartPristineLocalRot;
            }
        }

        private class RotationServoInfo : ServoInfo
        {
            private readonly int mainAxisIndex;
            private readonly Vector3d movingPartPristineLocalPos;
            private QuaternionD rotOffset;

            private double lastRotAngle;

            public RotationServoInfo(BaseServo baseServo, GameObject movingPartObject, Vector3d movingPartLocalPos, Quaternion movingPartLocalRot) : base(baseServo, movingPartObject)
            {
                switch (baseServo.mainAxis)
                {
                    case "X": mainAxisIndex = 0; break;
                    case "Y": mainAxisIndex = 1; break;
                    case "Z": mainAxisIndex = 2; break;
                }

                movingPartPristineLocalPos = movingPartLocalPos;
                lastRotAngle = CurrentAngle(movingPartLocalRot);
            }

            protected override void UpdateOffset()
            {
                QuaternionD localToVesselSpace = GetLocalToVesselSpace();

                // get rotation offset of the moving part since last update
                double rotAngle = CurrentAngle(movingPart.transform.localRotation);
                double angleOffset = rotAngle - lastRotAngle;
                lastRotAngle = rotAngle;
                rotOffset = QuaternionD.AngleAxis(angleOffset, mainAxis);

                // transform offset from the servo local space to the vessel space
                rotOffset = QuaternionD.Inverse(localToVesselSpace) * rotOffset * localToVesselSpace;

                // if servo is attached to its parent by its moving part, we need to :
                // - invert the offset
                // - rotate the servo part itself
                if (isInverted)
                {
                    rotOffset = QuaternionD.Inverse(rotOffset);
                    servo.part.orgRot = rotOffset * (QuaternionD)servo.part.orgRot;
                }
            }

            // Getting the euler angle along the servo axis is what is used by stock code to get the current angle.
            // This seems to be as accurate as using more complicated methods where we attempt to first get
            // the in-physics axis to account for the servo internal deformation. I guess all that matters is that
            // the resulting offset stays consistent over time.
            private double CurrentAngle(Quaternion movingObjectLocalRotation)
            {
                return movingObjectLocalRotation.eulerAngles[mainAxisIndex];
            }

            public override void GetMovingPartPristineCoords(out Vector3d localPos, out QuaternionD localRot)
            {
                localPos = movingPartPristineLocalPos;
                double rotAngle = CurrentAngle(movingPart.transform.localRotation);
                localRot = QuaternionD.AngleAxis(rotAngle, mainAxis);
            }

            protected override void RecurseChildCoordsUpdate(Part part)
            {
                RecurseChildCoordsUpdate(part, Vector3d.zero);
            }

            private void RecurseChildCoordsUpdate(Part part, Vector3d posOffset)
            {
                // get position offset between this part and its parent
                Vector3d orgPosOffset = part.orgPos - part.parent.orgPos;
                // add the upstream position offset (from all parents), then rotate the result
                orgPosOffset = rotOffset * (orgPosOffset + posOffset);
                // get the new position for this part
                Vector3d newOrgPos = part.parent.orgPos + orgPosOffset;
                // update the total offset that will be applied downstream to childrens
                posOffset = newOrgPos - part.orgPos;
                // apply the new position
                part.orgPos = newOrgPos;

                // apply the rotation
                part.orgRot = rotOffset * (QuaternionD)part.orgRot;

                // propagate to childrens
                for (int i = 0; i < part.children.Count; i++)
                    RecurseChildCoordsUpdate(part.children[i], posOffset);
            }
        }
    }

    public static class QuaternionExtensions
    {
        public static QuaternionD Normalize(this QuaternionD q)
        {
            double ls = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            double invNorm = 1.0 / Math.Sqrt(ls);
            return new QuaternionD(q.x * invNorm, q.y * invNorm, q.z * invNorm, q.w * invNorm);
        }
    }
}
