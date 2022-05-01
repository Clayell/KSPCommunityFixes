﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEngine;

namespace KSPCommunityFixes.QoL
{
    class BetterSAS : BasePatch
    {
        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Constructor(typeof(VesselAutopilot), new Type[]{typeof(Vessel)}),
                this, nameof(VesselAutopilot_Ctor_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.ConnectFlyByWire)),
                this, nameof(VesselAutopilot_VesselSAS_ConnectFlyByWire_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.DisconnectFlyByWire)),
                this, nameof(VesselAutopilot_VesselSAS_DisconnectFlyByWire_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(VesselAutopilot.VesselSAS), nameof(VesselAutopilot.VesselSAS.ResetAllPIDS)),
                this, nameof(VesselAutopilot_VesselSAS_ResetAllPIDS_Prefix)));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleCommand), nameof(ModuleCommand.OnLoad)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleCommand), nameof(ModuleCommand.OnSave)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Postfix,
                AccessTools.Method(typeof(ModuleCommand), nameof(ModuleCommand.OnStart)),
                this));
        }

        static bool VesselAutopilot_Ctor_Prefix(VesselAutopilot __instance, Vessel vessel)
        {
            __instance.vessel = vessel;
            __instance.sas = new KSPCFVesselSAS(vessel);
            return false;
        }

        static bool VesselAutopilot_VesselSAS_ConnectFlyByWire_Prefix(VesselAutopilot.VesselSAS __instance, bool reset)
        {
            if (!(__instance is KSPCFVesselSAS derivedInstance))
                return true;

            if (!derivedInstance.FBWconnected)
            {
                derivedInstance.vessel.OnAutopilotUpdate += new FlightInputCallback(derivedInstance.ControlUpdate);

                derivedInstance.FBWconnected = true;
                if (!(derivedInstance.storedTransform != derivedInstance.vessel.ReferenceTransform) && !(derivedInstance.storedTransform == null))
                {
                    derivedInstance.LockRotation(derivedInstance.storedTransform.rotation);
                }
                else
                {
                    derivedInstance.storedTransform = derivedInstance.vessel.ReferenceTransform;
                    derivedInstance.LockRotation(derivedInstance.storedTransform.rotation);
                }
            }
            if (reset)
            {
                derivedInstance.ResetAllPIDS();
            }

            return false;
        }

        static bool VesselAutopilot_VesselSAS_DisconnectFlyByWire_Prefix(VesselAutopilot.VesselSAS __instance)
        {
            if (!(__instance is KSPCFVesselSAS derivedInstance))
                return true;

            derivedInstance.targetOrientation = Vector3.zero;
            derivedInstance.lockedRotation = Quaternion.identity;
            derivedInstance.ResetAllPIDS();
            if (derivedInstance.FBWconnected)
            {
                derivedInstance.vessel.OnAutopilotUpdate -= new FlightInputCallback(derivedInstance.ControlUpdate);
                derivedInstance.FBWconnected = false;
            }

            return false;
        }

        static bool VesselAutopilot_VesselSAS_ResetAllPIDS_Prefix(VesselAutopilot.VesselSAS __instance)
        {
            if (!(__instance is KSPCFVesselSAS derivedInstance))
                return true;

            derivedInstance.ResetAllPIDS();
            return false;
        }

        private const string CONTROLLER_VALUENAME = "KSPCF_AttCtrl";

        static void ModuleCommand_OnLoad_Postfix(ModuleCommand __instance, ConfigNode node)
        {
            if (__instance.vessel.AsNull()?.autopilot?.sas == null || !(__instance.vessel.autopilot.sas is KSPCFVesselSAS customSAS))
                return;

            string controller = node.GetValue(CONTROLLER_VALUENAME);
            if (!string.IsNullOrEmpty(controller) && Enum.TryParse(controller, out KSPCFVesselSAS.AttitudeController value))
                customSAS.controller = value;
        }

        static void ModuleCommand_OnSave_Postfix(ModuleCommand __instance, ConfigNode node)
        {
            if (__instance.vessel.AsNull()?.autopilot?.sas == null || !(__instance.vessel.autopilot.sas is KSPCFVesselSAS customSAS))
                return;

            node.AddValue(CONTROLLER_VALUENAME, customSAS.controller.ToString());
        }

        static KSPEvent attitudeControllerKSPEvent = new KSPEvent
        {
            advancedTweakable = true,
            active = true,
            guiActive = true,
            guiActiveEditor = false
        };

        static void ModuleCommand_OnStart_Postfix(ModuleCommand __instance)
        {
            if (__instance.vessel.AsNull()?.autopilot?.sas == null || !(__instance.vessel.autopilot.sas is KSPCFVesselSAS customSAS))
                return;

            BaseEventDelegate baseEventDelegate = () => KSPCFVesselSAS.OnAttitudeControllerSwitch(__instance);
            BaseEvent baseEvent = __instance.events.Add(KSPCFVesselSAS.EVENT_NAME, baseEventDelegate, attitudeControllerKSPEvent);
            baseEvent.guiName = customSAS.AttitudeControllerGuiName();

            BaseEventDelegate baseEventDelegate2 = () => TorqueTest(__instance);
            BaseEvent baseEvent2 = __instance.events.Add("Torquetest", baseEventDelegate2, attitudeControllerKSPEvent);
            baseEvent2.guiName = "Torque test";
        }

        static void TorqueTest(ModuleCommand module)
        {
            Debug.Log("======= LOGGING VESSEL TORQUE PROVIDERS =======");
            foreach (ITorqueProvider torqueProvider in module.vessel.FindPartModulesImplementing<ITorqueProvider>())
            {
                torqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                Debug.Log($"pos={pos} neg={neg} {torqueProvider.GetType()} - {((PartModule)torqueProvider).part.partInfo.name}");
            }
        }
    }

    public class KSPCFVesselSAS : VesselAutopilot.VesselSAS
    {
        public enum AttitudeController
        {
            Stock = 1,
            MechJeb = 2
        }

        public AttitudeController controller = AttitudeController.MechJeb;

        public KSPCFVesselSAS(Vessel v) : base(v)
        {
        }

        public new void ControlUpdate(FlightCtrlState s)
        {
            switch (controller)
            {
                case AttitudeController.Stock:
                    StockSASControlUpdate(s);
                    break;
                case AttitudeController.MechJeb:
                    MechJebControlUpdate(s);
                    break;
            }
        }

        public new void ResetAllPIDS()
        {
            switch (controller)
            {
                case AttitudeController.Stock:
                    pidLockedPitch.Reset();
                    pidLockedRoll.Reset();
                    pidLockedYaw.Reset();
                    break;
                case AttitudeController.MechJeb:
                    MechJebResetResetPID(0);
                    MechJebResetResetPID(1);
                    MechJebResetResetPID(2);
                    break;
            }
        }

        private new void UpdateVesselTorque(FlightCtrlState s)
        {
            torqueVector = GetTotalVesselTorque(vessel);
        }

        static ProfilerMarker vesselTorqueProfiler = new ProfilerMarker("KSPCFVesselSAS.GetTotalVesselTorque");

        private new Vector3 GetTotalVesselTorque(Vessel v)
        {
            vesselTorqueProfiler.Begin();
            posTorque = Vector3.zero;
            negTorque = Vector3.zero;
            int partIdx = vessel.parts.Count;
            while (partIdx-- > 0)
            {
                Part part = vessel.parts[partIdx];
                int moduleIdx = part.Modules.Count;
                while (moduleIdx-- > 0)
                {
                    if (part.Modules[moduleIdx] is ITorqueProvider torqueProvider && torqueProvider != null)
                    {
                        torqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
                        posTorque += pos;
                        negTorque += neg;
                    }
                }
            }

            if (posTorque.x < 0f) posTorque.x = 0f;
            if (posTorque.y < 0f) posTorque.y = 0f;
            if (posTorque.z < 0f) posTorque.z = 0f;
            if (negTorque.x < 0f) negTorque.x = 0f;
            if (negTorque.y < 0f) negTorque.y = 0f;
            if (negTorque.z < 0f) negTorque.z = 0f;

            Vector3 averageTorque = new Vector3(
                0.5f * (posTorque.x + negTorque.x),
                0.5f * (posTorque.y + negTorque.y),
                0.5f * (posTorque.z + negTorque.z));

            vesselTorqueProfiler.End();
            return averageTorque;
        }

        public const string EVENT_NAME = "KSPCFAttitudeControllerSwitch";

        public static void OnAttitudeControllerSwitch(ModuleCommand origin)
        {
            if (origin.vessel.AsNull()?.autopilot?.sas == null || !(origin.vessel.autopilot.sas is KSPCFVesselSAS customSAS))
                return;

            customSAS.ResetAllPIDS();

            switch (customSAS.controller)
            {
                case AttitudeController.Stock:
                    customSAS.controller = AttitudeController.MechJeb;
                    break;
                case AttitudeController.MechJeb:
                    customSAS.controller = AttitudeController.Stock;
                    break;
            }

            string guiName = customSAS.AttitudeControllerGuiName();
            foreach (ModuleCommand moduleCommand in origin.vessel.FindPartModulesImplementing<ModuleCommand>())
            {
                BaseEvent baseEvent = moduleCommand.Events[EVENT_NAME];
                if (baseEvent != null)
                    baseEvent.guiName = guiName;
            }

            customSAS.ResetAllPIDS();
        }

        public string AttitudeControllerGuiName()
        {
            switch (controller)
            {
                case AttitudeController.MechJeb:
                    return "Attitude controller: MechJeb™";
                case AttitudeController.Stock:
                    return "Attitude controller: Stock SAS";
            }

            return string.Empty;
        }

        #region Stock SAS

        private Vector3 angularAccelerationPosMax;
        private Vector3 angularAccelerationNegMax;

        private void StockSASControlUpdate(FlightCtrlState s)
        {
            if (!(storedTransform == null))
            {
                UpdateVesselTorque(s); // called here instead from a separate OnAutopilotUpdate callback

                currentRotation = storedTransform.rotation;
                UpdateMaximumAcceleration();
                rotationDelta = Quaternion.Inverse(GetRotationDelta()).eulerAngles;
                if (!lockedMode)
                {
                    PitchYawAngle(vessel.ReferenceTransform, targetOrientation, out neededPitch, out neededYaw);
                    rotationDelta.x = 0f - neededPitch;
                    rotationDelta.z = neededYaw;
                    CheckCoasting();
                }
                else if (!dampingMode)
                {
                    CheckCoasting();
                }
                angularDelta.x = AngularTrim(rotationDelta.x);
                angularDelta.y = AngularTrim(rotationDelta.y);
                angularDelta.z = AngularTrim(rotationDelta.z);
                angularDeltaRad = angularDelta * 0.01745329238474369;
                StabilityDecay();
                AutoTuneScalar();
                sasResponse.x = pidLockedPitch.Update(angularDeltaRad.x, TimeWarp.deltaTime) / pidLockedPitch.clamp;
                sasResponse.y = pidLockedRoll.Update(angularDeltaRad.y, TimeWarp.deltaTime) / pidLockedRoll.clamp;
                sasResponse.z = pidLockedYaw.Update(angularDeltaRad.z, TimeWarp.deltaTime) / pidLockedYaw.clamp;
                CheckDamping();
                sasResponse.x = UtilMath.Clamp(sasResponse.x, -1.0, 1.0);
                sasResponse.y = UtilMath.Clamp(sasResponse.y, -1.0, 1.0);
                sasResponse.z = UtilMath.Clamp(sasResponse.z, -1.0, 1.0);
                s.pitch = (float)sasResponse.x;
                s.roll = (float)sasResponse.y;
                s.yaw = (float)sasResponse.z;
                lastRotation = currentRotation;
            }
        }

        private new void UpdateMaximumAcceleration()
        {
            angularAccelerationMax.x = Mathf.Max(torqueVector.x / vessel.MOI.x, 0.0001f);
            angularAccelerationMax.y = Mathf.Max(torqueVector.y / vessel.MOI.y, 0.0001f);
            angularAccelerationMax.z = Mathf.Max(torqueVector.z / vessel.MOI.z, 0.0001f);

            angularAccelerationPosMax.x = Mathf.Max(posTorque.x / vessel.MOI.x, 0.0001f);
            angularAccelerationPosMax.y = Mathf.Max(posTorque.y / vessel.MOI.y, 0.0001f);
            angularAccelerationPosMax.z = Mathf.Max(posTorque.z / vessel.MOI.z, 0.0001f);

            angularAccelerationNegMax.x = Mathf.Max(negTorque.x / vessel.MOI.x, 0.0001f);
            angularAccelerationNegMax.y = Mathf.Max(negTorque.y / vessel.MOI.y, 0.0001f);
            angularAccelerationNegMax.z = Mathf.Max(negTorque.z / vessel.MOI.z, 0.0001f);
        }

        private new void CheckCoasting()
        {
            if (angularAccelerationMax.x > 0f)
            {
                // float num = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.x / angularAccelerationMax.x);
                float num;
                if (vessel.angularVelocity.x > 0f)
                    num = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.x / angularAccelerationPosMax.x);
                else
                    num = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.x / angularAccelerationNegMax.x);


                float num2 = Mathf.Abs(rotationDelta.x * ((float)Math.PI / 180f) / vessel.angularVelocity.x);
                if (num * stopScalar > num2 && Math.Sign(rotationDelta.x) != Math.Sign(vessel.angularVelocity.x))
                {
                    pidLockedPitch.Reset();
                    rotationDelta.x = 0f - rotationDelta.x;
                }
                else if (num * coastScalar > num2 && Math.Sign(rotationDelta.x) != Math.Sign(vessel.angularVelocity.x))
                {
                    pidLockedPitch.Reset();
                    rotationDelta.x = 0f;
                }
            }

            if (angularAccelerationMax.z > 0f)
            {
                //float num3 = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.z / angularAccelerationMax.z);
                float num3;
                if (vessel.angularVelocity.z > 0f)
                    num3 = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.z / angularAccelerationPosMax.z);
                else
                    num3 = Mathf.Abs(Time.deltaTime + vessel.angularVelocity.z / angularAccelerationNegMax.z);


                float num4 = Mathf.Abs(rotationDelta.z * ((float)Math.PI / 180f) / vessel.angularVelocity.z);
                if (num3 * stopScalar > num4 && Math.Sign(rotationDelta.z) != Math.Sign(vessel.angularVelocity.z))
                {
                    pidLockedYaw.Reset();
                    rotationDelta.z = 0f - rotationDelta.z;
                }
                else if (num3 * coastScalar > num4 && Math.Sign(rotationDelta.z) != Math.Sign(vessel.angularVelocity.z))
                {
                    pidLockedYaw.Reset();
                    rotationDelta.z = 0f;
                }
            }
        }

        #endregion

        #region Mechjeb

        private class PIDLoop
        {
            public double Kp { get; set; } = 1.0;
            public double Ki { get; set; }
            public double Kd { get; set; }
            public double Ts { get; set; } = 0.02;
            public double N { get; set; } = 50;
            public double B { get; set; } = 1;
            public double C { get; set; } = 1;
            public double SmoothIn { get; set; } = 1.0;
            public double SmoothOut { get; set; } = 1.0;
            public double MinOutput { get; set; } = double.MinValue;
            public double MaxOutput { get; set; } = double.MaxValue;

            // internal state for PID filter
            private double _d1, _d2;

            // internal state for last measured and last output for low pass filters
            private double _m1 = double.NaN;
            private double _o1 = double.NaN;

            public double Update(double reference, double measured)
            {
                // lowpass filter the input
                measured = _m1.IsFiniteOrZero() ? _m1 + SmoothIn * (measured - _m1) : measured;

                double ep = B * reference - measured;
                double ei = reference - measured;
                double ed = C * reference - measured;

                // trapezoidal PID with derivative filtering as a digital biquad filter
                double a0 = 2 * N * Ts + 4;
                double a1 = -8 / a0;
                double a2 = (-2 * N * Ts + 4) / a0;
                double b0 = (4 * Kp * ep + 4 * Kd * ed * N + 2 * Ki * ei * Ts + 2 * Kp * ep * N * Ts + Ki * ei * N * Ts * Ts) / a0;
                double b1 = (2 * Ki * ei * N * Ts * Ts - 8 * Kp * ep - 8 * Kd * ed * N) / a0;
                double b2 = (4 * Kp * ep + 4 * Kd * ed * N - 2 * Ki * ei * Ts - 2 * Kp * ep * N * Ts + Ki * ei * N * Ts * Ts) / a0;

                // if we have NaN values saved into internal state that needs to be cleared here or it won't reset
                if (!_d1.IsFiniteOrZero())
                    _d1 = 0;
                if (!_d2.IsFiniteOrZero())
                    _d2 = 0;

                // transposed direct form 2
                double u0 = b0 + _d1;
                u0 = AttitudeUtils.Clamp(u0, MinOutput, MaxOutput);
                _d1 = b1 - a1 * u0 + _d2;
                _d2 = b2 - a2 * u0;

                // low pass filter the output
                _o1 = _o1.IsFiniteOrZero() ? _o1 + SmoothOut * (u0 - _o1) : u0;

                _m1 = measured;

                return _o1;
            }

            public void Reset()
            {
                _d1 = _d2 = 0;
                _m1 = double.NaN;
                _o1 = double.NaN;
            }
        }

        private static readonly Vector3d _vector3dnan = new Vector3d(double.NaN, double.NaN, double.NaN);

        private const double EPS = 2.2204e-16;

        private readonly double VelKp = 9.18299345180006;
        private readonly double VelKi = 16.2833478287224;
        private readonly double VelKd = -0.0921320503942923;
        private readonly double VelN = 99.6720838459594;
        private readonly double VelB = 0.596313214751797;
        private readonly double VelC = 0.596313214751797;
        private readonly double VelSmoothIn = 0.3;
        private readonly double VelSmoothOut = 1;
        private readonly double PosSmoothIn = 1;
        private readonly double PosFactor = 1.0;
        private readonly double maxStoppingTime = 2.0;
        private readonly double minFlipTime = 60;
        private readonly double rollControlRange = 5;
        private bool useControlRange = true;
        private bool useFlipTime = true;
        private bool useStoppingTime = true;

        /* error in pitch, roll, yaw */
        private Vector3d _error0 = Vector3d.zero;
        private Vector3d _error1 = _vector3dnan;

        /* max angular acceleration */
        private Vector3d _maxAlpha = Vector3d.zero;

        /* max angular rotation */
        private Vector3d _maxOmega = Vector3d.zero;
        private Vector3d _omega0 = _vector3dnan;
        private Vector3d _targetOmega = Vector3d.zero;
        private Vector3d _actuation = Vector3d.zero;

        /* error */
        private double _errorTotal;

        private readonly PIDLoop[] _pid =
        {
            new PIDLoop(),
            new PIDLoop(),
            new PIDLoop()
        };

        // ORGINIALLY IN MechJebModuleAttitudeController
        private Vector3d actuationControl = Vector3d.one;
        // END

        private Quaternion attitudeTarget;


        public void SetActuationControl(bool pitch, bool yaw, bool roll)
        {
            actuationControl = new Vector3d(pitch ? 1 : 0, roll ? 1 : 0, yaw ? 1 : 0);
        }

        private void MechJebControlUpdate(FlightCtrlState st)
        {
            if (storedTransform == null)
                return;

            UpdateAttitudeTarget();

            UpdatePredictionPI();

            for (int i = 0; i < 3; i++)
                if (Math.Abs(_actuation[i]) < EPS || double.IsNaN(_actuation[i]))
                    _actuation[i] = 0;

            Vector3d act = _actuation;

            act.Scale(actuationControl);

            SetFlightCtrlState(act, st, 1);
        }

        private void UpdateAttitudeTarget()
        {
            currentRotation = storedTransform.rotation;
            if (lockedMode)
            {
                attitudeTarget = lockedRotation;
            }
            else
            {
                Vector3 dir = targetOrientation;
                Vector3 up = -vessel.GetTransform().forward;
                Vector3.OrthoNormalize(ref dir, ref up);
                attitudeTarget = Quaternion.LookRotation(dir, up);
            }
        }

        private void SetFlightCtrlState(Vector3d act, FlightCtrlState s, float drive_limit)
        {
            bool userCommandingPitch = !Mathfx.Approx(s.pitch, s.pitchTrim, 0.1F);
            bool userCommandingYaw = !Mathfx.Approx(s.yaw, s.yawTrim, 0.1F);
            bool userCommandingRoll = !Mathfx.Approx(s.roll, s.rollTrim, 0.1F);

            // we will need that...
            //if (attitudeKILLROT)
            //    if (lastReferencePart != vessel.GetReferenceTransformPart() || userCommandingPitch || userCommandingYaw || userCommandingRoll)
            //    {
            //        attitudeTo(Quaternion.LookRotation(vessel.GetTransform().up, -vessel.GetTransform().forward), AttitudeReference.INERTIAL, null);
            //        lastReferencePart = vessel.GetReferenceTransformPart();
            //    }

            if (userCommandingPitch)
                MechJebResetResetPID(0);

            if (userCommandingRoll)
                MechJebResetResetPID(1);

            if (userCommandingYaw)
                MechJebResetResetPID(2);

            if (!userCommandingRoll)
                if (!double.IsNaN(act.y))
                    s.roll = Mathf.Clamp((float)act.y, -drive_limit, drive_limit);

            if (!userCommandingPitch && !userCommandingYaw)
            {
                if (!double.IsNaN(act.x)) s.pitch = Mathf.Clamp((float)act.x, -drive_limit, drive_limit);
                if (!double.IsNaN(act.z)) s.yaw = Mathf.Clamp((float)act.z, -drive_limit, drive_limit);
            }
        }

        private void UpdateError()
        {
            Transform vesselTransform = vessel.ReferenceTransform;

            // 1. The Euler(-90) here is because the unity transform puts "up" as the pointy end, which is wrong.  The rotation means that
            // "forward" becomes the pointy end, and "up" and "right" correctly define e.g. AoA/pitch and AoS/yaw.  This is just KSP being KSP.
            // 2. We then use the inverse ship rotation to transform the requested attitude into the ship frame (we do everything in the ship frame
            // first, and then negate the error to get the error in the target reference frame at the end).
            Quaternion deltaRotation;
            if (lockedMode)
                deltaRotation = Quaternion.identity;
            else
                deltaRotation = Quaternion.Inverse(vesselTransform.transform.rotation * Quaternion.Euler(-90, 0, 0)) * attitudeTarget;

            // get us some euler angles for the target transform
            Vector3d ea = deltaRotation.eulerAngles;
            double pitch = ea[0] * UtilMath.Deg2Rad;
            double yaw = ea[1] * UtilMath.Deg2Rad;
            double roll = ea[2] * UtilMath.Deg2Rad;

            // law of cosines for the "distance" of the miss in radians
            _errorTotal = Math.Acos(AttitudeUtils.Clamp(Math.Cos(pitch) * Math.Cos(yaw), -1, 1));

            // this is the initial direction of the great circle route of the requested transform
            // (pitch is latitude, yaw is -longitude, and we are "navigating" from 0,0)
            // doing this calculation is the ship frame is a bit easier to reason about.
            var temp = new Vector3d(Math.Sin(pitch), Math.Cos(pitch) * Math.Sin(-yaw), 0);
            temp = temp.normalized * _errorTotal;

            // we assemble phi in the pitch, roll, yaw basis that vessel.MOI uses (right handed basis)
            var phi = new Vector3d(
                AttitudeUtils.ClampRadiansPi(temp[0]), // pitch distance around the geodesic
                AttitudeUtils.ClampRadiansPi(roll),
                AttitudeUtils.ClampRadiansPi(temp[1]) // yaw distance around the geodesic
            );

            // apply the axis control from the parent controller
            //phi.Scale(ac.AxisControl);

            // the error in the ship's position is the negative of the reference position in the ship frame
            _error0 = -phi;
        }

        private void UpdatePredictionPI()
        {
            GetTotalVesselTorque(vessel);
            //GetVesselPotentialTorqueClampedByResponseSpeed();

            _omega0 = vessel.angularVelocityD;

            UpdateError();
            
            // lowpass filter on the error input
            _error0 = _error1.IsFiniteOrZero() ? _error1 + PosSmoothIn * (_error0 - _error1) : _error0;

            double deltaT = TimeWarp.fixedDeltaTime;

            // needed to stop wiggling at higher phys warp
            double warpFactor = Math.Pow(deltaT / 0.02, 0.90); // the power law here comes ultimately from the simulink PID tuning app

            // see https://archive.is/NqoUm and the "Alt Hold Controller", the acceleration PID is not implemented so we only
            // have the first two PIDs in the cascade.
            for (int i = 0; i < 3; i++)
            {
                double error = _error0[i];

                double MOI = vessel.MOI[i];
                double availableTorque = error > 0.0 ? negTorque[i] : posTorque[i];

                if (availableTorque != 0.0 && MOI != 0.0)
                {
                    _maxAlpha[i] = availableTorque / MOI;
                }
                else
                {
                    _maxAlpha[i] = 1.0;
                }

                double maxAlphaCbrt = Math.Pow(_maxAlpha[i], 1.0 / 3.0);
                double effLD = maxAlphaCbrt * PosFactor;
                double posKp = Math.Sqrt(_maxAlpha[i] / (2.0 * effLD));

                if (Math.Abs(error) <= 2.0 * effLD)
                    // linear ramp down of acceleration
                    _targetOmega[i] = -posKp * error;
                else
                    // v = -sqrt(2 * F * x / m) is target stopping velocity based on distance
                    _targetOmega[i] = -Math.Sqrt(2 * _maxAlpha[i] * (Math.Abs(error) - effLD)) * Math.Sign(error);

                if (useStoppingTime)
                {
                    _maxOmega[i] = _maxAlpha[i] * maxStoppingTime;
                    if (useFlipTime) _maxOmega[i] = Math.Max(_maxOmega[i], Math.PI / minFlipTime);
                    _targetOmega[i] = AttitudeUtils.Clamp(_targetOmega[i], -_maxOmega[i], _maxOmega[i]);
                }

                if (useControlRange && _errorTotal * Mathf.Rad2Deg > rollControlRange)
                    _targetOmega[1] = 0.0;

                _pid[i].Kp = VelKp / (_maxAlpha[i] * warpFactor);
                _pid[i].Ki = VelKi / (_maxAlpha[i] * warpFactor * warpFactor);
                _pid[i].Kd = VelKd / _maxAlpha[i];
                _pid[i].N = VelN / warpFactor;
                _pid[i].B = VelB;
                _pid[i].C = VelC;
                _pid[i].Ts = deltaT;
                _pid[i].SmoothIn = AttitudeUtils.Clamp01(VelSmoothIn);
                _pid[i].SmoothOut = AttitudeUtils.Clamp01(VelSmoothOut);
                _pid[i].MinOutput = -1;
                _pid[i].MaxOutput = 1;

                // need the negative from the pid due to KSP's orientation of actuation
                _actuation[i] = -_pid[i].Update(_targetOmega[i], _omega0[i]);

                if (Math.Abs(_actuation[i]) < EPS || double.IsNaN(_actuation[i])) 
                    _actuation[i] = 0;

                if (actuationControl[i] == 0)
                    MechJebResetResetPID(i);
            }

            _error1 = _error0;
        }

        private void MechJebResetResetPID(int i)
        {
            _pid[i].Reset();
            _omega0[i] = _error0[i] = _error1[i] = double.NaN;
        }

        #endregion
    }



    public static class AttitudeUtils
    {
        /// <summary>Return false if the value equals NaN or Infinity, true otherwise</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool IsFiniteOrZero(this double value)
        {
            long doubleAsLong = BitConverter.DoubleToInt64Bits(value);
            return (doubleAsLong & 0x7FFFFFFFFFFFFFFFL) < 9218868437227405312L;
        }

        /// <summary>Return false if any component of the vector equals NaN or Infinity, true otherwise</summary>
        public static bool IsFiniteOrZero(this Vector3d vector)
        {
            return vector.x.IsFiniteOrZero() && vector.y.IsFiniteOrZero() && vector.z.IsFiniteOrZero();
        }

        /// <summary>Clamp a value between min and max</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
            if (min > max)
                throw new ArgumentException($"{min} cannot be greater than {max}");

            if (value < min)
                return min;
            else if (value > max)
                return max;

            return value;
        }

        /// <summary>Clamp a value between 0.0 and 1.0</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;
            else if (value > 1.0)
                return 1.0;

            return value;
        }

        public static double ClampRadiansTwoPi(double angle)
        {
            angle = angle % (2 * Math.PI);
            if (angle < 0) return angle + 2 * Math.PI;
            else return angle;
        }

        public static double ClampRadiansPi(double angle)
        {
            angle = ClampRadiansTwoPi(angle);
            if (angle > Math.PI) angle -= 2 * Math.PI;
            return angle;
        }

    }
}
