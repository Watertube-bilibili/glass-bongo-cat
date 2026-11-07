using System;

namespace GlassBongoCat
{
    public sealed class CatOptions
    {
        public double Size = 300;
        public double Refraction = 1.65;
        public double Thickness = 1.35;
        public double Highlight = 1.1;
        public double Clarity = .98;
        public bool InputEnabled = true;
        public bool KeyboardFacesCat = true;
        public bool ShowMouse = true;
        public bool ReducedMotion;
        public bool RecordingMode;
        public bool ClickThrough;
        public bool FollowCursor;
        public double Left = Double.NaN, Top = Double.NaN;
        internal void Validate()
        {
            Size=Clamp(Size,160,480,300); Refraction=Clamp(Refraction,.3,2.5,1.65);
            Thickness=Clamp(Thickness,.65,1.8,1.35);Highlight=Clamp(Highlight,0,2,1.1);
            Clarity=Clamp(Clarity,0,1,.98);
            if(Double.IsInfinity(Left))Left=Double.NaN;if(Double.IsInfinity(Top))Top=Double.NaN;
        }
        private static double Clamp(double n,double min,double max,double fallback)
        {return Double.IsNaN(n)||Double.IsInfinity(n)?fallback:Math.Max(min,Math.Min(max,n));}
    }
}
