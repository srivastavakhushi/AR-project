using NUnit.Framework;
using UnityEngine;
using CrimeSceneVR.Core;

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
            _rigRoot = new GameObject("Test_OVRCameraRig");
            var trackingSpace = new GameObject("TrackingSpace");
            trackingSpace.transform.SetParent(_rigRoot.transform, false);

            var centerEye = new GameObject("CenterEyeAnchor");
            centerEye.transform.SetParent(trackingSpace.transform, false);
            centerEye.tag = "MainCamera";
            var cam = centerEye.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;

            var leftHand = new GameObject("LeftHandAnchor");
            leftHand.transform.SetParent(trackingSpace.transform, false);

            var rightHand = new GameObject("RightHandAnchor");
            rightHand.transform.SetParent(trackingSpace.transform, false);

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            rigRef.ResolveReferences();

            Assert.IsNotNull(VRRigReferences.Instance, "VRRigReferences singleton instance must be set.");
            Assert.AreEqual(_rigRoot.transform, rigRef.Root);
            Assert.AreEqual(trackingSpace.transform, rigRef.TrackingSpace);
            Assert.AreEqual(centerEye.transform, rigRef.CenterEyeAnchor);
            Assert.AreEqual(leftHand.transform, rigRef.LeftHandAnchor);
            Assert.AreEqual(rightHand.transform, rigRef.RightHandAnchor);
            Assert.AreEqual(cam, rigRef.MainCamera);
            Assert.AreEqual(0.05f, rigRef.MainCamera.nearClipPlane, 0.001f);
        }

        [Test]
        public void VRRigReferences_NearClipPlane_EnforcedAtFiveCentimeters()
        {
            _rigRoot = new GameObject("Test_OVRCameraRig_NearClip");
            var trackingSpace = new GameObject("TrackingSpace");
            trackingSpace.transform.SetParent(_rigRoot.transform, false);

            var centerEye = new GameObject("CenterEyeAnchor");
            centerEye.transform.SetParent(trackingSpace.transform, false);
            var cam = centerEye.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f; // Default camera near clip

            var rigRef = _rigRoot.AddComponent<VRRigReferences>();
            rigRef.ResolveReferences();

            Assert.AreEqual(0.05f, rigRef.MainCamera.nearClipPlane, 0.001f, "ResolveReferences must enforce nearClipPlane to 0.05m.");
        }
    }
}
