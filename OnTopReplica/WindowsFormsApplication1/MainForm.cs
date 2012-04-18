using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using OnTopReplica.Native;
using PreseriaPreview.Properties;
using OnTopReplica.StartupOptions;

using OnTopReplica.WindowSeekers;
using WindowsFormsAero.Dwm;
using WindowsFormsAero.TaskDialog;

namespace OnTopReplica {

    partial class MainForm : AspectRatioForm {

        //GUI elements
        ThumbnailPanel _thumbnailPanel;

        //Managers
        BaseWindowSeeker _windowSeeker = new TaskWindowSeeker {
            SkipNotVisibleWindows = true
        };
        MessagePumpManager _msgPumpManager = new MessagePumpManager();

        Options _startupOptions;

        public MainForm(Options startupOptions) {
            _startupOptions = startupOptions;
            
            //WinForms init pass
            InitializeComponent();

            //Store default values
            DefaultNonClickTransparencyKey = this.TransparencyKey;
            DefaultBorderStyle = this.FormBorderStyle;

            //Thumbnail panel
            _thumbnailPanel = new ThumbnailPanel {
                Location = Point.Empty,
                Dock = DockStyle.Fill
            };


            _thumbnailPanel.CloneClick += new EventHandler<CloneClickEventArgs>(Thumbnail_CloneClick);
            Controls.Add(_thumbnailPanel);
            ShowInTaskbar = false;
            TopMost = false;
            
            //Set to Key event preview
            this.KeyPreview = true;
        }

        #region Event override

        protected override void OnHandleCreated(EventArgs e){
 	        base.OnHandleCreated(e);

            //Window init
            KeepAspectRatio = false;
            GlassEnabled = true;
            GlassMargins = new Margins(-4);
            //Opacity = 255;

            //Window handlers
            _windowSeeker.OwnerHandle = this.Handle;
            _msgPumpManager.Initialize(this);

            //Platform specific form initialization
            Program.Platform.PostHandleFormInit(this);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);

            //Apply startup options
            _startupOptions.Apply(this);
            //FormBorderStyle = FormBorderStyle.None;
            //ControlBox = false;
            
        }

        protected override void OnClosing(CancelEventArgs e) {
            //Store last thumbnail, if any
            if (_thumbnailPanel.IsShowingThumbnail && CurrentThumbnailWindowHandle != null) {
                Settings.Default.RestoreLastWindowTitle = CurrentThumbnailWindowHandle.Title;
                Settings.Default.RestoreLastWindowHwnd = CurrentThumbnailWindowHandle.Handle.ToInt64();
                Settings.Default.RestoreLastWindowClass = CurrentThumbnailWindowHandle.Class;
            }

            _msgPumpManager.Dispose();
            Program.Platform.CloseForm(this);

            base.OnClosing(e);
        }

        protected override void OnMove(EventArgs e) {
            //dont call base
        }

        protected override void OnResizeEnd(EventArgs e) {
            //dont call base
        }

        protected override void OnActivated(EventArgs e) {
            base.OnActivated(e);

            //Deactivate click-through if form is reactivated
            if (ClickThroughEnabled) {
                ClickThroughEnabled = false;
            }
            if (IsChromeVisible)
            {
                IsChromeVisible = false;
            }

            Program.Platform.RestoreForm(this);
        }

        protected override void OnDeactivate(EventArgs e) {
            base.OnDeactivate(e);

            //HACK: sometimes, even if TopMost is true, the window loses its "always on top" status.
            //  This is a fix attempt that probably won't work...
            /*if (!IsFullscreen) { //fullscreen mode doesn't use TopMost
                TopMost = false;
                TopMost = true;
            }*/
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
           //dont call base
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e) {
            //dont call base
        }

        protected override void OnMouseClick(MouseEventArgs e) {
            base.OnMouseClick(e);

            //Same story as above (OnMouseDoubleClick)
            //if (e.Button == System.Windows.Forms.MouseButtons.Right) {
            //    OpenContextMenu();
            //}
        }

        protected override void WndProc(ref Message m) {
            if (_msgPumpManager != null) {
                if (_msgPumpManager.PumpMessage(ref m)) {
                    return;
                }
            }

            switch (m.Msg) {
                case WM.NCLBUTTONDBLCLK:
                    //Toggle fullscreen mode if double click on caption (whole glass area)
                    if (m.WParam.ToInt32() == HT.CAPTION) {
                        return;
                    }
                    break;
                case WM.SYSCOMMAND:
                    if (m.WParam.ToInt32() == 0xF010) // move message
                    {
                        return;
                    }
                    break;


                    case WM.NCLBUTTONDOWN:
                        if (m.WParam.ToInt32() == HT.CAPTION)
                    {
                        return;
                    }
                    break;//*/
                case WM.NCHITTEST:
                    //Make transparent to hit-testing if in click through mode
                    if (ClickThroughEnabled && (ModifierKeys & Keys.Alt) != Keys.Alt) {
                        m.Result = (IntPtr)HT.TRANSPARENT;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        #endregion


        #region Fullscreen

        bool _isFullscreen = false;
        Point _preFullscreenLocation;
        Size _preFullscreenSize;
        FormBorderStyle _preFullscreenBorderStyle;

        public bool IsFullscreen {
            get {
                return _isFullscreen;
            }
            set {
                if (IsFullscreen == value)
                    return;
                if (value && !_thumbnailPanel.IsShowingThumbnail)
                    return;

                //CloseSidePanel(); //on switch, always hide side panels

                //Location and size
                if (value) {
                    _preFullscreenLocation = Location;
                    _preFullscreenSize = ClientSize;
                    _preFullscreenBorderStyle = FormBorderStyle;

                    FormBorderStyle = FormBorderStyle.None;
                    var currentScreen = Screen.FromControl(this);
                    Size = currentScreen.WorkingArea.Size;
                    Location = currentScreen.WorkingArea.Location;
                }
                else {
                    FormBorderStyle = _preFullscreenBorderStyle;
                    Location = _preFullscreenLocation;
                    ClientSize = _preFullscreenSize;
                    RefreshAspectRatio();
                }

                //Common
                GlassEnabled = !value;
                //TopMost = !value;
                TopMost = false; //preseria keeps window topmost when needed
                HandleMouseMove = !value;

                _isFullscreen = value;

                Program.Platform.OnFormStateChange(this);
            }
        }

        #endregion

        #region Thumbnail operation

        void Thumbnail_CloneClick(object sender, CloneClickEventArgs e)
        {
            /*if (e.ClientClickLocation.X - _thumbnailPanel.Location.X < 0 ||  e.ClientClickLocation.Y - _thumbnailPanel.Location.Y < 0)
            {
                Console.WriteLine("Error out of bounds! too small" + e.ClientClickLocation.ToString());
                return;
            }
            if (_thumbnailPanel.Location.X + _thumbnailPanel.Size.Width - e.ClientClickLocation.X < 0 ||
                _thumbnailPanel.Location.Y + _thumbnailPanel.Size.Height - e.ClientClickLocation.Y < 0)
            {
                Console.WriteLine("Error out of bounds! too big" + e.ClientClickLocation.ToString());
                return;
            }*/
            Win32Helper.InjectFakeMouseClick(CurrentThumbnailWindowHandle.Handle, e);
            //CloneClickEventArgs fixedArgs = new CloneClickEventArgs(new Point(e.ClientClickLocation.X-Location.X, e., MouseButtons.Left);
            Win32Helper.MoveMouseToScreen(CurrentThumbnailWindowHandle.Handle, e);
        }

        /// <summary>
        /// Sets a new thumbnail.
        /// </summary>
        /// <param name="handle">Handle to the window to clone.</param>
        /// <param name="region">Region of the window to clone.</param>
        /// 
        public void SetThumbnail(WindowHandle handle, Rectangle? region) {
            try {
                CurrentThumbnailWindowHandle = handle;
                _thumbnailPanel.SetThumbnailHandle(handle);

#if DEBUG
                string windowClass = WindowMethods.GetWindowClass(handle.Handle);
                Console.WriteLine("Cloning window HWND {0} of class {1}.", handle.Handle, windowClass);
#endif

                if (region.HasValue)
                    _thumbnailPanel.SelectedRegion = region.Value;
                else
                    _thumbnailPanel.ConstrainToRegion = false;

                //Set aspect ratio (this will resize the form), do not refresh if in fullscreen
                SetAspectRatio(_thumbnailPanel.ThumbnailOriginalSize, !IsFullscreen);
            }
            catch (Exception ex) {
                ThumbnailError(ex, false,"Unable to create thumbnails");
            }
        }

        /// <summary>
        /// Enables group mode on a list of window handles.
        /// </summary>
        /// <param name="handles">List of window handles.</param>
        public void SetThumbnailGroup(IList<WindowHandle> handles) {
            if (handles.Count == 0)
                return;

            //At last one thumbnail
            SetThumbnail(handles[0], null);

            //Handle if no real group
            if (handles.Count == 1)
                return;

            CurrentThumbnailWindowHandle = null;
            _msgPumpManager.Get<MessagePumpProcessors.GroupSwitchManager>().EnableGroupMode(handles);
        }

        /// <summary>
        /// Disables the cloned thumbnail.
        /// </summary>
        public void UnsetThumbnail() {
            //Unset handle
            CurrentThumbnailWindowHandle = null;
            _thumbnailPanel.UnsetThumbnail();

            //Disable aspect ratio
            KeepAspectRatio = false;
        }

        /// <summary>
        /// Gets or sets the region displayed of the current thumbnail.
        /// </summary>
        public Rectangle? SelectedThumbnailRegion {
            get {
                if (!_thumbnailPanel.IsShowingThumbnail || !_thumbnailPanel.ConstrainToRegion)
                    return null;

                return _thumbnailPanel.SelectedRegion;
            }
            set {
                if (!_thumbnailPanel.IsShowingThumbnail)
                    return;

                if (value.HasValue) {
                    _thumbnailPanel.SelectedRegion = value.Value;
                    SetAspectRatio(value.Value.Size, true);
                }
                else {
                    _thumbnailPanel.ConstrainToRegion = false;
                    SetAspectRatio(_thumbnailPanel.ThumbnailOriginalSize, true);
                }

                FixPositionAndSize();
            }
        }

        const int FixMargin = 10;

        /// <summary>
        /// Fixes the form's position and size, ensuring it is fully displayed in the current screen.
        /// </summary>
        private void FixPositionAndSize() {
            var screen = Screen.FromControl(this);

            if (Width > screen.WorkingArea.Width) {
                Width = screen.WorkingArea.Width - FixMargin;
            }
            if (Height > screen.WorkingArea.Height) {
                Height = screen.WorkingArea.Height - FixMargin;
            }
            if (Location.X + Width > screen.WorkingArea.Right) {
                Location = new Point(screen.WorkingArea.Right - Width - FixMargin, Location.Y);
            }
            if (Location.Y + Height > screen.WorkingArea.Bottom) {
                Location = new Point(Location.X, screen.WorkingArea.Bottom - Height - FixMargin);
            }
        }

        private void ThumbnailError(Exception ex, bool suppress, string title) {
            if (!suppress) {
                ShowErrorDialog(title, "thumbnail error generic", ex.Message);
            }

            UnsetThumbnail();
        }

        /// <summary>Automatically sizes the window in order to accomodate the thumbnail p times.</summary>
        /// <param name="p">Scale of the thumbnail to consider.</param>
        private void FitToThumbnail(double p) {
            try {
                Size originalSize = _thumbnailPanel.ThumbnailOriginalSize;
                Size fittedSize = new Size((int)(originalSize.Width * p), (int)(originalSize.Height * p));
                ClientSize = fittedSize;
            }
            catch (Exception ex) {
                ThumbnailError(ex, false, "unable to fit thumbnail");
            }
        }


        /// <summary>
        /// Displays an error task dialog.
        /// </summary>
        /// <param name="mainInstruction">Main instruction of the error dialog.</param>
        /// <param name="explanation">Detailed informations about the error.</param>
        /// <param name="errorMessage">Expanded error codes/messages.</param>
        

        #endregion

        #region Accessors

        /// <summary>
        /// Gets the form's thumbnail panel.
        /// </summary>
        public ThumbnailPanel ThumbnailPanel {
            get {
                return _thumbnailPanel;
            }
        }

        /// <summary>
        /// Gets the form's message pump manager.
        /// </summary>
        public MessagePumpManager MessagePumpManager {
            get {
                return _msgPumpManager;
            }
        }

        /// <summary>
        /// Gets the form's window list drop down menu.
        /// </summary>
       /* public ContextMenuStrip MenuWindows {
            get {
                return menuWindows;
            }
        }*/

        /// <summary>
        /// Retrieves the window handle of the currently cloned thumbnail.
        /// </summary>
        public WindowHandle CurrentThumbnailWindowHandle {
            get;
            private set;
        }

        #endregion
        
    }
}
