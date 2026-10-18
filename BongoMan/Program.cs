using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Overlay tipo Bongo Cat: 3 capas, ventana transparente y click-through.
// Si faltan PNG en la carpeta "assets", dibuja formas de prueba.
//
// PNG esperados (todos del mismo tamaño de lienzo, ej. 800x800, con alfa):
//   bg_normal.png, bg_angry.png            -> Capa 3 (fondo)
//   body_idle.png, body_left.png,
//   body_right.png, body_both.png          -> Capa 1 (cuerpo + teclado)
//   mouse_hand.png                         -> Capa 2 (mano del ratón)

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new OverlayForm());
    }
}

class OverlayForm : Form
{
    // ---------- Ajustes ----------
    const int CanvasSize = 800;          // tamaño del lienzo de los PNG
    const float Scale = 0.5f;            // tamaño en pantalla (0.5 = 400x400)
    const float EnergyPerKey = 2.0f;     // energía por pulsación
    const float EnergyDecayPerSec = 0.8f;// cuánto decae por segundo
    const float EnergyMax = 8.0f;        // energía para fondo 100% agresivo
    const float HoldSeconds = 1.2f;      // tras la última tecla, la energía se mantiene este tiempo antes de decaer
    const float SpringK = 90f;           // rigidez del resorte
    const float SpringDamping = 12f;     // amortiguación
    const float MouseRange = 80f;        // máx. desplazamiento de la mano (px del lienzo)
    const int MarginRight = 20, MarginBottom = 60;

    // ---------- Estado ----------
    readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
    readonly HashSet<Keys> down = new HashSet<Keys>();
    static readonly HashSet<Keys> LeftKeys = new HashSet<Keys>
    {
        Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.A, Keys.S, Keys.D, Keys.F, Keys.G,
        Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5,
        Keys.Tab, Keys.CapsLock, Keys.LShiftKey, Keys.LControlKey, Keys.LMenu, Keys.Escape,
        Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5
    }; // todo lo demás (incluida la barra espaciadora) cuenta como mano derecha

    float energy;
    DateTime lastKeyTime = DateTime.MinValue;
    bool anyAsset;
    PointF handPos, handVel;
    readonly Bitmap frame;
    readonly int w, h;
    readonly Timer timer = new Timer { Interval = 16 };
    DateTime last = DateTime.UtcNow;
    readonly NotifyIcon tray;

    // ---------- Hook global de teclado ----------
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    readonly HookProc hookProc;
    IntPtr hookHandle;

    public OverlayForm()
    {
        w = (int)(CanvasSize * Scale);
        h = (int)(CanvasSize * Scale);
        frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - w - MarginRight, wa.Bottom - h - MarginBottom + 60);
        Size = new Size(w, h);

        LoadImages();

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BongoOverlay",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("Salir", null, (s, e) => Close());

        hookProc = KeyboardHook;
        hookHandle = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, hookProc, GetModuleHandle(null), 0);

        timer.Tick += (s, e) => Tick();
        timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // LAYERED | TRANSPARENT (click-through) | TOPMOST | TOOLWINDOW
            cp.ExStyle |= 0x80000 | 0x20 | 0x8 | 0x80;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        if (hookHandle != IntPtr.Zero) UnhookWindowsHookEx(hookHandle);
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }

    // ---------- Entrada ----------
    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            var key = (Keys)Marshal.ReadInt32(lParam); // vkCode
            if (msg == 0x100 || msg == 0x104) // KEYDOWN / SYSKEYDOWN
            {
                if (down.Add(key)) // pulsación nueva
                    energy = Math.Min(EnergyMax * 1.25f, energy + EnergyPerKey);
                lastKeyTime = DateTime.UtcNow; // el auto-repeat (tecla mantenida) también frena el decaimiento
            }
            else if (msg == 0x101 || msg == 0x105) // KEYUP / SYSKEYUP
            {
                down.Remove(key);
            }
        }
        return CallNextHookEx(hookHandle, code, wParam, lParam);
    }

    // ---------- Lógica ----------
    void Tick()
    {
        var now = DateTime.UtcNow;
        float dt = Math.Min(0.05f, (float)(now - last).TotalSeconds);
        last = now;

        // Capa 3: la energía decae de forma constante
        // Quita teclas que Windows dejó "pegadas" (keyup perdido)
        down.RemoveWhere(k => (GetAsyncKeyState((int)k) & 0x8000) == 0);

        if ((now - lastKeyTime).TotalSeconds > HoldSeconds)
            energy = Math.Max(0f, energy - EnergyDecayPerSec * dt);

        // Capa 2: resorte hacia la posición objetivo (cursor relativo al centro de pantalla)
        Point cur; GetCursorPos(out cur);
        var scr = Screen.FromPoint(cur).Bounds;
        float nx = ((cur.X - scr.Left) - scr.Width / 2f) / (scr.Width / 2f);
        float ny = ((cur.Y - scr.Top) - scr.Height / 2f) / (scr.Height / 2f);
        var target = new PointF(Clamp(nx) * MouseRange, Clamp(ny) * MouseRange);
        handVel.X += ((target.X - handPos.X) * SpringK - handVel.X * SpringDamping) * dt;
        handVel.Y += ((target.Y - handPos.Y) * SpringK - handVel.Y * SpringDamping) * dt;
        handPos.X += handVel.X * dt;
        handPos.Y += handVel.Y * dt;

        Render();
    }

    static float Clamp(float v) { return Math.Max(-1f, Math.Min(1f, v)); }

    // ---------- Dibujo ----------
    void Render()
    {
        bool left = false, right = false;
        foreach (var k in down)
        {
            if (LeftKeys.Contains(k)) left = true; else right = true;
        }
        string bodyKey = left && right ? "body_both" : left ? "body_left" : right ? "body_right" : "body_idle";
        // raíz cuadrada: con poca energía el fondo enojado ya se nota
        float level = (float)Math.Sqrt(Math.Min(1f, energy / EnergyMax));

        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(Scale, Scale);

            // Capa 3: fondo (mezcla normal -> agresivo)
            DrawLayer(g, "bg_normal", 1f, 0);
            DrawLayer(g, "bg_angry", level, 0);

            // Capa 1: cuerpo + teclado
            DrawBody(g, bodyKey);

            // Capa 2: mano del ratón (única que se mueve)
            g.TranslateTransform(handPos.X, handPos.Y);
            DrawLayer(g, "mouse_hand", 1f, 2);
            g.ResetTransform();
        }
        Present();
    }

    void DrawLayer(Graphics g, string name, float alpha, int placeholderKind)
    {
        if (alpha <= 0.001f) return;
        Bitmap bmp;
        if (images.TryGetValue(name, out bmp))
        {
            if (alpha >= 0.999f)
            {
                g.DrawImage(bmp, 0, 0, CanvasSize, CanvasSize);
            }
            else
            {
                var cm = new ColorMatrix { Matrix33 = alpha };
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmp, new Rectangle(0, 0, CanvasSize, CanvasSize),
                        0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return;
        }
        if (!anyAsset) DrawPlaceholder(g, name, alpha); // formas de prueba solo si no hay ningún PNG
    }

    // Cuerpo con alternativas si falta algún PNG
    void DrawBody(Graphics g, string key)
    {
        if (images.ContainsKey(key)) { DrawLayer(g, key, 1f, 1); return; }
        if (key == "body_both")
        {
            // sin body_both: superpone izquierda y derecha
            bool l = images.ContainsKey("body_left"), r = images.ContainsKey("body_right");
            if (l || r)
            {
                if (l) DrawLayer(g, "body_left", 1f, 1);
                if (r) DrawLayer(g, "body_right", 1f, 1);
                return;
            }
        }
        if (key != "body_idle" && images.ContainsKey("body_idle")) { DrawLayer(g, "body_idle", 1f, 1); return; }
        DrawLayer(g, key, 1f, 1);
    }

    // Formas de prueba cuando no hay PNG
    void DrawPlaceholder(Graphics g, string name, float alpha)
    {
        int a = (int)(255 * alpha);
        if (name == "bg_normal")
            using (var b = new SolidBrush(Color.FromArgb((int)(120 * alpha), 40, 60, 90)))
                g.FillEllipse(b, 20, 20, 760, 760);
        else if (name == "bg_angry")
            using (var b = new SolidBrush(Color.FromArgb((int)(170 * alpha), 200, 30, 30)))
                g.FillEllipse(b, 20, 20, 760, 760);
        else if (name.StartsWith("body_"))
        {
            using (var body = new SolidBrush(Color.FromArgb(255, 235, 170, 90)))
            using (var paw = new SolidBrush(Color.FromArgb(255, 250, 210, 150)))
            using (var key = new SolidBrush(Color.FromArgb(255, 60, 60, 70)))
            {
                g.FillEllipse(body, 220, 180, 360, 330);        // cuerpo/cabeza
                g.FillRectangle(key, 200, 560, 400, 90);        // teclado
                bool l = name == "body_left" || name == "body_both";
                bool r = name == "body_right" || name == "body_both";
                g.FillEllipse(paw, 240, l ? 590 : 520, 100, 80); // pata izq (baja al teclear)
                g.FillEllipse(paw, 460, r ? 590 : 520, 100, 80); // pata der
            }
        }
        else if (name == "mouse_hand")
            using (var b = new SolidBrush(Color.FromArgb(255, 250, 210, 150)))
            using (var p = new Pen(Color.FromArgb(255, 120, 80, 30), 4))
            {
                g.FillEllipse(b, 600, 600, 100, 80);
                g.DrawEllipse(p, 600, 600, 100, 80);
            }
    }

    void LoadImages()
    {
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        foreach (var n in new[] { "bg_normal", "bg_angry", "body_idle", "body_left", "body_right", "body_both", "mouse_hand" })
        {
            string path = Path.Combine(dir, n + ".png");
            if (!File.Exists(path)) continue;
            using (var tmp = new Bitmap(path))
            {
                // copia a ARGB para no mantener el archivo bloqueado
                var copy = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(copy)) g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                images[n] = copy;
            }
        }
        anyAsset = images.Count > 0;
    }

    // ---------- Ventana por píxel con alfa ----------
    void Present()
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = frame.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE { cx = w, cy = h };
            var src = new POINT { x = 0, y = 0 };
            var dst = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------- Win32 ----------
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
}