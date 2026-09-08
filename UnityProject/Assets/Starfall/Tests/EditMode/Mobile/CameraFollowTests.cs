using System.Reflection;
using NUnit.Framework;
using Starfall.Presentation;
using UnityEngine;

namespace Starfall.Tests.EditMode
{
    public sealed class CameraFollowTests
    {
        private GameObject cameraObject;
        private GameObject target;
        private EveCameraController controller;
        private static readonly MethodInfo Pose = typeof(EveCameraController).GetMethod(
            "UpdatePose", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp] public void SetUp()
        {
            cameraObject = new GameObject("CameraFollowTest");
            controller = cameraObject.AddComponent<EveCameraController>();
            typeof(EveCameraController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            target = new GameObject("FollowTarget");
            controller.SetPlayerTarget(target.transform);
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(target);
        }

        private void Tick(float dt) => Pose.Invoke(controller, new object[] { dt });
        private void AssertCentered()
        {
            var projected = controller.Camera.WorldToViewportPoint(target.transform.position);
            Assert.That(projected.x, Is.EqualTo(.5f).Within(.0002f));
            Assert.That(projected.y, Is.EqualTo(.5f).Within(.0002f));
            Assert.That(projected.z, Is.GreaterThan(6f), "Target must never cross the camera near plane");
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void TwentyHertzWarpDoesNotShakeOrFlipAtRenderCadence(int fps)
        {
            Tick(1f / fps);
            var rotation = cameraObject.transform.rotation;
            var accumulated = 0f;
            for (var frame = 0; frame < fps * 8; frame++)
            {
                accumulated += 1f / fps;
                while (accumulated >= .05f)
                {
                    target.transform.position += new Vector3(35f, 0f, 12f);
                    accumulated -= .05f;
                }
                Tick(1f / fps);
                AssertCentered();
                Assert.That(Quaternion.Angle(rotation, cameraObject.transform.rotation), Is.LessThan(.01f));
            }
        }

        [Test] public void TeleportOrbitAndZoomKeepTheTargetInFront()
        {
            Tick(1f / 60f);
            target.transform.position = new Vector3(-5000, 0, 3000);
            controller.ApplyOrbit(new Vector2(220, -60));
            controller.ApplyZoom(2f);
            for (var i = 0; i < 120; i++) { Tick(1f / 60f); AssertCentered(); }
            Assert.That(Vector3.Distance(cameraObject.transform.position, target.transform.position),
                Is.EqualTo(16f).Within(.01f));
        }
    }
}
