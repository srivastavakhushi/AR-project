using NUnit.Framework;
using UnityEngine;
using CrimeSceneVR.Core;
using CrimeSceneVR.VR;

namespace CrimeSceneVR.Tests
{
    [TestFixture]
    public class VRRigTests
    {
        private GameObject _rigRoot;

        [TearDown]
        public void TearDown()
        {
            if (_rigRoot != null)
            {
                Object.DestroyImmediate(_rigRoot);
            }
        }

        [Test]
        public void VRRigReferences_ResolvesAnchorsAndCamera_Correctly()
        {
            _rigRoot = CreateTestRigHierarchy(out Transform trackingSpace, out Transform centerEye, out Transform leftHand, out Transform rightHand, out Camera cam);

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            rigRef.ResolveReferences();

            Assert.IsNotNull(VRRigReferences.Instance, "VRRigReferences singleton instance must be set.");
            Assert.AreEqual(_rigRoot.transform, rigRef.Root);
            Assert.AreEqual(trackingSpace, rigRef.TrackingSpace);
            Assert.AreEqual(centerEye, rigRef.CenterEyeAnchor);
            Assert.AreEqual(leftHand, rigRef.LeftHandAnchor);
            Assert.AreEqual(rightHand, rigRef.RightHandAnchor);
            Assert.AreEqual(cam, rigRef.MainCamera);
            Assert.AreEqual(0.05f, rigRef.MainCamera.nearClipPlane, 0.001f);
        }

        [Test]
        public void VRRigReferences_NearClipPlane_EnforcedAtFiveCentimeters()
        {
            _rigRoot = CreateTestRigHierarchy(out _, out _, out _, out _, out Camera cam);
            cam.nearClipPlane = 0.3f;

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            rigRef.ResolveReferences();

            Assert.AreEqual(0.05f, rigRef.MainCamera.nearClipPlane, 0.001f, "ResolveReferences must enforce nearClipPlane to 0.05m.");
        }

        [Test]
        public void VRLocomotionController_SimulateMove_TranslatesRigForward()
        {
            _rigRoot = CreateTestRigHierarchy(out _, out Transform centerEye, out _, out _, out _);
            centerEye.rotation = Quaternion.identity;

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            var loco = _rigRoot.AddComponent<VRLocomotionController>();
            loco.Configure(rigRef);
            loco.MoveSpeed = 2.0f;

            Vector3 initialPos = _rigRoot.transform.position;
            loco.SimulateMove(new Vector2(0f, 1f), 0.5f); // Move forward for 0.5s at 2 m/s = 1.0m

            Assert.AreEqual(initialPos.z + 1.0f, _rigRoot.transform.position.z, 0.01f, "Rig must translate forward along head direction.");
        }

        [Test]
        public void VRLocomotionController_ProcessTurning_SnapTurnsOnceUntilReset()
        {
            _rigRoot = CreateTestRigHierarchy(out _, out _, out _, out _, out _);
            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            var loco = _rigRoot.AddComponent<VRLocomotionController>();
            loco.Configure(rigRef);
            loco.UseSnapTurn = true;
            loco.SnapTurnAngle = 45f;

            // Flick right stick to +1.0
            bool firstTurn = loco.ProcessTurning(new Vector2(1.0f, 0f), 0.1f);
            Assert.IsTrue(firstTurn, "First flick above threshold must trigger snap turn.");
            Assert.AreEqual(45f, _rigRoot.transform.eulerAngles.y, 0.1f);

            // Holding right stick at +1.0 must not re-trigger until released below reset threshold
            bool heldTurn = loco.ProcessTurning(new Vector2(1.0f, 0f), 0.1f);
            Assert.IsFalse(heldTurn, "Holding thumbstick must not repeat snap turn until reset.");

            // Release stick to 0, then flick again
            loco.ProcessTurning(Vector2.zero, 0.1f);
            bool secondTurn = loco.ProcessTurning(new Vector2(1.0f, 0f), 0.1f);
            Assert.IsTrue(secondTurn, "Snap turn must trigger again after stick resets to neutral.");
            Assert.AreEqual(90f, _rigRoot.transform.eulerAngles.y, 0.1f);
        }

        [Test]
        public void VRLocomotionController_TeleportTo_AlignsHeadWithTargetPoint()
        {
            _rigRoot = CreateTestRigHierarchy(out _, out Transform centerEye, out _, out _, out _);
            // Offset head 0.2m on X inside tracking space
            centerEye.localPosition = new Vector3(0.2f, 1.6f, -0.1f);

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            var loco = _rigRoot.AddComponent<VRLocomotionController>();
            loco.Configure(rigRef);

            Vector3 targetFloorPoint = new Vector3(3.0f, 0.0f, 4.0f);
            bool teleported = loco.TeleportTo(targetFloorPoint);

            Assert.IsTrue(teleported);
            Assert.AreEqual(targetFloorPoint.x, centerEye.position.x, 0.001f, "Head world X must match teleport target X.");
            Assert.AreEqual(targetFloorPoint.z, centerEye.position.z, 0.001f, "Head world Z must match teleport target Z.");
            Assert.AreEqual(targetFloorPoint.y, _rigRoot.transform.position.y, 0.001f, "Rig root Y must match floor target Y.");
        }

        private static GameObject CreateTestRigHierarchy(
            out Transform trackingSpace,
            out Transform centerEye,
            out Transform leftHand,
            out Transform rightHand,
            out Camera cam)
        {
            var rigRoot = new GameObject("Test_OVRCameraRig");
            var tsGo = new GameObject("TrackingSpace");
            tsGo.transform.SetParent(rigRoot.transform, false);
            trackingSpace = tsGo.transform;

            var ceGo = new GameObject("CenterEyeAnchor");
            ceGo.transform.SetParent(trackingSpace, false);
            ceGo.tag = "MainCamera";
            cam = ceGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            centerEye = ceGo.transform;

            var lhGo = new GameObject("LeftHandAnchor");
            lhGo.transform.SetParent(trackingSpace, false);
            leftHand = lhGo.transform;

            var rhGo = new GameObject("RightHandAnchor");
            rhGo.transform.SetParent(trackingSpace, false);
            rightHand = rhGo.transform;

            return rigRoot;
        }
    }
}
