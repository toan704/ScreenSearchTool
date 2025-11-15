using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;
using Tesseract;
using Microsoft.Web.WebView2.WinForms;
using Gma.System.MouseKeyHook;

namespace ScreenSearchTool
{
    public partial class Form1 : Form
    {
        private IKeyboardMouseEvents _hook;
        Rectangle sel;
        bool dragging;
        Point start;
        string ocrText = "";


        public Form1()
        {
            InitializeComponent();
            TopMost = true; // luôn nổi
            lblStatus.Text = "Sẵn sàng";

            webView21.NavigationStarting += (s, e) => lblStatus.Text = "Đang tải...";
            webView21.NavigationCompleted += (s, e) => lblStatus.Text = "Sẵn sàng";
            webView21.Source = new Uri("https://www.google.com");

            // Ẩn form lúc khởi động
            this.Hide();

            // Đăng ký hotkey Ctrl+Alt+T
            _hook = Gma.System.MouseKeyHook.Hook.GlobalEvents();
            _hook.KeyDown += Hotkey_KeyDown;
        }

        private void Hotkey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Alt && e.KeyCode == Keys.T)
            {
                if (!this.Visible) this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                    this.WindowState = FormWindowState.Normal;
                this.TopMost = true;
                this.Activate();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            bool startupEnabled = IsStartupEnabled(); // hàm kiểm tra registry
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (startupEnabled)
                {
                    e.Cancel = true; // vẫn giữ ẩn form
                    this.Hide();
                }
                else
                {
                    e.Cancel = false; // tắt hoàn toàn
                }
            }
        }


        private void btnSelect_Click(object sender, EventArgs e)
        {
            using var overlay = new Form { FormBorderStyle = 0, Opacity = 0.3, BackColor = Color.Black, WindowState = FormWindowState.Maximized, TopMost = true };
            overlay.MouseDown += (s, e) => { dragging = true; start = e.Location; };
            overlay.MouseMove += (s, e) => { if (dragging) sel = Rect(start, e.Location); overlay.Invalidate(); };
            overlay.MouseUp += (s, e) => { dragging = false; overlay.Close(); };
            overlay.Paint += (s, e) => { if (sel.Width > 0) e.Graphics.DrawRectangle(Pens.Red, sel); };
            overlay.ShowDialog();

            if (sel.Width < 5 || sel.Height < 5) return;

            using var bmp = new Bitmap(sel.Width, sel.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(sel.Location, Point.Empty, sel.Size);

            var settings = SettingsManager.Load();
            string lang = settings.SelectedLanguage ?? "Auto";

            if (lang == "Auto")
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                lang = Directory.Exists(path)
                    ? string.Join("+", Directory.GetFiles(path, "*.traineddata").Select(f => Path.GetFileNameWithoutExtension(f)))
                    : "eng";
            }

            using var engine = new TesseractEngine("tessdata", lang, EngineMode.Default);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix);

            ocrText = page.GetText()?.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                if (settings.AutoCopy)
                    Clipboard.SetText(ocrText);

                webView21.Source = new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(ocrText));
            }
        }

        private async void btnAI_Click(object sender, EventArgs e)
        {
            var settings = SettingsManager.Load();
            string apiKey = settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("API Key chưa được thiết lập!", "Thông báo");
                return;
            }

            if (string.IsNullOrWhiteSpace(ocrText))
            {
                webView21.NavigateToString("<html><body>Không có văn bản để gửi đến AI.</body></html>");
                return;
            }

            string prompt = $"Tóm tắt hoặc trả lời văn bản: \"{ocrText}\"";
            var payload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                var response = await client.PostAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}",
                    new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
                );

                string json = await response.Content.ReadAsStringAsync();
                string aiText = response.IsSuccessStatusCode ? ExtractGeminiResponse(json) : $"API Không hợp lệ! Vui lòng kiểm tra và cấu hình lại";

                webView21.NavigateToString($@"
<html><body>
<h3>🤖 Kết quả AI:</h3>
<blockquote>{System.Net.WebUtility.HtmlEncode(aiText).Replace("\n", "<br>")}</blockquote>
<hr>
<small>OCR gửi:</small><br><code>{System.Net.WebUtility.HtmlEncode(ocrText)}</code>
</body></html>");
            }
            catch (Exception ex)
            {
                webView21.NavigateToString($"<html><body>Lỗi kết nối: {System.Net.WebUtility.HtmlEncode(ex.Message)}</body></html>");
            }
        }

        /// <summary>
        /// Trích xuất nội dung văn bản từ phản hồi JSON của Gemini API.
        /// </summary>
        /// <param name="json">Chuỗi JSON nhận được từ API.</param>
        /// <returns>Nội dung văn bản được tạo bởi AI, hoặc chuỗi rỗng nếu không tìm thấy.</returns>
        private string ExtractGeminiResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                // Đường dẫn tới nội dung: root -> candidates[0] -> content -> parts[0] -> text
                var root = document.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                    {
                        var part = parts[0];
                        if (part.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString() ?? string.Empty;
                        }
                    }
                }
                return string.Empty;
            }
            catch
            {
                // Xử lý lỗi phân tích JSON
                return string.Empty;
            }
        }

        private Rectangle Rect(Point a, Point b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private void btnSettings_Click(object sender, EventArgs e)
        {
            var settings = SettingsManager.Load();

            using var f = new Form()
            {
                Text = "Cài đặt",
                Size = new Size(420, 280),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10),
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // API Key
            table.Controls.Add(new Label { Text = "API Gemini:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var txtApi = new TextBox { Text = settings.ApiKey, Dock = DockStyle.Fill };
            txtApi.TextChanged += (s, ev) => { settings.ApiKey = txtApi.Text.Trim(); SettingsManager.Save(settings); };
            table.Controls.Add(txtApi, 1, 0);

            // Auto Copy
            var chkCopy = new CheckBox { Text = "Tự động copy OCR", Checked = settings.AutoCopy, AutoSize = true, Anchor = AnchorStyles.Left };
            chkCopy.CheckedChanged += (s, ev) => { settings.AutoCopy = chkCopy.Checked; SettingsManager.Save(settings); };
            table.Controls.Add(chkCopy, 0, 1);
            table.SetColumnSpan(chkCopy, 2);

            // Language OCR
            table.Controls.Add(new Label { Text = "Ngôn ngữ OCR:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            var cbLang = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cbLang.Items.Add("Auto");
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (Directory.Exists(path))
                foreach (var fPath in Directory.GetFiles(path, "*.traineddata"))
                    cbLang.Items.Add(Path.GetFileNameWithoutExtension(fPath));
            cbLang.SelectedItem = settings.SelectedLanguage ?? "Auto";
            cbLang.SelectedIndexChanged += (s, ev) => { settings.SelectedLanguage = cbLang.SelectedItem.ToString(); SettingsManager.Save(settings); };
            table.Controls.Add(cbLang, 1, 2);

            // Startup
            var chkStartup = new CheckBox { Text = "Khởi động cùng Windows (CTRL + ALT + T)", AutoSize = true, Anchor = AnchorStyles.Left };
            chkStartup.Checked = IsStartupEnabled();
            chkStartup.CheckedChanged += (s, ev) => SetStartup(chkStartup.Checked);
            table.Controls.Add(chkStartup, 0, 3);
            table.SetColumnSpan(chkStartup, 2);

            // Liên hệ
            var linkText = "Liên hệ: fb.com/toan704";
            var linkContact = new LinkLabel { Text = linkText, AutoSize = true, LinkColor = Color.Blue, Anchor = AnchorStyles.Left };
            linkContact.Links.Add(8, linkText.Length - 8, "https://fb.com/toan704"); // link từ ký tự thứ 8 đến cuối
            linkContact.LinkClicked += (s, ev) =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ev.Link.LinkData.ToString()) { UseShellExecute = true });
            table.Controls.Add(linkContact, 0, 5);
            table.SetColumnSpan(linkContact, 2);

            // Button Close
            var btnClose = new Button { Text = "Đóng", AutoSize = true, Anchor = AnchorStyles.Right };
            btnClose.Click += (s, ev) => f.Close();
            table.Controls.Add(btnClose, 1, 6);


            f.Controls.Add(table);
            f.ShowDialog();
        }


        // Kiểm tra registry xem đã khởi động cùng Windows chưa
        private bool IsStartupEnabled()
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("ScreenSearchTool") != null;
        }

        // Ghi/ xóa registry để bật/tắt tự khởi động
        private void SetStartup(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (enable)
                    key.SetValue("ScreenSearchTool", $"\"{exe}\"");
                else
                    key.DeleteValue("ScreenSearchTool", false);
            }
            catch { }
        }



    }

    class Settings
    {
        public string SelectedLanguage { get; set; } = "Auto";
        public bool AutoCopy { get; set; } = true;
        public string ApiKey { get; set; } = "";
    }

    static class SettingsManager
    {
        static string file = "settings.json";

        public static Settings Load() =>
            File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) : new Settings();

        public static void Save(Settings s) =>
            File.WriteAllText(file, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
