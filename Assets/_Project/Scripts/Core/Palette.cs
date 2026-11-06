using UnityEngine;

namespace SnowFactory.Core
{
    /// <summary>
    /// Single source of truth for the Snow Factory Art Bible palette ("Cryo-Industrial Alpine").
    /// Style: Clean stylized cryo-industrial facility: crisp snow surfaces, dark gunmetal steel, warm safety amber & cryo-cyan accents.
    /// Mood: Crisp sub-zero winter sunlight (#FFF0D6) with cool arctic sky bounce and deep slate shadows.
    /// </summary>
    public static class Palette
    {
        public static readonly Color SnowFloor = FromHex("#DCE8F2");
        public static readonly Color ConcreteFloor = FromHex("#2E3640");
        public static readonly Color WallSteel = FromHex("#1E242B");
        public static readonly Color TrimSlate = FromHex("#3B4654");
        public static readonly Color MachinePrimary = FromHex("#D97724");
        public static readonly Color MachineSecondary = FromHex("#8FA1B3");
        public static readonly Color CryoAccent = FromHex("#2CE5F2");
        public static readonly Color ShadowDeep = FromHex("#161C24");
        public static readonly Color KeySunlight = FromHex("#FFF0D6");
        public static readonly Color AmbientSky = FromHex("#8AB4D6");
        public static readonly Color AmbientEquator = FromHex("#4E6278");
        public static readonly Color AmbientGround = FromHex("#252B33");
        public static readonly Color FogArctic = FromHex("#B8D0E6");
        public static readonly Color GhostValid = new Color(0.17f, 0.90f, 0.95f, 0.45f);
        public static readonly Color GhostInvalid = new Color(0.95f, 0.22f, 0.22f, 0.45f);
        public static readonly Color HudPanelBg = new Color(0.08f, 0.11f, 0.14f, 0.85f);
        public static readonly Color HudSlotActive = FromHex("#2CE5F2");
        public static readonly Color HudSlotInactive = FromHex("#3B4654");
        public static readonly Color RawSnowItem = FromHex("#EFF6FC");
        public static readonly Color IceBrickItem = FromHex("#42D9F5");

        private static Color FromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
            {
                return color;
            }
            return Color.white;
        }
    }
}
