using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KSPCommunityFixes.BugFixes
{
    [KSPAddon(KSPAddon.Startup.PSystemSpawn, true)]
    public class MoveMoho : MonoBehaviour
    {
        void Awake()
        {
            PSystem psystem = PSystemManager.Instance.systemPrefab;
            foreach (PSystemBody body in psystem.GetBodies(psystem.rootBody))
            {
                if (body.celestialBody.name == "Duna")
                {
                    body.orbitDriver.orbit = new Orbit(45.0, 0.0, 1e15, 0.0, 0.0, 0.0, 0.0, body.orbitDriver.orbit.referenceBody);
                }
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SinkingBugFix : MonoBehaviour
    {
        private SphereCollider collider;

        public void Start()
        {
            gameObject.layer = 10;
            collider = gameObject.AddComponent<SphereCollider>();
        }

        private void Update()
        {
            CelestialBody mainBody = FlightGlobals.currentMainBody;
            if (mainBody == null)
            {
                collider.enabled = false;
                return;
            }
            else if (!collider.enabled)
            {
                collider.enabled = true;
            }

            Vector3 dir = mainBody.scaledBody.transform.position - FlightGlobals.Bodies[0].scaledBody.transform.position;
            transform.position = mainBody.scaledBody.transform.position + dir * 0.001f;
        }
    }

    //[KSPAddon(KSPAddon.Startup.Flight, false)]
    //public class SinkingStuff : MonoBehaviour
    //{

    //    public bool putOnOrbitingList = true;
    //    public bool enableScaledCollider = false;
    //    public bool enableCustomCollider = true;
    //    public bool moveIke = true;

    //    private GameObject farBigCollider;

    //    public void Awake()
    //    {

    //        farBigCollider = new GameObject("farBigCollider");
    //        farBigCollider.layer = 10; // Scaled Scenery
    //        SphereCollider collider2 = farBigCollider.AddComponent<SphereCollider>();
    //        //collider2.radius = 1000f;
    //        //farBigCollider.transform.position = new Vector3(-1.35443939E+11f, -2.21869135f, -9.74079017E+10f);

    //    }

    //    public void Update()
    //    {
    //        CelestialBody duna = null;
    //        CelestialBody ike = null;
    //        foreach (CelestialBody celestialBody in FlightGlobals.Bodies)
    //        {
    //            if (celestialBody.name == "Ike")
    //                ike = celestialBody;

    //            if (celestialBody.name == "Duna")
    //                duna = celestialBody;
    //        }

    //        Orbit dunaOrbit = duna.orbit;
    //        Orbit colliderOrbit;


    //        if (moveIke)
    //        {
    //            ike.orbit.SetOrbit(dunaOrbit.inclination, dunaOrbit.eccentricity, dunaOrbit.semiMajorAxis * 1.001, dunaOrbit.LAN, dunaOrbit.argumentOfPeriapsis, dunaOrbit.meanAnomalyAtEpoch, dunaOrbit.epoch, dunaOrbit.referenceBody);
    //            //colliderOrbit = ike.orbit;
    //        }

    //        colliderOrbit = new Orbit(dunaOrbit.inclination, dunaOrbit.eccentricity, dunaOrbit.semiMajorAxis * 1.001, dunaOrbit.LAN, dunaOrbit.argumentOfPeriapsis, dunaOrbit.meanAnomalyAtEpoch, dunaOrbit.epoch, dunaOrbit.referenceBody);
    //        colliderOrbit.UpdateFromUT(Planetarium.GetUniversalTime());
    //        Vector3d pos = colliderOrbit.pos;
    //        pos.Swizzle();
    //        pos = colliderOrbit.referenceBody.position + pos;
    //        pos = ScaledSpace.LocalToScaledSpace(pos);


    //        if (putOnOrbitingList && !duna.orbitingBodies.Contains(ike))
    //        {
    //            duna.orbitingBodies.Add(ike);
    //            //pos = ike.scaledBody.transform.position;
    //        }
    //        else
    //        {
    //            duna.orbitingBodies.Remove(ike);

    //        }

    //        if (moveIke)
    //        {
    //            ike.enabled = false;

    //            //foreach (Renderer renderer in ike.scaledBody.GetComponentsInChildren<Renderer>())
    //            //    renderer.enabled = false;

    //            foreach (ScaledSpaceFader fader in ike.scaledBody.GetComponentsInChildren<ScaledSpaceFader>())
    //                fader.enabled = false;

    //            foreach (Collider collider in ike.scaledBody.GetComponentsInChildren<Collider>())
    //                collider.enabled = enableScaledCollider;
    //        }

    //        farBigCollider.gameObject.SetActive(enableCustomCollider);

    //        if (enableCustomCollider)
    //            farBigCollider.transform.position = pos;
    //    }

    //}
}
