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
    /// Builds and animates a compact aged-parchment 3D world-space nameplate displaying only player name(s),
    /// paired with a subtle selection ring and spatial click audio.
    /// Prefers TextMeshPro when a TMP_FontAsset is available and falls back cleanly to UnityEngine.UI.Text.
    /// </summary>
    public sealed class NameplateCardView
    {
        private static readonly Color FriendRingColor = new Color(0.20f, 0.85f, 0.50f, 0.92f);
        private static readonly Color HostileRingColor = new Color(0.95f, 0.30f, 0.30f, 0.92f);
        private static readonly Color ClusterRingColor = new Color(0.95f, 0.76f, 0.28f, 0.92f);
        private static readonly Color ParchmentInkColor = new Color(0.17f, 0.12f, 0.08f, 1.0f);

        private const float BaseCanvasWidth = 300f;
        private const float SingleRowHeight = 84f;
        private const float ExtraClusterRowHeight = 34f;
        private const float CanvasWorldScale = 0.00032f; // 300 px -> ~9.6 cm wide

        private sealed class TextLabelSlot
        {
            public TextMeshProUGUI Tmp;
            public Text Legacy;
            public RectTransform Rect;

            public void Configure(string value, int maxFontSize, int minFontSize, bool singleLine)
            {
                if (Tmp != null)
                {
                    Tmp.text = value ?? string.Empty;
                    Tmp.enableWordWrapping = !singleLine;
                    Tmp.enableAutoSizing = true;
                    Tmp.fontSizeMin = minFontSize;
                    Tmp.fontSizeMax = maxFontSize;
                    Tmp.fontSize = maxFontSize;
                }
                else if (Legacy != null)
                {
                    Legacy.text = value ?? string.Empty;
                    Legacy.horizontalOverflow = singleLine ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
                    Legacy.resizeTextForBestFit = true;
                    Legacy.resizeTextMinSize = minFontSize;
                    Legacy.resizeTextMaxSize = maxFontSize;
                    Legacy.fontSize = maxFontSize;
                }
            }
        }

        private readonly GameObject _cardRoot;
        private readonly Canvas _canvas;
        private readonly RectTransform _canvasRect;
        private readonly RectTransform _backdropRect;
        private readonly CanvasGroup _canvasGroup;
        private readonly TextLabelSlot _namesLabel;

        private readonly GameObject _ringRoot;
        private readonly LineRenderer _ringRenderer;
        private readonly AudioSource _audioSource;
        private readonly AudioClip _clickClip;

        private static Material _doubleSidedUiMat;
        private static Material _doubleSidedFontMat;
        private static Material _ringMaterial;
        private static Texture2D _parchmentTexture;
        private static Sprite _parchmentSprite;

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
            canvasGo.transform.localScale = Vector3.one * CanvasWorldScale;

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 600;

            _canvasRect = canvasGo.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(BaseCanvasWidth, SingleRowHeight);

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            Material uiMat = GetDoubleSidedUiMaterial();
            Sprite parchment = GetParchmentSprite();
            TMP_FontAsset tmpFont = ResolveTmpFontAsset();
            Font legacyFont = tmpFont == null ? ResolveLegacyFont() : null;
            Material legacyFontMat = legacyFont != null ? GetDoubleSidedFontMaterial(legacyFont) : null;

            Image backdrop = CreateParchmentBackdrop(
                canvasGo.transform,
                new Vector2(BaseCanvasWidth, SingleRowHeight),
                parchment,
                uiMat);
            _backdropRect = backdrop.rectTransform;

            _namesLabel = CreateNameLabel(
                canvasGo.transform,
                tmpFont,
                legacyFont,
                legacyFontMat,
                new Vector2(BaseCanvasWidth - 42f, SingleRowHeight - 20f));

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
            _ = showStatusInfo; // Card deliberately shows player name(s) only for compact readability.

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

            ApplySelection(selection);
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

            // Keep compact card close to the pin yet cleanly forward of the board surface to avoid Z-fighting.
            float forwardOffset = 0.026f + 0.008f * clampedScale;
            float verticalLift = 0.032f * clampedScale;
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
            _ringRoot.transform.position = pinWorldPos + boardNormal * 0.005f;
            _ringRoot.transform.rotation = Quaternion.LookRotation(boardNormal, Vector3.up);
            _ringRoot.transform.localScale = Vector3.one * pulse;

            _ringRenderer.startWidth = 0.0032f;
            _ringRenderer.endWidth = 0.0032f;
        }

        private void ApplySelection(ClusterSelection selection)
        {
            PinSample focused = selection.FocusedMember;
            IReadOnlyList<PinSample> members = selection.Members;
            bool isCluster = members.Count > 1;

            Color ringColor;
            if (!isCluster)
            {
                ringColor = focused.IsFriend ? FriendRingColor : HostileRingColor;
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

                ringColor = allFriend ? FriendRingColor : (!anyFriend ? HostileRingColor : ClusterRingColor);
            }

            if (_ringRenderer != null)
            {
                _ringRenderer.startColor = ringColor;
                _ringRenderer.endColor = ringColor;
            }

            if (!isCluster)
            {
                ResizeCard(SingleRowHeight);
                _namesLabel.Configure(focused.DisplayName, maxFontSize: 36, minFontSize: 18, singleLine: true);
                return;
            }

            int maxRows = Math.Min(members.Count, 4);
            int startRow = 0;
            if (selection.ActiveFocusIndex >= maxRows)
                startRow = Math.Min(members.Count - maxRows, selection.ActiveFocusIndex - maxRows + 1);

            var sb = new StringBuilder();
            for (int r = 0; r < maxRows; r++)
            {
                int idx = startRow + r;
                if (r > 0) sb.Append('\n');
                sb.Append(members[idx].DisplayName);
            }

            int rowCount = maxRows;
            if (members.Count > maxRows)
            {
                sb.Append("\n+").Append(members.Count - maxRows).Append(" more");
                rowCount++;
            }

            float cardHeight = SingleRowHeight + Math.Max(0, rowCount - 1) * ExtraClusterRowHeight;
            ResizeCard(cardHeight);
            _namesLabel.Configure(sb.ToString(), maxFontSize: 28, minFontSize: 16, singleLine: false);
        }

        private void ResizeCard(float height)
        {
            var size = new Vector2(BaseCanvasWidth, height);
            if (_canvasRect != null)
                _canvasRect.sizeDelta = size;
            if (_backdropRect != null)
                _backdropRect.sizeDelta = size;
            if (_namesLabel.Rect != null)
                _namesLabel.Rect.sizeDelta = new Vector2(BaseCanvasWidth - 42f, height - 20f);
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
            lr.startWidth = 0.0032f;
            lr.endWidth = 0.0032f;

            Material mat = GetRingMaterial();
            if (mat != null)
                lr.sharedMaterial = mat;

            const int segments = 28;
            const float baseRadius = 0.020f;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * baseRadius, Mathf.Sin(angle) * baseRadius, 0f));
            }

            return lr;
        }

        private static Image CreateParchmentBackdrop(
            Transform parent,
            Vector2 sizeDelta,
            Sprite parchmentSprite,
            Material material)
        {
            var go = new GameObject("ParchmentBackdrop");
            go.layer = 2;
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeDelta;
            rect.localPosition = new Vector3(0f, 0f, 1.5f);

            Image img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            if (parchmentSprite != null)
            {
                img.sprite = parchmentSprite;
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.color = new Color(0.86f, 0.79f, 0.69f, 0.96f);
            }

            if (material != null)
                img.material = material;

            return img;
        }

        private static TextLabelSlot CreateNameLabel(
            Transform parent,
            TMP_FontAsset tmpFont,
            Font legacyFont,
            Material legacyMat,
            Vector2 sizeDelta)
        {
            var go = new GameObject("PlayerName");
            go.layer = 2;
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeDelta;
            rect.localPosition = new Vector3(0f, 0f, -1f);

            var slot = new TextLabelSlot { Rect = rect };

            if (tmpFont != null)
            {
                TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = tmpFont;
                tmp.fontSize = 36f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = ParchmentInkColor;
                tmp.enableWordWrapping = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 18f;
                tmp.fontSizeMax = 36f;
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
            txt.fontSize = 36;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = ParchmentInkColor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 18;
            txt.resizeTextMaxSize = 36;
            txt.raycastTarget = false;
            slot.Legacy = txt;
            return slot;
        }

        private static Sprite GetParchmentSprite()
        {
            if (_parchmentSprite != null && _parchmentTexture != null)
                return _parchmentSprite;

            try
            {
                const int width = 256;
                const int height = 128;

                var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.hideFlags = HideFlags.HideAndDontSave;

                var pixels = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    float ny = y / (float)(height - 1);
                    float leftDeckle = 5.5f + 3.2f * ValueNoise1D(ny * 18f, 11) + 1.4f * ValueNoise1D(ny * 47f, 23);
                    float rightDeckle = 5.5f + 3.2f * ValueNoise1D(ny * 18f, 37) + 1.4f * ValueNoise1D(ny * 47f, 41);

                    for (int x = 0; x < width; x++)
                    {
                        float nx = x / (float)(width - 1);
                        float bottomDeckle = 4.5f + 2.8f * ValueNoise1D(nx * 24f, 53) + 1.2f * ValueNoise1D(nx * 64f, 67);
                        float topDeckle = 4.5f + 2.8f * ValueNoise1D(nx * 24f, 79) + 1.2f * ValueNoise1D(nx * 64f, 89);

                        // Multi-octave blotchy tea-stain paper variation
                        float blotch =
                            0.55f * ValueNoise2D(nx * 4.5f, ny * 2.5f, 101) +
                            0.30f * ValueNoise2D(nx * 10f, ny * 6f, 203) +
                            0.15f * ValueNoise2D(nx * 22f, ny * 12f, 307);

                        // Subtle woven linen fiber streaks
                        float weaveH = ValueNoise2D(nx * 14f, ny * 96f, 409) - 0.5f;
                        float weaveV = ValueNoise2D(nx * 160f, ny * 10f, 521) - 0.5f;
                        float fineGrain = Hash01(x, y, 631) - 0.5f;
                        float weave = weaveH * 0.045f + weaveV * 0.045f + fineGrain * 0.025f;

                        // Aged edge darkening (stronger near left/right borders like weathered parchment)
                        float edgeFadeX = Mathf.Min(nx, 1f - nx);
                        float edgeFadeY = Mathf.Min(ny, 1f - ny);
                        float vignetteX = Mathf.Clamp01(edgeFadeX / 0.16f);
                        float vignetteY = Mathf.Clamp01(edgeFadeY / 0.20f);
                        float vignette = 1f - (1f - vignetteX * 0.85f - 0.15f) * 0.65f - (1f - vignetteY) * 0.12f;
                        float edgeDarken = Mathf.Clamp01(Mathf.Min(vignetteX, vignetteY) * 0.65f + 0.35f);

                        // Warm cream (#EAE0CE) to aged tan (#C1AA8D)
                        float tone = Mathf.Clamp01(0.42f + (blotch - 0.5f) * 0.52f + weave + (edgeDarken - 1f) * 0.42f);
                        float r = Mathf.Lerp(184f, 232f, tone) * Mathf.Clamp(vignette, 0.84f, 1.0f);
                        float g = Mathf.Lerp(161f, 217f, tone) * Mathf.Clamp(vignette, 0.82f, 1.0f);
                        float b = Mathf.Lerp(134f, 194f, tone) * Mathf.Clamp(vignette, 0.78f, 1.0f);

                        // Organic deckled paper alpha mask along all four edges
                        float distLeft = x - leftDeckle;
                        float distRight = (width - 1 - x) - rightDeckle;
                        float distBottom = y - bottomDeckle;
                        float distTop = (height - 1 - y) - topDeckle;
                        float minDist = Mathf.Min(Mathf.Min(distLeft, distRight), Mathf.Min(distBottom, distTop));
                        float alpha = Mathf.Clamp01((minDist + 1.2f) / 2.6f);

                        pixels[y * width + x] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 248f), 0, 255));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                Sprite sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, width, height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0u,
                    SpriteMeshType.FullRect,
                    new Vector4(20f, 16f, 20f, 16f));
                sprite.hideFlags = HideFlags.HideAndDontSave;

                _parchmentTexture = tex;
                _parchmentSprite = sprite;
                return _parchmentSprite;
            }
            catch
            {
                return null;
            }
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float ValueNoise1D(float x, int seed)
        {
            int i0 = Mathf.FloorToInt(x);
            float f = x - i0;
            float u = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash01(i0, 0, seed), Hash01(i0 + 1, 0, seed), u);
        }

        private static float ValueNoise2D(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);

            float a = Hash01(ix, iy, seed);
            float b = Hash01(ix + 1, iy, seed);
            float c = Hash01(ix, iy + 1, seed);
            float d = Hash01(ix + 1, iy + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
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
                return Font.CreateDynamicFontFromOSFont("Arial", 32);
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
