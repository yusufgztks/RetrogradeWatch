using System.IO;1
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WPath = System.Windows.Shapes.Path;

namespace RetroSaat;

// ---------- Ayarlar (konum, boyut, her zaman üstte) ----------
public class Settings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Size { get; set; } = 300;
    public bool Topmost { get; set; }

    static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RetroSaat", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* ayar kaydedilemezse sorun değil */ }
    }
}

public partial class MainWindow : Window
{
    // Üstteki pivot: retrograde saat + dakika | alttaki pivot: retrograde tarih
    static readonly Point P1 = new(200, 172);
    static readonly Point P2 = new(200, 292);

    static readonly Brush Silver = Brush("#ECEBE6");
    static readonly Brush Ink = Brush("#2B2B2E");
    static readonly Brush HandBlue = Brush("#1F3A5F");

    readonly Settings _s = Settings.Load();
    readonly RotateTransform _hour = new(), _min = new(), _date = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        Width = Height = _s.Size;
        Topmost = _s.Topmost;

        var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                          SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (double.IsNaN(_s.Left) || !vs.Contains(new Point(_s.Left + 20, _s.Top + 20)))
        {
            Left = SystemParameters.WorkArea.Right - Width - 40;
            Top = SystemParameters.WorkArea.Top + 60;
        }
        else { Left = _s.Left; Top = _s.Top; }

        BuildDial();
        BuildMenu();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();

        MouseLeftButtonDown += (_, _) => { DragMove(); SavePosition(); };
        Closing += (_, _) => SavePosition();
    }

    // ---------- Yardımcılar ----------
    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    // Açı: yukarıdan saat yönünde derece
    static Point Polar(Point c, double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
    }

    T Place<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Root.Children.Add(e);
        return e;
    }

    static WPath Arc(Point c, double r, double a0, double a1, double thickness, Brush stroke)
    {
        var fig = new PathFigure { StartPoint = Polar(c, r, a0) };
        fig.Segments.Add(new ArcSegment(Polar(c, r, a1), new Size(r, r), 0,
            Math.Abs(a1 - a0) > 180,
            a1 > a0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        return new WPath { Data = new PathGeometry(new[] { fig }), Stroke = stroke, StrokeThickness = thickness };
    }

    void Tickmark(Point c, double r0, double r1, double deg, double thickness, Brush b)
    {
        var p0 = Polar(c, r0, deg);
        var p1 = Polar(c, r1, deg);
        Root.Children.Add(new Line { X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = b, StrokeThickness = thickness });
    }

    void Label(string text, Point c, double r, double deg, double size)
    {
        var p = Polar(c, r, deg);
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Georgia"),
            Foreground = Ink
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Place(tb, p.X - tb.DesiredSize.Width / 2, p.Y - tb.DesiredSize.Height / 2);
    }

    void AddHand(Point pivot, double len, double wid, RotateTransform rt)
    {
        var poly = new Polygon
        {
            Fill = HandBlue,
            Points = new PointCollection
            {
                new Point(-1.6, 14), new Point(-1.6, -len + 16), new Point(-wid, -len + 16),
                new Point(0, -len),
                new Point(wid, -len + 16), new Point(1.6, -len + 16), new Point(1.6, 14)
            },
            RenderTransform = rt,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1.5, Opacity = .4 }
        };
        Place(poly, pivot.X, pivot.Y);
        Place(new Ellipse { Width = 11, Height = 11, Fill = HandBlue }, pivot.X - 5.5, pivot.Y - 5.5);
        Place(new Ellipse { Width = 4, Height = 4, Fill = Silver }, pivot.X - 2, pivot.Y - 2);
    }

    // ---------- Kadran çizimi ----------
    void BuildDial()
    {
        var steel = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0xF6, 0xF8, 0xFA), 0),
            new GradientStop(Color.FromRgb(0x8E, 0x96, 0x9E), 0.45),
            new GradientStop(Color.FromRgb(0xE9, 0xED, 0xF0), 0.7),
            new GradientStop(Color.FromRgb(0x6F, 0x77, 0x80), 1)
        }, 45);
        var steelRev = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromRgb(0x6F, 0x77, 0x80), 0),
            new GradientStop(Color.FromRgb(0xF0, 0xF3, 0xF5), 0.5),
            new GradientStop(Color.FromRgb(0x9A, 0xA2, 0xAA), 1)
        }, 45);

        // Kasa (yastık/tonneau şekli), tokalı çerçeve, kadran
        Place(new Rectangle
        {
            Width = 380, Height = 380, RadiusX = 120, RadiusY = 120, Fill = steel,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = .5 }
        }, 10, 10);
        Place(new Rectangle { Width = 348, Height = 348, RadiusX = 104, RadiusY = 104, Fill = steelRev }, 26, 26);
        Place(new Rectangle { Width = 324, Height = 324, RadiusX = 92, RadiusY = 92, Fill = Brush("#C98F6A") }, 38, 38);

        // Güneş ışını (sunburst) desenli bakır kadran
        var rays = new Canvas
        {
            Width = 400, Height = 400,
            Clip = new RectangleGeometry(new Rect(38, 38, 324, 324), 92, 92)
        };
        var center = new Point(200, 200);
        var light = Brush("#E7B896");
        for (int i = 0; i < 72; i += 2)
        {
            rays.Children.Add(new Polygon
            {
                Fill = light,
                Points = new PointCollection { center, Polar(center, 300, i * 5), Polar(center, 300, (i + 1) * 5) }
            });
        }
        Root.Children.Add(rays);

        // Gümüş skala bantları
        var outline = Brush("#8A6A58");
        Root.Children.Add(Arc(P1, 102, -127, 127, 70, outline));
        Root.Children.Add(Arc(P1, 102, -127, 127, 66, Silver));
        Root.Children.Add(Arc(P2, 50, 305, 55, 38, outline));
        Root.Children.Add(Arc(P2, 50, 305, 55, 34, Silver));

        // Dakika skalası: 0..60, 240° yay (4°/dk)
        for (int m = 0; m <= 60; m++)
        {
            double a = -120 + m * 4;
            if (m % 5 == 0)
            {
                Tickmark(P1, 124, 134, a, 1.6, Ink);
                Label(m.ToString(), P1, 114, a, 10);
            }
            else Tickmark(P1, 128, 134, a, 0.8, Ink);
        }

        // Saat skalası: 6-7-...-12-1-...-6, 240° yay (20°/saat)
        for (int k = 0; k <= 12; k++)
        {
            int h = (6 + k) % 12;
            Label((h == 0 ? 12 : h).ToString(), P1, 86, -120 + k * 20, 17);
        }

        // Tarih alt kadranı: 1..31
        for (int d = 1; d <= 31; d++)
        {
            double a = 300 - (d - 1) * 8;
            Tickmark(P2, 60, 65, a, d % 2 == 1 ? 1.4 : 0.8, Ink);
            if (d % 2 == 1) Label(d.ToString(), P2, 47, a, 9);
        }

        // Ibreler (en üstte)
        AddHand(P2, 40, 4, _date);
        AddHand(P1, 66, 6, _hour);
        AddHand(P1, 114, 5, _min);
    }

    // ---------- Zaman ----------
    void Tick()
    {
        var n = DateTime.Now;

        double h = n.Hour % 12 + n.Minute / 60.0;          // 0..12
        Move(_hour, h >= 6 ? (h - 12) * 20 : h * 20);       // 6'da geri atlar
        Move(_min, -120 + (n.Minute + n.Second / 60.0) * 4); // saat başı geri atlar
        Move(_date, 300 - (n.Day - 1) * 8);                  // ay sonu geri atlar

        Root.ToolTip = n.ToString("HH:mm  •  dd MMMM yyyy dddd");
    }

    void Move(RotateTransform rt, double target)
    {
        bool snapBack = Math.Abs(target - rt.Angle) > 100;   // retrograde geri dönüş
        rt.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(snapBack ? 800 : 300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
    }

    // ---------- Sağ tık menüsü ----------
    void BuildMenu()
    {
        var menu = new ContextMenu();

        var top = new MenuItem { Header = "Her zaman üstte", IsCheckable = true, IsChecked = _s.Topmost };
        top.Click += (_, _) => { Topmost = _s.Topmost = top.IsChecked == true; SavePosition(); };
        menu.Items.Add(top);

        var sizeMenu = new MenuItem { Header = "Boyut" };
        foreach (var (name, size) in new[] { ("Küçük", 220.0), ("Orta", 300.0), ("Büyük", 420.0) })
        {
            var mi = new MenuItem { Header = name };
            mi.Click += (_, _) => { Width = Height = size; _s.Size = size; SavePosition(); };
            sizeMenu.Items.Add(mi);
        }
        menu.Items.Add(sizeMenu);

        var auto = new MenuItem { Header = "Windows ile başlat", IsCheckable = true, IsChecked = IsAutoStart() };
        auto.Click += (_, _) => SetAutoStart(auto.IsChecked == true);
        menu.Items.Add(auto);

        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "Kapat" };
        close.Click += (_, _) => { SavePosition(); Application.Current.Shutdown(); };
        menu.Items.Add(close);

        ContextMenu = menu;
    }

    void SavePosition()
    {
        _s.Left = Left;
        _s.Top = Top;
        _s.Save();
    }

    // ---------- Windows ile başlat ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static bool IsAutoStart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("RetroSaat") != null;
    }

    static void SetAutoStart(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue("RetroSaat", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("RetroSaat", false);
    }
}
