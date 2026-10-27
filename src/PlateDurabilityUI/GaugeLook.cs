using System.Collections.Generic;
using UnityEngine;

namespace PlateDurabilityUI
{
    /// <summary>
    /// Everything one <see cref="GaugeStyle"/> changes: the gauge's sizes, sprites and colours.
    /// Each is built once, on first use (its sprites are textures, so on the main thread).
    /// </summary>
    internal sealed class GaugeLook
    {
        // Layout, in UI units at Size 1.
        internal float BarWidth = 7f;
        internal float BarHeight = 20f;
        internal float BarGap = 2f;
        internal float Pad = 3f;
        internal float SoftHeight = 3f;
        internal float SoftGap = 2f;

        /// <summary>False: this style never draws slot letters, whatever "Show slot letters" says.</summary>
        internal bool Letters = true;

        internal Sprite Panel;
        internal Color PanelColor = Color.white;

        /// <summary>A copy of the panel, darkened, one unit lower: the drop shadow the class badges have.</summary>
        internal bool PanelShadow;

        internal Sprite Track;
        internal Color TrackColor = Color.white;

        /// <summary>How far the track reaches outside the bar. Styles with no backing use it as an outline.</summary>
        internal float TrackOutset;

        /// <summary>The gap between the bar's edge and its coloured fill.</summary>
        internal float FillInset = 1f;

        /// <summary>The dashed outline of an empty slot.</summary>
        internal Color32 Dash;

        internal Color LetterColor;
        internal bool LetterBold;

        /// <summary>Clear: no shadow under the letters.</summary>
        internal Color LetterShadow = Color.clear;
        internal Vector2 ShadowOffset = new Vector2(0f, -1f);

        private Color32 _red;
        private Color32 _amber;
        private Color32 _green;

        private static readonly Dictionary<GaugeStyle, GaugeLook> Built = new Dictionary<GaugeStyle, GaugeLook>();

        internal Color Ramp(float fraction)
        {
            return Sprites.Blend(fraction, _red, _amber, _green);
        }

        internal static GaugeLook For(GaugeStyle style)
        {
            if (!Built.TryGetValue(style, out GaugeLook look))
            {
                look = Make(style);
                Built[style] = look;
            }
            return look;
        }

        private static GaugeLook Make(GaugeStyle style)
        {
            // Softer than Classic's: on the game's grey cells, full health should be the quiet state.
            Color32 red = new Color32(196, 86, 66, 255);
            Color32 amber = new Color32(206, 168, 78, 255);
            Color32 green = new Color32(130, 160, 92, 255);
            Color32 pale = new Color32(226, 222, 206, 230);

            switch (style)
            {
                case GaugeStyle.Classic:
                    return new GaugeLook
                    {
                        Panel = Sprites.Panel,
                        Track = Sprites.Track,
                        Dash = Sprites.EmptyEdge,
                        LetterColor = Sprites.Letter,
                        _red = Sprites.Red,
                        _amber = Sprites.Amber,
                        _green = Sprites.Green
                    };

                case GaugeStyle.Bare:
                    return new GaugeLook
                    {
                        Pad = 2f,
                        Panel = Sprites.White,
                        PanelColor = Color.clear,
                        Track = Sprites.Framed(new Color32(14, 15, 15, 140), new Color32(0, 0, 0, 140)),
                        TrackOutset = 1f,
                        Dash = pale,
                        LetterColor = new Color32(226, 222, 207, 255),
                        LetterBold = true,
                        LetterShadow = new Color(0f, 0f, 0f, 0.75f),
                        ShadowOffset = new Vector2(1f, -1f),
                        _red = red,
                        _amber = amber,
                        _green = green
                    };

                case GaugeStyle.Silver:
                    return new GaugeLook
                    {
                        // The class badges' own silver, top to bottom, and their dark rim.
                        Panel = Sprites.Chip(new Color32(182, 186, 190, 255), new Color32(128, 130, 134, 255), new Color32(55, 56, 58, 255)),
                        PanelShadow = true,
                        Track = Sprites.White,
                        TrackColor = new Color32(42, 44, 46, 255),
                        Dash = new Color32(42, 44, 46, 255),
                        LetterColor = new Color32(29, 31, 33, 255),
                        LetterBold = true,
                        _red = new Color32(214, 84, 62, 255),
                        _amber = new Color32(222, 172, 64, 255),
                        _green = new Color32(128, 176, 84, 255)
                    };

                case GaugeStyle.Slim:
                    return new GaugeLook
                    {
                        BarWidth = 4f,
                        BarHeight = 24f,
                        BarGap = 3f,
                        Pad = 2f,
                        SoftHeight = 2f,
                        Letters = false,
                        Panel = Sprites.White,
                        PanelColor = Color.clear,
                        Track = Sprites.Framed(new Color32(12, 13, 13, 153), new Color32(0, 0, 0, 128)),
                        TrackOutset = 1f,
                        FillInset = 0f,
                        Dash = pale,
                        _red = red,
                        _amber = amber,
                        _green = green
                    };

                default:
                    return new GaugeLook
                    {
                        Panel = Sprites.Rounded(new Color32(10, 12, 12, 107)),
                        Track = Sprites.White,
                        TrackColor = new Color32(0, 0, 0, 97),
                        FillInset = 0f,
                        Dash = new Color32(220, 216, 200, 204),
                        LetterColor = new Color32(220, 216, 200, 255),
                        LetterShadow = new Color(0f, 0f, 0f, 0.7f),
                        _red = red,
                        _amber = amber,
                        _green = green
                    };
            }
        }
    }
}
