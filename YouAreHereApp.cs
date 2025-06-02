using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouAreHereApp
{
    // Settings configuration class
    public class ArrowSettings
    {
        public double AnimationSpeed { get; set; } = 1.0;
        public double ArrowSize { get; set; } = 1.0;
        public bool AutoStartEnabled { get; set; } = false;
        public string ArrowColor { get; set; } = "#FF0000"; // Red
    }
    
    // Arrow states
    public enum ArrowState 
    { 
        Ready,      // Normal state - responds to clicks
        Moving,     // Currently animating to target
        Parked      // Locked at position - ignores normal clicks
    }
    
    public partial class MainWindow : Window
    {
        // Win32 API imports for mouse hooks
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Class members
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelMouseProc _proc = HookCallback;
        private static MainWindow _instance;
        
        private DispatcherTimer _animationTimer;
        
        private Canvas _arrowCanvas;
        private Polygon _arrowShape;
        
        private Point _currentArrowPos;
        private Point _targetPos;
        private ArrowState _currentState = ArrowState.Ready;
        
        // Animation constants
        private const double BASE_ANIMATION_SPEED = 800.0; // pixels per second
        private const double MIN_ANIMATION_DURATION = 0.2; // minimum seconds
        private const double MAX_ANIMATION_DURATION = 2.0; // maximum seconds
        
        // Arrow size and styling
        private const double ARROW_WIDTH = 40;
        private const double ARROW_HEIGHT = 30;
        
        // System tray components
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ArrowSettings _settings;
        private string _settingsPath;

        // Animation tracking
        private DateTime _animationStartTime;
        private Point _animationStartPos;
        private double _animationDuration;
        private bool _isAnimating = false;

        public MainWindow()
        {
            _instance = this;
            SetupWindow();
            SetupArrowGraphics();
            SetupTimers();
            SetupMouseHook();
            SetupSystemTray();
            LoadSettings();
        }

        private void SetupWindow()
        {
            // Make window transparent and click-through
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            
            // Cover entire virtual screen (all monitors)
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            
            // Don't use Maximized state for multi-monitor support
            WindowState = WindowState.Normal;
        }

        private void SetupArrowGraphics()
        {
            _arrowCanvas = new Canvas();
            Content = _arrowCanvas;
            
            // Create arrow shape
            CreateArrowShape();
            
            _arrowCanvas.Children.Add(_arrowShape);
            
            // Initial position (center of screen)
            _currentArrowPos = new Point(
                SystemParameters.VirtualScreenWidth / 2, 
                SystemParameters.VirtualScreenHeight / 2
            );
            _targetPos = _currentArrowPos;
            
            UpdateArrowPosition();
        }

        private void CreateArrowShape()
        {
            _arrowShape = new Polygon();
            
            // Create arrow points (pointing right initially)
            var points = new PointCollection
            {
                new Point(0, ARROW_HEIGHT / 2),          // Left tip
                new Point(ARROW_WIDTH * 0.6, 0),         // Top right
                new Point(ARROW_WIDTH * 0.6, ARROW_HEIGHT * 0.3), // Top inner
                new Point(ARROW_WIDTH, ARROW_HEIGHT * 0.3),       // Top head
                new Point(ARROW_WIDTH, ARROW_HEIGHT * 0.7),       // Bottom head
                new Point(ARROW_WIDTH * 0.6, ARROW_HEIGHT * 0.7), // Bottom inner
                new Point(ARROW_WIDTH * 0.6, ARROW_HEIGHT),       // Bottom right
            };
            
            _arrowShape.Points = points;
            
            // Style the arrow
            var redBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            redBrush.Opacity = 0.8;
            _arrowShape.Fill = redBrush;
            
            // Add subtle stroke
            _arrowShape.Stroke = new SolidColorBrush(Color.FromRgb(200, 0, 0));
            _arrowShape.StrokeThickness = 2;
            
            // Add drop shadow effect
            var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.3,
                ShadowDepth = 3,
                BlurRadius = 5
            };
            _arrowShape.Effect = dropShadow;
            
            // Set transform origin to center for rotation
            _arrowShape.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void SetupTimers()
        {
            // Animation timer for smooth movement
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Start();
        }

        private void SetupMouseHook()
        {
            _hookID = SetHook(_proc);
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                _instance?.OnMouseActivity(wParam);
            }
            return CallNextHookEx(_instance._hookID, nCode, wParam, lParam);
        }

        private void OnMouseActivity(IntPtr wParam)
        {
            // Handle left mouse button click
            if (wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                // Get current mouse position
                GetCursorPos(out POINT point);
                var clickPos = new Point(point.x, point.y);
                
                // Check if Ctrl key is pressed
                bool isCtrlPressed = (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL = 0x11
                
                if (isCtrlPressed)
                {
                    HandleCtrlClick(clickPos);
                }
                else
                {
                    HandleNormalClick(clickPos);
                }
            }
        }

        private void HandleNormalClick(Point clickPos)
        {
            // Only respond to normal clicks if not parked
            if (_currentState != ArrowState.Parked)
            {
                MoveArrowTo(clickPos);
            }
        }

        private void HandleCtrlClick(Point clickPos)
        {
            if (_currentState == ArrowState.Parked)
            {
                // Unpark the arrow
                _currentState = ArrowState.Ready;
                UpdateArrowAppearance();
                
                // Show notification
                _notifyIcon.ShowBalloonTip(2000, "You Are Here", "Arrow unparked - now responds to clicks", ToolTipIcon.Info);
            }
            else
            {
                // Move arrow to position and park it
                MoveArrowTo(clickPos, parkAfterMove: true);
            }
        }

        private void MoveArrowTo(Point targetPos, bool parkAfterMove = false)
        {
            _targetPos = targetPos;
            _currentState = ArrowState.Moving;
            
            // Calculate animation duration based on distance
            var distance = Math.Sqrt(Math.Pow(_targetPos.X - _currentArrowPos.X, 2) + 
                                   Math.Pow(_targetPos.Y - _currentArrowPos.Y, 2));
            
            _animationDuration = Math.Max(MIN_ANIMATION_DURATION, 
                                        Math.Min(MAX_ANIMATION_DURATION, 
                                               distance / (BASE_ANIMATION_SPEED * _settings.AnimationSpeed)));
            
            _animationStartTime = DateTime.Now;
            _animationStartPos = _currentArrowPos;
            _isAnimating = true;
            
            // Calculate rotation angle for arrow to point toward target
            var angle = Math.Atan2(_targetPos.Y - _currentArrowPos.Y, _targetPos.X - _currentArrowPos.X);
            var degrees = angle * (180.0 / Math.PI);
            
            // Apply rotation
            var rotateTransform = new RotateTransform(degrees);
            _arrowShape.RenderTransform = rotateTransform;
            
            // Store park intention
            if (parkAfterMove)
            {
                // We'll park after animation completes
                _animationTimer.Tag = "park_after_move";
            }
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (_isAnimating)
            {
                var elapsed = DateTime.Now - _animationStartTime;
                var progress = Math.Min(1.0, elapsed.TotalSeconds / _animationDuration);
                
                // Smooth easing function (ease-out)
                var easedProgress = 1 - Math.Pow(1 - progress, 3);
                
                // Interpolate position
                _currentArrowPos.X = _animationStartPos.X + ((_targetPos.X - _animationStartPos.X) * easedProgress);
                _currentArrowPos.Y = _animationStartPos.Y + ((_targetPos.Y - _animationStartPos.Y) * easedProgress);
                
                UpdateArrowPosition();
                
                // Check if animation is complete
                if (progress >= 1.0)
                {
                    _isAnimating = false;
                    _currentArrowPos = _targetPos;
                    
                    // Check if we should park after this move
                    if (_animationTimer.Tag as string == "park_after_move")
                    {
                        _currentState = ArrowState.Parked;
                        UpdateArrowAppearance();
                        _notifyIcon.ShowBalloonTip(2000, "You Are Here", "Arrow parked at position", ToolTipIcon.Info);
                        _animationTimer.Tag = null;
                    }
                    else
                    {
                        _currentState = ArrowState.Ready;
                    }
                    
                    UpdateArrowAppearance();
                }
            }
        }

        private void UpdateArrowPosition()
        {
            // Convert absolute screen coordinates to canvas coordinates
            var canvasX = _currentArrowPos.X - SystemParameters.VirtualScreenLeft - (ARROW_WIDTH / 2);
            var canvasY = _currentArrowPos.Y - SystemParameters.VirtualScreenTop - (ARROW_HEIGHT / 2);
            
            Canvas.SetLeft(_arrowShape, canvasX);
            Canvas.SetTop(_arrowShape, canvasY);
        }

        private void UpdateArrowAppearance()
        {
            // Change appearance based on state
            var brush = _arrowShape.Fill as SolidColorBrush;
            if (brush != null)
            {
                switch (_currentState)
                {
                    case ArrowState.Ready:
                        brush.Opacity = 0.8;
                        break;
                    case ArrowState.Moving:
                        brush.Opacity = 0.9;
                        break;
                    case ArrowState.Parked:
                        brush.Opacity = 1.0;
                        // Add pulsing animation for parked state
                        var pulseAnimation = new DoubleAnimation(1.0, 0.6, TimeSpan.FromSeconds(1))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever
                        };
                        brush.BeginAnimation(SolidColorBrush.OpacityProperty, pulseAnimation);
                        return; // Don't override with static opacity
                }
                
                // Stop any existing animations
                brush.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Make window click-through
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }

        // Additional Win32 API for click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        protected override void OnClosed(EventArgs e)
        {
            // Clean up mouse hook
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
            }
            base.OnClosed(e);
        }

        private void SetupSystemTray()
        {
            // Initialize settings path
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var youAreHerePath = System.IO.Path.Combine(appDataPath, "YouAreHere");
            Directory.CreateDirectory(youAreHerePath);
            _settingsPath = System.IO.Path.Combine(youAreHerePath, "settings.json");
            
            // Create system tray icon
            _notifyIcon = new NotifyIcon
            {
                Icon = CreateArrowIcon(),
                Text = "You Are Here - Pointer Tool",
                Visible = true
            };
            
            // Create context menu
            CreateContextMenu();
            _notifyIcon.ContextMenuStrip = _contextMenu;
            
            // Handle left-click to show current status
            _notifyIcon.Click += (s, e) =>
            {
                if (((System.Windows.Forms.MouseEventArgs)e).Button == MouseButtons.Left)
                {
                    var statusText = $"Status: {GetCurrentStateName()}\nPosition: ({_currentArrowPos.X:F0}, {_currentArrowPos.Y:F0})\nSpeed: {_settings.AnimationSpeed:F1}x";
                    _notifyIcon.ShowBalloonTip(3000, "You Are Here", statusText, ToolTipIcon.Info);
                }
            };
        }
        
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<ArrowSettings>(json) ?? new ArrowSettings();
                }
                else
                {
                    _settings = new ArrowSettings();
                    SaveSettings();
                }
                
                // Apply loaded settings
                ApplySettings();
            }
            catch
            {
                _settings = new ArrowSettings();
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Settings save failed - continue without saving
            }
        }
        
        private void ApplySettings()
        {
            // Apply settings (animation speed is used in MoveArrowTo method)
            // Arrow size and color could be applied here if we add those settings
        }
        
        private string GetCurrentStateName()
        {
            return _currentState switch
            {
                ArrowState.Ready => "Ready",
                ArrowState.Moving => "Moving",
                ArrowState.Parked => "Parked",
                _ => "Unknown"
            };
        }
        
        private System.Drawing.Icon CreateArrowIcon()
        {
            // Create a simple arrow icon programmatically
            var bitmap = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                
                // Draw simple arrow shape
                var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
                
                // Arrow pointing right
                var points = new System.Drawing.Point[]
                {
                    new System.Drawing.Point(2, 8),   // Left tip
                    new System.Drawing.Point(8, 4),   // Top
                    new System.Drawing.Point(8, 6),   // Top inner
                    new System.Drawing.Point(14, 6),  // Top right
                    new System.Drawing.Point(14, 10), // Bottom right
                    new System.Drawing.Point(8, 10),  // Bottom inner
                    new System.Drawing.Point(8, 12)   // Bottom
                };
                
                g.FillPolygon(brush, points);
                brush.Dispose();
            }
            
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
        
        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();
            
            // Status header
            var statusItem = new ToolStripLabel("🎯 You Are Here")
            {
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };
            _contextMenu.Items.Add(statusItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            // Instructions
            var instructionsItem = new ToolStripMenuItem("📖 Instructions");
            instructionsItem.Click += (s, e) => {
                var instructions = "How to use You Are Here:\n\n" +
                                 "• Left Click: Move arrow to clicked position\n" +
                                 "• Ctrl + Left Click: Move arrow and park it\n" +
                                 "• Ctrl + Left Click (when parked): Unpark arrow\n\n" +
                                 "Perfect for live broadcasting and presentations!";
                System.Windows.Forms.MessageBox.Show(instructions, "You Are Here - Instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _contextMenu.Items.Add(instructionsItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            // Reset position
            var resetItem = new ToolStripMenuItem("🎯 Reset to Center");
            resetItem.Click += (s, e) => {
                var centerPos = new Point(
                    SystemParameters.VirtualScreenWidth / 2, 
                    SystemParameters.VirtualScreenHeight / 2
                );
                _currentState = ArrowState.Ready; // Unpark if parked
                MoveArrowTo(centerPos);
            };
            _contextMenu.Items.Add(resetItem);
            
            // Exit
            var exitItem = new ToolStripMenuItem("❌ Exit");
            exitItem.Click += (s, e) => {
                _notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            };
            _contextMenu.Items.Add(exitItem);
        }
    }

    // App.xaml.cs equivalent
    public partial class App : System.Windows.Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run(new MainWindow());
        }
    }
}
