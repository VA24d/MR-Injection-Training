namespace UnityEngine.XR.Templates.MR
{
    /// <summary>
    /// Fixed colour language for injection guidance (paper case study). Amber renders all
    /// guidance geometry (entry-angle cone and depth zones) the same. Green marks the optimal,
    /// in-tolerance state in the overlay and on-screen text. Yellow is reserved for warning /
    /// corrective text cues only.
    /// </summary>
    public static class InjectionGuidancePalette
    {
        /// <summary>Default guidance geometry — angle cone and depth zones.</summary>
        public static readonly Color GeometryAmber = new Color(1.00f, 0.72f, 0.18f, 0.38f);

        /// <summary>In-tolerance geometry — e.g. cone turns green once θ is within band.</summary>
        public static readonly Color GeometryOptimal = new Color(0.49f, 1.00f, 0.42f, 0.45f);

        /// <summary>On-target coaching text — maintain, hold, in-band flow.</summary>
        public static readonly Color TextOptimal = new Color(0.49f, 1.00f, 0.42f, 0.95f);

        /// <summary>Corrective coaching text — lift, insert deeper, close thumb faster, etc.</summary>
        public static readonly Color TextWarning = new Color(1.00f, 0.95f, 0.42f, 1.00f);

        /// <summary>Injection-site spot disc.</summary>
        public static readonly Color Spot = new Color(1f, 1f, 1f, 0.6f);

        public const string OptimalHex = "#7CFF6C";
        public const string WarningHex = "#FFF36A";
    }
}
