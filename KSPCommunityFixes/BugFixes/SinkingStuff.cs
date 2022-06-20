using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using VehiclePhysics;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace KSPCommunityFixes.BugFixes
{
    class PQMeshFix : BasePatch
    {
        protected override void ApplyPatches(ref List<PatchInfo> patches)
        {
            patches.Add(new PatchInfo(
                PatchMethodType.Prefix,
                AccessTools.Method(typeof(PQSMod_QuadMeshColliders), nameof(PQSMod_QuadMeshColliders.OnQuadBuilt)),
                this));
        }

        private const MeshColliderCookingOptions PQ_COOKING_OPTIONS =
            MeshColliderCookingOptions.None;
            //MeshColliderCookingOptions.EnableMeshCleaning
            //| MeshColliderCookingOptions.CookForFasterSimulation
            //| MeshColliderCookingOptions.WeldColocatedVertices;

        static bool PQSMod_QuadMeshColliders_OnQuadBuilt_Prefix(PQSMod_QuadMeshColliders __instance, PQ quad)
        {
            if (quad.subdivision >= __instance.minLevel)
            {
                if (quad.meshCollider == null)
                {
                    quad.meshCollider = quad.gameObject.AddComponent<MeshCollider>();
                }

                Mesh testMesh = new Mesh();
                testMesh.vertices = quad.mesh.vertices;
                testMesh.triangles = quad.mesh.triangles;
                testMesh.RecalculateNormals();
                testMesh.RecalculateBounds();

                quad.meshCollider.cookingOptions = PQ_COOKING_OPTIONS;
                quad.meshCollider.sharedMesh = testMesh;
                quad.meshCollider.sharedMaterial = __instance.physicsMaterial;
                quad.meshCollider.enabled = true;
            }

            return false;
        }

    }


    [KSPAddon(KSPAddon.Startup.PSystemSpawn, true)]
    public class MoveDuna : MonoBehaviour
    {
        void Awake()
        {
            PSystem psystem = PSystemManager.Instance.systemPrefab;
            foreach (PSystemBody body in psystem.GetBodies(psystem.rootBody))
            {
                if (body.celestialBody.name == "Duna")
                {
                    body.orbitDriver.orbit = new Orbit(0.0, 0.0, 1e15, 0.0, 0.0, 0.0, 0.0, body.orbitDriver.orbit.referenceBody);
                }
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MoveSomeColliderFarAway : MonoBehaviour
    {
        private GameObject obj;
        private void Start() => obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);

        private void FixedUpdate()
        {
            obj.transform.position = new Vector3(Random.Range(1e12f, 1e15f), Random.Range(1e12f, 1e15f), Random.Range(1e12f, 1e15f));
        }
    }


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RaycastFailingFix : MonoBehaviour
    {
        private class BodyLocalSpace
        {
            public CelestialBody celestialBody;
            public GameObject gameObject;
            public HashSet<Collider> disabledColliders = new HashSet<Collider>();

            public BodyLocalSpace(CelestialBody celestialBody)
            {
                this.celestialBody = celestialBody;
                gameObject = celestialBody.pqsController.gameObject;
            }

            public void CheckColliders()
            {
                if (gameObject.transform.position.sqrMagnitude > maxDistanceSqr)
                {
                    gameObject.GetComponentsInChildren(true, colliderBuffer);
                    for (int i = colliderBuffer.Count; i-- > 0;)
                    {
                        Collider collider = colliderBuffer[i];
                        if (collider.enabled)
                        {
                            collider.enabled = false;
                            disabledColliders.Add(collider);
                        }
                    }
                    colliderBuffer.Clear();
                }
                else if (disabledColliders.Count > 0)
                {
                    foreach (Collider collider in disabledColliders)
                        if (collider != null)
                            collider.enabled = true;

                    disabledColliders.Clear();
                }
            }
        }

        /// <summary>
        /// The max distance from the origin where colliders are allowed to be enabled. Default to 1e10.
        /// </summary>
        public static float MaxColliderDistance
        {
            get => Mathf.Sqrt(maxDistanceSqr);
            set => maxDistanceSqr = value * value;
        }

        private static float maxDistanceSqr = 1e11f * 1e11f;

        private static List<BodyLocalSpace> pqsControllers;
        private static List<SphereCollider> scaledBodyColliders;

        private static List<Collider> colliderBuffer = new List<Collider>();
        private int nextPQSController;

        private void Awake()
        {
            if (pqsControllers == null)
            {
                pqsControllers = new List<BodyLocalSpace>(FlightGlobals.Bodies.Count);
                scaledBodyColliders = new List<SphereCollider>(FlightGlobals.Bodies.Count);

                foreach (CelestialBody cb in FlightGlobals.Bodies)
                {
                    if (cb.pqsController != null)
                        pqsControllers.Add(new BodyLocalSpace(cb));

                    SphereCollider collider = cb.scaledBody.GetComponent<SphereCollider>();
                    if (collider != null)
                        scaledBodyColliders.Add(collider);
                }
            }
        }

        private void Start()
        {
            CheckScaledSpaceColliders();

            foreach (BodyLocalSpace pqsController in pqsControllers)
                if (pqsController.celestialBody != FlightGlobals.currentMainBody)
                    pqsController.CheckColliders();
        }

        private void FixedUpdate()
        {
            CheckScaledSpaceColliders();

            BodyLocalSpace pqsController = pqsControllers[nextPQSController];
            if (pqsController.celestialBody != FlightGlobals.currentMainBody)
                pqsController.CheckColliders();

            nextPQSController = (nextPQSController + 1) % pqsControllers.Count;
        }

        private void CheckScaledSpaceColliders()
        {
            foreach (SphereCollider scaledBodyCollider in scaledBodyColliders)
            {
                if (scaledBodyCollider.transform.position.sqrMagnitude > maxDistanceSqr)
                {
                    if (scaledBodyCollider.enabled)
                        scaledBodyCollider.enabled = false;
                }
                else if (!scaledBodyCollider.enabled)
                {
                    scaledBodyCollider.enabled = true;
                }
            }
        }

        private void OnDisable()
        {
            ReenableAll();
        }

        private void OnDestroy()
        {
            ReenableAll();
        }

        private void ReenableAll()
        {
            foreach (BodyLocalSpace pqsController in pqsControllers)
            {
                foreach (Collider collider in pqsController.disabledColliders)
                    if (collider != null)
                        collider.enabled = true;

                pqsController.disabledColliders.Clear();
            }

            foreach (SphereCollider scaledBodyCollider in scaledBodyColliders)
            {
                if (!scaledBodyCollider.enabled)
                    scaledBodyCollider.enabled = true;
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SinkingBugFix : MonoBehaviour
    {
        internal static Dictionary<int, bool>[] colliderStatus = new Dictionary<int, bool>[FlightGlobals.Bodies.Count];
        internal uint counter = 0;

        private void Start()
        {
            for (Int32 i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                colliderStatus[i] = new Dictionary<int, bool>();
            }
        }

        private void FixedUpdate()
        {
            counter++;
            if (counter > 20)
            {
                counter = 0;
                CelestialBody mainBody = FlightGlobals.currentMainBody;
                for (Int32 i = 0; i < FlightGlobals.Bodies.Count; i++)
                {
                    CelestialBody cb = FlightGlobals.Bodies[i];
                    if (Vector3.Distance(FlightGlobals.currentMainBody.transform.position, cb.transform.position) < 100000000000)
                    {
                        RestoreColliderState(cb, i);
                    }
                    else if (Vector3.Distance(FlightGlobals.currentMainBody.transform.position, cb.transform.position) > 100000000000)
                    {
                        HibernateColliderState(cb, i);
                    }
                }
            }
        }
        private void RestoreColliderState(CelestialBody cb, int index)
        {
            foreach (Collider collider in cb.GetComponentsInChildren<Collider>(true))
            {
                if (colliderStatus[index].ContainsKey(collider.gameObject.GetInstanceID()))
                {
                    collider.enabled = colliderStatus[index][collider.gameObject.GetInstanceID()];
                    colliderStatus[index].Remove(collider.gameObject.GetInstanceID());
                }
            }
        }
        private void HibernateColliderState(CelestialBody cb, int index)
        {
            foreach (Collider collider in cb.GetComponentsInChildren<Collider>(true))
            {
                if (!colliderStatus[index].ContainsKey(collider.gameObject.GetInstanceID()))
                {
                    colliderStatus[index].Add(collider.gameObject.GetInstanceID(), collider.enabled);
                }
                collider.enabled = false;
            }
        }
    }


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SinkingBugPQSFix : MonoBehaviour
    {
        private void Update()
        {
            if (FlightGlobals.ActiveVessel == null || FlightGlobals.currentMainBody == null)
                return;

            Vector3 vesselPos = FlightGlobals.ActiveVessel.transform.position;
            Vector3 down = (FlightGlobals.currentMainBody.position - vesselPos).normalized;
            Vector3 up = -down;

            float du = Vector3.Dot(up, Vector3.up);
            float df = Vector3.Dot(up, Vector3.forward);
            Vector3 v1 = Mathf.Abs(du) < Mathf.Abs(df) ? Vector3.up : Vector3.forward;
            Vector3 forward = Vector3.Cross(v1, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);

            Vector3 upOrigin = up * 100f;

            LayerMask mask = LayerMask.GetMask("Local Scenery");

            for (int i = -10; i < 11; i++)
            {
                for (int j = -10; j < 11; j++)
                {
                    Vector3 origin = vesselPos + upOrigin + (forward * i * 2.5f) + (right * j * 2.5f);
                    if (Physics.Raycast(origin, down, out RaycastHit hitInfo, 200f, mask))
                    {
                        DebugDrawer.DebugPoint(hitInfo.point, Color.green);
                    }
                }
            }
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

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    class DebugDrawer : MonoBehaviour
    {
        private static readonly List<Line> lines = new List<Line>();
        private static readonly List<Point> points = new List<Point>();
        private static readonly List<Trans> transforms = new List<Trans>();
        public Material lineMaterial;

        private struct Line
        {
            public readonly Vector3 start;
            public readonly Vector3 end;
            public readonly Color color;

            public Line(Vector3 start, Vector3 end, Color color)
            {
                this.start = start;
                this.end = end;
                this.color = color;
            }
        }

        private struct Point
        {
            public readonly Vector3 pos;
            public readonly Color color;

            public Point(Vector3 pos, Color color)
            {
                this.pos = pos;
                this.color = color;
            }
        }

        private struct Trans
        {
            public readonly Vector3 pos;
            public readonly Vector3 up;
            public readonly Vector3 right;
            public readonly Vector3 forward;

            public Trans(Vector3 pos, Vector3 up, Vector3 right, Vector3 forward)
            {
                this.pos = pos;
                this.up = up;
                this.right = right;
                this.forward = forward;
            }
        }

        public static void DebugLine(Vector3 start, Vector3 end, Color col)
        {
            lines.Add(new Line(start, end, col));
        }

        public static void DebugPoint(Vector3 start, Color col)
        {
            points.Add(new Point(start, col));
        }

        public static void DebugTransforms(Transform t)
        {
            transforms.Add(new Trans(t.position, t.up, t.right, t.forward));
        }

        private void Start()
        {
            DontDestroyOnLoad(this);
            if (!lineMaterial)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                lineMaterial = new Material(shader);
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
                lineMaterial.SetInt("_ZWrite", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            StartCoroutine("EndOfFrameDrawing");
        }

        private IEnumerator EndOfFrameDrawing()
        {
            Debug.Log("DebugDrawer starting");
            while (true)
            {
                yield return new WaitForEndOfFrame();

                Camera cam = GetActiveCam();

                if (cam == null) continue;

                try
                {
                    transform.position = Vector3.zero;

                    GL.PushMatrix();
                    lineMaterial.SetPass(0);

                    // In a modern Unity we would use cam.projectionMatrix.decomposeProjection to get the decomposed matrix
                    // and Matrix4x4.Frustum(FrustumPlanes frustumPlanes) to get a new one

                    // Change the far clip plane of the projection matrix
                    Matrix4x4 projectionMatrix = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, float.MaxValue);
                    GL.LoadProjectionMatrix(projectionMatrix);
                    GL.MultMatrix(cam.worldToCameraMatrix);
                    //GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));

                    GL.Begin(GL.LINES);

                    for (int i = 0; i < lines.Count; i++)
                    {
                        Line line = lines[i];
                        DrawLine(line.start, line.end, line.color);
                    }

                    for (int i = 0; i < points.Count; i++)
                    {
                        Point point = points[i];
                        DrawPoint(point.pos, point.color);
                    }

                    for (int i = 0; i < transforms.Count; i++)
                    {
                        Trans t = transforms[i];
                        DrawTransform(t.pos, t.up, t.right, t.forward);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("EndOfFrameDrawing Exception" + e);
                }
                finally
                {
                    GL.End();
                    GL.PopMatrix();

                    lines.Clear();
                    points.Clear();
                    transforms.Clear();
                }
            }
        }

        private static Camera GetActiveCam()
        {
            if (!HighLogic.fetch)
                return Camera.main;

            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch)
                return EditorLogic.fetch.editorCamera;

            if (HighLogic.LoadedSceneIsFlight && PlanetariumCamera.fetch && FlightCamera.fetch)
                return MapView.MapIsEnabled ? PlanetariumCamera.Camera : FlightCamera.fetch.mainCamera;

            return Camera.main;
        }

        private static void DrawLine(Vector3 origin, Vector3 destination, Color color)
        {
            GL.Color(color);
            GL.Vertex(origin);
            GL.Vertex(destination);
        }

        private static void DrawRay(Vector3 origin, Vector3 direction, Color color)
        {
            GL.Color(color);
            GL.Vertex(origin);
            GL.Vertex(origin + direction);
        }

        private static void DrawTransform(Vector3 position, Vector3 up, Vector3 right, Vector3 forward, float scale = 1.0f)
        {
            DrawRay(position, up * scale, Color.green);
            DrawRay(position, right * scale, Color.red);
            DrawRay(position, forward * scale, Color.blue);
        }

        private static void DrawPoint(Vector3 position, Color color, float scale = 1.0f)
        {
            DrawRay(position + Vector3.up * (scale * 0.5f), -Vector3.up * scale, color);
            DrawRay(position + Vector3.right * (scale * 0.5f), -Vector3.right * scale, color);
            DrawRay(position + Vector3.forward * (scale * 0.5f), -Vector3.forward * scale, color);
        }
    }
}
