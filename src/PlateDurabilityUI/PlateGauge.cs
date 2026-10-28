using System;
using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PlateDurabilityUI
{
    /// <summary>
    /// The gauge on one item view: one bar per plate slot (front, back, left, right), slot labels
    /// under them, and a thin bar for the weakest soft insert at the bottom, in the corner and
    /// style the settings name (<see cref="GaugeLook"/>).
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

        // What Armor Class Icon (slpf) names the badges it adds straight to a view that has no
        // class icon slot of its own. Its usual badge is the game's own ArmorClassIcon image in
        // the bottom panel, which the panel scan already finds.
        private const string ArmorClassBadgeName = "ArmorClassBadge";

        // Layout, in UI units at Size 1. A grid cell is 63 units, so body armor (3x3) is 189. The
        // bars' own sizes come with the style (GaugeLook).
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
        private static readonly List<Graphic> GraphicScratch = new List<Graphic>();
        private static readonly List<TMP_Text> TextScratch = new List<TMP_Text>();

        // The item name (top right) and a player's tag on the item (top left) are on the view
        // itself, outside the info panel, so the top corners look them up directly.
        private static readonly AccessTools.FieldRef<GridItemView, TextMeshProUGUI> CaptionRef = TextField("Caption");
        private static readonly AccessTools.FieldRef<GridItemView, TextMeshProUGUI> TagNameRef = TextField("TagName");
        private static readonly Vector3[] Corners = new Vector3[4];

        private GridItemView _view;
        private ItemViewBottomPanel _bottom;
        private Item _item;
        private RectTransform _body;
        private Image _panelShadow;
        private Image _panel;
        private GaugeLook _look;
        private readonly List<Bar> _bars = new List<Bar>();
        private Bar _soft;
        private readonly ArmorReading _reading = new ArmorReading();
        private Vector2 _size;
        private bool _snap = true;
        private bool _sliding;
        private bool _layoutDirty;
        private bool _layoutStale;
        private float _nextPoll;

        private sealed class Bar
        {
            public RectTransform Root;
            public RectTransform TrackRect;
            public Image Track;
            public Image Fill;
            public Image Wear;
            public Image Pattern;
            public TextMeshProUGUI Letter;
            public TextMeshProUGUI LetterShadow;
            public bool Horizontal;
            public GaugeLook Look;
            public PlateState State;
            public float Shown;
            public float Target;
            public float WearAmount;
        }

        // ---- entry points ----------------------------------------------------------------

        /// <summary>From the UpdateInfo postfix: show, refresh or turn off the view's gauge.</summary>
        internal static void Refresh(GridItemView view, RectTransform infoPanel, ItemViewBottomPanel bottom, bool examined)
        {
            Transform parent = infoPanel != null ? infoPanel : view.transform;
            PlateGauge gauge = Find(parent);

            bool applicable = PlateDurabilityPlugin.Enabled.Value
                              && examined
                              && ShownOn(view)
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

        /// <summary>The per-screen switches, by the screen type the game gives the view's context.</summary>
        private static bool ShownOn(GridItemView view)
        {
            switch (view.ItemContext?.ViewType)
            {
                case EItemViewType.Ragfair:
                case EItemViewType.NewOffer:
                    return PlateDurabilityPlugin.OnFlea.Value;
                case EItemViewType.TradingTrader:
                case EItemViewType.TradingSell:
                case EItemViewType.TradingPlayer:
                    return PlateDurabilityPlugin.AtTraders.Value;
                default:
                    return PlateDurabilityPlugin.InInventory.Value;
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
            _reading.Softs.AddRange(reading.Softs);
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

            // The panel is a child, not the body's own image, so a shadow can sit behind it.
            RectTransform shadowRect = NewRect("Shadow", _body);
            Stretch(shadowRect, 0f);
            shadowRect.offsetMin = new Vector2(0f, -1f);
            shadowRect.offsetMax = new Vector2(0f, -1f);
            _panelShadow = shadowRect.gameObject.AddComponent<Image>();
            _panelShadow.type = Image.Type.Sliced;
            _panelShadow.color = new Color(0f, 0f, 0f, 0.45f);
            _panelShadow.raycastTarget = false;

            RectTransform panelRect = NewRect("Panel", _body);
            Stretch(panelRect, 0f);
            _panel = panelRect.gameObject.AddComponent<Image>();
            _panel.type = Image.Type.Sliced;
            _panel.raycastTarget = false;

            body.AddComponent<Hover>().Owner = this;
        }

        /// <summary>
        /// Pointing at the gauge opens the plate tooltip. Only the backing panel is a raycast
        /// target (in the styles with no backing it is there but clear); pointer enter and exit
        /// reach this on the body from it. Clicks, drags and scrolling find no handler on either
        /// and go up to the item view exactly as before.
        /// </summary>
        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            internal PlateGauge Owner;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Owner != null && Owner._item != null)
                {
                    PlateTooltip.OpenFor(Owner._view, Owner._item);
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
            bar.TrackRect = NewRect("Track", bar.Root);
            bar.Track = bar.TrackRect.gameObject.AddComponent<Image>();
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

        /// <summary>The bar's letter and, behind it, a copy for the styles that shadow their letters.</summary>
        private void NewLetter(Bar bar)
        {
            TextMeshProUGUI template = _bottom != null ? _bottom.ItemValue : null;
            if (template == null || template.font == null)
            {
                return;
            }
            // A copy rather than a TMP underlay: that would need its own instance of the game's
            // font material, and the shader may not have one.
            bar.LetterShadow = NewText("LetterShadow", template);
            bar.Letter = NewText("Letter", template);
        }

        private TextMeshProUGUI NewText(string name, TextMeshProUGUI template)
        {
            RectTransform rect = NewRect(name, _body);
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
            Stretch(rect, 1f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = Sprites.White;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Fills the parent, <paramref name="inset"/> in from each edge (out, if negative).</summary>
        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // ---- drawing ---------------------------------------------------------------------

        /// <summary>Puts the panel in <paramref name="look"/>; bars follow in <see cref="Restyle"/>.</summary>
        private void UseLook(GaugeLook look)
        {
            if (_look == look)
            {
                return;
            }
            _look = look;
            _panel.sprite = look.Panel;
            _panel.color = look.PanelColor;
            _panelShadow.sprite = look.Panel;
            _panelShadow.enabled = look.PanelShadow;
        }

        private static void Restyle(Bar bar, GaugeLook look)
        {
            if (bar.Look == look)
            {
                return;
            }
            bar.Look = look;
            bar.Track.sprite = look.Track;
            bar.Track.color = look.TrackColor;
            Stretch(bar.TrackRect, -look.TrackOutset);
            Stretch(bar.Fill.rectTransform, look.FillInset);
            Stretch(bar.Wear.rectTransform, look.FillInset);
            if (bar.Letter != null)
            {
                FontStyles style = look.LetterBold ? FontStyles.Bold : FontStyles.Normal;
                bar.Letter.color = look.LetterColor;
                bar.Letter.fontStyle = style;
                bar.LetterShadow.color = look.LetterShadow;
                bar.LetterShadow.fontStyle = style;
            }
        }

        private void Apply()
        {
            GaugeLook look = GaugeLook.For(PlateDurabilityPlugin.Style.Value);
            UseLook(look);
            bool letters = look.Letters && PlateDurabilityPlugin.ShowLetters.Value;
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

            float width = look.Pad * 2f + count * look.BarWidth + Mathf.Max(count - 1, 0) * look.BarGap;
            float softY = look.Pad;
            float letterY = softY + (soft ? look.SoftHeight + look.SoftGap : 0f);
            float barY = letterY + (letters ? LetterHeight + LetterGap : 0f);
            float height = barY + look.BarHeight + look.Pad;
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
                        bar.LetterShadow.gameObject.SetActive(false);
                    }
                    continue;
                }

                PlateReading plate = _reading.Plates[i];
                float x = look.Pad + i * (look.BarWidth + look.BarGap);
                if (letters && bar.Letter == null)
                {
                    NewLetter(bar);
                    bar.Look = null;
                }
                Set(bar.Root, x, barY, look.BarWidth, look.BarHeight);
                Restyle(bar, look);
                SetTarget(bar, plate, animate);

                if (bar.Letter != null)
                {
                    bool shadow = letters && look.LetterShadow.a > 0f;
                    bar.Letter.gameObject.SetActive(letters);
                    bar.LetterShadow.gameObject.SetActive(shadow);
                    if (letters)
                    {
                        string text = LabelFor(plate);
                        Set(bar.Letter.rectTransform, x - 2f, letterY, look.BarWidth + 4f, LetterHeight);
                        bar.Letter.text = text;
                        if (shadow)
                        {
                            Set(bar.LetterShadow.rectTransform, x - 2f + look.ShadowOffset.x, letterY + look.ShadowOffset.y,
                                look.BarWidth + 4f, LetterHeight);
                            bar.LetterShadow.text = text;
                        }
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
                    Set(_soft.Root, look.Pad, softY, width - look.Pad * 2f, look.SoftHeight);
                    Restyle(_soft, look);
                    SetTarget(_soft, _reading.Soft, animate);
                }
            }

            _snap = false;
            _layoutDirty = true;
            _layoutStale = true;
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
                float inner = (bar.Horizontal ? bar.Root.sizeDelta.x : bar.Root.sizeDelta.y) - 2f * bar.Look.FillInset;
                float sliver = inner > 0f ? 1f / inner : 0f;
                bar.Fill.fillAmount = bar.Shown > 0f ? Mathf.Max(bar.Shown, sliver) : 0f;
                bar.Fill.color = bar.Look.Ramp(bar.Shown);
                bar.Wear.fillAmount = bar.WearAmount;
                return;
            }

            // Cached per state, colour and size, and the sprite setter ignores the same sprite.
            Vector2 size = bar.Root.sizeDelta;
            bar.Pattern.sprite = Sprites.Pattern(bar.State == PlateState.Broken, bar.Look.Dash,
                                                 Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        }

        private static string LabelFor(PlateReading plate)
        {
            if (PlateDurabilityPlugin.Labels.Value == SlotLabel.ArmorClass)
            {
                int armorClass = plate.Armor != null ? plate.Armor.ArmorClass : 0;
                return armorClass > 0 ? armorClass.ToString() : "-";
            }
            switch (plate.Zone)
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

        /// <summary>In the corner the settings name, clear of whatever the game or another mod draws there.</summary>
        private void Place()
        {
            if (!(_view.transform is RectTransform viewRect))
            {
                return;
            }
            bool top = PlateDurabilityPlugin.Corner.Value != GaugeCorner.BottomLeft;
            if (_layoutStale)
            {
                _layoutStale = false;
                SettleLayout(top);
            }

            Rect item = RectIn(viewRect);
            float scale = PlateDurabilityPlugin.Size.Value;
            float x;
            float y;
            if (top)
            {
                PlaceTop(item, PlateDurabilityPlugin.Corner.Value == GaugeCorner.TopRight, ref scale, out x, out y);
            }
            else
            {
                PlaceBottomLeft(viewRect, item, ref scale, out x, out y);
            }

            _body.localScale = new Vector3(scale, scale, 1f);
            _body.localPosition = new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Bottom-left of the icon, right of any small icon already in that corner, and never into
        /// the durability numbers on the right. On an item too narrow for both, the gauge shrinks
        /// rather than overlap.
        /// </summary>
        private void PlaceBottomLeft(RectTransform viewRect, Rect item, ref float scale, out float x, out float y)
        {
            x = item.xMin + Margin;
            y = item.yMin + Margin;
            float bandTop = y + _size.y * scale;

            if (_bottom != null)
            {
                ImageScratch.Clear();
                _bottom.GetComponentsInChildren(false, ImageScratch);
                foreach (Image image in ImageScratch)
                {
                    x = Clear(image, item, bandTop, x);
                }
            }

            for (int i = 0; i < viewRect.childCount; i++)
            {
                Transform child = viewRect.GetChild(i);
                if (child.name == ArmorClassBadgeName && child.gameObject.activeSelf)
                {
                    x = Clear(child.GetComponent<Image>(), item, bandTop, x);
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
        }

        /// <summary>
        /// A top corner, under whatever the game draws there: the item name top right, the
        /// quick-slot number top left, the price across the top on trader screens, an item tag.
        /// Text is measured by its letters, not its box, which often spans the whole icon. On an
        /// item too short for the gauge under all that, it shrinks.
        /// </summary>
        private void PlaceTop(Rect item, bool right, ref float scale, out float x, out float y)
        {
            float width = _size.x * scale;
            float left = right ? item.xMax - Margin - width : item.xMin + Margin;
            float top = item.yMax - Margin;

            GraphicScratch.Clear();
            transform.parent.GetComponentsInChildren(false, GraphicScratch);
            foreach (Graphic graphic in GraphicScratch)
            {
                top = Under(graphic, item, left, left + width, top);
            }
            top = Under(Field(CaptionRef, _view), item, left, left + width, top);
            top = Under(Field(TagNameRef, _view), item, left, left + width, top);

            float fit = (top - (item.yMin + Margin)) / (_size.y * scale);
            if (fit < 1f)
            {
                scale *= Mathf.Max(fit, MinFit);
            }
            width = _size.x * scale;
            x = right ? item.xMax - Margin - width : item.xMin + Margin;
            y = top - _size.y * scale;
        }

        /// <summary>
        /// <paramref name="top"/>, moved down below <paramref name="graphic"/> if that is an icon
        /// or a piece of text in the top half of the item, over the gauge's columns.
        /// </summary>
        private float Under(Graphic graphic, Rect item, float xMin, float xMax, float top)
        {
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.color.a < 0.05f
                || graphic.transform.IsChildOf(transform))
            {
                return top;
            }

            Rect r;
            if (graphic is TMP_Text text)
            {
                Bounds bounds = text.textBounds;
                if (string.IsNullOrEmpty(text.text) || bounds.size.x <= 0f)
                {
                    return top;
                }
                Vector3 a = transform.InverseTransformPoint(text.rectTransform.TransformPoint(bounds.min));
                Vector3 c = transform.InverseTransformPoint(text.rectTransform.TransformPoint(bounds.max));
                r = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
            }
            else if (graphic is Image image && image.sprite != null)
            {
                r = RectIn(image.rectTransform);
                if (r.width > item.width * 0.5f || r.height > item.height * 0.5f)
                {
                    return top; // a backdrop: the item's own picture, a colour panel, a border
                }
            }
            else
            {
                return top; // hover targets and other graphics that draw nothing
            }

            bool topHalf = r.center.y > item.center.y;
            bool overColumns = r.xMax > xMin && r.xMin < xMax;
            return r.width > 0f && topHalf && overColumns ? Mathf.Min(top, r.yMin - IconGap) : top;
        }

        private static T Field<T>(AccessTools.FieldRef<GridItemView, T> field, GridItemView view) where T : class
        {
            return field != null && view != null ? field(view) : null;
        }

        private static AccessTools.FieldRef<GridItemView, TextMeshProUGUI> TextField(string name)
        {
            // Serialized fields keep their names in the unpatched assembly too, but may be private there.
            try
            {
                return AccessTools.FieldRefAccess<GridItemView, TextMeshProUGUI>(name);
            }
            catch (Exception e)
            {
                PlateDurabilityPlugin.Debug("GridItemView." + name + " not found, the top corners will not avoid it: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// The icons and text the gauge keeps clear of sit in layout groups (the bottom-left icon
        /// stack, with Armor Class Icon's badge, which is the game's class icon turned on; the
        /// quick-slot number; the trader price), and Unity only moves them into place when the
        /// canvas rebuilds, after LateUpdate. Measured right after UpdateInfo they are still where
        /// the pooled view last had them, so the gauge would land on top of them and only jump
        /// clear on the next poll. Run those layouts now instead, and the text meshes the gauge
        /// measures with them, so the first placement is the right one.
        /// </summary>
        private void SettleLayout(bool top)
        {
            // A layout rebuild does nothing on a rect with no layout group of its own (the info
            // panel), so each group under it is rebuilt by itself.
            Transform panel = transform.parent;
            if (_bottom != null && _bottom.transform is RectTransform bottomRect && bottomRect.parent != panel)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(bottomRect);
            }
            for (int i = 0; i < panel.childCount; i++)
            {
                if (panel.GetChild(i) is RectTransform child && child.gameObject.activeInHierarchy
                    && child.GetComponent<LayoutGroup>() != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(child);
                }
            }

            TextMeshProUGUI value = _bottom != null ? _bottom.ItemValue : null;
            if (value != null && value.isActiveAndEnabled)
            {
                value.ForceMeshUpdate();
            }
            if (!top)
            {
                return;
            }
            TextScratch.Clear();
            panel.GetComponentsInChildren(false, TextScratch);
            TextScratch.Add(Field(CaptionRef, _view));
            TextScratch.Add(Field(TagNameRef, _view));
            foreach (TMP_Text text in TextScratch)
            {
                if (text != null && text.isActiveAndEnabled && !text.transform.IsChildOf(transform))
                {
                    text.ForceMeshUpdate();
                }
            }
        }

        /// <summary>The gauge's left edge, pushed right of <paramref name="image"/> if that is a small icon in its way.</summary>
        private float Clear(Image image, Rect item, float bandTop, float x)
        {
            if (image == null || !image.enabled || image.sprite == null || image.color.a < 0.05f)
            {
                return x;
            }
            Rect r = RectIn(image.rectTransform);
            bool backdrop = r.width > item.width * 0.5f || r.height > item.height * 0.5f;
            bool inBand = r.yMin < bandTop && r.yMax > item.yMin;
            bool leftHalf = r.center.x < item.center.x;
            if (r.width > 0f && !backdrop && inBand && leftHalf)
            {
                return Mathf.Max(x, r.xMax + IconGap);
            }
            return x;
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
