namespace PhotoLikerUI
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            toolStrip1 = new ToolStrip();
            openToolStripButton = new ToolStripButton();
            saveToolStripButton = new ToolStripButton();
            toolStripSeparator = new ToolStripSeparator();
            copyToolStripButton = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            themeToggleToolStripButton = new ToolStripButton();
            toolStripSeparator4 = new ToolStripSeparator();
            openSourceFolderToolStripButton = new ToolStripButton();
            openLikedFolderToolStripButton = new ToolStripButton();
            loadLikedFolderToolStripButton = new ToolStripButton();
            generateVideoToolStripButton = new ToolStripButton();
            toolStripSeparator2 = new ToolStripSeparator();
            contextMenuToolStripButton = new ToolStripButton();
            toolStripSeparator3 = new ToolStripSeparator();
            helpToolStripButton = new ToolStripButton();
            splitContainer2 = new SplitContainer();
            settingsPropertyGrid = new PropertyGrid();
            imageMetaPropertyGrid = new PropertyGrid();
            splitContainer3 = new SplitContainer();
            scrollPanel = new Panel();
            pictureBox1 = new PictureBox();
            statusStrip1 = new StatusStrip();
            toolStripStatusLabelGpsLink = new ToolStripStatusLabel();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            previewFlowPanel = new FlowLayoutPanel();
            toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.Panel2.SuspendLayout();
            splitContainer2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer3).BeginInit();
            splitContainer3.Panel1.SuspendLayout();
            splitContainer3.Panel2.SuspendLayout();
            splitContainer3.SuspendLayout();
            scrollPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            //
            // toolStrip1
            //
            toolStrip1.AutoSize = false;
            toolStrip1.Dock = DockStyle.Left;
            toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip1.ImageScalingSize = new Size(16, 16);
            toolStrip1.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
            toolStrip1.Items.AddRange(new ToolStripItem[] { openToolStripButton, saveToolStripButton, toolStripSeparator, copyToolStripButton, toolStripSeparator1, themeToggleToolStripButton, toolStripSeparator4, openSourceFolderToolStripButton, openLikedFolderToolStripButton, loadLikedFolderToolStripButton, generateVideoToolStripButton, toolStripSeparator2, contextMenuToolStripButton, toolStripSeparator3, helpToolStripButton });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Padding = new Padding(0, 4, 0, 4);
            toolStrip1.Size = new Size(44, 940);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // openToolStripButton
            // 
            openToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            openToolStripButton.Image = (Image)resources.GetObject("openToolStripButton.Image");
            openToolStripButton.ImageTransparentColor = Color.Magenta;
            openToolStripButton.Name = "openToolStripButton";
            openToolStripButton.Size = new Size(23, 22);
            openToolStripButton.Text = "&Open";
            openToolStripButton.Click += OpenToolStripButton_Click;
            // 
            // saveToolStripButton
            // 
            saveToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            saveToolStripButton.Image = (Image)resources.GetObject("saveToolStripButton.Image");
            saveToolStripButton.ImageTransparentColor = Color.Magenta;
            saveToolStripButton.Name = "saveToolStripButton";
            saveToolStripButton.Size = new Size(23, 22);
            saveToolStripButton.Text = "&Save";
            saveToolStripButton.Click += saveToolStripButton_Click;
            // 
            // toolStripSeparator
            // 
            toolStripSeparator.Name = "toolStripSeparator";
            toolStripSeparator.Size = new Size(6, 25);
            // 
            // copyToolStripButton
            // 
            copyToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            copyToolStripButton.Image = (Image)resources.GetObject("copyToolStripButton.Image");
            copyToolStripButton.ImageTransparentColor = Color.Magenta;
            copyToolStripButton.Name = "copyToolStripButton";
            copyToolStripButton.Size = new Size(23, 22);
            copyToolStripButton.Text = "&Copy";
            copyToolStripButton.Click += CopyToolStripButton_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 25);
            // 
            // themeToggleToolStripButton
            // 
            themeToggleToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image; // the sun or the moon, set when the theme is applied
            themeToggleToolStripButton.Name = "themeToggleToolStripButton";
            themeToggleToolStripButton.Size = new Size(50, 22);
            themeToggleToolStripButton.Text = "🌙 Dark";
            themeToggleToolStripButton.ToolTipText = "Toggle dark / light theme";
            themeToggleToolStripButton.Click += ThemeToggleToolStripButton_Click;
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new Size(6, 25);
            // 
            // openSourceFolderToolStripButton
            // 
            openSourceFolderToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            openSourceFolderToolStripButton.Image = (Image)resources.GetObject("openSourceFolderToolStripButton.Image");
            openSourceFolderToolStripButton.Name = "openSourceFolderToolStripButton";
            openSourceFolderToolStripButton.Size = new Size(23, 20);
            openSourceFolderToolStripButton.Text = "Open Source Folder";
            openSourceFolderToolStripButton.ToolTipText = "Open source folder in File Explorer";
            openSourceFolderToolStripButton.Click += OpenSourceFolderToolStripButton_Click;
            // 
            // openLikedFolderToolStripButton
            // 
            openLikedFolderToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            openLikedFolderToolStripButton.Image = (Image)resources.GetObject("openLikedFolderToolStripButton.Image");
            openLikedFolderToolStripButton.Name = "openLikedFolderToolStripButton";
            openLikedFolderToolStripButton.Size = new Size(23, 20);
            openLikedFolderToolStripButton.Text = "Open Liked Folder";
            openLikedFolderToolStripButton.ToolTipText = "Open liked folder in File Explorer";
            openLikedFolderToolStripButton.Click += OpenLikedFolderToolStripButton_Click;
            // 
            // loadLikedFolderToolStripButton
            // 
            loadLikedFolderToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            loadLikedFolderToolStripButton.Image = (Image)resources.GetObject("loadLikedFolderToolStripButton.Image");
            loadLikedFolderToolStripButton.Name = "loadLikedFolderToolStripButton";
            loadLikedFolderToolStripButton.Size = new Size(23, 20);
            loadLikedFolderToolStripButton.Text = "Load Liked Folder";
            loadLikedFolderToolStripButton.ToolTipText = "Select and load a liked folder";
            loadLikedFolderToolStripButton.Click += LoadLikedFolderToolStripButton_Click;
            // 
            // generateVideoToolStripButton
            // 
            generateVideoToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            generateVideoToolStripButton.Image = (Image)resources.GetObject("generateVideoToolStripButton.Image");
            generateVideoToolStripButton.Name = "generateVideoToolStripButton";
            generateVideoToolStripButton.Size = new Size(23, 20);
            generateVideoToolStripButton.Text = "Generate Video";
            generateVideoToolStripButton.ToolTipText = "Generate video from loaded photos using ffmpeg";
            generateVideoToolStripButton.Click += GenerateVideoToolStripButton_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(6, 25);
            // 
            // contextMenuToolStripButton
            // 
            contextMenuToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            contextMenuToolStripButton.Image = (Image)resources.GetObject("contextMenuToolStripButton.Image");
            contextMenuToolStripButton.Name = "contextMenuToolStripButton";
            contextMenuToolStripButton.Size = new Size(23, 20);
            contextMenuToolStripButton.Text = "Register Context Menu";
            contextMenuToolStripButton.ToolTipText = "Register Context Menu";
            contextMenuToolStripButton.Click += contextMenuToolStripButton_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(6, 25);
            // 
            // helpToolStripButton
            // 
            helpToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            helpToolStripButton.Image = (Image)resources.GetObject("helpToolStripButton.Image");
            helpToolStripButton.ImageTransparentColor = Color.Magenta;
            helpToolStripButton.Name = "helpToolStripButton";
            helpToolStripButton.Size = new Size(23, 20);
            helpToolStripButton.Text = "He&lp";
            helpToolStripButton.Click += HelpToolStripButton_Click;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Left;
            splitContainer2.Location = new Point(44, 0);
            splitContainer2.Name = "splitContainer2";
            splitContainer2.Orientation = Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(settingsPropertyGrid);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(imageMetaPropertyGrid);
            splitContainer2.Size = new Size(320, 940);
            splitContainer2.SplitterDistance = 379;
            splitContainer2.TabIndex = 2;
            // 
            // settingsPropertyGrid
            // 
            settingsPropertyGrid.BackColor = SystemColors.Control;
            settingsPropertyGrid.Dock = DockStyle.Fill;
            settingsPropertyGrid.Location = new Point(0, 0);
            settingsPropertyGrid.Name = "settingsPropertyGrid";
            settingsPropertyGrid.Size = new Size(320, 379);
            settingsPropertyGrid.TabIndex = 0;
            // 
            // imageMetaPropertyGrid
            // 
            imageMetaPropertyGrid.BackColor = SystemColors.Control;
            imageMetaPropertyGrid.Dock = DockStyle.Fill;
            imageMetaPropertyGrid.Location = new Point(0, 0);
            imageMetaPropertyGrid.Name = "imageMetaPropertyGrid";
            imageMetaPropertyGrid.Size = new Size(320, 557);
            imageMetaPropertyGrid.TabIndex = 0;
            // 
            // splitContainer3
            // 
            splitContainer3.Dock = DockStyle.Fill;
            splitContainer3.Location = new Point(364, 0);
            splitContainer3.Name = "splitContainer3";
            // 
            // splitContainer3.Panel1
            // 
            splitContainer3.Panel1.Controls.Add(scrollPanel);
            // 
            // splitContainer3.Panel2
            // 
            splitContainer3.Panel2.Controls.Add(previewFlowPanel);
            splitContainer3.Size = new Size(757, 940);
            splitContainer3.SplitterDistance = 734;
            splitContainer3.TabIndex = 0;
            // 
            // scrollPanel
            // 
            scrollPanel.Controls.Add(pictureBox1);
            scrollPanel.Controls.Add(statusStrip1);
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.Location = new Point(0, 0);
            scrollPanel.Name = "scrollPanel";
            scrollPanel.Size = new Size(734, 940);
            scrollPanel.TabIndex = 1;
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(734, 918);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabelGpsLink, toolStripStatusLabel1 });
            statusStrip1.Location = new Point(0, 918);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(734, 22);
            statusStrip1.TabIndex = 1;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelGpsLink
            // 
            toolStripStatusLabelGpsLink.IsLink = true;
            toolStripStatusLabelGpsLink.LinkBehavior = LinkBehavior.HoverUnderline;
            toolStripStatusLabelGpsLink.Name = "toolStripStatusLabelGpsLink";
            toolStripStatusLabelGpsLink.Size = new Size(0, 17);
            toolStripStatusLabelGpsLink.ToolTipText = "Open location in Google Maps";
            toolStripStatusLabelGpsLink.Visible = false;
            toolStripStatusLabelGpsLink.Click += ToolStripStatusLabelGpsLink_Click;
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(719, 17);
            toolStripStatusLabel1.Spring = true;
            // 
            // previewFlowPanel
            // 
            previewFlowPanel.AutoScroll = true;
            previewFlowPanel.BackColor = SystemColors.ControlDark;
            previewFlowPanel.Dock = DockStyle.Fill;
            previewFlowPanel.FlowDirection = FlowDirection.TopDown;
            previewFlowPanel.Location = new Point(0, 0);
            previewFlowPanel.Name = "previewFlowPanel";
            previewFlowPanel.Padding = new Padding(4);
            previewFlowPanel.Size = new Size(192, 940);
            previewFlowPanel.TabIndex = 0;
            previewFlowPanel.WrapContents = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1121, 940);
            Controls.Add(splitContainer3);
            Controls.Add(splitContainer2);
            Controls.Add(toolStrip1);
            Name = "MainForm";
            Text = "PhotoLiker";
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            splitContainer2.Panel1.ResumeLayout(false);
            splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            splitContainer3.Panel1.ResumeLayout(false);
            splitContainer3.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer3).EndInit();
            splitContainer3.ResumeLayout(false);
            scrollPanel.ResumeLayout(false);
            scrollPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private PropertyGrid settingsPropertyGrid;
        private ToolStrip toolStrip1;
        private ToolStripButton openToolStripButton;
        private ToolStripButton saveToolStripButton;
        private ToolStripSeparator toolStripSeparator;
        private ToolStripButton copyToolStripButton;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton helpToolStripButton;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripButton contextMenuToolStripButton;
        private PictureBox pictureBox1;
        private Panel scrollPanel;
        private SplitContainer splitContainer2;
        private SplitContainer splitContainer3;
        private FlowLayoutPanel previewFlowPanel;
        private PropertyGrid imageMetaPropertyGrid;
        private StatusStrip statusStrip1;
            private ToolStripStatusLabel toolStripStatusLabel1;
            private ToolStripStatusLabel toolStripStatusLabelGpsLink;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripButton themeToggleToolStripButton;
        private ToolStripSeparator toolStripSeparator4;
        private ToolStripButton openSourceFolderToolStripButton;
        private ToolStripButton openLikedFolderToolStripButton;
        private ToolStripButton loadLikedFolderToolStripButton;
        private ToolStripButton generateVideoToolStripButton;
    }
}
