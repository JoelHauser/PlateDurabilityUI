using System.Collections.Generic;
using UnityEngine;

namespace PlateDurabilityUI
{
    /// <summary>
    /// The gauge's few sprites, drawn in code at start-up so the mod ships as one DLL. All are
    /// point-filtered: at icon size a soft edge reads as blur, and the game's own UI is crisp.
    /// </summary>
    internal static class Sprites
    {
        internal static readonly Color32 PanelFill = new Color32(6, 7, 7, 225);
        internal static readonly Color32 PanelEdge = new Color32(48, 53, 49, 255);
        internal static readonly Color32 TrackFill = new Color32(20, 23, 21, 255);
        internal static readonly Color32 TrackEdge = new Color32(62, 68, 62, 255);
        internal static readonly Color32 Wear = new Color32(92, 38, 30, 255);
        internal static readonly Color32 EmptyEdge = new Color32(196, 192, 174, 255);
        internal static readonly Color32 Broken = new Color32(229, 85, 61, 255);
        internal static readonly Color32 Letter = new Color32(201, 197, 180, 255);

        private static Sprite _white;
        private static Sprite _panel;
        private static Sprite _track;
        private static readonly Dictionary<long, Sprite> Patterned = new Dictionary<long, Sprite>();

        /// <summary>Plain white, for filled images tinted in code.</summary>
        internal static Sprite White => _white ? _white : _white = Framed(Color.white, Color.white);

        /// <summary>The gauge's backing: dark, a one-pixel lighter edge. Nine-sliced.</summary>
        internal static Sprite Panel => _panel ? _panel : _panel = Framed(PanelFill, PanelEdge);

        /// <summary>An empty bar: near-black, one-pixel edge. Nine-sliced.</summary>
        internal static Sprite Track => _track ? _track : _track = Framed(TrackFill, TrackEdge);

        /// <summary>
        /// A dashed outline (an empty slot) or a red-striped box (a broken plate) at an exact
        /// pixel size. Dashes do not survive nine-slice stretching, so each size gets its own.
        /// </summary>
        internal static Sprite Pattern(bool broken, int width, int height)
        {
            width = Mathf.Max(width, 2);
            height = Mathf.Max(height, 2);
            long key = ((long)(broken ? 1 : 0) << 40) | ((long)width << 20) | (uint)height;
            if (Patterned.TryGetValue(key, out Sprite cached) && cached)
            {
                return cached;
            }

            Texture2D texture = NewTexture(width, height);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 stripe = new Color32(Broken.r, Broken.g, Broken.b, 150);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    Color32 c;
                    if (broken)
                    {
                        c = edge ? Broken : (((x + y) & 3) < 2 ? stripe : clear);
                    }
                    else
                    {
                        // Two on, two off, running round the outline.
                        c = edge && ((x + y) & 3) < 2 ? EmptyEdge : clear;
                    }
                    pixels[y * width + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = broken ? "PlateGauge.Broken" : "PlateGauge.Empty";
            Patterned[key] = sprite;
            return sprite;
        }

        private static Sprite Framed(Color32 fill, Color32 edge)
        {
            Texture2D texture = NewTexture(3, 3);
            Color32[] pixels = new Color32[9];
            for (int i = 0; i < 9; i++)
            {
                pixels[i] = i == 4 ? fill : edge;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 3, 3), new Vector2(0.5f, 0.5f), 100f, 0,
                                          SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            sprite.name = "PlateGauge.Framed";
            return sprite;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        /// <summary>
        /// Bar colour for a durability fraction: red at nothing, amber at half, green when full,
        /// blended continuously so every percent has its own shade.
        /// </summary>
        internal static Color Ramp(float fraction)
        {
            Color red = new Color32(229, 85, 61, 255);
            Color amber = new Color32(227, 178, 60, 255);
            Color green = new Color32(127, 178, 74, 255);
            fraction = Mathf.Clamp01(fraction);
            return fraction <= 0.5f
                ? Color.Lerp(red, amber, fraction / 0.5f)
                : Color.Lerp(amber, green, (fraction - 0.5f) / 0.5f);
        }
    }
}
