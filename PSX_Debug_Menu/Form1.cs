using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Media; // Required for running lightweight background wave audio streams

namespace PSX_Debug_Menu
{
    // Form1 must be the first class in the file for the Designer to work
    public partial class Form1 : Form
    {
        static string basePath = AppDomain.CurrentDomain.BaseDirectory;

        string jsFilePath;
        string mode1Source;
        string mode2Source;
        string htmlPath;
        
        private Timer fadeTimer;
        private Timer staggeredLoadTimer; 
        private Timer masterEngineTimer; // Single master engine loop to animate all boxes globally
        private ToolTip gifToolTip; // Dynamic hover component mapping
        private Timer introTimer; // Timer to handle the 7-second intro window
        private PictureBox introDisplayBox; // Dedicated control to handle and render the native GIF animation loop smoothly

        private int currentLoadingIndex = 0; 
        private bool isSequenceLoading = false; 
        
        private bool isFadingOut = false;
        private bool transitioningToDebug = true; 
        private Image debugMenuBg;
        private Image mainMenuBg;
        private Image introGif; // Holds reference to the startup intro GIF
        private bool inMainMenu = true;
        private bool isShowingIntro = true; // State tracker to isolate the intro sequence
        
        private bool isProcessingClick = false;

        // Separate lists to track active selections for Mode 1 and Mode 2 globally
        private List<PictureBox> selectedGifButtonsMode1 = new List<PictureBox>();
        private List<PictureBox> selectedGifButtonsMode2 = new List<PictureBox>();

        int selectedInterval = 5000;

        private int topGrowX = 36;
        private int topGrowY = 6;
        private int mainGrowX = 12; 
        private int mainGrowY = 6;
        private int timerGrowX = 5;
        private int timerGrowY = 5;

        // Arrays to manage sequential loading systematically
        private PictureBox[] mode1Gifs;
        private PictureBox[] mode2Gifs;
        
        // Dictionary tracking caches to optimize graphic processing lookups
        private Dictionary<string, GifTracker> gifMetadataCache = new Dictionary<string, GifTracker>();

        [StructLayout(LayoutKind.Sequential)]
        public struct IconInfo
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr CreateIconIndirect(ref IconInfo icon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetIconInfo(IntPtr hIcon, ref IconInfo pIconInfo);

        public Form1()
        {
            InitializeComponent();
            
            jsFilePath = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "PSX Player.js");
            htmlPath = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "PSX Player.html");
            mode1Source = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "Modes", "Mode 1 Full.js");
            mode2Source = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "Modes", "Mode 2 Full.js");

            // Initialize the matching array maps for sequential load processing
            mode1Gifs = new PictureBox[] {
                Mode_1_Radar, Mode_1_Bumping_Disks, Mode_1_The_Spire, Mode_1_Pulse_Tower, Mode_1_Particle_Storm,
                Mode_1_Solar_Flare, Mode_1_Phasing_Shards, Mode_1_Electric_Serpent, Mode_1_Fusion_Thunderball, Mode_1_S_Curve,
                Mode_1_O_Scope, Mode_1_Ridging_Magma, Mode_1_Star_Strobe, Mode_1_Tangled_Distortion, Mode_1_Spiky_Ember
            };

            mode2Gifs = new PictureBox[] {
                Mode_2_Radar, Mode_2_Bumping_Disks, Mode_2_The_Spire, Mode_2_Pulse_Tower, Mode_2_Particle_Storm,
                Mode_2_Solar_Flare, Mode_2_Phasing_Shards, Mode_2_Electric_Serpent, Mode_2_Fusion_Thunderball, Mode_2_S_Curve,
                Mode_2_O_Scope, Mode_2_Ridging_Magma, Mode_2_Star_Strobe, Mode_2_Tangled_Distortion, Mode_2_Spiky_Ember
            };

            SetupFadeSystem();
            SetupStaggeredLoader();
            SetupMasterEngineTimer();
            SetupToolTips();
            SetupUI();
            SetupHoverEffects(this);
            SetupIntroTimer();
            
            SetMenuStateImmediate(true);

            // INITIAL Startup Configuration: Force window to 0% opacity so it fades in elegantly on Boot.wav execution
            this.Opacity = 0.0;
        }

        private void SetupFadeSystem()
        {
            fadeTimer = new Timer();
            fadeTimer.Interval = 20; 
            fadeTimer.Tick += FadeTimer_Tick;
        }

        private void SetupStaggeredLoader()
        {
            staggeredLoadTimer = new Timer();
            staggeredLoadTimer.Interval = 2000; // 2 seconds between every loaded pair
            staggeredLoadTimer.Tick += StaggeredLoadTimer_Tick;
        }

        private void SetupMasterEngineTimer()
        {
            masterEngineTimer = new Timer();
            masterEngineTimer.Interval = 33; // Fixed 30 FPS tick updates to prevent layout thread strain
            masterEngineTimer.Tick += MasterEngineTimer_Tick;
        }

        private void SetupToolTips()
        {
            gifToolTip = new ToolTip();
            gifToolTip.InitialDelay = 150; // Snappy visual delivery
            gifToolTip.ReshowDelay = 100;
            gifToolTip.ShowAlways = true;
        }

        private void SetupIntroTimer()
        {
            introTimer = new Timer();
            introTimer.Interval = 6500; // Exact 7-second runtime duration loop
            introTimer.Tick += IntroTimer_Tick;
        }

        private void SetupUI()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen; // Fixed alignment type vector assignment mutation here

            try {
                string debugPath = Path.Combine(basePath, "PSX PLAYER", "Debug Menu", "PSX Player Debug Menu.png");
                string mainPath = Path.Combine(basePath, "PSX PLAYER", "Main Menu", "PSX Player Main Menu.png");
                string introPath = Path.Combine(basePath, "PSX PLAYER", "PSX Misc", "PSX Intro.gif");

                if (File.Exists(debugPath)) debugMenuBg = Image.FromFile(debugPath);
                if (File.Exists(mainPath)) mainMenuBg = Image.FromFile(mainPath);

                // Set up initial black solid background layer
                this.BackColor = Color.Black;
                this.BackgroundImage = null;

                if (File.Exists(introPath))
                {
                    introGif = Image.FromFile(introPath);

                    // Dynamic picture box generation forces clean, optimized GIF hardware frames playback rendering
                    introDisplayBox = new PictureBox();
                    introDisplayBox.Dock = DockStyle.Fill;
                    introDisplayBox.Image = introGif;
                    introDisplayBox.SizeMode = PictureBoxSizeMode.StretchImage;
                    introDisplayBox.BackColor = Color.Black;
                    this.Controls.Add(introDisplayBox);
                    introDisplayBox.BringToFront();
                }

                string cursorPath = Path.Combine(basePath, "PSX PLAYER", "PSX Misc", "Pointer.png");
                if (File.Exists(cursorPath))
                {
                    using (Bitmap cursorBmp = (Bitmap)Image.FromFile(cursorPath))
                    {
                        this.Cursor = CreateCursor(cursorBmp, 0, 0); 
                    }
                }
            } catch { }
        }

        private void SetupHoverEffects(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (ctrl is PictureBox pb)
                {
                    // Skip the dynamic introductory viewport control container entirely
                    if (pb == introDisplayBox) continue;

                    pb.SizeMode = PictureBoxSizeMode.StretchImage;
                    pb.BackColor = Color.Transparent;

                    // Make sure "Mode_1_All" (your regular PNG button) is ignored by the GIF engine hook
                    if (pb.Name.StartsWith("Mode_1_") && pb.Name != "Mode_1_All")
                    {
                        pb.Tag = new ButtonState { OriginalSize = pb.Size, OriginalLocation = pb.Location };
                        pb.Click += GifButton_Click; 
                        pb.Paint += GifButton_Paint; 
                        pb.MouseEnter += GifButton_MouseEnter; // Bind popup hover checker
                        continue; 
                    }
                    
                    // Make sure "Mode_2_All" (your regular PNG button) is ignored by the GIF engine hook
                    if (pb.Name.StartsWith("Mode_2_") && pb.Name != "Mode_2_All")
                    {
                        pb.Tag = new ButtonState { OriginalSize = pb.Size, OriginalLocation = pb.Location };
                        pb.Click += GifButtonMode2_Click; 
                        pb.Paint += GifButton_Paint; 
                        pb.MouseEnter += GifButton_MouseEnter; // Bind popup hover checker
                        continue;
                    }

                    if (pb.Image != null && pb.Tag == null)
                    {
                        pb.Tag = new ButtonState { 
                            OriginalImage = pb.Image, 
                            OriginalSize = pb.Size, 
                            OriginalLocation = pb.Location 
                        }; 

                        pb.MouseEnter += Button_MouseEnter;
                        pb.MouseLeave += Button_MouseLeave;
                    }
                }
                if (ctrl.HasChildren) SetupHoverEffects(ctrl);
            }
        }

        private void GifButton_MouseEnter(object sender, EventArgs e)
        {
            if (isSequenceLoading || inMainMenu || isShowingIntro) return;

            PictureBox pb = (PictureBox)sender;
            string cleanName = pb.Name.Replace("Mode_1_", "").Replace("Mode_2_", "").Replace("_", " ");
            gifToolTip.SetToolTip(pb, cleanName);
        }

        // --- MODE 1 LOGIC ---
        private void GifButton_Click(object sender, EventArgs e)
        {
            if (isSequenceLoading || isShowingIntro) return; 
            ProcessGifSelection((PictureBox)sender, "Mode 1 Select.js", "Mode 1 Select", selectedGifButtonsMode1, true);
        }

        // --- MODE 2 LOGIC ---
        private void GifButtonMode2_Click(object sender, EventArgs e)
        {
            if (isSequenceLoading || isShowingIntro) return; 
            ProcessGifSelection((PictureBox)sender, "Mode 2 Select.js", "Mode 2 Select", selectedGifButtonsMode2, false);
        }

        // --- REBUILD-DRIVEN PROCESSOR ---
        private void ProcessGifSelection(PictureBox pb, string templateName, string samplesFolder, List<PictureBox> trackerList, bool isMode1)
        {
            string libraryDir = Path.Combine(basePath, "PSX PLAYER", "Player Menu");
            string modesDir = Path.Combine(libraryDir, "Modes");
            string sourceTemplate = Path.Combine(modesDir, templateName);
            string destinationJs = Path.Combine(libraryDir, "PSX Player.js");

            try
            {
                if (trackerList.Contains(pb))
                {
                    trackerList.Remove(pb);
                }
                else
                {
                    string specificSampleCheck = Path.Combine(modesDir, samplesFolder, pb.Name + ".js");
                    if (File.Exists(specificSampleCheck))
                    {
                        trackerList.Add(pb);
                    }
                    else
                    {
                        MessageBox.Show("Sample File Not Found:\n" + specificSampleCheck, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                if (isMode1) selectedGifButtonsMode2.Clear();
                else selectedGifButtonsMode1.Clear();

                if (File.Exists(sourceTemplate))
                {
                    File.Copy(sourceTemplate, destinationJs, true);
                }
                else
                {
                    MessageBox.Show("Template Missing: " + sourceTemplate, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string playerCode = File.ReadAllText(destinationJs);

                foreach (var activeBox in trackerList)
                {
                    string activeSamplePath = Path.Combine(modesDir, samplesFolder, activeBox.Name + ".js");
                    if (File.Exists(activeSamplePath))
                    {
                        string sampleCode = File.ReadAllText(activeSamplePath);
                        string startMarker = $"// Start {activeBox.Name}";
                        string endMarker = $"// End {activeBox.Name}";
                        string pattern = $@"{startMarker}.*?{endMarker}";

                        playerCode = Regex.Replace(playerCode, pattern, sampleCode, RegexOptions.Singleline);
                    }
                }

                File.WriteAllText(destinationJs, playerCode);

                foreach (var item in mode1Gifs) item.Refresh();
                foreach (var item in mode2Gifs) item.Refresh();
            }
            catch (Exception ex) { MessageBox.Show("Error processing code alignment: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // --- UPDATED SOFT OUTER GLOW RENDER LOGIC ---
        private void GifButton_Paint(object sender, PaintEventArgs e)
        {
            PictureBox pb = (PictureBox)sender;
            if (selectedGifButtonsMode1.Contains(pb) || selectedGifButtonsMode2.Contains(pb))
            {
                // Enable anti-aliasing for smooth alpha transitions
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Step-down transparency vectors to simulate a radiant backglow drop
                int[] alphaLevels = { 180, 110, 60, 25 }; 
                float baseThickness = 1f; 

                for (int i = 0; i < alphaLevels.Length; i++)
                {
                    using (Pen glowPen = new Pen(Color.FromArgb(alphaLevels[i], Color.White), baseThickness + (i * 1)))
                    {
                        // Inset rectangles sequentially to expand the light scatter area perfectly
                        int offset = i + 1;
                        e.Graphics.DrawRectangle(glowPen, offset, offset, pb.Width - (offset * 2), pb.Height - (offset * 2));
                    }
                }
            }
        }

        private async Task ProcessClickEffect(PictureBox pb, bool isTransitioning = false)
        {
            if (isProcessingClick) return;
            isProcessingClick = true; 
            pb.Enabled = false;

            ButtonState state = (ButtonState)pb.Tag;
            Image original = state.OriginalImage;

            if (original == null)
            {
                await Task.Delay(200);
                if (!isTransitioning) pb.Enabled = true;
                return;
            }

            Size grownSize = pb.Size;
            Point grownLocation = pb.Location;

            for (float brightness = 1.0f; brightness >= 0.2f; brightness -= 0.1f)
            {
                Bitmap frame = new Bitmap(original.Width, original.Height);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    ColorMatrix matrix = new ColorMatrix(new float[][]
                    {
                        new float[] {brightness, 0, 0, 0, 0},
                        new float[] {0, brightness, 0, 0, 0},
                        new float[] {0, 0, brightness, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });

                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(matrix);
                        g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                pb.Image = frame;
                pb.Size = grownSize;
                pb.Location = grownLocation;
                await Task.Delay(30); 
            }

            await Task.Delay(200);
            if (isTransitioning) return; 
            pb.Enabled = true;
        }

        private void FinishButtonReset(PictureBox pb)
        {
            if (pb.Tag is ButtonState state && state.OriginalImage != null)
            {
                pb.Image = state.OriginalImage;
                pb.Size = state.OriginalSize;
                pb.Location = state.OriginalLocation;
            }
            
            isProcessingClick = false;

            if (pb.ClientRectangle.Contains(pb.PointToClient(Control.MousePosition)))
            {
                Button_MouseEnter(pb, EventArgs.Empty);
            }
        }

        private (int x, int y) GetGrowthValues(string name)
        {
            if (name == "Settings" || name == "PSX_Player_1") return (topGrowX, topGrowY);
            if (name.StartsWith("Mode_Timer")) return (timerGrowX, timerGrowY);
            return (mainGrowX, mainGrowY);
        }

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            if (isProcessingClick || isSequenceLoading || isShowingIntro) return; 
            if (sender is PictureBox pb && pb.Tag is ButtonState state && state.OriginalImage != null)
            {
                var grow = GetGrowthValues(pb.Name);
                pb.Size = new Size(state.OriginalSize.Width + grow.x, state.OriginalSize.Height + grow.y);
                pb.Location = new Point(state.OriginalLocation.X - (grow.x / 2), state.OriginalLocation.Y - (grow.y / 2));
                pb.BringToFront(); 
            }
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            if (isProcessingClick || isSequenceLoading || isShowingIntro) return; 
            if (sender is PictureBox pb && pb.Tag is ButtonState state && state.OriginalImage != null)
            {
                pb.Size = state.OriginalSize;
                pb.Location = state.OriginalLocation;
            }
        }

        private async void Settings_Click(object sender, EventArgs e)
        {
            if (inMainMenu && !isFadingOut && !isShowingIntro)
            {
                await ProcessClickEffect((PictureBox)sender, true); 
                transitioningToDebug = true;
                isFadingOut = true;
                fadeTimer.Start();
            }
        }

        private async void ExitToMenu_Click(object sender, EventArgs e)
        {
            if (!inMainMenu && !isFadingOut && !isShowingIntro)
            {
                masterEngineTimer.Stop();
                ClearAllGifs();

                await ProcessClickEffect((PictureBox)sender, true); 
                transitioningToDebug = false;
                isFadingOut = true;
                fadeTimer.Start();
            }
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            if (isFadingOut)
            {
                if (this.Opacity > 0.0) { this.Opacity -= 0.05; }
                else
                {
                    isFadingOut = false;
                    ResetAllButtonsToZero(this);
                    isProcessingClick = false; 

                    if (isShowingIntro)
                    {
                        isShowingIntro = false;

                        // Safely dismantle the dynamic intro viewport block container to prevent parameter thread collisions
                        if (introDisplayBox != null)
                        {
                            this.Controls.Remove(introDisplayBox);
                            introDisplayBox.Image = null;
                            introDisplayBox.Dispose();
                            introDisplayBox = null;
                        }

                        if (introGif != null)
                        {
                            introGif.Dispose();
                            introGif = null;
                        }

                        // Apply Main Menu properties seamlessly while target layers are completely transparent
                        this.BackgroundImage = mainMenuBg;
                        this.BackgroundImageLayout = ImageLayout.Stretch;
                        SetMenuStateStaticButtons(true);
                    }
                    else if (transitioningToDebug)
                    {
                        inMainMenu = false;
                        this.BackgroundImage = debugMenuBg;
                        SetMenuStateStaticButtons(false);
                    }
                    else
                    {
                        staggeredLoadTimer.Stop(); 
                        masterEngineTimer.Stop(); 
                        isSequenceLoading = false;
                        inMainMenu = true;
                        this.BackgroundImage = mainMenuBg;
                        SetMenuStateStaticButtons(true);
                    }
                }
            }
            else
            {
                if (this.Opacity < 1.0) { this.Opacity += 0.05; }
                else 
                { 
                    fadeTimer.Stop(); 
                    if (isShowingIntro)
                    {
                        // Kick-start intro timing countdown loop once fully opaque
                        introTimer.Start();
                    }
                    else if (!inMainMenu)
                    {
                        currentLoadingIndex = 0;
                        isSequenceLoading = true; 
                        staggeredLoadTimer.Start();
                    }
                }
            }
        }

        private void IntroTimer_Tick(object sender, EventArgs e)
        {
            introTimer.Stop();
            
            // Trigger fading sequence phase immediately. Hardware cleanup is safely postponed until Opacity hits 0.
            isFadingOut = true;
            fadeTimer.Start();
        }

        private void ResetAllButtonsToZero(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                if (ctrl is PictureBox pb && pb.Tag is ButtonState state)
                {
                    if ((pb.Name.StartsWith("Mode_1_") && pb.Name != "Mode_1_All") || 
                        (pb.Name.StartsWith("Mode_2_") && pb.Name != "Mode_2_All"))
                        continue;

                    pb.Image = state.OriginalImage;
                    pb.Size = state.OriginalSize;
                    pb.Location = state.OriginalLocation;
                    pb.Enabled = true;
                }
                if (ctrl.HasChildren) ResetAllButtonsToZero(ctrl);
            }
        }

        private void SetMenuStateStaticButtons(bool isMainMenuActive)
        {
            // If the introductory sequence is playing, keep all controls completely invisible
            if (isShowingIntro)
            {
                Settings.Visible = false;
                PSX_Player_1.Visible = false;
                Exit.Visible = false;
                Mode_1_All.Visible = false;
                Mode_2_All.Visible = false;
                Patch.Visible = false;
                PSX_Player_2.Visible = false;
                Mode_Timer_1.Visible = false;
                Mode_Timer_2.Visible = false;
                Mode_Timer_3.Visible = false;
                Mode_Timer_4.Visible = false;

                foreach (var pb in mode1Gifs) pb.Visible = false;
                foreach (var pb in mode2Gifs) pb.Visible = false;
                return;
            }

            Settings.Visible = isMainMenuActive;
            PSX_Player_1.Visible = isMainMenuActive;

            Exit.Visible = !isMainMenuActive;
            Mode_1_All.Visible = !isMainMenuActive;
            Mode_2_All.Visible = !isMainMenuActive;
            Patch.Visible = !isMainMenuActive;
            PSX_Player_2.Visible = !isMainMenuActive;
            Mode_Timer_1.Visible = !isMainMenuActive;
            Mode_Timer_2.Visible = !isMainMenuActive;
            Mode_Timer_3.Visible = !isMainMenuActive;
            Mode_Timer_4.Visible = !isMainMenuActive;

            if (isMainMenuActive)
            {
                ClearAllGifs();
            }
            else
            {
                foreach (var pb in mode1Gifs) pb.Visible = false;
                foreach (var pb in mode2Gifs) pb.Visible = false;
            }
        }

        private void StaggeredLoadTimer_Tick(object sender, EventArgs e)
        {
            if (currentLoadingIndex >= 15)
            {
                staggeredLoadTimer.Stop(); 
                isSequenceLoading = false; 
                
                masterEngineTimer.Start();
                return;
            }

            PrecacheAndDrawFrame(mode1Gifs[currentLoadingIndex], "Mode 1 Select");
            PrecacheAndDrawFrame(mode2Gifs[currentLoadingIndex], "Mode 2 Select");

            currentLoadingIndex++;
            Application.DoEvents(); 
        }

        private void PrecacheAndDrawFrame(PictureBox pb, string folder)
        {
            string filePath = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "Modes", folder, pb.Name + ".gif");
            if (File.Exists(filePath))
            {
                if (!gifMetadataCache.ContainsKey(pb.Name))
                {
                    Image diskImage = Image.FromFile(filePath);
                    FrameDimension dimension = new FrameDimension(diskImage.FrameDimensionsList[0]);
                    int totalFrames = diskImage.GetFrameCount(dimension);

                    gifMetadataCache[pb.Name] = new GifTracker {
                        GifImage = diskImage,
                        Dimension = dimension,
                        FrameCount = totalFrames,
                        CurrentFrameIndex = 0
                    };
                }

                UpdateBoxFrame(pb);
            }
            pb.Visible = true;
        }

        private void UpdateBoxFrame(PictureBox pb)
        {
            if (gifMetadataCache.TryGetValue(pb.Name, out GifTracker tracker))
            {
                tracker.GifImage.SelectActiveFrame(tracker.Dimension, tracker.CurrentFrameIndex);
                
                // FIXED: Force the drawn layout boundaries to match the PictureBox UI sizes perfectly
                Bitmap frameClone = new Bitmap(pb.Width, pb.Height);
                using (Graphics g = Graphics.FromImage(frameClone))
                {
                    g.DrawImage(tracker.GifImage, 0, 0, pb.Width, pb.Height);
                }

                if (pb.Image != null) pb.Image.Dispose();
                pb.Image = frameClone;
            }
        }

        private void MasterEngineTimer_Tick(object sender, EventArgs e)
        {
            foreach (var pb in mode1Gifs) AdvanceCachedFrame(pb);
            foreach (var pb in mode2Gifs) AdvanceCachedFrame(pb);
        }

        private void AdvanceCachedFrame(PictureBox pb)
        {
            if (pb.Visible && gifMetadataCache.TryGetValue(pb.Name, out GifTracker tracker))
            {
                tracker.CurrentFrameIndex = (tracker.CurrentFrameIndex + 1) % tracker.FrameCount;
                UpdateBoxFrame(pb);
            }
        }

        private void ClearAllGifs()
        {
            foreach (var pb in mode1Gifs) HideAndClearBox(pb);
            foreach (var pb in mode2Gifs) HideAndClearBox(pb);

            foreach (var kvp in gifMetadataCache)
            {
                kvp.Value.GifImage.Dispose();
            }
            gifMetadataCache.Clear();

            selectedGifButtonsMode1.Clear();
            selectedGifButtonsMode2.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void HideAndClearBox(PictureBox pb)
        {
            pb.Visible = false;
            if (pb.Image != null)
            {
                pb.Image.Dispose();
                pb.Image = null;
            }
            gifToolTip.SetToolTip(pb, null);
        }

        private void SetMenuStateImmediate(bool isMainMenuActive)
        {
            SetMenuStateStaticButtons(isMainMenuActive);
        }

        public static Cursor CreateCursor(Bitmap bmp, int xHotSpot, int yHotSpot)
        {
            IntPtr ptr = bmp.GetHicon();
            IconInfo tmp = new IconInfo();
            GetIconInfo(ptr, ref tmp);
            tmp.xHotspot = xHotSpot;
            tmp.yHotspot = yHotSpot;
            tmp.fIcon = false; 
            ptr = CreateIconIndirect(ref tmp);
            return new Cursor(ptr);
        }

        private void TimerButton_Click(object sender, EventArgs e)
        {
            if (isShowingIntro) return;
            PictureBox pb = (PictureBox)sender;
            
            if (pb.Name == "Mode_Timer_1") selectedInterval = 5000;
            else if (pb.Name == "Mode_Timer_2") selectedInterval = 10000;
            else if (pb.Name == "Mode_Timer_3") selectedInterval = 15000;
            else if (pb.Name == "Mode_Timer_4") selectedInterval = 20000;
            
            MessageBox.Show($"Mode Timer set to {selectedInterval / 1000} sec", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FinishButtonReset(pb);
        }

        public async void Mode1_Click(object sender, EventArgs e) {
            if (isShowingIntro) return;
            PictureBox pb = (PictureBox)sender;
            await ProcessClickEffect(pb, false);
            FileOperation(mode1Source, "Mode 1 Copied Successfully!");
            FinishButtonReset(pb);
        }

        public async void Mode2_Click(object sender, EventArgs e) {
            if (isShowingIntro) return;
            PictureBox pb = (PictureBox)sender;
            await ProcessClickEffect(pb, false);
            FileOperation(mode2Source, "Mode 2 Copied Successfully!");
            FinishButtonReset(pb);
        }

        private void FileOperation(string source, string successMsg)
        {
            try {
                if (File.Exists(source)) {
                    File.Copy(source, jsFilePath, true);
                    MessageBox.Show(successMsg, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    selectedGifButtonsMode1.Clear();
                    selectedGifButtonsMode2.Clear();
                    
                    foreach (var item in mode1Gifs) item.Refresh();
                    foreach (var item in mode2Gifs) item.Refresh();
                } else MessageBox.Show($"Error: Source file not found.\nPath: {source}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } catch (Exception ex) { MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        public async void Patch_Click(object sender, EventArgs e)
        {
            if (isShowingIntro) return;
            PictureBox pb = (PictureBox)sender;
            await ProcessClickEffect(pb, false);
            
            if (!File.Exists(jsFilePath)) {
                MessageBox.Show("Error: Mode(s) in PSX Player not found.\nSelect at least 1 mode before patching.", "Missing Selection", MessageBoxButtons.OK, MessageBoxIcon.Error);
                FinishButtonReset(pb);
                return;
            }

            string content = "";
            try {
                content = File.ReadAllText(jsFilePath);
            } catch (Exception ex) { 
                MessageBox.Show("Error reading target script: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); 
                FinishButtonReset(pb);
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                RenderStatusOverlay("Error.png");
                MessageBox.Show("Patch Error: The source JavaScript runtime file is completely empty.\nPlease re-select your desired engine mode parameters to rewrite the configuration block.", "Runtime Compilation Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                FinishButtonReset(pb);
                return;
            }

            // Read template select files to compare configuration status
            string selectTemplate1Path = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "Modes", "Mode 1 Select.js");
            string selectTemplate2Path = Path.Combine(basePath, "PSX PLAYER", "Player Menu", "Modes", "Mode 2 Select.js");

            bool isUntouchedTemplate1 = File.Exists(selectTemplate1Path) && content.Trim() == File.ReadAllText(selectTemplate1Path).Trim();
            bool isUntouchedTemplate2 = File.Exists(selectTemplate2Path) && content.Trim() == File.ReadAllText(selectTemplate2Path).Trim();

            // Error trigger logic: Tracker lists are empty AND the file is either entirely unmodified template base content or lacks markers
            bool lacksMarkers = !content.Contains("// Start Mode_1_") && !content.Contains("// Start Mode_2_");
            bool isEmptySelection = selectedGifButtonsMode1.Count == 0 && selectedGifButtonsMode2.Count == 0;

            if (isEmptySelection && (isUntouchedTemplate1 || isUntouchedTemplate2 || lacksMarkers))
            {
                RenderStatusOverlay("Error.png");
                MessageBox.Show("Error: Mode(s) in PSX Player not found.\nSelect at least 1 mode before patching.", "Missing Selection", MessageBoxButtons.OK, MessageBoxIcon.Error);
                FinishButtonReset(pb);
                return;
            }

            try {
                content = Regex.Replace(content, @"\}, \d+\);", $"}}, {selectedInterval});");
                File.WriteAllText(jsFilePath, content);

                string updatedContent = File.ReadAllText(jsFilePath);
                Match match = Regex.Match(updatedContent, @"\}, (\d+)\);");
                int modeNumber = 1;
                if (match.Success) {
                    modeNumber = int.Parse(match.Groups[1].Value) / 5000;
                }

                // Render verification overlay indicator on matching zero point structure form layout dimensions
                RenderStatusOverlay("Patched.png");

                MessageBox.Show($"Selected mode(s) patched successfully!\nCurrent Mode Timer = {modeNumber}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) { MessageBox.Show("Patch failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            
            FinishButtonReset(pb);
        }

        // Helper rendering mechanism parsing the requested icon matching strict form coordinates layout parameters from zero point.
        private void RenderStatusOverlay(string imageName)
        {
            try {
                string imagePath = Path.Combine(basePath, "PSX PLAYER", "Debug Menu", "Buttons", imageName);
                if (File.Exists(imagePath))
                {
                    using (Graphics g = this.CreateGraphics())
                    {
                        using (Image overlayImg = Image.FromFile(imagePath))
                        {
                            g.DrawImage(overlayImg, 0, 0, this.Width, this.Height);
                        }
                    }
                }
            } catch { }
        }

        public async void OpenPlayer_Click(object sender, EventArgs e)
        {
            if (isShowingIntro) return;
            PictureBox pb = (PictureBox)sender;
            await ProcessClickEffect(pb, true);
            if (File.Exists(htmlPath))
            {
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
                Application.Exit(); 
            }
            else MessageBox.Show($"Could not find HTML at:\n{htmlPath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // --- UPDATED STARTUP ENGINE LIFE HOOKS ---
        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // FIXED: Directing audio thread explicitly inside PSX PLAYER\PSX Misc\ boot pathing map
                string waveFile = Path.Combine(basePath, "PSX PLAYER", "PSX Misc", "Boot.wav");
                if (File.Exists(waveFile))
                {
                    using (SoundPlayer nativeSpeaker = new SoundPlayer(waveFile))
                    {
                        nativeSpeaker.Play();
                    }
                }
            }
            catch { }

            // Engage the structural fade system layer to smoothly run Opacity from 0.0 straight up to 1.0
            isFadingOut = false;
            fadeTimer.Start();
        }
    }

    public class GifTracker
    {
        public Image GifImage { get; set; }
        public FrameDimension Dimension { get; set; }
        public int FrameCount { get; set; }
        public int CurrentFrameIndex { get; set; }
    }

    public class ButtonState
    {
        public Image OriginalImage { get; set; }
        public Size OriginalSize { get; set; }
        public Point OriginalLocation { get; set; }
    }
}