namespace ScreenSearchTool
{
    partial class Form2
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lbStatus = new ListBox();
            progressBar1 = new ProgressBar();
            btnCancel = new Button();
            lblTitle = new Label();
            SuspendLayout();
            // 
            // lbStatus
            // 
            lbStatus.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lbStatus.BackColor = SystemColors.Window;
            lbStatus.Font = new Font("Consolas", 9F);
            lbStatus.FormattingEnabled = true;
            lbStatus.ItemHeight = 18;
            lbStatus.Location = new Point(14, 47);
            lbStatus.Margin = new Padding(3, 4, 3, 4);
            lbStatus.Name = "lbStatus";
            lbStatus.Size = new Size(579, 202);
            lbStatus.TabIndex = 0;
            // 
            // progressBar1
            // 
            progressBar1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressBar1.Location = new Point(14, 290);
            progressBar1.Margin = new Padding(3, 4, 3, 4);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(487, 31);
            progressBar1.TabIndex = 1;
            // 
            // btnCancel
            // 
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Location = new Point(507, 290);
            btnCancel.Margin = new Padding(3, 4, 3, 4);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(86, 31);
            btnCancel.TabIndex = 2;
            btnCancel.Text = "Hủy";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += btnCancel_Click;
            // 
            // lblTitle
            // 
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            lblTitle.ForeColor = Color.FromArgb(64, 64, 64);
            lblTitle.Location = new Point(14, 12);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(126, 20);
            lblTitle.TabIndex = 3;
            lblTitle.Text = "Khởi tạo ban đầu";
            // 
            // Form2
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(607, 337);
            Controls.Add(lblTitle);
            Controls.Add(btnCancel);
            Controls.Add(progressBar1);
            Controls.Add(lbStatus);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Form2";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Screen Search Tool";
            Load += Form2_Load;
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private ListBox lbStatus;
        private ProgressBar progressBar1;
        private Button btnCancel;
        private Label lblTitle;
    }
}