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
            splitContainer1 = new SplitContainer();
            toolStrip1 = new ToolStrip();
            newToolStripButton = new ToolStripButton();
            openToolStripButton = new ToolStripButton();
            saveToolStripButton = new ToolStripButton();
            printToolStripButton = new ToolStripButton();
            toolStripSeparator = new ToolStripSeparator();
            cutToolStripButton = new ToolStripButton();
            copyToolStripButton = new ToolStripButton();
            pasteToolStripButton = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            helpToolStripButton = new ToolStripButton();
            toolStripSeparator2 = new ToolStripSeparator();
            contextMenuToolStripButton = new ToolStripButton();
            toolStripSeparator3 = new ToolStripSeparator();
            themeToggleToolStripButton = new ToolStripButton();
            splitContainer2 = new SplitContainer();
            settingsPropertyGrid = new PropertyGrid();
            imageMetaPropertyGrid = new PropertyGrid();
            splitContainer3 = new SplitContainer();
            scrollPanel = new Panel();
            pictureBox1 = new PictureBox();
            previewFlowPanel = new FlowLayoutPanel();
            statusStrip1 = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            toolStripStatusLabelGpsLink = new ToolStripStatusLabel();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
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
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(toolStrip1);
            splitContainer1.Panel1.Controls.Add(splitContainer2);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(splitContainer3);
            splitContainer1.Size = new Size(800, 450);
            splitContainer1.SplitterDistance = 178;
            splitContainer1.TabIndex = 0;
            // 
            // toolStrip1
            // 
            toolStrip1.Items.AddRange(new ToolStripItem[] { newToolStripButton, openToolStripButton, saveToolStripButton, printToolStripButton, toolStripSeparator, cutToolStripButton, copyToolStripButton, pasteToolStripButton, toolStripSeparator1, helpToolStripButton, toolStripSeparator2, contextMenuToolStripButton, toolStripSeparator3, themeToggleToolStripButton });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(178, 25);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // newToolStripButton
            // 
            newToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            newToolStripButton.Image = (Image)resources.GetObject("newToolStripButton.Image");
            newToolStripButton.ImageTransparentColor = Color.Magenta;
            newToolStripButton.Name = "newToolStripButton";
            newToolStripButton.Size = new Size(23, 22);
            newToolStripButton.Text = "&New";
            // 
            // openToolStripButton
            // 
            openToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            openToolStripButton.Image = (Image)resources.GetObject("openToolStripButton.Image");
            openToolStripButton.ImageTransparentColor = Color.Magenta;
            openToolStripButton.Name = "openToolStripButton";
            openToolStripButton.Size = new Size(23, 22);
            openToolStripButton.Text = "&Open";
            openToolStripButton.Click += openToolStripButton_Click;
            // 
            // saveToolStripButton
            // 
            saveToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            saveToolStripButton.Image = (Image)resources.GetObject("saveToolStripButton.Image");
            saveToolStripButton.ImageTransparentColor = Color.Magenta;
            saveToolStripButton.Name = "saveToolStripButton";
            saveToolStripButton.Size = new Size(23, 22);
            saveToolStripButton.Text = "&Save";
            // 
            // printToolStripButton
            // 
            printToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            printToolStripButton.Image = (Image)resources.GetObject("printToolStripButton.Image");
            printToolStripButton.ImageTransparentColor = Color.Magenta;
            printToolStripButton.Name = "printToolStripButton";
            printToolStripButton.Size = new Size(23, 22);
            printToolStripButton.Text = "&Print";
            // 
            // toolStripSeparator
            // 
            toolStripSeparator.Name = "toolStripSeparator";
            toolStripSeparator.Size = new Size(6, 25);
            // 
            // cutToolStripButton
            // 
            cutToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            cutToolStripButton.Image = (Image)resources.GetObject("cutToolStripButton.Image");
            cutToolStripButton.ImageTransparentColor = Color.Magenta;
            cutToolStripButton.Name = "cutToolStripButton";
            cutToolStripButton.Size = new Size(23, 22);
            cutToolStripButton.Text = "C&ut";
            // 
            // copyToolStripButton
            // 
            copyToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            copyToolStripButton.Image = (Image)resources.GetObject("copyToolStripButton.Image");
            copyToolStripButton.ImageTransparentColor = Color.Magenta;
            copyToolStripButton.Name = "copyToolStripButton";
            copyToolStripButton.Size = new Size(23, 22);
            copyToolStripButton.Text = "&Copy";
            // 
            // pasteToolStripButton
            // 
            pasteToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            pasteToolStripButton.Image = (Image)resources.GetObject("pasteToolStripButton.Image");
            pasteToolStripButton.ImageTransparentColor = Color.Magenta;
            pasteToolStripButton.Name = "pasteToolStripButton";
            pasteToolStripButton.Size = new Size(23, 20);
            pasteToolStripButton.Text = "&Paste";
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 25);
            // 
            // helpToolStripButton
            // 
            helpToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            helpToolStripButton.Image = (Image)resources.GetObject("helpToolStripButton.Image");
            helpToolStripButton.ImageTransparentColor = Color.Magenta;
            helpToolStripButton.Name = "helpToolStripButton";
            helpToolStripButton.Size = new Size(23, 20);
            helpToolStripButton.Text = "He&lp";
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(6, 25);
            // 
            // contextMenuToolStripButton
            // 
            contextMenuToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            contextMenuToolStripButton.Name = "contextMenuToolStripButton";
            contextMenuToolStripButton.Size = new Size(23, 22);
            contextMenuToolStripButton.Text = "Register Context Menu";
            contextMenuToolStripButton.Click += contextMenuToolStripButton_Click;
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(6, 25);
            // 
            // themeToggleToolStripButton
            // 
            themeToggleToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            themeToggleToolStripButton.Name = "themeToggleToolStripButton";
            themeToggleToolStripButton.Size = new Size(23, 22);
            themeToggleToolStripButton.Text = "🌙 Dark";
            themeToggleToolStripButton.ToolTipText = "Toggle dark / light theme";
            themeToggleToolStripButton.Click += (s, e) => ThemeToggleToolStripButton_Click(s, e);
            // 
            // splitContainer2
            // 
            splitContainer2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitContainer2.Location = new Point(3, 28);
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
            splitContainer2.Size = new Size(172, 419);
            splitContainer2.SplitterDistance = 175;
            splitContainer2.TabIndex = 2;
            // 
            // settingsPropertyGrid
            // 
            settingsPropertyGrid.BackColor = SystemColors.Control;
            settingsPropertyGrid.Dock = DockStyle.Fill;
            settingsPropertyGrid.Location = new Point(0, 0);
            settingsPropertyGrid.Name = "settingsPropertyGrid";
            settingsPropertyGrid.Size = new Size(172, 175);
            settingsPropertyGrid.TabIndex = 0;
            // 
            // imageMetaPropertyGrid
            // 
            imageMetaPropertyGrid.BackColor = SystemColors.Control;
            imageMetaPropertyGrid.Dock = DockStyle.Fill;
            imageMetaPropertyGrid.Location = new Point(0, 0);
            imageMetaPropertyGrid.Name = "imageMetaPropertyGrid";
            imageMetaPropertyGrid.Size = new Size(172, 240);
            imageMetaPropertyGrid.TabIndex = 0;
            // 
            // splitContainer3
            // 
            splitContainer3.Dock = DockStyle.Fill;
            splitContainer3.Location = new Point(0, 0);
            splitContainer3.Name = "splitContainer3";
            // 
            // splitContainer3.Panel1
            // 
            splitContainer3.Panel1.Controls.Add(scrollPanel);
            // 
            // splitContainer3.Panel2
            // 
            splitContainer3.Panel2.Controls.Add(previewFlowPanel);
            splitContainer3.Size = new Size(618, 450);
            splitContainer3.SplitterDistance = 490;
            splitContainer3.TabIndex = 0;
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
            previewFlowPanel.Size = new Size(124, 450);
            previewFlowPanel.TabIndex = 0;
            previewFlowPanel.WrapContents = false;
            // 
            // scrollPanel
            // 
            scrollPanel.Controls.Add(pictureBox1);
            scrollPanel.Controls.Add(statusStrip1);
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.Location = new Point(0, 0);
            scrollPanel.Name = "scrollPanel";
            scrollPanel.Size = new Size(490, 450);
            scrollPanel.TabIndex = 1;
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(618, 428);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabelGpsLink, toolStripStatusLabel1 });
            statusStrip1.Location = new Point(0, 428);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(618, 22);
            statusStrip1.TabIndex = 1;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelGpsLink
            // 
            toolStripStatusLabelGpsLink.Name = "toolStripStatusLabelGpsLink";
            toolStripStatusLabelGpsLink.Size = new Size(0, 17);
            toolStripStatusLabelGpsLink.IsLink = true;
            toolStripStatusLabelGpsLink.LinkBehavior = LinkBehavior.HoverUnderline;
            toolStripStatusLabelGpsLink.Visible = false;
            toolStripStatusLabelGpsLink.ToolTipText = "Open location in Google Maps";
            toolStripStatusLabelGpsLink.Click += ToolStripStatusLabelGpsLink_Click;
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(0, 17);
            toolStripStatusLabel1.Spring = true;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(splitContainer1);
            Name = "MainForm";
            Text = "PhotoLiker";
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
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

        private SplitContainer splitContainer1;
        private PropertyGrid settingsPropertyGrid;
        private ToolStrip toolStrip1;
        private ToolStripButton newToolStripButton;
        private ToolStripButton openToolStripButton;
        private ToolStripButton saveToolStripButton;
        private ToolStripButton printToolStripButton;
        private ToolStripSeparator toolStripSeparator;
        private ToolStripButton cutToolStripButton;
        private ToolStripButton copyToolStripButton;
        private ToolStripButton pasteToolStripButton;
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
    }
}
