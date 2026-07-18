using System.Reflection;
using NUnit.Framework;
using Starfall.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starfall.Tests.EditMode.Mobile
{
    public sealed class WorldGestureRecognizerTests
    {
        private WorldGestureRecognizer recognizer;

        [SetUp]
        public void SetUp()
        {
            recognizer = new WorldGestureRecognizer();
            recognizer.ConfigureDpi(160f);
        }

        [Test]
        public void TapAndDoubleTapUseTimeAndDpDistance()
        {
            recognizer.Begin(1, new Vector2(100f, 100f), 0d, false);
            var first = recognizer.End(1, new Vector2(101f, 100f), 0.05d);
            Assert.That(first?.Type, Is.EqualTo(WorldGestureType.Tap));

            recognizer.Begin(2, new Vector2(120f, 100f), 0.2d, false);
            var second = recognizer.End(2, new Vector2(120f, 100f), 0.25d);
            Assert.That(second?.Type, Is.EqualTo(WorldGestureType.DoubleTap));
        }

        [Test]
        public void DragThresholdScalesWithDensityAndSuppressesTap()
        {
            recognizer.ConfigureDpi(420f);
            recognizer.Begin(7, Vector2.zero, 0d, false);
            Assert.That(recognizer.Move(7, new Vector2(31f, 0f), 0.1d), Is.Null);
            var drag = recognizer.Move(7, new Vector2(32f, 0f), 0.2d);
            Assert.That(drag?.Type, Is.EqualTo(WorldGestureType.Drag));
            Assert.That(recognizer.End(7, new Vector2(32f, 0f), 0.3d), Is.Null);
        }

        [Test]
        public void LongPressFiresOnceAndSuppressesTap()
        {
            recognizer.Begin(3, new Vector2(5f, 6f), 1d, false);
            Assert.That(recognizer.Tick(1.49d), Is.Null);
            var hold = recognizer.Tick(1.5d);
            Assert.That(hold?.Type, Is.EqualTo(WorldGestureType.LongPress));
            Assert.That(recognizer.Tick(2d), Is.Null);
            Assert.That(recognizer.End(3, new Vector2(5f, 6f), 2.1d), Is.Null);
        }

        [Test]
        public void PinchReportsScaleAndSuppressesBothTaps()
        {
            recognizer.Begin(1, Vector2.zero, 0d, false);
            recognizer.Begin(2, new Vector2(100f, 0f), 0d, false);
            var pinch = recognizer.Move(2, new Vector2(125f, 0f), 0.1d);
            Assert.That(pinch?.Type, Is.EqualTo(WorldGestureType.Pinch));
            Assert.That(pinch?.Scale, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(recognizer.End(2, new Vector2(125f, 0f), 0.2d), Is.Null);
            Assert.That(recognizer.End(1, Vector2.zero, 0.3d), Is.Null);
        }

        [Test]
        public void UiBlockedPointerNeverLeaksIntoWorld()
        {
            recognizer.Begin(11, new Vector2(20f, 20f), 0d, true);
            Assert.That(recognizer.Move(11, new Vector2(200f, 200f), 0.2d), Is.Null);
            Assert.That(recognizer.Tick(1d), Is.Null);
            Assert.That(recognizer.End(11, new Vector2(200f, 200f), 1.1d), Is.Null);
        }

        [Test]
        public void UiInteractionBreaksWorldDoubleTapSequence()
        {
            var position = new Vector2(40f, 60f);
            recognizer.Begin(1, position, 0d, false);
            Assert.That(recognizer.End(1, position, 0.05d)?.Type, Is.EqualTo(WorldGestureType.Tap));

            recognizer.Begin(2, position, 0.1d, true);
            Assert.That(recognizer.End(2, position, 0.12d), Is.Null);

            recognizer.Begin(3, position, 0.18d, false);
            Assert.That(recognizer.End(3, position, 0.22d)?.Type, Is.EqualTo(WorldGestureType.Tap),
                "A UI interaction between world taps must clear the pending double-tap candidate.");
        }

        [Test]
        public void CanceledWorldTouchBreaksDoubleTapSequence()
        {
            var position = new Vector2(25f, 35f);
            recognizer.Begin(1, position, 0d, false);
            Assert.That(recognizer.End(1, position, 0.04d)?.Type, Is.EqualTo(WorldGestureType.Tap));

            recognizer.Begin(2, position, 0.1d, false);
            recognizer.Cancel(2);

            recognizer.Begin(3, position, 0.16d, false);
            Assert.That(recognizer.End(3, position, 0.2d)?.Type, Is.EqualTo(WorldGestureType.Tap),
                "A canceled touch stream must not leave a stale world double-tap candidate.");
        }

        [Test]
        public void DragStartingOnSelectableDoesNotOrbitOrTap()
        {
            recognizer.Begin(12, Vector2.zero, 0d, false, false);
            Assert.That(recognizer.Move(12, new Vector2(20f, 0f), 0.2d), Is.Null);
            Assert.That(recognizer.End(12, new Vector2(20f, 0f), 0.3d), Is.Null);
        }

        [Test]
        public void MissingDpiUsesDocumentedFallback()
        {
            recognizer.ConfigureDpi(0f);
            Assert.That(recognizer.PixelsPerDp, Is.EqualTo(420f / 160f).Within(0.001f));
        }
    }

    public sealed class SpaceWorldPresenterInputSystemTests : InputTestFixture
    {
        private static readonly MethodInfo UpdateTouchInputMethod = typeof(SpaceWorldPresenter).GetMethod(
            "UpdateTouchInput", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo PresenterAwakeMethod = typeof(SpaceWorldPresenter).GetMethod(
            "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CameraAwakeMethod = typeof(EveCameraController).GetMethod(
            "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CameraDistanceField = typeof(EveCameraController).GetField(
            "distance", BindingFlags.Instance | BindingFlags.NonPublic);

        private Touchscreen touchscreen;
        private GameObject presenterObject;
        private SpaceWorldPresenter presenter;

        public override void Setup()
        {
            base.Setup();
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            presenterObject = new GameObject("InputSystem Space Presenter Test");
            presenter = presenterObject.AddComponent<SpaceWorldPresenter>();
            Assert.That(UpdateTouchInputMethod, Is.Not.Null);
            Assert.That(PresenterAwakeMethod, Is.Not.Null);
            Assert.That(CameraAwakeMethod, Is.Not.Null);
            Assert.That(CameraDistanceField, Is.Not.Null);
            PresenterAwakeMethod.Invoke(presenter, null);
            var cameraController = presenterObject.GetComponentInChildren<EveCameraController>(true);
            Assert.That(cameraController, Is.Not.Null);
            CameraAwakeMethod.Invoke(cameraController, null);
            Assert.That(presenter.MainCamera, Is.Not.Null);
        }

        public override void TearDown()
        {
            WorldPointerBlocker.Current = null;
            if (presenterObject != null) Object.DestroyImmediate(presenterObject);
            presenter = null;
            presenterObject = null;
            touchscreen = null;
            base.TearDown();
        }

        [Test]
        public void TwoInputSystemTouchesDrivePresenterPinchZoom()
        {
            var center = ScreenCenter();
            var cameraController = presenter.MainCamera.GetComponent<EveCameraController>();
            var before = (float)CameraDistanceField.GetValue(cameraController);

            BeginTouch(1, center + Vector2.left * 100f,
                queueEventOnly: true, screen: touchscreen, time: 1d);
            BeginTouch(2, center + Vector2.right * 100f,
                queueEventOnly: true, screen: touchscreen, time: 1d);
            InputSystem.Update();
            InvokeTouchUpdate();

            MoveTouch(1, center + Vector2.left * 150f, Vector2.left * 50f,
                queueEventOnly: true, screen: touchscreen, time: 1.1d);
            MoveTouch(2, center + Vector2.right * 150f, Vector2.right * 50f,
                queueEventOnly: true, screen: touchscreen, time: 1.1d);
            InputSystem.Update();
            InvokeTouchUpdate();

            var after = (float)CameraDistanceField.GetValue(cameraController);
            Assert.That(after, Is.LessThan(before),
                "Two Touchscreen controls moving apart must zoom the presenter camera in.");
        }

        [Test]
        public void CanceledInputSystemTouchDoesNotDispatchTap()
        {
            var center = ScreenCenter();
            var target = new WorldObjectViewData
            {
                Id = "cancel-target",
                Name = "Cancel Target",
                Kind = WorldViewKind.Beacon,
                Position = Vector3.forward * 12f,
                Radius = 2f,
            };
            var snapshot = new SpaceSnapshot();
            snapshot.Objects.Add(target);
            presenter.Present(snapshot);
            presenter.MainCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();

            string selectedId = null;
            presenter.SelectionChanged += value => selectedId = value;

            BeginTouch(7, center, queueEventOnly: true, screen: touchscreen, time: 2d);
            InputSystem.Update();
            InvokeTouchUpdate();

            CancelTouch(7, center, queueEventOnly: true, screen: touchscreen, time: 2.1d);
            InputSystem.Update();
            InvokeTouchUpdate();

            Assert.That(selectedId, Is.Null,
                "A canceled Android touch stream must not be converted into a world tap.");
        }

        private void InvokeTouchUpdate()
        {
            UpdateTouchInputMethod.Invoke(presenter, null);
        }

        private static Vector2 ScreenCenter()
        {
            return new Vector2(Mathf.Max(1f, Screen.width * 0.5f), Mathf.Max(1f, Screen.height * 0.5f));
        }
    }
}
