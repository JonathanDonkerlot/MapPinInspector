using MapPinInspector.Core;
using Xunit;

namespace MapPinInspector.Tests
{
    public class DismissalEvaluatorTests
    {
        [Fact]
        public void ShouldAutoDismiss_UsesHysteresisBand()
        {
            Assert.False(DismissalEvaluator.ShouldAutoDismiss(3.4f, 3.5f, currentlyDismissed: false, hysteresisMeters: 0.25f));
            Assert.True(DismissalEvaluator.ShouldAutoDismiss(3.6f, 3.5f, currentlyDismissed: false, hysteresisMeters: 0.25f));
            // Once dismissed, staying inside hysteresis zone (3.30m > 3.25m) keeps it dismissed
            Assert.True(DismissalEvaluator.ShouldAutoDismiss(3.30f, 3.5f, currentlyDismissed: true, hysteresisMeters: 0.25f));
            Assert.False(DismissalEvaluator.ShouldAutoDismiss(3.10f, 3.5f, currentlyDismissed: true, hysteresisMeters: 0.25f));
        }

        [Fact]
        public void StepFadeAlpha_ClampsBetweenZeroAndOne()
        {
            float a1 = DismissalEvaluator.StepFadeAlpha(0.5f, targetVisible: true, fadeSpeedPerSecond: 10f, deltaTime: 0.2f);
            Assert.Equal(1.0f, a1, 4);

            float a2 = DismissalEvaluator.StepFadeAlpha(0.2f, targetVisible: false, fadeSpeedPerSecond: 10f, deltaTime: 0.2f);
            Assert.Equal(0.0f, a2, 4);
        }

        [Fact]
        public void TapDebouncer_BlocksRapidDoubleTapsPerSource()
        {
            var debouncer = new TapDebouncer(0.30f);
            Assert.True(debouncer.TryAccept(1, 10.00f));
            Assert.False(debouncer.TryAccept(1, 10.15f));
            Assert.True(debouncer.TryAccept(2, 10.15f));
            Assert.True(debouncer.TryAccept(1, 10.35f));
        }
    }
}
