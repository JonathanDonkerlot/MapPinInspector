using System.Collections.Generic;

namespace MapPinInspector.Core
{
    /// <summary>
    /// Handles auto-dismiss distance hysteresis, smooth alpha fading, and per-hand tap debouncing.
    /// </summary>
    public static class DismissalEvaluator
    {
        public static bool ShouldAutoDismiss(
            float headDistanceMeters,
            float autoDismissDistanceMeters,
            bool currentlyDismissed,
            float hysteresisMeters = 0.25f)
        {
            float threshold = autoDismissDistanceMeters < 0.5f ? 0.5f : autoDismissDistanceMeters;
            float hyst = hysteresisMeters < 0f ? 0f : hysteresisMeters;

            if (currentlyDismissed)
                return headDistanceMeters > threshold - hyst;

            return headDistanceMeters > threshold;
        }

        public static float StepFadeAlpha(
            float currentAlpha,
            bool targetVisible,
            float fadeSpeedPerSecond,
            float deltaTime)
        {
            if (deltaTime <= 0f)
                return currentAlpha;

            float speed = fadeSpeedPerSecond < 0.1f ? 0.1f : fadeSpeedPerSecond;
            float step = speed * deltaTime;
            float next = targetVisible ? currentAlpha + step : currentAlpha - step;
            if (next < 0f) return 0f;
            if (next > 1f) return 1f;
            return next;
        }
    }

    public sealed class TapDebouncer
    {
        private readonly Dictionary<int, float> _lastTriggerBySource = new Dictionary<int, float>();
        private float _cooldownSeconds;

        public TapDebouncer(float cooldownSeconds = 0.28f)
        {
            SetCooldown(cooldownSeconds);
        }

        public void SetCooldown(float cooldownSeconds)
        {
            _cooldownSeconds = cooldownSeconds < 0.05f ? 0.05f : cooldownSeconds;
        }

        public bool TryAccept(int sourceId, float nowSeconds)
        {
            if (_lastTriggerBySource.TryGetValue(sourceId, out float last) &&
                nowSeconds - last < _cooldownSeconds)
            {
                return false;
            }

            _lastTriggerBySource[sourceId] = nowSeconds;
            return true;
        }

        public void Reset()
        {
            _lastTriggerBySource.Clear();
        }
    }
}
