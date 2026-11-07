using UnityEngine;

namespace SnowFactory.Core
{
    /// <summary>
    /// Single source of truth for the Snow Factory Art Bible palette ("Cryo-Industrial Alpine").
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
        public static readonly Color HudPanelBg = new Color(0.08f, 0.11f, 0.14f, 0.85f);
        public static readonly Color RawSnowItem = FromHex("#EFF6FC");
        public static readonly Color WoodHandle = FromHex("#8C5830");
        public static readonly Color ShovelBlade = FromHex("#4E5B69");
        public static readonly Color HearthBrick = FromHex("#4A2C23");
        public static readonly Color FireEmber = FromHex("#FF5E14");
        public static readonly Color FireFlame = FromHex("#FFB82E");

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
