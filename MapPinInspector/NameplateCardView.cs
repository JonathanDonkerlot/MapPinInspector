using System;
using System.Collections.Generic;
using System.Text;
using MapPinInspector.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MapPinInspector
{
    /// <summary>
    /// Builds and animates the 3D world-space nameplate UI card, selection ring, and spatial click audio.
    /// Prefers TextMeshPro when a TMP_FontAsset is available and falls back cleanly to UnityEngine.UI.Text.
    /// </summary>
    public sealed class NameplateCardView
    {
        private static readonly Color FriendAccent = new Color(0.20f, 0.85f, 0.50f, 0.98f);
        private static readonly Color HostileAccent = new Color(0.95f, 0.30f, 0.30f, 0.98f);
        private static readonly Color ClusterAccent = new Color(0.95f, 0.76f, 0.28f, 0.98f);
        private static readonly Color CardBackground = new Color(0.06f, 0.08f, 0.13f, 0.92f);

        private sealed class TextLabelSlot
        {
            public TextMeshProUGUI Tmp;
            public Text Legacy;

            public void SetText(string value)
            {
                if (Tmp != null)
                    Tmp.text = value ?? string.Empty;
                else if (Legacy != null)
                    Legacy.text = value ?? string.Empty;
            }

            public void SetColor(Color color)
            {
                if (Tmp != null)
                    Tmp.color = color;
                else if (Legacy != null)
                    Legacy.color = color;
            }
        }

        private readonly GameObject _cardRoot;
        private readonly Canvas _canvas;
        private readonly CanvasGroup _canvasGroup;
        private readonly Image[] _borderImages;
        private readonly TextLabelSlot _badgeLabel;
        private readonly TextLabelSlot _namesLabel;
        private readonly TextLabelSlot _statusLabel;
        private readonly TextLabelSlot _hintLabel;

        private readonly GameObject _ringRoot;
        private readonly LineRenderer _ringRenderer;
        private readonly AudioSource _audioSource;
        private readonly AudioClip _clickClip;

        private static Material _doubleSidedUiMat;
        private static Material _doubleSidedFontMat;
        private static Material _ringMaterial;

        private float _currentAlpha;
        private float _pulseElapsed;
        private bool _targetVisible;

        public bool IsAlive => _cardRoot != null;
        public float CurrentAlpha => _currentAlpha;

        public NameplateCardView(Transform initialParent)
        {
            _cardRoot = new GameObject("MapPinInspector_CardRoot");
            _cardRoot.layer = 2; // Ignore Raycast
            _cardRoot.transform.SetParent(initialParent, false);
            _cardRoot.transform.localScale = Vector3.one;

            var canvasGo = new GameObject("CardCanvas");
            canvasGo.layer = 2;
            canvasGo.transform.SetParent(_cardRoot.transform, false);
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.00055f; // 460x220 px -> ~25cm x 12cm

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 600;

            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(460f, 220f);

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            Material uiMat = GetDoubleSidedUiMaterial();
            TMP_FontAsset tmpFont = ResolveTmpFontAsset();
            Font legacyFont = tmpFont == null ? ResolveLegacyFont() : null;
            Material legacyFontMat = legacyFont != null ? GetDoubleSidedFontMaterial(legacyFont) : null;

            CreateRectImage(
                "PanelBackdrop",
                canvasGo.transform,
                Vector2.zero,
                new Vector2(452f, 212f),
                3f,
                CardBackground,
                uiMat);

            _borderImages = new[]
            {
                CreateRectImage("BorderTop", canvasGo.transform, new Vector2(0f, 106f), new Vector2(460f, 8f), 1.5f, FriendAccent, uiMat),
                CreateRectImage("BorderBottom", canvasGo.transform, new Vector2(0f, -106f), new Vector2(460f, 8f), 1.5f, FriendAccent, uiMat),
                CreateRectImage("BorderLeft", canvasGo.transform, new Vector2(-226f, 0f), new Vector2(8f, 204f), 1.5f, FriendAccent, uiMat),
                CreateRectImage("BorderRight", canvasGo.transform, new Vector2(226f, 0f), new Vector2(8f, 204f), 1.5f, FriendAccent, uiMat)
            };

            _badgeLabel = CreateLabel(
                "Badge",
                canvasGo.transform,
                tmpFont,
                legacyFont,
                legacyFontMat,
                fontSize: 22,
                bold: true,
                TextAlignmentOptions.Center,
                TextAnchor.MiddleCenter,
                FriendAccent,
                new Vector2(0f, 76f),
                new Vector2(420f, 32f));

            _namesLabel = CreateLabel(
                "PlayerNames",
                canvasGo.transform,
                tmpFont,
                legacyFont,
                legacyFontMat,
                fontSize: 32,
                bold: true,
                TextAlignmentOptions.Center,
                TextAnchor.MiddleCenter,
                Color.white,
                new Vector2(0f, 14f),
                new Vector2(420f, 92f));

            _statusLabel = CreateLabel(
                "StatusLine",
                canvasGo.transform,
                tmpFont,
                legacyFont,
                legacyFontMat,
                fontSize: 20,
                bold: false,
                TextAlignmentOptions.Center,
                TextAnchor.MiddleCenter,
                new Color(0.78f, 0.85f, 0.93f, 0.95f),
                new Vector2(0f, -46f),
                new Vector2(420f, 36f));

            _hintLabel = CreateLabel(
                "HintLine",
                canvasGo.transform,
                tmpFont,
                legacyFont,
                legacyFontMat,
                fontSize: 16,
                bold: false,
                TextAlignmentOptions.Center,
                TextAnchor.MiddleCenter,
                new Color(0.56f, 0.64f, 0.75f, 0.90f),
                new Vector2(0f, -82f),
                new Vector2(420f, 26f));

            _ringRoot = new GameObject("MapPinInspector_SelectionRing");
            _ringRoot.layer = 2;
            _ringRoot.transform.SetParent(initialParent, false);
            _ringRenderer = BuildSelectionRing(_ringRoot);

            _audioSource = _cardRoot.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1.0f;
            _audioSource.volume = 0.25f;
            _audioSource.minDistance = 0.15f;
            _audioSource.maxDistance = 3.0f;
            _clickClip = CreateClickClip();

            _cardRoot.SetActive(false);
            _ringRoot.SetActive(false);
        }

        public void SetSelection(ClusterSelection selection, Transform pinAnchorTransform, bool showStatusInfo, bool playFeedback)
        {
            if (_cardRoot == null || selection == null || selection.FocusedMember == null)
            {
                SetVisible(false);
                return;
            }

            if (pinAnchorTransform != null)
            {
                if (_cardRoot.transform.parent != pinAnchorTransform)
                    _cardRoot.transform.SetParent(pinAnchorTransform, false);
                if (_ringRoot != null && _ringRoot.transform.parent != pinAnchorTransform)
                    _ringRoot.transform.SetParent(pinAnchorTransform, false);
            }

            ApplySelectionTextAndColors(selection, showStatusInfo);
            SetVisible(true);
            _pulseElapsed = 0f;

            if (playFeedback && _audioSource != null && _clickClip != null)
                _audioSource.PlayOneShot(_clickClip);
        }

        public void SetVisible(bool visible)
        {
            _targetVisible = visible;
            if (visible)
            {
                if (_cardRoot != null && !_cardRoot.activeSelf)
                    _cardRoot.SetActive(true);
                if (_ringRoot != null && !_ringRoot.activeSelf)
                    _ringRoot.SetActive(true);
            }
        }

        public void UpdateTransformAndFade(
            Vector3 pinWorldPos,
            Vector3 boardOutwardNormal,
            Camera viewerCamera,
            float cardScale,
            float deltaTime)
        {
            if (_cardRoot == null)
                return;

            _currentAlpha = DismissalEvaluator.StepFadeAlpha(_currentAlpha, _targetVisible, 8.5f, deltaTime);
            if (_canvasGroup != null)
                _canvasGroup.alpha = _currentAlpha;

            if (!_targetVisible && _currentAlpha <= 0.005f)
            {
                if (_cardRoot.activeSelf)
                    _cardRoot.SetActive(false);
                if (_ringRoot != null && _ringRoot.activeSelf)
                    _ringRoot.SetActive(false);
                return;
            }

            float clampedScale = Mathf.Clamp(cardScale, 0.4f, 2.5f);
            _cardRoot.transform.localScale = Vector3.one * clampedScale;

            Vector3 safeNormal = boardOutwardNormal.sqrMagnitude > 1e-6f
                ? boardOutwardNormal.normalized
                : Vector3.forward;

            // Offset forward along board surface normal + slightly upward so the card never Z-fights or clips the board.
            float forwardOffset = 0.048f + 0.014f * clampedScale;
            float verticalLift = 0.068f * clampedScale;
            Vector3 cardWorldPos = pinWorldPos + safeNormal * forwardOffset + Vector3.up * verticalLift;
            _cardRoot.transform.position = cardWorldPos;

            Vector3 camPos = viewerCamera != null
                ? viewerCamera.transform.position
                : (cardWorldPos + safeNormal);

            Vec3 forwardVec = MapProjectionMath.ComputeBillboardCanvasForward(
                new Vec3(cardWorldPos.x, cardWorldPos.y, cardWorldPos.z),
                new Vec3(camPos.x, camPos.y, camPos.z),
                maxPitchDegrees: 35f);

            Vector3 worldForward = new Vector3(forwardVec.X, forwardVec.Y, forwardVec.Z);
            if (worldForward.sqrMagnitude > 1e-6f)
                _cardRoot.transform.rotation = Quaternion.LookRotation(worldForward, Vector3.up);

            UpdateSelectionRing(pinWorldPos, safeNormal, deltaTime);
        }

        private void UpdateSelectionRing(Vector3 pinWorldPos, Vector3 boardNormal, float deltaTime)
        {
            if (_ringRoot == null || _ringRenderer == null)
                return;

            _pulseElapsed += deltaTime;
            float pulse = 1f + 0.14f * Mathf.Sin(_pulseElapsed * 5.2f);
            float radius = 0.026f * pulse;

            _ringRoot.transform.position = pinWorldPos + boardNormal * 0.005f;
            _ringRoot.transform.rotation = Quaternion.LookRotation(boardNormal, Vector3.up);
            _ringRoot.transform.localScale = Vector3.one * (radius / 0.026f);

            // Keep line width constant in world meters despite parent scale.
            _ringRenderer.startWidth = 0.0038f;
            _ringRenderer.endWidth = 0.0038f;
        }

        private void ApplySelectionTextAndColors(ClusterSelection selection, bool showStatusInfo)
        {
            PinSample focused = selection.FocusedMember;
            IReadOnlyList<PinSample> members = selection.Members;
            bool isCluster = members.Count > 1;

            Color accent;
            string badge;

            if (!isCluster)
            {
                accent = focused.IsFriend ? FriendAccent : HostileAccent;
                badge = focused.IsLocalPlayer
                    ? "YOU"
                    : (focused.IsFriend ? "FRIEND" : "PLAYER");
            }
            else
            {
                bool allFriend = true;
                bool anyFriend = false;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i].IsFriend) anyFriend = true;
                    else allFriend = false;
                }

                accent = allFriend ? FriendAccent : (!anyFriend ? HostileAccent : ClusterAccent);
                badge = $"CLUSTER • {selection.ActiveFocusIndex + 1}/{members.Count}";
            }

            if (_borderImages != null)
            {
                for (int i = 0; i < _borderImages.Length; i++)
                {
                    if (_borderImages[i] != null)
                        _borderImages[i].color = accent;
                }
            }

            if (_ringRenderer != null)
            {
                Color ringCol = new Color(accent.r, accent.g, accent.b, 0.92f);
                _ringRenderer.startColor = ringCol;
                _ringRenderer.endColor = ringCol;
            }

            _badgeLabel.SetText(badge);
            _badgeLabel.SetColor(accent);

            if (!isCluster)
            {
                _namesLabel.SetText(focused.DisplayName);
            }
            else
            {
                var sb = new StringBuilder();
                int maxRows = Math.Min(members.Count, 4);
                int startRow = 0;
                if (selection.ActiveFocusIndex >= maxRows)
                    startRow = Math.Min(members.Count - maxRows, selection.ActiveFocusIndex - maxRows + 1);

                for (int r = 0; r < maxRows; r++)
                {
                    int idx = startRow + r;
                    PinSample m = members[idx];
                    if (r > 0) sb.Append('\n');
                    string marker = idx == selection.ActiveFocusIndex ? "▶ " : "• ";
                    string tag = m.IsFriend ? " [F]" : string.Empty;
                    sb.Append(marker).Append(m.DisplayName).Append(tag);
                }

                if (members.Count > maxRows)
                    sb.Append("\n+").Append(members.Count - maxRows).Append(" more");

                _namesLabel.SetText(sb.ToString());
            }

            if (showStatusInfo && !string.IsNullOrEmpty(focused.StatusLabel))
                _statusLabel.SetText(focused.StatusLabel);
            else
                _statusLabel.SetText(string.Empty);

            _hintLabel.SetText(isCluster
                ? "Tap pin to cycle • Walk away to close"
                : "Tap pin again to close");
        }

        public void Destroy()
        {
            if (_cardRoot != null)
                UnityEngine.Object.Destroy(_cardRoot);
            if (_ringRoot != null)
                UnityEngine.Object.Destroy(_ringRoot);
            if (_clickClip != null)
                UnityEngine.Object.Destroy(_clickClip);
        }

        private static LineRenderer BuildSelectionRing(GameObject ringGo)
        {
            LineRenderer lr = ringGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.widthMultiplier = 1f;
            lr.startWidth = 0.0038f;
            lr.endWidth = 0.0038f;

            Material mat = GetRingMaterial();
            if (mat != null)
                lr.sharedMaterial = mat;

            const int segments = 28;
            const float baseRadius = 0.026f;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * baseRadius, Mathf.Sin(angle) * baseRadius, 0f));
            }

            return lr;
        }

        private static Image CreateRectImage(
            string name,
            Transform parent,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            float localZ,
            Color color,
            Material material)
        {
            var go = new GameObject(name);
            go.layer = 2;
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            rect.localPosition = new Vector3(anchoredPos.x, anchoredPos.y, localZ);

            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (material != null)
                img.material = material;
            return img;
        }

        private static TextLabelSlot CreateLabel(
            string name,
            Transform parent,
            TMP_FontAsset tmpFont,
            Font legacyFont,
            Material legacyMat,
            int fontSize,
            bool bold,
            TextAlignmentOptions tmpAlign,
            TextAnchor legacyAlign,
            Color color,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.layer = 2;
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            rect.localPosition = new Vector3(anchoredPos.x, anchoredPos.y, -1f);

            var slot = new TextLabelSlot();

            if (tmpFont != null)
            {
                TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = tmpFont;
                tmp.fontSize = fontSize;
                tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                tmp.alignment = tmpAlign;
                tmp.color = color;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Truncate;
                tmp.raycastTarget = false;
                tmp.richText = false;
                slot.Tmp = tmp;
                return slot;
            }

            Text txt = go.AddComponent<Text>();
            if (legacyFont != null)
                txt.font = legacyFont;
            if (legacyMat != null)
                txt.material = legacyMat;
            txt.fontSize = fontSize;
            txt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            txt.alignment = legacyAlign;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;
            slot.Legacy = txt;
            return slot;
        }

        private static TMP_FontAsset ResolveTmpFontAsset()
        {
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                    return TMP_Settings.defaultFontAsset;

                TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (loaded != null && loaded.Length > 0)
                {
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        if (loaded[i] != null && loaded[i].material != null)
                            return loaded[i];
                    }
                }
            }
            catch
            {
                // Fall back to legacy Unity UI Text if TMP_Settings is uninitialized.
            }

            return null;
        }

        private static Font ResolveLegacyFont()
        {
            Font builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (builtin != null)
                return builtin;

            try
            {
                return Font.CreateDynamicFontFromOSFont("Arial", 24);
            }
            catch
            {
                return null;
            }
        }

        private static Material GetDoubleSidedUiMaterial()
        {
            if (_doubleSidedUiMat != null)
                return _doubleSidedUiMat;

            try
            {
                Material src = Graphic.defaultGraphicMaterial;
                if (src != null)
                {
                    var mat = new Material(src);
                    if (mat.HasProperty("_Cull"))
                        mat.SetInt("_Cull", 0);
                    _doubleSidedUiMat = mat;
                }
            }
            catch
            {
                _doubleSidedUiMat = null;
            }

            return _doubleSidedUiMat;
        }

        private static Material GetDoubleSidedFontMaterial(Font font)
        {
            if (_doubleSidedFontMat != null)
                return _doubleSidedFontMat;

            try
            {
                if (font != null && font.material != null)
                {
                    var mat = new Material(font.material);
                    if (mat.HasProperty("_Cull"))
                        mat.SetInt("_Cull", 0);
                    _doubleSidedFontMat = mat;
                }
            }
            catch
            {
                _doubleSidedFontMat = null;
            }

            return _doubleSidedFontMat;
        }

        private static Material GetRingMaterial()
        {
            if (_ringMaterial != null)
                return _ringMaterial;

            try
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("UI/Default");
                if (shader != null)
                    _ringMaterial = new Material(shader);
            }
            catch
            {
                _ringMaterial = null;
            }

            return _ringMaterial;
        }

        private static AudioClip CreateClickClip()
        {
            const int sampleRate = 22050;
            const int samples = 520;
            AudioClip clip = AudioClip.Create("MapPinInspector_Click", samples, 1, sampleRate, false);
            float[] pcm = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Exp(-t * 155f);
                pcm[i] = Mathf.Sin(2f * Mathf.PI * 1650f * t) * env * 0.55f;
            }
            clip.SetData(pcm, 0);
            return clip;
        }
    }
}
