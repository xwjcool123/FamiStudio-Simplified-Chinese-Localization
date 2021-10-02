﻿using System;
using System.IO;
using System.Threading;

using Android.Widget;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Opengl;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Content.PM;
using AndroidX.Core.View;
using AndroidX.AppCompat.App;
using AndroidX.Core.Content;
using Javax.Microedition.Khronos.Opengles;
using Google.Android.Material.BottomSheet;

using Debug        = System.Diagnostics.Debug;
using DialogResult = System.Windows.Forms.DialogResult;

namespace FamiStudio
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, ResizeableActivity = false, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    public class FamiStudioForm : AppCompatActivity, GLSurfaceView.IRenderer, GestureDetector.IOnGestureListener, ScaleGestureDetector.IOnScaleGestureListener, Choreographer.IFrameCallback
    {
        private LinearLayout linearLayout;
        private GLSurfaceView glSurfaceView;

        private FamiStudio famistudio;
        private FamiStudioControls controls;

        // For context menus.
        BottomSheetDialog contextMenuDialog;

        // For property or multi-property dialogs.
        private bool glThreadIsRunning;
        private bool glStopRequested;
        private bool glStartRequested;
        private static bool activityRunning;
        private long lastFrameTime = -1;
        private object renderLock = new object();
        private BaseDialogActivityInfo activeDialog;
        private GLControl captureControl;

        public static bool ActivityRunning => activityRunning;
        public static FamiStudioForm Instance { get; private set; }
        public BaseDialogActivityInfo ActiveDialog => activeDialog;
        public bool IsAsyncDialogInProgress => activeDialog != null;

        public FamiStudio      FamiStudio      => famistudio;
        public Toolbar         ToolBar         => controls.ToolBar;
        public Sequencer       Sequencer       => controls.Sequencer;
        public PianoRoll       PianoRoll       => controls.PianoRoll;
        public ProjectExplorer ProjectExplorer => controls.ProjectExplorer;
        public QuickAccessBar  QuickAccessBar  => controls.QuickAccessBar;
        public GLControl       ActiveControl   => controls.ActiveControl;

        public System.Drawing.Size Size => new System.Drawing.Size(glSurfaceView.Width, glSurfaceView.Height);
        public bool IsLandscape => glSurfaceView.Width > glSurfaceView.Height;
        public string Text { get; set; }

        private GestureDetectorCompat detector;
        private ScaleGestureDetector  scaleDetector;

        public FamiStudioForm()
        {
        }

        public FamiStudioForm(FamiStudio f)
        {
            Debug.Assert(false);
        }

        public System.Drawing.Rectangle Bounds
        {
            get
            {
                // DROIDTODO : Not sure about this.
                Android.Graphics.Rect rect = new Android.Graphics.Rect();
                glSurfaceView.GetDrawingRect(rect);
                return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width(), rect.Height());
            }
        }

        public void SetActiveControl(GLControl ctrl, bool animate = true)
        {
            controls.SetActiveControl(ctrl, animate);
        }

        private void EnableFullscreenMode(Window win)
        {
            // Fullscreen mode.
            win.AddFlags(WindowManagerFlags.Fullscreen);

            int uiOptions = (int)win.DecorView.SystemUiVisibility;

            uiOptions |= (int)SystemUiFlags.LowProfile;
            uiOptions |= (int)SystemUiFlags.Fullscreen;
            uiOptions |= (int)SystemUiFlags.HideNavigation;
            uiOptions |= (int)SystemUiFlags.ImmersiveSticky;

            win.DecorView.SystemUiVisibility = (StatusBarVisibility)uiOptions;
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);

            EnableFullscreenMode(Window);
            Window.ClearFlags(WindowManagerFlags.KeepScreenOn);

#if DEBUG
            Debug.Listeners.Add(new DebuggerBreakListener());
#endif

            Init.InitializeBaseSystems();

            // Only create the app once.
            var appAlreadyExists = FamiStudio.StaticInstance != null;

            if (appAlreadyExists)
                famistudio = FamiStudio.StaticInstance;
            else
                famistudio = new FamiStudio();

            glSurfaceView = new GLSurfaceView(this);
            glSurfaceView.PreserveEGLContextOnPause = true;
#if DEBUG
            glSurfaceView.DebugFlags = DebugFlags.CheckGlError;
#endif
            glSurfaceView.SetEGLContextClientVersion(1);
            glSurfaceView.SetEGLConfigChooser(8, 8, 8, 8, 0, 0);
            glSurfaceView.SetRenderer(this);
            glSurfaceView.RenderMode = Rendermode.WhenDirty;
            glThreadIsRunning = true;

            linearLayout = new LinearLayout(this);
            linearLayout.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            linearLayout.AddView(glSurfaceView);

            SetContentView(linearLayout);

            controls = new FamiStudioControls(this);

            Instance = this;

            // Simply update the form if the app already exists.
            if (appAlreadyExists)
                famistudio.SetMainForm(this);
            else
                famistudio.Initialize(null);

            detector = new GestureDetectorCompat(this, this);
            detector.IsLongpressEnabled = true;
            scaleDetector = new ScaleGestureDetector(this, this);
            scaleDetector.QuickScaleEnabled = false;

            Choreographer.Instance.PostFrameCallback(this);
            
            StartCleanCacheFolder();
        }

        private void StartCleanCacheFolder()
        {
            new Thread(() =>
            {
                var files = Directory.GetFiles(Path.GetTempPath());
                foreach (var f in files)
                    File.Delete(f);
            }).Start();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            EnableFullscreenMode(Window);
        }

        public void StartLoadFileActivityAsync(string mimeType, Action<string> callback)
        {
            Debug.Assert(activeDialog == null);
            activeDialog = new LoadDialogActivityInfo(callback);
            StopGLThread();

            Intent intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(mimeType);
            StartActivityForResult(intent, activeDialog.RequestCode);
        }

        public void StartSaveFileActivityAsync(string mimeType, string filename, Action<string> callback)
        {
            activeDialog = new SaveDialogActivityInfo(callback);
            StopGLThread();

            Intent intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(mimeType);
            intent.PutExtra(Intent.ExtraTitle, filename);
            StartActivityForResult(intent, activeDialog.RequestCode);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
        }

        public void StartPropertyDialogActivity(Action<DialogResult> callback, PropertyDialog dlg)
        {
            activeDialog = new PropertyDialogActivityInfo(dlg, callback);
            StopGLThread();
            StartActivityForResult(new Intent(this, typeof(PropertyDialogActivity)), activeDialog.RequestCode);
        }

        public void StartMultiPropertyDialogActivity(Action<DialogResult> callback, MultiPropertyDialog dlg)
        {
            activeDialog = new MultiPropertyDialogActivityInfo(dlg, callback);
            StopGLThread();
            StartActivityForResult(new Intent(this, typeof(MultiPropertyDialogActivity)), activeDialog.RequestCode);
        }

        public void StartFileSharingActivity(string filename, Action callback)
        {
            activeDialog = new ShareActivityInfo(callback);
            StopGLThread();

            var uri = FileProvider.GetUriForFile(this, "org.famistudio.fileprovider", new Java.IO.File(filename), filename); 

            Intent shareIntent = new Intent(Intent.ActionSend);
            shareIntent.SetType("*/*");
            shareIntent.PutExtra(Intent.ExtraStream, uri);
            StartActivityForResult(Intent.CreateChooser(shareIntent, "Share File"), activeDialog.RequestCode);
        }

        private void StartGLThread()
        {
            Debug.Assert(!glStartRequested && !glStopRequested);
            glStartRequested = true;
        }

        private void StopGLThread()
        {
            Debug.Assert(!glStartRequested && !glStopRequested);
            glStopRequested = true;
        }

        private void ConditionalStartGLThread()
        {
            // If not more dialog are needed, restart GL thread.
            if (activeDialog == null)
                StartGLThread();
        }

        // For debugging property pages.
        private void DialogTest()
        {
#if DEBUG
            var dlg = new PropertyDialog("Test Dialog", 200);

            dlg.Properties.AddTextBox("TextBox", "Hello1", 0, "This is a long tooltip explaining what this property is all about");
            dlg.Properties.AddColorPicker(System.Drawing.Color.Pink);
            dlg.Properties.AddButton("Hey", "This is a button", "Button tooltip");
            dlg.Properties.AddNumericUpDown("Integer", 10, 2, 50, "Integer Tooltip");
            dlg.Properties.AddDropDownList("Hey", new[] { "Option1 QQQ", "Option2 QQQ", "Option3 QQQ", "Option4 QQQ" }, "Option3 QQQ", "Dropdown tooltip");
            dlg.Properties.AddRadioButton("This is a radio", "Radio 123", false);
            dlg.Properties.AddRadioButton("This is a radio", "Radio 435", true);
            dlg.Properties.AddRadioButton("This is a radio", "Radio 888", false);
            dlg.Properties.BeginAdvancedProperties();
            dlg.Properties.AddCheckBoxList("Check box list", new[] { "Check1", "Check2", "Check3", "Check4" }, new[] { false, true, true, false });
            dlg.Properties.AddCheckBox("CheckBox1", true, "Checkbox tooltip!");
            dlg.Properties.AddSlider("Slider", 50, 0, 100, 1.0f, 2, "Allo {0} XXX", "Tooltip for slider");

            dlg.ShowDialogAsync(this, (r) =>
            {
                if (r == DialogResult.OK)
                {
                    Debug.WriteLine("Hello!");
                }
            });
#endif
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            // This happens if the activity was destroyed while inside a dialog. The main activity will 
            // have been re-created and we wont remember what we were doing. Simply ignore. 
            if (activeDialog == null)
                return;

            var dialog = activeDialog;

            if (activeDialog.IsDialogDone(resultCode))
                activeDialog = null;

            dialog.OnResult(this, resultCode, data);

            ConditionalStartGLThread();
            base.OnActivityResult(requestCode, resultCode, data);
        }

        public void FinishSaveFileActivityAsync(bool commit, Action callback)
        {
            Debug.Assert(activeDialog != null);
            var saveInfo = activeDialog as SaveDialogActivityInfo;
            Debug.Assert(saveInfo != null);
            activeDialog = null;
            saveInfo.Finish(commit, callback);
            Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            ConditionalStartGLThread();
        }

        public void DoFrame(long frameTimeNanos)
        {
            if (lastFrameTime < 0)
                lastFrameTime = frameTimeNanos;

            if (glStartRequested || glStopRequested)
            {
                Debug.Assert(glStartRequested ^ glStopRequested);

                Choreographer.Instance.RemoveFrameCallback(this);

                if (glStartRequested)
                    glSurfaceView.OnResume();
                else
                    glSurfaceView.OnPause();

                lock (renderLock) { }; // Extra safety.

                glThreadIsRunning = glStartRequested;

                if (glThreadIsRunning)
                    MarkDirty();

                glStartRequested = false;
                glStopRequested  = false;
            }

            if (glThreadIsRunning)
            {
                float deltaTime = (float)((frameTimeNanos - lastFrameTime) / 1000000000.0);

                if (deltaTime > 0.03)
                    Console.WriteLine($"FRAME SKIP!!!!!!!!!!!!!!!!!!!!!! {deltaTime}");

                lock (renderLock)
                {
                    famistudio.Tick(deltaTime);
                    controls.Tick(deltaTime);
                }

                if (controls.NeedsRedraw())
                    glSurfaceView.RequestRender();
            }

            Choreographer.Instance.PostFrameCallback(this);
            lastFrameTime = frameTimeNanos;
        }

        float x = 0.0f;

        // GL thread.
        public void OnDrawFrame(IGL10 gl)
        {
            lock (renderLock)
            {
                //gl.GlClearColor((float)Math.Sin(x), (float)Math.Cos(x * 0.5f), (float)Math.Sin(x * 0.25), 1);
                //gl.GlClear(GLES11.GlColorBufferBit);
                //x += 0.2f;
                controls.Redraw();
            }
        }

        // GL thread.
        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            controls.Resize(width, height);
        }

        // GL thread.
        public void OnSurfaceCreated(IGL10 gl, Javax.Microedition.Khronos.Egl.EGLConfig config)
        {
            controls.Resize(glSurfaceView.Width, glSurfaceView.Height);
            controls.InitializeGL();
        }

        public System.Windows.Forms.Keys GetModifierKeys()
        {
            return System.Windows.Forms.Keys.None;
        }

        public System.Drawing.Point GetCursorPosition()
        {
            return System.Drawing.Point.Empty;
        }

        public System.Drawing.Point PointToClient(System.Drawing.Point p)
        {
            return System.Drawing.Point.Empty;
        }

        public System.Drawing.Point PointToScreen(System.Drawing.Point p)
        {
            return System.Drawing.Point.Empty;
        }

        public System.Drawing.Point PointToClient(GLControl ctrl, System.Drawing.Point p)
        {
            return System.Drawing.Point.Empty;
        }

        public System.Drawing.Point PointToScreen(GLControl ctrl, System.Drawing.Point p)
        {
            return System.Drawing.Point.Empty;
        }

        public static bool IsKeyDown(System.Windows.Forms.Keys key)
        {
            return false;
        }

        public void CaptureMouse(GLControl ctrl)
        {
            Debug.Assert(captureControl == null);
            captureControl = ctrl;
        }

        public void ReleaseMouse()
        {
            captureControl = null;
        }

        public void RefreshLayout()
        {
        }

        public void MarkDirty()
        {
            controls.MarkDirty();
        }

        public void Refresh()
        {
        }

        public void RefreshCursor()
        {
        }

        public void Run()
        {
        }

        protected override void OnStop()
        {
            Debug.WriteLine("FamiStudioForm.OnStop");

            base.OnStop();
        }

        protected override void OnStart()
        {
            Debug.WriteLine("FamiStudioForm.OnStart");

            base.OnStart();

        }

        protected override void OnDestroy()
        {
            Debug.WriteLine("FamiStudioForm.OnDestroy");

            // This will stop all audio and prevent issues.
            famistudio.Suspend();
            base.OnDestroy();
        }

        protected override void OnPause()
        {
            Debug.WriteLine("FamiStudioForm.OnPause");

            Debug.Assert(activityRunning);
            activityRunning = false;

            // If we are begin stopped, but not because we are opening a dialog,
            // this likely mean the user is switching app. Let's suspend.
            // The property dialogs will handle this themselves.
            if (activeDialog == null || activeDialog.ShouldSuspend)
                famistudio.Suspend();

            famistudio.SaveWorkInProgress();

            if (glThreadIsRunning)
                glSurfaceView.OnPause();

            base.OnPause();
        }

        protected override void OnResume()
        {
            Debug.WriteLine("FamiStudioForm.OnResume");

            Debug.Assert(!activityRunning);
            activityRunning = true;

            famistudio.Resume();

            if (glThreadIsRunning)
                glSurfaceView.OnResume();

            base.OnResume();
        }

        public void ShowContextMenu(ContextMenuOption[] options)
        {
            Debug.Assert(contextMenuDialog == null);

            var bgColor = DroidUtils.ToAndroidColor(global::FamiStudio.Theme.DarkGreyFillColor1);

            var linearLayout = new LinearLayout(this);
            linearLayout.LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            linearLayout.Orientation = Orientation.Vertical;
            linearLayout.SetBackgroundColor(bgColor);

            var imagePad  = DroidUtils.DpToPixels(2);
            var imageSize = DroidUtils.DpToPixels(32);

            for (int i = 0; i < options.Length; i++)
            {
                var opt = options[i];

                var bmp = new BitmapDrawable(Resources, PlatformUtils.LoadBitmapFromResource($"FamiStudio.Resources.{opt.Image}.png", true)); // $"FamiStudio.Resources.{opt.Image}.png"
                bmp.SetBounds(0, 0, imageSize, imageSize);

                var textView = new TextView(new ContextThemeWrapper(this, Resource.Style.LightGrayTextMedium));
                textView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                textView.SetCompoundDrawables(bmp, null, null, null);
                textView.CompoundDrawablePadding = imagePad;
                textView.SetPadding(imagePad, imagePad, imagePad, imagePad);
                textView.Gravity = GravityFlags.CenterVertical;
                textView.Text = opt.Text;
                textView.Tag = new ContextMenuTag(opt);
                textView.Click += ContextMenuDialog_Click;

                linearLayout.AddView(textView);
            }

            DisplayMetrics metrics = new DisplayMetrics();
            Window.WindowManager.DefaultDisplay.GetMetrics(metrics);

            contextMenuDialog = new BottomSheetDialog(this);
            EnableFullscreenMode(contextMenuDialog.Window);
            contextMenuDialog.Window.AddFlags(WindowManagerFlags.NotFocusable); // Prevents nav bar from appearing.
            contextMenuDialog.SetContentView(linearLayout);
            contextMenuDialog.Behavior.MaxWidth = Math.Min(metrics.HeightPixels, metrics.WidthPixels);
            contextMenuDialog.Behavior.State = BottomSheetBehavior.StateExpanded;
            contextMenuDialog.DismissEvent += ContextMenuDialog_DismissEvent;

            // In portrait mode, add a bit of padding to cover the navigation bar.
            if (metrics.HeightPixels != glSurfaceView.Height)
            {
                // DROIDTODO : When using gesture navigation, in landscape more, we draw a full-width bar.
                // Needs to match the dialog.
                GradientDrawable invisible = new GradientDrawable();
                GradientDrawable navBar = new GradientDrawable();
                navBar.SetColor(bgColor);

                LayerDrawable windowBackground = new LayerDrawable(new Drawable[] { invisible, navBar });
                windowBackground.SetLayerInsetTop(1, metrics.HeightPixels);
                contextMenuDialog.Window.SetBackgroundDrawable(windowBackground);
            }

            contextMenuDialog.Show();

            PlatformUtils.VibrateClick();
        }

        private void ContextMenuDialog_DismissEvent(object sender, EventArgs e)
        {
            contextMenuDialog = null;
            MarkDirty();
        }

        private void ContextMenuDialog_Click(object sender, EventArgs e)
        {
            var tag = (sender as TextView).Tag as ContextMenuTag;

            tag.opt.Callback();
            contextMenuDialog.Dismiss();
            MarkDirty();

            PlatformUtils.VibrateTick();
        }

        int c = 0;

        private GLControl GetCapturedControlAtCoord(int formX, int formY, out int ctrlX, out int ctrlY)
        {
            if (captureControl != null)
            {
                Debug.Assert(controls.CanAcceptInput);

                ctrlX = formX - captureControl.Left;
                ctrlY = formY - captureControl.Top;
                return captureControl;
            }
            else if (controls.CanAcceptInput)
            {
                return controls.GetControlAtCoord(formX, formY, out ctrlX, out ctrlY);
            }
            else
            {
                ctrlX = 0;
                ctrlY = 0;
                return null;
            }
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            if (!IsAsyncDialogInProgress)
            {
                if (e.Action == MotionEventActions.Up)
                {
                    Debug.WriteLine($"{c++} Up {e.PointerCount} ({e.GetX()}, {e.GetY()})");
                    lock (renderLock)
                        GetCapturedControlAtCoord((int)e.GetX(), (int)e.GetY(), out var x, out var y)?.TouchUp(x, y);
                }
                else if (e.Action == MotionEventActions.Move && !scaleDetector.IsInProgress)
                {
                    Debug.WriteLine($"{c++} Move {e.PointerCount} ({e.GetX()}, {e.GetY()})");
                    lock (renderLock)
                        GetCapturedControlAtCoord((int)e.GetX(), (int)e.GetY(), out var x, out var y)?.TouchMove(x, y);
                }

                detector.OnTouchEvent(e);
                scaleDetector.OnTouchEvent(e);

                return base.OnTouchEvent(e);
            }
            else
            {
                return false;
            }
        }

        public bool OnDown(MotionEvent e)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} OnDown {e.PointerCount} ({e.GetX()}, {e.GetY()})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)e.GetX(), (int)e.GetY(), out var x, out var y)?.TouchDown(x, y);
            }
            return false;
        }

        public bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
        {
            if (!IsAsyncDialogInProgress)
            {

                Debug.WriteLine($"{c++} OnFling {e1.PointerCount} ({e1.GetX()}, {e1.GetY()}) ({velocityX}, {velocityY})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)e1.GetX(), (int)e1.GetY(), out var x, out var y)?.TouchFling(x, y, velocityX, velocityY);
            }
            return false;
        }

        public void OnLongPress(MotionEvent e)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} OnLongPress {e.PointerCount} ({e.GetX()}, {e.GetY()})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)e.GetX(), (int)e.GetY(), out var x, out var y)?.TouchLongPress(x, y);
            }
        }

        public bool OnScroll(MotionEvent e1, MotionEvent e2, float distanceX, float distanceY)
        {
            return false;
        }

        public void OnShowPress(MotionEvent e)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} {e.PointerCount} OnShowPress ({e.GetX()}, {e.GetY()})");
            }
        }

        public bool OnSingleTapUp(MotionEvent e)
        {
            if (!IsAsyncDialogInProgress)
            {
                //DialogTest();
                //StartSaveFileActivity("audio/mpeg", "Toto.mp3");

                Debug.WriteLine($"{c++} {e.PointerCount} OnSingleTapUp ({e.GetX()}, {e.GetY()})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)e.GetX(), (int)e.GetY(), out var x, out var y)?.TouchClick(x, y);
            }
            return false;
        }

        public bool OnScale(ScaleGestureDetector detector)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} OnScale ({detector.FocusX}, {detector.FocusY}) {detector.ScaleFactor}");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)detector.FocusX, (int)detector.FocusY, out var x, out var y)?.TouchScale(x, y, detector.ScaleFactor, TouchScalePhase.Scale);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool OnScaleBegin(ScaleGestureDetector detector)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} OnScaleBegin ({detector.FocusX}, {detector.FocusY})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)detector.FocusX, (int)detector.FocusY, out var x, out var y)?.TouchScale(x, y, detector.ScaleFactor, TouchScalePhase.Begin);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void OnScaleEnd(ScaleGestureDetector detector)
        {
            if (!IsAsyncDialogInProgress)
            {
                Debug.WriteLine($"{c++} OnScaleEnd ({detector.FocusX}, {detector.FocusY})");
                lock (renderLock)
                    GetCapturedControlAtCoord((int)detector.FocusX, (int)detector.FocusY, out var x, out var y)?.TouchScale(x, y, detector.ScaleFactor, TouchScalePhase.End);
            }
        }

        private class ContextMenuTag : Java.Lang.Object
        {
            public ContextMenuOption opt;
            public ContextMenuTag(ContextMenuOption o)
            {
                opt = o;
            }
        };
    }

    public abstract class BaseDialogActivityInfo
    {
        protected int requestCode;
        public int  RequestCode => requestCode;
        public virtual bool ShouldSuspend => true;
        public virtual bool IsDialogDone(Result result) => true;
        public abstract void OnResult(FamiStudioForm main, Result code, Intent data);
    };

    public class LoadDialogActivityInfo : BaseDialogActivityInfo
    {
        protected Action<string> callback;

        public LoadDialogActivityInfo(Action<string> cb)
        {
            requestCode = 1000;
            callback = cb;
        }

        public override void OnResult(FamiStudioForm main, Result code, Intent data)
        {
            if (code == Result.Ok)
            {
                var filename = (string)null;

                var c = main.ContentResolver.Query(data.Data, null, null, null);
                if (c != null && c.MoveToFirst())
                {
                    int id = c.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
                    if (id != -1)
                        filename = c.GetString(id);
                }

                if (filename != null)
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), filename);
                    var buffer = new byte[256 * 1024];

                    using (var streamIn = main.ContentResolver.OpenInputStream(data.Data))
                    {
                        using (var streamOut = File.OpenWrite(tempFile))
                        {
                            while (true)
                            {
                                var len = streamIn.Read(buffer, 0, buffer.Length);
                                if (len == 0)
                                    break;
                                streamOut.Write(buffer, 0, len);
                            }
                        }
                    }

                    callback(tempFile);
                }
            }

            main.Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }
    }

    public class SaveDialogActivityInfo : BaseDialogActivityInfo
    {
        protected Action<string> callback;
        protected string lastSaveTempFile;
        protected Android.Net.Uri lastSaveFileUri;

        public SaveDialogActivityInfo(Action<string> cb)
        {
            requestCode = 1001;
            callback = cb;
        }

        public override bool IsDialogDone(Result result) => result != Result.Ok;

        public override void OnResult(FamiStudioForm main, Result code, Intent data)
        {
            if (code == Result.Ok)
            {
                lastSaveFileUri  = data.Data;
                lastSaveTempFile = Path.GetTempFileName();

                // Save to temporary file and copy.
                callback(lastSaveTempFile);
            }
        }

        public void Finish(bool commit, Action callback)
        {
            if (commit)
            {
                if (lastSaveTempFile != null && File.Exists(lastSaveTempFile))
                {
                    Log.LogMessage(LogSeverity.Info, "Copying file to storage...");

                    var buffer = new byte[256 * 1024];

                    using (var streamIn = File.OpenRead(lastSaveTempFile))
                    {
                        using (var streamOut = FamiStudioForm.Instance.ContentResolver.OpenOutputStream(lastSaveFileUri))
                        {
                            while (true)
                            {
                                var len = streamIn.Read(buffer, 0, buffer.Length);
                                if (len == 0)
                                    break;
                                streamOut.Write(buffer, 0, len);
                            }
                        }
                    }

                    File.Delete(lastSaveTempFile);
                }
            }
            else if (lastSaveTempFile != null && File.Exists(lastSaveTempFile))
            {
                File.Delete(lastSaveTempFile);
            }

            callback();
        }
    }

    public class PropertyDialogActivityInfo : BaseDialogActivityInfo
    {
        protected PropertyDialog dialog;
        protected Action<DialogResult> callback;
        public PropertyDialog Dialog => dialog;
        public override bool ShouldSuspend => false;

        public PropertyDialogActivityInfo(PropertyDialog dlg, Action<DialogResult> cb)
        {
            requestCode = 2000;
            dialog = dlg;
            callback = cb;
        }

        public override void OnResult(FamiStudioForm main, Result code, Intent data)
        {
            callback(code == Result.Ok ? DialogResult.OK : DialogResult.Cancel);
        }
    }

    public class MultiPropertyDialogActivityInfo : BaseDialogActivityInfo
    {
        protected MultiPropertyDialog dialog;
        protected Action<DialogResult> callback;
        public MultiPropertyDialog Dialog => dialog;
        public override bool ShouldSuspend => false;

        public MultiPropertyDialogActivityInfo(MultiPropertyDialog dlg, Action<DialogResult> cb)
        {
            requestCode = 2001;
            dialog = dlg;
            callback = cb;
        }

        public override void OnResult(FamiStudioForm main, Result code, Intent data)
        {
            callback(code == Result.Ok ? DialogResult.OK : DialogResult.Cancel);
        }
    }

    public class ShareActivityInfo : BaseDialogActivityInfo
    {
        protected Action callback;

        public ShareActivityInfo(Action cb)
        {
            requestCode = 2002;
            callback = cb;
        }

        public override void OnResult(FamiStudioForm main, Result code, Intent data)
        {
            if (code == Result.Ok)
                callback();
        }
    }
#if DEBUG
    // By default Debug.Assert() doesnt break in the debugger on Android. This does that.
    public class DebuggerBreakListener : System.Diagnostics.TraceListener
    {
        bool breakOnFail = true;

        public override void Write(string message)
        {
        }

        public override void WriteLine(string message)
        {
        }

        public override void Fail(string message, string detailMessage)
        {
            base.Fail(message, detailMessage);
            
            if (breakOnFail)
                System.Diagnostics.Debugger.Break();
        }
    }
#endif
}