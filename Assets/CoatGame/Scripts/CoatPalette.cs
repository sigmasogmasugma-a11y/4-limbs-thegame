using UnityEngine;

namespace Coat
{
    /// Every colour in the game, in one place: Frog Sqwad's palette as applied to
    /// 4 Limbs (see "The 4 Limbs Look" art direction page). Written as the sRGB
    /// hex values a designer quotes, so a swatch in the doc and a value here are
    /// the same number.
    ///
    /// The rule behind it: colour gives you away. The world, the coat and the
    /// observer are muted; the four limbs are the only loud colour on the body,
    /// so every tell shows up in colour. Goals and hazards (the van, the loot,
    /// ledges) are the only other strong colours.
    ///
    /// Characters are toon shaded (Coat/Toon), so each character colour has a
    /// shadow tone. Where the palette names one it is here; Shade() derives the
    /// rest the same way the doc did.
    public static class CoatPalette
    {
        // ---- the four limbs (Frog Sqwad's frog colours) ------------------------
        // Each keeps its old colour family so players still know their limb:
        // red stays red, blue became cyan, yellow became orange, green became lime.
        public static readonly Color RightArm = Hex(0xFE564D), RightArmShade = Hex(0xB22E27);
        public static readonly Color LeftArm  = Hex(0x57D0D9), LeftArmShade  = Hex(0x329198);
        public static readonly Color RightLeg = Hex(0xFEAF32), RightLegShade = Hex(0xB27412);
        public static readonly Color LeftLeg  = Hex(0x93DE5A), LeftLegShade  = Hex(0x619B34);

        // ---- the disguise: muted on purpose, it is trying to look normal --------
        public static readonly Color Trenchcoat = Hex(0x958D7B), TrenchcoatShade = Hex(0x676A54);
        public static readonly Color Skin = Hex(0xC79E80);
        public static readonly Color EyeWhite = Hex(0xF7F7F5);
        public static readonly Color Pupil = Hex(0x2B393D);
        /// Outlines on every character. Frog Sqwad never uses pure black.
        public static readonly Color Outline = Hex(0x2F1643);

        // ---- the observer: the audience, not a fifth player ---------------------
        public static readonly Color ObserverSuit = Hex(0x516E79), ObserverSuitShade = Hex(0x465B64);

        // ---- the street ---------------------------------------------------------
        public static readonly Color Road = Hex(0x494A4D);
        public static readonly Color Pavement = Hex(0x607084);
        public static readonly Color KerbStone = Hex(0x676A54);
        public static readonly Color Doorway = Hex(0x6F6498);
        public static readonly Color Wood = Hex(0xB4913D);
        public static readonly Color Tray = Hex(0xC7C9D4);

        // ---- goals and hazards ---------------------------------------------------
        public static readonly Color Van = Hex(0x8861D2), VanShutter = Hex(0x6748AD);
        public static readonly Color Tyre = Hex(0x25282A);
        public static readonly Color Icing = Hex(0xD867BB);
        public static readonly Color Hazard = Hex(0xEDBC40), HazardDark = Hex(0x25282A);

        // ---- menus: Frog Sqwad's logo and buttons ------------------------------
        public static readonly Color UiBackground = Hex(0x1B0F27);
        public static readonly Color UiPanel = Hex(0x2F1643);
        public static readonly Color UiRow = Hex(0x411950);
        public static readonly Color UiButton = Hex(0x9E4CF8);
        public static readonly Color UiButtonHover = Hex(0xB77BFA);
        /// Selected, switched on, "yes".
        public static readonly Color UiSelected = Hex(0x92EB3B);
        public static readonly Color UiText = Hex(0xF1ECF7);
        public static readonly Color UiMutedText = Hex(0xC2B3D6);

        public static Color Limb(CoatRole role)
        {
            switch (role)
            {
                case CoatRole.RightArm: return RightArm;
                case CoatRole.LeftArm:  return LeftArm;
                case CoatRole.RightLeg: return RightLeg;
                default:                return LeftLeg;
            }
        }

        public static Color LimbShade(CoatRole role)
        {
            switch (role)
            {
                case CoatRole.RightArm: return RightArmShade;
                case CoatRole.LeftArm:  return LeftArmShade;
                case CoatRole.RightLeg: return RightLegShade;
                default:                return LeftLegShade;
            }
        }

        /// A toon shadow tone for a colour the palette doesn't name one for:
        /// same hue, 30% darker, a little more saturated.
        public static Color Shade(Color lit)
        {
            Color.RGBToHSV(lit, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(1f, s * 1.12f), v * 0.70f);
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
