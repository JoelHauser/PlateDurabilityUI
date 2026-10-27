using System;
using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PlateDurabilityUI
{
    /// <summary>
    /// The gauge on one item view: a dark plate in the bottom-left corner of the icon with one
    /// bar per plate slot (front, back, left, right), slot letters under them, and a thin bar for
    /// the weakest soft insert at the bottom.
    ///
    /// It lives on the view's info panel, the layer the game draws the item's name and numbers
    /// on, so it shows and hides with them: it is gone while the item is unsearched, being
    /// examined or dragged. It is made once per view and kept when the view goes back to the
    /// game's pool; every UpdateInfo points it at the view's current item or turns it off.
    ///
    /// Between UpdateInfo calls it polls its item four times a second, which is what catches a
    /// plate taking damage in raid while the inventory is open (nothing redraws the view then).
    /// Inactive gauges, and every gauge while the inventory is closed, cost nothing.
    /// </summary>
    internal sealed class PlateGauge : MonoBehaviour
    {
        private const string ObjectName = "PlateDurabilityGauge";

        // Layout, in UI units at Size 1. A grid cell is 63 units, so body armor (3x3) is 189.
        private const float BarWidth = 7f;
        private const float BarHeight = 20f;
        private const float BarGap = 2f;
        private const float Pad = 3f;
        private const float SoftHeight = 3f;
        private const float SoftGap = 2f;
        private const float LetterHeight = 8f;
        private const float LetterGap = 1f;
        private const float LetterSize = 8f;
        private const float Margin = 3f;
        private const float IconGap = 2f;
        private const float ValueGap = 4f;
        private const float MinFit = 0.6f;

        private const float PollInterval = 0.25f;
        private const float SlidePerSecond = 1.5f;

        private static readonly ArmorReading Scratch = new ArmorReading();
        private static readonly List<Image> ImageScratch = new List<Image>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private GridItemView _view;
        private ItemViewBottomPanel _bottom;
        private Item _item;
        private RectTransform _body;
        private Image _panel;
        private readonly List<Bar> _bars = new List<Bar>();
        private Bar _soft;
        private readonly ArmorReading _reading = new ArmorReading();
        private Vector2 _size;
        private bool _snap = true;
        private bool _sliding;
        private bool _layoutDirty;
        private float _nextPoll;

        private sealed class Bar
        {
            public RectTransform Root;
            public Image Track;
            public Image Fill;
            public Image Wear;
            public Image Pattern;
            public TextMeshProUGUI Letter;
            public bool Horizontal;
            public PlateState State;
            public float Shown;
            public float Target;
            public float WearAmount;
            public Vector2Int PatternSize;
        }

        // ---- entry points ----------------------------------------------------------------

        /// <summary>From the UpdateInfo postfix: show, refresh or turn off the view's gauge.</summary>
        internal static void Refresh(GridItemView view, RectTransform infoPanel, ItemViewBottomPanel bottom, bool examined)
        {
            Transform parent = infoPanel != null ? infoPanel : view.transform;
            PlateGauge gauge = Find(parent);

            bool applicable = PlateDurabilityPlugin.Enabled.Value
                              && examined
                              && PlateReader.TryRead(view.Item, Scratch, log: true);
            if (!applicable)
            {
                if (gauge != null)
                {
                    gauge.Detach();
                }
                return;
            }

            if (gauge == null)
            {
                gauge = Create(parent);
            }
            gauge.Attach(view, bottom, Scratch);
        }

        /// <summary>
        /// After a setting changes: have every item view on screen redraw its numbers, which runs
        /// the postfix again, so gauges appear, vanish or restyle at once.
        /// </summary>
        internal static void MarkAllDirty()
        {
            foreach (GridItemView view in Object.FindObjectsOfType<GridItemView>())
            {
                if (view == null || !view.isActiveAndEnabled || view.Item == null)
                {
                    continue;
                }
                try
                {
                    view.UpdateInfo();
                }
                catch (Exception e)
                {
                    PlateDurabilityPlugin.Debug("Redraw after a setting change failed for one view: " + e.Message);
                }
            }
        }

        private static PlateGauge Find(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == ObjectName)
                {
                    return child.GetComponent<PlateGauge>();
                }
            }
            return null;
        }

        private static PlateGauge Create(Transform parent)
        {
            GameObject go = new GameObject(ObjectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (parent is RectTransform parentRect)
            {
                rect.pivot = parentRect.pivot;
            }
            rect.SetAsLastSibling();

            PlateGauge gauge = go.AddComponent<PlateGauge>();
            gauge.Build();
            return gauge;
        }

        // ---- lifetime --------------------------------------------------------------------

        private void Attach(GridItemView view, ItemViewBottomPanel bottom, ArmorReading reading)
        {
            if (_item != view.Item)
            {
                // A different item (or a pooled view reused): draw it as it is, no slide.
                _snap = true;
                _item = view.Item;
            }
            _view = view;
            _bottom = bottom;
            Copy(reading);
            gameObject.SetActive(true);
            _nextPoll = Time.unscaledTime + PollInterval;
            Apply();
        }

        private void Detach()
        {
            _item = null;
            _view = null;
            _snap = true;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_item == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= _nextPoll)
            {
                _nextPoll = now + PollInterval;
                if (!PlateReader.TryRead(_item, Scratch, log: false))
                {
                    Detach();
                    return;
                }
                if (Scratch.Signature != _reading.Signature)
                {
                    Copy(Scratch);
                    Apply();
                }
                // Neighbouring icons (insured, repair buff) come and go without telling anyone.
                _layoutDirty = true;
            }

            if (_sliding)
            {
                Slide(Time.unscaledDeltaTime);
            }
        }

        private void LateUpdate()
        {
            if (_layoutDirty && _view != null)
            {
                _layoutDirty = false;
                Place();
            }
        }

        private void Copy(ArmorReading reading)
        {
            _reading.Clear();
            _reading.Plates.AddRange(reading.Plates);
            _reading.HasSoft = reading.HasSoft;
            _reading.Soft = reading.Soft;
            _reading.Signature = reading.Signature;
        }

        // ---- building --------------------------------------------------------------------

        private void Build()
        {
            GameObject body = new GameObject("Body", typeof(RectTransform));
            body.layer = gameObject.layer;
            _body = (RectTransform)body.transform;
            _body.SetParent(transform, false);
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 0.5f);
            _body.pivot = Vector2.zero;

            _panel = body.AddComponent<Image>();
            _panel.sprite = Sprites.Panel;
            _panel.type = Image.Type.Sliced;
            _panel.raycastTarget = false;

            body.AddComponent<Hover>().Owner = this;
        }

        /// <summary>
        /// Pointing at the gauge opens the plate tooltip. Only the backing panel is a raycast
        /// target; clicks, drags and scrolling find no handler on it and go up to the item view
        /// exactly as before.
        /// </summary>
        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            internal PlateGauge Owner;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Owner != null && Owner._item != null)
                {
                    PlateTooltip.OpenFor(Owner._item);
                }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                PlateTooltip.Close();
            }
        }

        private Bar NewBar(bool horizontal)
        {
            Bar bar = new Bar { Horizontal = horizontal };

            bar.Root = NewRect(horizontal ? "Soft" : "Plate", _body);
            bar.Track = bar.Root.gameObject.AddComponent<Image>();
            bar.Track.sprite = Sprites.Track;
            bar.Track.type = Image.Type.Sliced;
            bar.Track.raycastTarget = false;

            bar.Fill = NewInset("Fill", bar.Root);
            bar.Fill.type = Image.Type.Filled;
            bar.Fill.fillMethod = horizontal ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
            bar.Fill.fillOrigin = horizontal ? (int)Image.OriginHorizontal.Left : (int)Image.OriginVertical.Bottom;

            bar.Wear = NewInset("Wear", bar.Root);
            bar.Wear.type = Image.Type.Filled;
            bar.Wear.fillMethod = horizontal ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
            bar.Wear.fillOrigin = horizontal ? (int)Image.OriginHorizontal.Right : (int)Image.OriginVertical.Top;
            bar.Wear.color = Sprites.Wear;

            RectTransform patternRect = NewRect("Pattern", bar.Root);
            patternRect.anchorMin = Vector2.zero;
            patternRect.anchorMax = Vector2.one;
            patternRect.offsetMin = patternRect.offsetMax = Vector2.zero;
            bar.Pattern = patternRect.gameObject.AddComponent<Image>();
            bar.Pattern.raycastTarget = false;
            bar.Pattern.enabled = false;

            return bar;
        }

        private TextMeshProUGUI NewLetter(Bar bar)
        {
            TextMeshProUGUI template = _bottom != null ? _bottom.ItemValue : null;
            if (template == null || template.font == null)
            {
                return null;
            }
            RectTransform rect = NewRect("Letter", _body);
            TextMeshProUGUI letter = rect.gameObject.AddComponent<TextMeshProUGUI>();
            letter.font = template.font;
            letter.fontSharedMaterial = template.fontSharedMaterial;
            letter.fontSize = LetterSize;
            letter.enableAutoSizing = false;
            letter.richText = false;
            letter.alignment = TextAlignmentOptions.Center;
            letter.overflowMode = TextOverflowModes.Overflow;
            letter.color = Sprites.Letter;
            letter.raycastTarget = false;
            return letter;
        }

        private RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            return rect;
        }

        private Image NewInset(string name, Transform parent)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(1f, 1f);
            rect.offsetMax = new Vector2(-1f, -1f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprites.White;
            image.raycastTarget = false;
            return image;
        }

        // ---- drawing ---------------------------------------------------------------------

        private void Apply()
        {
            bool letters = PlateDurabilityPlugin.ShowLetters.Value;
            bool soft = PlateDurabilityPlugin.ShowSoftBar.Value && _reading.HasSoft;
            bool animate = PlateDurabilityPlugin.Animate.Value && !_snap;
            int count = _reading.Plates.Count;

            bool hidden = PlateDurabilityPlugin.HideHealthy.Value
                          && _reading.AllAtOrAbove(PlateDurabilityPlugin.HealthyAbove.Value / 100f);
            _body.gameObject.SetActive(!hidden);
            _panel.raycastTarget = PlateDurabilityPlugin.HoverGauge.Value
                                   && PlateDurabilityPlugin.DetailedTooltip.Value
                                   && PlateTooltip.CanOpenFromGauge;

            while (_bars.Count < count)
            {
                _bars.Add(NewBar(horizontal: false));
            }

            float width = Pad * 2f + count * BarWidth + Mathf.Max(count - 1, 0) * BarGap;
            float softY = Pad;
            float letterY = softY + (soft ? SoftHeight + SoftGap : 0f);
            float barY = letterY + (letters ? LetterHeight + LetterGap : 0f);
            float height = barY + BarHeight + Pad;
            _size = new Vector2(width, height);
            _body.sizeDelta = _size;

            for (int i = 0; i < _bars.Count; i++)
            {
                Bar bar = _bars[i];
                bool used = i < count;
                bar.Root.gameObject.SetActive(used);
                if (!used)
                {
                    if (bar.Letter != null)
                    {
                        bar.Letter.gameObject.SetActive(false);
                    }
                    continue;
                }

                PlateReading plate = _reading.Plates[i];
                float x = Pad + i * (BarWidth + BarGap);
                Set(bar.Root, x, barY, BarWidth, BarHeight);
                SetTarget(bar, plate, animate);

                if (letters && bar.Letter == null)
                {
                    bar.Letter = NewLetter(bar);
                }
                if (bar.Letter != null)
                {
                    bar.Letter.gameObject.SetActive(letters);
                    if (letters)
                    {
                        Set(bar.Letter.rectTransform, x - 2f, letterY, BarWidth + 4f, LetterHeight);
                        bar.Letter.text = LetterFor(plate.Zone);
                    }
                }
            }

            if (soft && _soft == null)
            {
                _soft = NewBar(horizontal: true);
            }
            if (_soft != null)
            {
                _soft.Root.gameObject.SetActive(soft);
                if (soft)
                {
                    Set(_soft.Root, Pad, softY, width - Pad * 2f, SoftHeight);
                    SetTarget(_soft, _reading.Soft, animate);
                }
            }

            _snap = false;
            _layoutDirty = true;
        }

        private void SetTarget(Bar bar, PlateReading plate, bool animate)
        {
            bar.State = plate.State;
            bar.Target = plate.State == PlateState.Fitted ? plate.Fraction : 0f;
            bar.WearAmount = PlateDurabilityPlugin.ShowWear.Value ? plate.Wear : 0f;
            if (!animate || plate.State != PlateState.Fitted)
            {
                bar.Shown = bar.Target;
            }
            else if (!Mathf.Approximately(bar.Shown, bar.Target))
            {
                _sliding = true;
            }
            Draw(bar);
        }

        private void Slide(float dt)
        {
            bool moving = false;
            foreach (Bar bar in _bars)
            {
                moving |= Step(bar, dt);
            }
            if (_soft != null)
            {
                moving |= Step(_soft, dt);
            }
            _sliding = moving;
        }

        private bool Step(Bar bar, float dt)
        {
            if (!bar.Root.gameObject.activeSelf || Mathf.Approximately(bar.Shown, bar.Target))
            {
                return false;
            }
            bar.Shown = Mathf.MoveTowards(bar.Shown, bar.Target, SlidePerSecond * dt);
            Draw(bar);
            return !Mathf.Approximately(bar.Shown, bar.Target);
        }

        private static void Draw(Bar bar)
        {
            bool fitted = bar.State == PlateState.Fitted;
            bar.Track.enabled = fitted;
            bar.Fill.enabled = fitted;
            bar.Wear.enabled = fitted && bar.WearAmount > 0.005f;
            bar.Pattern.enabled = !fitted;

            if (fitted)
            {
                // Anything left at all keeps at least a sliver, so "almost gone" never reads as "empty".
                float inner = (bar.Horizontal ? bar.Root.sizeDelta.x : bar.Root.sizeDelta.y) - 2f;
                float sliver = inner > 0f ? 1f / inner : 0f;
                bar.Fill.fillAmount = bar.Shown > 0f ? Mathf.Max(bar.Shown, sliver) : 0f;
                bar.Fill.color = Sprites.Ramp(bar.Shown);
                bar.Wear.fillAmount = bar.WearAmount;
                return;
            }

            Vector2 size = bar.Root.sizeDelta;
            Vector2Int pixels = new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
            if (bar.Pattern.sprite == null || pixels != bar.PatternSize || bar.Pattern.sprite.name != PatternName(bar.State))
            {
                bar.Pattern.sprite = Sprites.Pattern(bar.State == PlateState.Broken, pixels.x, pixels.y);
                bar.PatternSize = pixels;
            }
        }

        private static string PatternName(PlateState state)
        {
            return state == PlateState.Broken ? "PlateGauge.Broken" : "PlateGauge.Empty";
        }

        private static string LetterFor(PlateZone zone)
        {
            switch (zone)
            {
                case PlateZone.Front: return "F";
                case PlateZone.Back: return "B";
                case PlateZone.Left: return "L";
                case PlateZone.Right: return "R";
                default: return "+";
            }
        }

        private static void Set(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        // ---- placing ---------------------------------------------------------------------

        /// <summary>
        /// Bottom-left of the icon, clear of whatever else the game (or another mod) has put in
        /// that corner, and never into the durability numbers on the right. On an item too narrow
        /// for both, the gauge shrinks rather than overlap.
        /// </summary>
        private void Place()
        {
            if (!(_view.transform is RectTransform viewRect))
            {
                return;
            }
            Rect item = RectIn(viewRect);
            float scale = PlateDurabilityPlugin.Size.Value;
            float x = item.xMin + Margin;
            float y = item.yMin + Margin;
            float bandTop = y + _size.y * scale;

            if (_bottom != null)
            {
                ImageScratch.Clear();
                _bottom.GetComponentsInChildren(false, ImageScratch);
                foreach (Image image in ImageScratch)
                {
                    if (!image.enabled || image.sprite == null || image.color.a < 0.05f)
                    {
                        continue;
                    }
                    Rect r = RectIn(image.rectTransform);
                    bool backdrop = r.width > item.width * 0.5f || r.height > item.height * 0.5f;
                    bool inBand = r.yMin < bandTop && r.yMax > item.yMin;
                    bool leftHalf = r.center.x < item.center.x;
                    if (r.width > 0f && !backdrop && inBand && leftHalf)
                    {
                        x = Mathf.Max(x, r.xMax + IconGap);
                    }
                }
            }

            float limit = item.xMax - Margin;
            TextMeshProUGUI value = _bottom != null ? _bottom.ItemValue : null;
            if (value != null && value.isActiveAndEnabled && !string.IsNullOrEmpty(value.text))
            {
                Bounds bounds = value.textBounds;
                if (bounds.size.x > 0f)
                {
                    Vector3 left = transform.InverseTransformPoint(
                        value.rectTransform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, 0f)));
                    if (left.y < bandTop)
                    {
                        limit = Mathf.Min(limit, left.x - ValueGap);
                    }
                }
            }

            float fit = (limit - x) / (_size.x * scale);
            if (fit < 1f)
            {
                scale *= Mathf.Max(fit, MinFit);
            }

            _body.localScale = new Vector3(scale, scale, 1f);
            _body.localPosition = new Vector3(x, y, 0f);
        }

        private Rect RectIn(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            Vector3 a = transform.InverseTransformPoint(Corners[0]);
            Vector3 c = transform.InverseTransformPoint(Corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }
    }
}
