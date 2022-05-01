﻿using System;
using HarmonyLib;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/*
This patch is a rewrite of the stock implementations for `ITorqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg)`.
All 4 of the stock implementations have various issues and are generally giving unreliable (not to say plain wrong) results.
Those issues are commented in each patch, but to summarize :
- ModuleRectionWheels is mostly ok, its only issue is to ignore the state of "authority limiter" tweakable
- ModuleRCS is giving entirely random results, and the stcok implementation just doesn't make any sense. Note that compared to
  other custom implementations (MechJeb, TCA, kOS), the KSPCF implementation account for the RCS module control scheme thrust 
  clamping and the actual thrust power (instead of the theoretical maximum).
- ModuleGimbal results are somewhat coherent, but their magnitude for pitch/yaw is wrong. They are underestimated for CoM-aligned 
  engines and vastly overestimated for engines placed off-CoM-center.
- ModuleControlSurface results are generally unreliable. Beside the fact that they can be randomly negative, the magnitude is
  usually wrong and inconsistent. Depending on part placement relative to the CoM, they can return zero available torque or being 
  vastly overestimated. They also don't account for drag induced torque, and are almost entirely borked when a control surface is 
  in the deployed state.

Note that the KSPCF GetPotentialTorque() implementations for ModuleControlSurface and especially for ModuleGimbal are more 
computationally intensive that the stock ones. Profiling a stock Dynawing with RCS enabled during ascent show a ~30% degradation 
when summing the vessel total available torque (~250 calls median : 0.31ms vs 0.24ms, frame time : 1.81% vs 1.46% ). Overall
this feels acceptable, but this is still is a non-negligible impact that will likely be noticeable in some cases (ie, 
atmospheric flight with a large vessel having many gimballing engines and control surfaces).
The implementations are pretty naive and could probably be vastly optimized by someone with a better understanding than me of 
the underlying maths and physics.

The KSPCF implementations follow these conventions :
- in pos/neg : x is pitch, y is roll, z is yaw
- `pos` is the actuation induced torque for a positive FlightCtrlState (pitch = 1, roll, = 1 yaw = 1) control request
- `neg` is the actuation induced torque for a negative FlightCtrlState (pitch = -1, roll, = -1 yaw = -1) control request
- Contrary to the stock implementations, values are strictly the **actuation induced** torque (ie, the torque difference
  between the neutral state and the actuated state). Especially in the case of ModuleGimbal, the stock implementation
  returns the actuation torque plus the eventual "structural" torque due to an eventual CoM/CoT misalignement.
- Positive values mean actuation will induce a torque in the desired direction. Negatives values mean that actuation will
  induce a torque in the opposite direction. For example, a negative `pos.x` value mean that for a positive roll actuation
  (ctrlState.roll = 1), the torque provider will produce a torque inducing a negative roll, essentially reducing the total
  available torque in that direction. This can notably happen with the stock aero control surfaces, due to their control 
  scheme being only based on their relative position/orientation to the vessel CoM and ignoring other factors like AoA.
- Like the stock implementations, they will give reliable results only if called from FixedUpdate(), including the control 
  state callbacks like `Vessel.OnFlyByWire` or `Vessel.On*AutopilotUpdate`. Calling them from the Update() loop will result 
  in an out-of-sync CoM position being used, producing garbage results.

So in the context of the KSPCF patch, a correct implementation of a `GetVesselPotentialTorque()` method is :
 ```cs
 foreach (ITorqueProvider torqueProvider)
 {
   torqueProvider.GetPotentialTorque(out Vector3 pos, out Vector3 neg);
   vesselPosTorque += pos;
   vesselNegTorque += neg;
 }
 if (vesselPosTorque.x < 0f) vesselPosTorque.x = 0f;
 if (vesselPosTorque.y < 0f) vesselPosTorque.y = 0f;
 if (vesselPosTorque.z < 0f) vesselPosTorque.z = 0f;
 if (vesselNegTorque.x < 0f) vesselNegTorque.x = 0f;
 if (vesselNegTorque.y < 0f) vesselNegTorque.y = 0f;
 if (vesselNegTorque.z < 0f) vesselNegTorque.z = 0f;
 ```

Quick review of how the stock implementations are handled in the modding ecosystem :
- *It seems* Mechjeb doesn't care about a value being from "pos" or "neg", it assume a negative value from either of the vector3 
  is a negative torque component (ie, if "pos.x" or "neg.x" is negative, it add that as negative available torque around x).
  Ref : https://github.com/MuMech/MechJeb2/blob/f5c1193813da7d2e2e347f963dd4ee4b7fb11a90/MechJeb2/VesselState.cs#L1073-L1076
  Ref2 : https://github.com/MuMech/MechJeb2/blob/f5c1193813da7d2e2e347f963dd4ee4b7fb11a90/MechJeb2/Vector6.cs#L82-L93
  As it is, since MechJeb doesn't care for pos/neg and only consider the max, the patches will result in wrong values, but arguably 
  since it reimplement RCS they will only be "different kind of wrong" for control surfaces and gimbals, and probably "less wrong"
  overall.
- kOS assume that the absolute value should be used.
  (side note : kOS reimplements ModuleReactionWheel.GetPotentialTorque() to get around the authority limiter bug)
  Ref : https://github.com/KSP-KOS/KOS/blob/7b7874153bc6c428404b3a1a913487b2fd0a9d99/src/kOS/Control/SteeringManager.cs#L658-L664
  The patches should apply mostly alright for kOS, at the exception of occasional negative values for gimbals and control surfaces
  being treated as positive, resulting in a higher available torque than what it should.
- TCA doesn't seem aware of the possibility of negative values, it assume they are positive.
  Ref : https://github.com/allista/ThrottleControlledAvionics/blob/b79a7372ab69616801f9953256b43ee872b90cf2/VesselProps/TorqueProps.cs#L167-L169
  The patches should more or less work for TCA, at the exception of negative gimbal/control surfaces values being treated incorrectly
  and the reaction wheels authority limiter being applied twice.
- Atmospheric Autopilot replace the stock module implementation by its own and doesn't use the interface at all
  Ref : https://github.com/Boris-Barboris/AtmosphereAutopilot/blob/master/AtmosphereAutopilot/SyncModuleControlSurface.cs
- FAR implements a replacement for ModuleControlSurface and consequently has a custom GetPotentialTorque() implementation.
  It seems that it will *always* return positive "pos" values and negative "neg" values :
  Ref : https://github.com/dkavolis/Ferram-Aerospace-Research/blob/95e127ae140b4be9699da8783d24dd8db726d753/FerramAerospaceResearch/LEGACYferram4/FARControllableSurface.cs#L294-L300
*/

namespace KSPCommunityFixes.BugFixes
{
    class GetPotentialTorqueFixes : BasePatch
    {
        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleReactionWheel), nameof(ModuleReactionWheel.GetPotentialTorque)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleRCS), nameof(ModuleRCS.GetPotentialTorque)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleControlSurface), nameof(ModuleControlSurface.GetPotentialTorque)),
                this));

            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(ModuleGimbal), nameof(ModuleGimbal.GetPotentialTorque)),
                this));
        }

        static ProfilerMarker rwProfiler = new ProfilerMarker("ModuleReactionWheel.GetPotentialTorque");
        static ProfilerMarker rcsProfiler = new ProfilerMarker("ModuleRCS.GetPotentialTorque");
        static ProfilerMarker ctrlSrfProfiler = new ProfilerMarker("ModuleControlSurface.GetPotentialTorque");
        static ProfilerMarker gimbalProfiler = new ProfilerMarker("ModuleGimbal.GetPotentialTorque");

        #region ModuleReactionWheel

        // Fix reaction wheels reporting incorrect available torque when the "Wheel Authority" tweakable is set below 100%.
        static bool ModuleReactionWheel_GetPotentialTorque_Prefix(ModuleReactionWheel __instance, out Vector3 pos, out Vector3 neg)
        {
            rwProfiler.Begin();
            if (__instance.moduleIsEnabled && __instance.wheelState == ModuleReactionWheel.WheelState.Active && __instance.actuatorModeCycle != 2)
            {
                float authorityLimiter = __instance.authorityLimiter * 0.01f;
                neg.x = pos.x = __instance.PitchTorque * authorityLimiter;
                neg.y = pos.y = __instance.RollTorque * authorityLimiter;
                neg.z = pos.z = __instance.YawTorque * authorityLimiter;
                rwProfiler.End();
                return false;
            }

            pos = neg = Vector3.zero;
            rwProfiler.End();
            return false;
        }

        #endregion

        #region ModuleRCS

        // The stock implementation is 100% broken, this is a complete replacement
        static bool ModuleRCS_GetPotentialTorque_Prefix(ModuleRCS __instance, out Vector3 pos, out Vector3 neg)
        {
            rcsProfiler.Begin();
            pos = Vector3.zero;
            neg = Vector3.zero;

            if (!__instance.moduleIsEnabled
                || !__instance.rcsEnabled
                || !__instance.rcs_active
                || __instance.IsAdjusterBreakingRCS()
                || __instance.isJustForShow
                || __instance.flameout
                || (__instance.part.ShieldedFromAirstream && !__instance.shieldedCanThrust)
                || (!__instance.enablePitch && !__instance.enableRoll && !__instance.enableYaw))
            {
                rcsProfiler.End();
                return false;
            }

            float power = GetMaxRCSPower(__instance);
            if (power < 0.0001f)
            {
                rcsProfiler.End();
                return false;
            }

            Vector3 currentCoM = __instance.vessel.CurrentCoM;

            Quaternion controlRotation = __instance.vessel.ReferenceTransform.rotation;
            Vector3 pitchCtrl = controlRotation * Vector3.right;
            Vector3 rollCtrl = controlRotation * Vector3.up;
            Vector3 yawCtrl = controlRotation * Vector3.forward;

            for (int i = __instance.thrusterTransforms.Count - 1; i >= 0; i--)
            {
                Transform thruster = __instance.thrusterTransforms[i];

                if (thruster.position == Vector3.zero || !thruster.gameObject.activeInHierarchy)
                    continue;

                Vector3 thrusterPosFromCoM = thruster.position - currentCoM;
                Vector3 thrusterDirFromCoM = thrusterPosFromCoM.normalized;
                Vector3 thrustDirection = __instance.useZaxis ? thruster.forward : thruster.up;

                float thrusterPower = power;
                if (FlightInputHandler.fetch.precisionMode)
                {
                    if (__instance.useLever)
                    {
                        float leverDistance = __instance.GetLeverDistance(thruster, thrustDirection, currentCoM);
                        if (leverDistance > 1f)
                        {
                            thrusterPower /= leverDistance;
                        }
                    }
                    else
                    {
                        thrusterPower *= __instance.precisionFactor;
                    }
                }

                Vector3 thrusterThrust = thrustDirection * thrusterPower;
                Vector3 thrusterTorque = Vector3.Cross(thrusterPosFromCoM, thrusterThrust);
                // transform in vessel control space
                thrusterTorque = __instance.vessel.ReferenceTransform.InverseTransformDirection(thrusterTorque);

                if (__instance.enablePitch && Math.Abs(thrusterTorque.x) > 0.0001f)
                {
                    Vector3 pitchRot = Vector3.Cross(pitchCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, pitchCtrl));
                    float actuation = Vector3.Dot(thrustDirection, pitchRot);
                    if (actuation > 0f)
                        pos.x += thrusterTorque.x * actuation;
                    else
                        neg.x += thrusterTorque.x * actuation;
                }

                if (__instance.enableRoll && Math.Abs(thrusterTorque.y) > 0.0001f)
                {
                    Vector3 rollRot = Vector3.Cross(rollCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, rollCtrl));
                    float actuation = Vector3.Dot(thrustDirection, rollRot);
                    if (actuation > 0f)
                        pos.y += thrusterTorque.y * actuation;
                    else
                        neg.y += thrusterTorque.y * actuation;
                }

                if (__instance.enableYaw && Math.Abs(thrusterTorque.z) > 0.0001f)
                {
                    Vector3 yawRot = Vector3.Cross(yawCtrl, Vector3.ProjectOnPlane(thrusterDirFromCoM, yawCtrl));
                    float actuation = Vector3.Dot(thrustDirection, yawRot);
                    if (actuation > 0f)
                        pos.z += thrusterTorque.z * actuation;
                    else
                        neg.z += thrusterTorque.z * actuation;
                }
            }

#if DEBUG
            TorqueUIModule ui = __instance.part.FindModuleImplementing<TorqueUIModule>();
            if (ui != null)
            {
                ui.pos = pos;
                ui.neg = neg;
            }
#endif
            rcsProfiler.End();
            return false;
        }

        private static float GetMaxRCSPower(ModuleRCS mrcs)
        {
            if (!mrcs.requiresFuel)
                return 1f; 

            double flowMult = mrcs.flowMult;
            if (mrcs.useThrustCurve)
                flowMult *= mrcs.thrustCurveDisplay;

            float result = (float)(flowMult * mrcs.maxFuelFlow * mrcs.exhaustVel * mrcs.thrustPercentage * 0.01);
            return result;
        }

        #endregion

        #region ModuleControlSurface

        // The stock ModuleControlSurface.GetPotentialTorque() implementation has several issues and its results are overall very wrong :
        // - It doesn't take drag forces into account (only lift).
        // - It attempt to provide actuation torque (ie, the torque difference between pos/neg actuation and the neutral state) by substracting
        //   the neutral lift force vector to the actuated pos/neg force vectors. This is wrong and produce garbage results, it's the resulting
        //   torque from the neutral vector that should be substracted to the resulting torque from the pos/neg force vectors.
        // - It entirely fails to correct the raw torque results for the pitch/roll/yaw actuation inversion and clamping logic, resulting in
        //   wrongly negative values and random inversions of the neg / pos value.
        // - For reasons that escape my understanding entirely, it multiply the torque results by the vessel CoM to Part vector, resulting in 
        //   more non-sense sign inversions and a near squaring of the result magnitude.
        // - It generally doesn't handle correctly control surfaces in the deployed state.

        // This reimplementation fixes all the above issues.
        // It still has a few shortcomings. Notably, it partially reuse results from the previous FixedUpdate() and mix them with current values.
        // This mean the results are slightly wrong, but this saves quite a bit of extra processing in that already quite performance heavy method,
        // and the error magnitude shouldn't matter for potential applications of GetPotentialTorque().
        // It shall also be noted that the result magnitude is an approximation when the actuation is clamped by the module (ie, when the control
        // surface neutral state isn't aligned with the airflow), with the error being greater when the allowed actuation is lower.

        // Note that the results can still return negative components. The meaning of a negative value is that the actuation of that component will
        // induce a torque in the opposite direction. For example, a negative pos.x value mean that for a positive roll actuation (ctrlState.roll > 0),
        // the control surface will produce a torque incuding a negative roll, essentially reducing the total available torque in that direction.

        static bool ModuleControlSurface_GetPotentialTorque_Prefix(ModuleControlSurface __instance, out Vector3 pos, out Vector3 neg)
        {
            ctrlSrfProfiler.Begin();
            pos = Vector3.zero;
            neg = Vector3.zero;

            if (__instance.Qlift == 0.0 || (__instance.ignorePitch && __instance.ignoreYaw && __instance.ignoreRoll))
            {
                ctrlSrfProfiler.End();
                return false;
            }

            if (__instance.displaceVelocity)
            {
                // This case is for handling "propeller blade" control surfaces. Those have a completely different behavior and
                // actuation scheme (and why this wasn't implemented as a separate module is beyond my understanding).
                // This is the stock GetPotentialTorque() implementation for them, I've no idea how correct it is and just don't
                // have the motivation to investigate.

                Vector3 potentialForcePos = __instance.GetPotentialLift(true);
                Vector3 potentialForceNeg = __instance.GetPotentialLift(false);
                float magnitude = __instance.vesselBladeLiftReference.magnitude;
                pos = Vector3.Dot(potentialForcePos, __instance.vesselBladeLiftReference) * __instance.potentialBladeControlTorque / magnitude;
                neg = Vector3.Dot(potentialForceNeg, __instance.vesselBladeLiftReference) * __instance.potentialBladeControlTorque / magnitude;
            }
            else
            {
                // The stock method doesn't handle correctly the deployed state :
                // - It always apply `currentDeployAngle` in the AngleAxis() call, but that field is updated only if `mcs.deploy == true`, and
                //   it isn't reverted to 0 if deploy changes from true to false, resulting in the deployed angle still being applied after un-deploying.
                // - It always substract `baseLiftForce` which is always the non-deployed lift vector, resulting in the positive deflection being twice
                //   what it should be and the negative deflection being always zero.

                float deployAngle;
                Vector3 neutralForce;
                if (!__instance.deploy)
                {
                    deployAngle = 0f;
                    neutralForce = __instance.baseLiftForce * __instance.ctrlSurfaceArea;
                    neutralForce += GetDragForce(__instance, __instance.absDot, (float) __instance.part.machNumber);
                }
                else
                {
                    deployAngle = __instance.currentDeployAngle;
                    Vector3 rhsNeutral = Quaternion.AngleAxis(deployAngle, __instance.baseTransform.rotation * Vector3.right) * __instance.baseTransform.forward;
                    float dotNeutral = Vector3.Dot(__instance.nVel, rhsNeutral);
                    float absDotNeutral = Mathf.Abs(dotNeutral);
                    float machNumber = (float) __instance.part.machNumber;
                    neutralForce = __instance.GetLiftVector(rhsNeutral, dotNeutral, absDotNeutral, __instance.Qlift, machNumber) * __instance.ctrlSurfaceArea;
                    neutralForce += GetDragForce(__instance, absDotNeutral, machNumber);
                }

                Vector3 potentialForcePos = GetPotentialLiftAndDrag(__instance, deployAngle, true);
                Vector3 potentialForceNeg = GetPotentialLiftAndDrag(__instance, deployAngle, false);

                Vector3 currentCoM = __instance.vessel.CurrentCoM;
                Vector3 partPosition = __instance.part.Rigidbody.worldCenterOfMass - currentCoM;

                Vector3 posTorque = __instance.vessel.ReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, potentialForcePos));
                Vector3 negTorque = __instance.vessel.ReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, potentialForceNeg));

                Vector3 neutralTorque = __instance.vessel.ReferenceTransform.InverseTransformDirection(Vector3.Cross(partPosition, neutralForce));

                posTorque -= neutralTorque;
                negTorque -= neutralTorque;

                // At this point, we have raw torque results for two given actuations. However, GetPotentialTorque() is supposed to
                // represent the torque produced by pitch/roll/yaw requests. We need to determine which actuation is applied for a pos=(1,1,1)
                // and neg=(-1,-1,-1) ctrlState, then swap the raw torque components accordingly. Said otherwise, to take an example,
                // we need to answer : does a positive pitch request results in a positive or negative actuation ?
                // Additionally, ModuleControlSurface will clamp actuation magnitude depending on the surface orientation, and apply an
                // additional (weird) clamping for the deployed state.

                // The following code is essentially derived from ModuleControlSurface.FixedCtrlSurfaceUpdate(), which is the method responsible
                // for updating the control surface angle according to the vessel.ctrlState pitch/roll/yaw request.

                float deployAction;
                if (__instance.deploy)
                {
                    deployAction = __instance.usesMirrorDeploy
                        ? ((__instance.deployInvert ? (-1f) : 1f) * (__instance.partDeployInvert ? (-1f) : 1f) * (__instance.mirrorDeploy ? (-1f) : 1f))
                        : ((__instance.deployInvert ? (-1f) : 1f) * Mathf.Sign((Quaternion.Inverse(__instance.vessel.ReferenceTransform.rotation) * (__instance.baseTransform.position - currentCoM)).x));

                    deployAction *= -1f;
                }
                else
                {
                    deployAction = 0f;
                }

                Vector3 comRelPos = __instance.baseTransform.InverseTransformPoint(currentCoM);

#if DEBUG
                Vector3 posAction = Vector3.zero;
                Vector3 negAction = Vector3.zero;
#endif

                if (!__instance.ignorePitch)
                {
                    Vector3 pitchVector = __instance.vessel.ReferenceTransform.rotation * new Vector3(1f, 0f, 0f);
                    float pitchActionPos = Vector3.Dot(pitchVector, __instance.baseTransform.rotation * Vector3.right);
                    if (comRelPos.y < 0f)
                        pitchActionPos = -pitchActionPos;

                    float pitchActionNeg = -pitchActionPos;

                    if (__instance.deploy)
                    {
                        pitchActionPos = Mathf.Clamp(pitchActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        pitchActionNeg = Mathf.Clamp(pitchActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    // I hope I got this right. TBH, this was mostly a trial and error job.
                    // - the control surface actuation direction depends on sign of the action
                    // - then we clamp and inverse the raw torque by the action
                    //   note that this direct scaling is a rough approximation, as the torque output vs actuation function isn't linear,
                    //   but the whole thing is computationally intensive (and complex) enough already...
                    if (pitchActionPos > 0f)
                    {
                        pos.x = negTorque.x * pitchActionPos;
                        neg.x = posTorque.x * pitchActionNeg;
                    }
                    else
                    {
                        pos.x = posTorque.x * pitchActionNeg;
                        neg.x = negTorque.x * pitchActionPos;
                    }
#if DEBUG
                    posAction.x = pitchActionPos;
                    negAction.x = pitchActionNeg;
#endif
                }

                if (!__instance.ignoreYaw)
                {
                    Vector3 yawVector = __instance.vessel.ReferenceTransform.rotation * new Vector3(0f, 0f, 1f);
                    float yawActionPos = Vector3.Dot(yawVector, __instance.baseTransform.rotation * Vector3.right);
                    if (comRelPos.y < 0f)
                        yawActionPos = -yawActionPos;

                    float yawActionNeg = -yawActionPos;

                    if (__instance.deploy)
                    {
                        yawActionPos = Mathf.Clamp(yawActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        yawActionNeg = Mathf.Clamp(yawActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    if (yawActionPos > 0f)
                    {
                        pos.z = negTorque.z * yawActionPos;
                        neg.z = posTorque.z * yawActionNeg;
                    }
                    else
                    {
                        pos.z = posTorque.z * yawActionNeg;
                        neg.z = negTorque.z * yawActionPos;
                    }
#if DEBUG
                    posAction.z = yawActionPos;
                    negAction.z = yawActionNeg;
#endif
                }

                if (!__instance.ignoreRoll)
                {
                    // optimization note : we could get rollAction by doing `rollAction = mcs.roll / ctrlStateRoll` where
                    // ctrlStateRoll is the `vessel.ctrlState.roll` value from the last fixedUpdate(). 
                    // But implementing that would be a mess, and the value would be slightly wrong due to being a frame outdated
                    // (altough this won't matter much, the overall GetPotentialTorque() implementation already rely on a bunch of
                    // one-frame outdated values for performance optimization reasons)

                    Vector3 rhs = new Vector3(comRelPos.x, 0f, comRelPos.z);

                    float rollActionPos = Vector3.Dot(Vector3.right, rhs)
                                          * (1f - (Mathf.Abs(Vector3.Dot(rhs.normalized, Quaternion.Inverse(__instance.baseTransform.rotation) * __instance.vessel.ReferenceTransform.up)) * 0.5f + 0.5f))
                                          * Mathf.Sign(Vector3.Dot(__instance.baseTransform.up, __instance.vessel.ReferenceTransform.up))
                                          * Mathf.Sign(__instance.ctrlSurfaceRange)
                                          * -1f;

                    rollActionPos = Mathf.Clamp(rollActionPos, -1f, 1f);

                    float rollActionNeg = -rollActionPos;

                    if (__instance.deploy)
                    {
                        rollActionPos = Mathf.Clamp(rollActionPos + deployAction, -1.5f, 1.5f) - deployAction;
                        rollActionNeg = Mathf.Clamp(rollActionNeg + deployAction, -1.5f, 1.5f) - deployAction;
                    }

                    if (rollActionPos > 0f)
                    {
                        pos.y = negTorque.y * rollActionPos;
                        neg.y = posTorque.y * rollActionNeg;
                    }
                    else
                    {
                        pos.y = posTorque.y * rollActionNeg;
                        neg.y = negTorque.y * rollActionPos;
                    }
#if DEBUG
                    posAction.y = rollActionPos;
                    negAction.y = rollActionNeg;
#endif
                }

#if DEBUG
                TorqueUIModule ui = __instance.part.FindModuleImplementing<TorqueUIModule>();
                if (ui != null)
                {
                    ui.pos = pos;
                    ui.neg = neg;

                    ui.Fields["spos"].guiActive = true;
                    ui.spos = posTorque;

                    ui.Fields["sneg"].guiActive = true;
                    ui.sneg = negTorque;

                    ui.Fields["posAction"].guiActive = true;
                    ui.posAction = posAction;

                    ui.Fields["negAction"].guiActive = true;
                    ui.negAction = negAction;
                }
#endif
            }

            ctrlSrfProfiler.End();
            return false;
        }

        public static Vector3 GetPotentialLiftAndDrag(ModuleControlSurface mcs, float deployAngle, bool positiveDeflection)
        {
            float deflectionDir = positiveDeflection ? 1f : -1f;
            float angle = deployAngle + (deflectionDir * mcs.ctrlSurfaceRange * mcs.authorityLimiter * 0.01f);
            Vector3 rhs = Quaternion.AngleAxis(angle, mcs.baseTransform.rotation * Vector3.right) * mcs.baseTransform.forward;
            float dot = Vector3.Dot(mcs.nVel, rhs);
            float absDot = Mathf.Abs(dot);
            float machNumber = (float) mcs.part.machNumber;
            Vector3 result = mcs.GetLiftVector(rhs, dot, absDot, mcs.Qlift, machNumber) * mcs.ctrlSurfaceArea;
            result += GetDragForce(mcs, absDot, machNumber);
            return result;
        }

        public static Vector3 GetDragForce(ModuleControlSurface mcs, float absDot, float machNumber)
        {
            if (!mcs.useInternalDragModel || (mcs.nodeEnabled && mcs.attachNode.attachedPart != null))
                return Vector3.zero;

            float dragScalar = mcs.dragCurve.Evaluate(absDot) * mcs.dragMachCurve.Evaluate(machNumber);
            dragScalar *= mcs.deflectionLiftCoeff;
            if (dragScalar != 0f && !float.IsNaN(dragScalar))
            {
                dragScalar = (float) mcs.Qdrag * dragScalar * PhysicsGlobals.LiftDragMultiplier;
                return -mcs.nVel * dragScalar * mcs.ctrlSurfaceArea;
            }

            return Vector3.zero;
        }

        #endregion

        #region ModuleGimbal

        static bool ModuleGimbal_GetPotentialTorque_Prefix(ModuleGimbal __instance, out Vector3 pos, out Vector3 neg)
        {
            gimbalProfiler.Begin();

            pos = Vector3.zero;
            neg = Vector3.zero;

            if (__instance.gimbalLock || !__instance.gimbalActive || !__instance.moduleIsEnabled || (!__instance.enablePitch && !__instance.enableRoll && !__instance.enableYaw))
            {
                gimbalProfiler.End();
                return false;
            }

            if (__instance.engineMultsList == null)
                __instance.CreateEngineList();

            Vector3 worldCoM = __instance.vessel.CurrentCoM;
            Transform vesselReferenceTransform = __instance.vessel.ReferenceTransform;

            int transformIndex = __instance.gimbalTransforms.Count;
            while (transformIndex-- > 0)
            {
                List<KeyValuePair<ModuleEngines, float>> engines = __instance.engineMultsList[transformIndex];
                int engineIndex = engines.Count;

                // bail out as early as possible to avoid useless processing when engines are inactive
                bool providesThrust = false;
                while (engineIndex-- > 0)
                    if (engines[engineIndex].Key.finalThrust > 0f)
                        providesThrust = true;

                if (!providesThrust)
                    continue;

                Transform gimbalTransform = __instance.gimbalTransforms[transformIndex];

                // this is the neutral gimbalTransform.localRotation
                Quaternion neutralLocalRot = __instance.initRots[transformIndex];
                Quaternion neutralWorldRot = gimbalTransform.parent.rotation * neutralLocalRot;
                // get the rotation between the current gimbal rotation and the neutral rotation
                Quaternion gimbalWorldRotToNeutral = neutralWorldRot * Quaternion.Inverse(gimbalTransform.rotation);

                Vector3 neutralTorque = Vector3.zero;
                Vector3 pitchPosTorque = Vector3.zero;
                Vector3 pitchNegTorque = Vector3.zero;
                Vector3 rollPosTorque = Vector3.zero;
                Vector3 rollNegTorque = Vector3.zero;
                Vector3 yawPosTorque = Vector3.zero;
                Vector3 yawNegTorque = Vector3.zero;

                Vector3 controlPoint = vesselReferenceTransform.InverseTransformPoint(gimbalTransform.position);
                Vector3 localCoM = vesselReferenceTransform.InverseTransformPoint(worldCoM);
                bool inversedControl = localCoM.y < controlPoint.y;

                Quaternion pitchPosActuation;
                Quaternion pitchNegActuation;
                if (__instance.enablePitch)
                {
                    pitchPosActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.right, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    pitchNegActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.left, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    pitchPosActuation = Quaternion.identity;
                    pitchNegActuation = Quaternion.identity;
                }

                Quaternion rollPosActuation;
                Quaternion rollNegActuation;
                if (__instance.enableRoll)
                {
                    rollPosActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.up, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    rollNegActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.down, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    rollPosActuation = Quaternion.identity;
                    rollNegActuation = Quaternion.identity;
                }

                Quaternion yawPosActuation;
                Quaternion yawNegActuation;
                if (__instance.enableYaw)
                {
                    yawPosActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.forward, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                    yawNegActuation = GetGimbalWorldRotation(__instance, gimbalTransform, Vector3.back, controlPoint, inversedControl, neutralLocalRot, neutralWorldRot);
                }
                else
                {
                    yawPosActuation = Quaternion.identity;
                    yawNegActuation = Quaternion.identity;
                }

                engineIndex = engines.Count;
                while (engineIndex-- > 0)
                {
                    KeyValuePair<ModuleEngines, float> engineThrustMultiplier = engines[engineIndex];

                    ModuleEngines engine = engineThrustMultiplier.Key;
                    float thrustMultiplier = engineThrustMultiplier.Value;
                    float thrustMagnitude = engine.finalThrust * thrustMultiplier;

                    if (thrustMagnitude <= 0f)
                        continue;

                    int thrustTransformIndex = engine.thrustTransforms.Count;
                    while (thrustTransformIndex-- > 0)
                    {
                        Transform thrustTransform = engine.thrustTransforms[thrustTransformIndex];

                        // To get the "neutral" transform position, we need to walk back the transform hierarchy to correct for the current gimbal
                        // rotation induced thrustTransform position offset. It's not critical to do it (see below as for why), but it would be weird
                        // to have the end results varying slightly depending on the current actuation.
                        // But note that when getting the actuated forces, we don't use the modified thrustTransform position. In most cases, the  
                        // actuation induced position shift of the thrustTransform won't matter much, since the gimbal pivot - thrustTransform distance
                        // is usally tiny compared to the CoM-thrustTransform distance.
                        Vector3 thrustTransformPosition = gimbalTransform.position + (gimbalWorldRotToNeutral * (thrustTransform.position - gimbalTransform.position));
                        Vector3 trustPosFromCoM = thrustTransformPosition - worldCoM;

                        // get the neutral thrust force by removing the thrustTransform current actuation induced rotation 
                        Vector3 neutralThrustForce = gimbalWorldRotToNeutral * (thrustTransform.forward * thrustMagnitude);

                        // get the "natural" torque induced by the engine thrust, in world space
                        neutralTorque += Vector3.Cross(trustPosFromCoM, neutralThrustForce);

                        if (__instance.enablePitch)
                        {
                            pitchPosTorque += Vector3.Cross(trustPosFromCoM, pitchPosActuation * neutralThrustForce);
                            pitchNegTorque += Vector3.Cross(trustPosFromCoM, pitchNegActuation * neutralThrustForce);
                        }

                        if (__instance.enableRoll)
                        {
                            rollPosTorque += Vector3.Cross(trustPosFromCoM, rollPosActuation * neutralThrustForce);
                            rollNegTorque += Vector3.Cross(trustPosFromCoM, rollNegActuation * neutralThrustForce);
                        }

                        if (__instance.enableYaw)
                        {
                            yawPosTorque += Vector3.Cross(trustPosFromCoM, yawPosActuation * neutralThrustForce);
                            yawNegTorque += Vector3.Cross(trustPosFromCoM, yawNegActuation * neutralThrustForce);
                        }
                    }
                }

                neutralTorque = vesselReferenceTransform.InverseTransformDirection(neutralTorque);

                if (__instance.enablePitch)
                {
                    pitchPosTorque = vesselReferenceTransform.InverseTransformDirection(pitchPosTorque);
                    pitchNegTorque = vesselReferenceTransform.InverseTransformDirection(pitchNegTorque);
                    pos.x += pitchPosTorque.x - neutralTorque.x;
                    neg.x -= pitchNegTorque.x - neutralTorque.x;
                }

                if (__instance.enableRoll)
                {
                    rollPosTorque = vesselReferenceTransform.InverseTransformDirection(rollPosTorque);
                    rollNegTorque = vesselReferenceTransform.InverseTransformDirection(rollNegTorque);
                    pos.y += rollPosTorque.y - neutralTorque.y;
                    neg.y -= rollNegTorque.y - neutralTorque.y;
                }

                if (__instance.enableYaw)
                {
                    yawPosTorque = vesselReferenceTransform.InverseTransformDirection(yawPosTorque);
                    yawNegTorque = vesselReferenceTransform.InverseTransformDirection(yawNegTorque);
                    pos.z += yawPosTorque.z - neutralTorque.z;
                    neg.z -= yawNegTorque.z - neutralTorque.z;
                }
            }

#if DEBUG
            TorqueUIModule ui = __instance.part.FindModuleImplementing<TorqueUIModule>();
            if (ui != null)
            {
                ui.pos = pos;
                ui.neg = neg;
            }
#endif
            gimbalProfiler.End();
            return false;
        }

        static Quaternion GetGimbalWorldRotation(ModuleGimbal mg, Transform gimbalTransform, Vector3 ctrlState, Vector3 controlPoint, bool inversedControl, Quaternion neutralLocalRot, Quaternion neutralWorldRot)
        {
            if (inversedControl)
            {
                ctrlState.x *= -1f;
                ctrlState.z *= -1f;
            }

            if (ctrlState.y != 0f && mg.enableRoll)
            {
                if (controlPoint.x > mg.minRollOffset)
                    ctrlState.x += ctrlState.y;
                else if (controlPoint.x < -mg.minRollOffset)
                    ctrlState.x -= ctrlState.y;

                if (controlPoint.z > mg.minRollOffset)
                    ctrlState.z += ctrlState.y;
                else if (controlPoint.z < -mg.minRollOffset)
                    ctrlState.z -= ctrlState.y;
            }

            // Stock does gimbalTransform.InverseTransformDirection(), resulting in the available torque varying with the current gimbal actuation...
            // To work around that, we call InverseTransformDirection() on the parent, then apply the neutral rotation.
            Vector3 localActuation = 
                Quaternion.Inverse(neutralLocalRot) 
                * gimbalTransform.parent.InverseTransformDirection(mg.vessel.ReferenceTransform.TransformDirection(ctrlState));

            // get actuation angles
            localActuation.x = Mathf.Clamp(localActuation.x, -1f, 1f) * ((localActuation.x > 0f) ? mg.gimbalRangeXP : mg.gimbalRangeXN) * mg.gimbalLimiter * 0.01f;
            localActuation.y = Mathf.Clamp(localActuation.y, -1f, 1f) * ((localActuation.y > 0f) ? mg.gimbalRangeYP : mg.gimbalRangeYN) * mg.gimbalLimiter * 0.01f;
            
            // get local rotation
            Quaternion gimbalRotation =
                neutralLocalRot
                * Quaternion.AngleAxis(localActuation.x, mg.xMult * Vector3.right) 
                * Quaternion.AngleAxis(localActuation.y, mg.yMult * (mg.flipYZ ? Vector3.forward : Vector3.up));

            // transform in world space
            gimbalRotation = (gimbalTransform.parent.rotation * gimbalRotation) * Quaternion.Inverse(neutralWorldRot);

            return gimbalRotation;
        }

        #endregion
    }

#if DEBUG
    public class TorqueUIModule : PartModule
    {
        [KSPField(guiActive = true, guiFormat = "F1")]
        public Vector3 pos;
        [KSPField(guiActive = true, guiFormat = "F1")]
        public Vector3 neg;

        // control surface debug stuff
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 spos;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 sneg;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 posAction;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 negAction;
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 actionV;

        // gimbal debug stuff
        [KSPField(guiActive = false, guiFormat = "F1")]
        public Vector3 gimbalNeutralTorque;
    }
#endif
}
