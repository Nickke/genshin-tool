// Stubs for Roslyn analysis — not part of the real build
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;

namespace InventoryKamera
{
    // Domain types
    public class Weapon { public override string ToString() => ""; }
    public class Artifact { public override string ToString() => ""; }
    public struct SubStat { public string stat; public decimal value; }

    // Inventory data container
    public enum InventoryPage { CharacterDevelopmentItems, Materials, Weapons, Artifacts, Characters }
    public class Inventory
    {
        public HashSet<Material> Materials { get; } = new HashSet<Material>();
        public List<Artifact> Artifacts { get; } = new List<Artifact>();
        public List<Weapon> Weapons { get; } = new List<Weapon>();
    }

    // Scraper base
    public abstract class InventoryScraper
    {
        protected InventoryPage inventoryPage;
        protected static readonly InventoryPage[] materialPages = new InventoryPage[0];
        protected bool StopScanning;
        protected bool SortByLevel;
        protected int ScanItemCount() => 0;
        protected (List<Rectangle>, int, int) GetPageOfItems(int page, bool acceptLess = false) => (new List<Rectangle>(), 0, 0);
        protected void SaveInventoryBitmap(Bitmap bm, string filename) { }
    }

    // Navigation static class
    public static class Navigation
    {
        public enum Speed { Normal, Fast, Slow, SelectNextInventoryItem, InventoryScroll }
        public static WindowsInput.InputSimulator sim = new WindowsInput.InputSimulator();
        public static void SetCursor(int x, int y) { }
        public static void Click() { }
        public static void Wait(int ms) { }
        public static void SystemWait(Speed speed) { }
        public static Bitmap CaptureRegion(Rectangle r) => new Bitmap(1, 1);
        public static Bitmap CaptureRegion(RECT r) => new Bitmap(1, 1);
        public static int GetWidth() => 1280;
        public static int GetHeight() => 720;
        public static Size GetAspectRatio() => new Size(16, 9);
    }

    // GenshinProcesor static class
    public static class GenshinProcesor
    {
        public static void SetGamma(double r, double g, double b, ref Bitmap bm) { }
        public static Bitmap ConvertToGrayscale(Bitmap bm) => new Bitmap(bm.Width, bm.Height);
        public static void SetInvert(ref Bitmap bm) { }
        public static string AnalyzeText(Bitmap bm, Tesseract.PageSegMode mode = Tesseract.PageSegMode.Auto) => "";
        public static string FindClosestDevelopmentName(string text) => text;
        public static string FindClosestMaterialName(string text) => text;
        public static Bitmap ResizeImage(Bitmap bm, int w, int h) => new Bitmap(w, h);
        public static void FilterColors(ref Bitmap bm, Accord.IntRange r, Accord.IntRange b, Accord.IntRange g) { }
    }

    // Properties.Settings stub matching InventoryKamera.Properties.Settings.Default pattern
    public static class Properties
    {
        public static class Settings
        {
            public static SettingsData Default { get; } = new SettingsData();
            public class SettingsData
            {
                public bool LogScreenshots { get; set; } = false;
                public string ExportPath { get; set; } = "";
                public void Save() { }
            }
        }
    }

    // RECT struct
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int Left, int Top, int Right, int Bottom) { this.Left = Left; this.Top = Top; this.Right = Right; this.Bottom = Bottom; }
        public static implicit operator Rectangle(RECT r) => new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
