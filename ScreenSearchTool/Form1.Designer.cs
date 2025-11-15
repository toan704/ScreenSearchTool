namespace ScreenSearchTool
{
    partial class Form1
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            btnSelect = new Button();
            webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            tableLayoutPanel = new TableLayoutPanel();
            panelTooltip = new Panel();
            btnSettings = new Button();
            btnAI = new Button();
            lblStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            tableLayoutPanel.SuspendLayout();
            panelTooltip.SuspendLayout();
            SuspendLayout();
            // 
            // btnSelect
            // 
            btnSelect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSelect.Location = new Point(519, 0);
            btnSelect.Name = "btnSelect";
            btnSelect.Size = new Size(94, 34);
            btnSelect.TabIndex = 0;
            btnSelect.Text = "Chọn vùng";
            btnSelect.UseVisualStyleBackColor = true;
            btnSelect.Click += btnSelect_Click;
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Dock = DockStyle.Fill;
            webView21.Location = new Point(3, 3);
            webView21.Name = "webView21";
            webView21.Size = new Size(616, 285);
            webView21.TabIndex = 1;
            webView21.ZoomFactor = 1D;
            // 
            // tableLayoutPanel
            // 
            tableLayoutPanel.ColumnCount = 1;
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel.Controls.Add(webView21, 0, 0);
            tableLayoutPanel.Controls.Add(panelTooltip, 0, 1);
            tableLayoutPanel.Dock = DockStyle.Fill;
            tableLayoutPanel.Location = new Point(0, 0);
            tableLayoutPanel.Name = "tableLayoutPanel";
            tableLayoutPanel.RowCount = 2;
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            tableLayoutPanel.Size = new Size(622, 331);
            tableLayoutPanel.TabIndex = 2;
            // 
            // panelTooltip
            // 
            panelTooltip.BackColor = SystemColors.Control;
            panelTooltip.Controls.Add(btnSettings);
            panelTooltip.Controls.Add(btnAI);
            panelTooltip.Controls.Add(lblStatus);
            panelTooltip.Controls.Add(btnSelect);
            panelTooltip.Dock = DockStyle.Fill;
            panelTooltip.Location = new Point(3, 294);
            panelTooltip.Name = "panelTooltip";
            panelTooltip.Size = new Size(616, 34);
            panelTooltip.TabIndex = 2;
            // 
            // btnSettings
            // 
            btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSettings.Location = new Point(319, 0);
            btnSettings.Name = "btnSettings";
            btnSettings.Size = new Size(94, 34);
            btnSettings.TabIndex = 5;
            btnSettings.Text = "Cài đặt";
            btnSettings.UseVisualStyleBackColor = true;
            btnSettings.Click += btnSettings_Click;
            // 
            // btnAI
            // 
            btnAI.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAI.Location = new Point(419, 0);
            btnAI.Name = "btnAI";
            btnAI.Size = new Size(94, 34);
            btnAI.TabIndex = 2;
            btnAI.Text = "AI - Gemini";
            btnAI.UseVisualStyleBackColor = true;
            btnAI.Click += btnAI_Click;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(9, 7);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(18, 20);
            lblStatus.TabIndex = 1;
            lblStatus.Text = "...";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(622, 331);
            Controls.Add(tableLayoutPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "ScreenSearch Mini Tool | Toan704";
            FormClosing += Form1_FormClosing;
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            tableLayoutPanel.ResumeLayout(false);
            panelTooltip.ResumeLayout(false);
            panelTooltip.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Button btnSelect;
        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private TableLayoutPanel tableLayoutPanel;
        private Panel panelTooltip;
        private Label lblStatus;
        private Button btnAI;
        private Button btnSettings;
    }
}
